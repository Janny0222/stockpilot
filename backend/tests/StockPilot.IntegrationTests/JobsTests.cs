using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using StockPilot.Domain.Identity;
using StockPilot.Domain.Notifications;
using StockPilot.Infrastructure.Jobs;
using StockPilot.IntegrationTests.Infrastructure;

namespace StockPilot.IntegrationTests;

/// <summary>
/// Background jobs, run through the Super Admin's "Run now" (same runner, lock and record as the scheduler, which is
/// switched off in tests). Every company shares the test database, so assertions target specific rows, not counts.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class JobsTests(ApiFactory factory)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private async Task<JsonElement> RunAsync(string job)
    {
        var admin = await factory.LoginAsync(ApiFactory.SuperAdminEmail, ApiFactory.SuperAdminPassword);
        var response = await admin.Client.PostAsync($"/api/v1/platform/jobs/{job}/run", null);
        response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>(Json);
    }

    [Fact]
    public async Task Token_cleanup_deletes_only_tokens_that_expired_over_a_day_ago()
    {
        var owner = await factory.RegisterCompanyAsync();
        var now = DateTimeOffset.UtcNow;
        var (oldRefresh, liveRefresh, oldLink) = await factory.WithDbAsync(async db =>
        {
            var old = RefreshToken.Issue(owner.UserId, Guid.NewGuid().ToString("N"), Guid.NewGuid(), now.AddDays(-10), TimeSpan.FromDays(7), null, null);
            var live = RefreshToken.Issue(owner.UserId, Guid.NewGuid().ToString("N"), Guid.NewGuid(), now, TimeSpan.FromDays(7), null, null);
            var link = UserToken.Create(owner.UserId, UserTokenPurpose.PasswordReset, Guid.NewGuid().ToString("N"), now.AddDays(-3), TimeSpan.FromHours(1));
            db.AddRange(old, live, link);
            await db.SaveChangesAsync();
            return (old.Id, live.Id, link.Id);
        });

        var result = await RunAsync("token-cleanup");
        result.GetProperty("ran").GetBoolean().ShouldBeTrue();
        result.GetProperty("succeeded").GetBoolean().ShouldBeTrue();

        await factory.WithDbAsync(async db =>
        {
            (await db.RefreshTokens.AnyAsync(t => t.Id == oldRefresh)).ShouldBeFalse();
            (await db.RefreshTokens.AnyAsync(t => t.Id == liveRefresh)).ShouldBeTrue();
            (await db.UserTokens.AnyAsync(t => t.Id == oldLink)).ShouldBeFalse();
            return true;
        });

        // The user is still signed in: their real session wasn't touched.
        (await owner.Client.GetAsync("/api/v1/auth/me")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Notification_retention_keeps_recent_and_unread_ones()
    {
        var owner = await factory.RegisterCompanyAsync();
        var now = DateTimeOffset.UtcNow;
        Notification Make(int daysAgo, bool read)
        {
            var n = new Notification(owner.TenantId, owner.UserId, NotificationType.LowStock, $"{daysAgo}d", "test", null, null, now.AddDays(-daysAgo));
            if (read) n.MarkRead(now.AddDays(-daysAgo));
            return n;
        }

        var (oldRead, oldUnread, ancient, recentRead) = (Make(100, true), Make(100, false), Make(400, false), Make(10, true));
        await factory.WithDbAsync(db =>
        {
            db.AddRange(oldRead, oldUnread, ancient, recentRead);
            return db.SaveChangesAsync();
        });

        await RunAsync("notification-retention");

        var left = await factory.WithDbAsync(db => db.Notifications.IgnoreQueryFilters()
            .Where(n => n.RecipientUserId == owner.UserId).Select(n => n.Id).ToListAsync());
        left.ShouldBe([oldUnread.Id, recentRead.Id], ignoreOrder: true);
    }

    [Fact]
    public async Task Low_stock_digest_goes_only_to_people_who_opted_in_and_have_something_low()
    {
        var owner = await factory.RegisterCompanyAsync();
        var manager = await factory.CreateUserAsync(owner.TenantId, SystemRoles.ManagerId); // not opted in
        var warehouse = await factory.DefaultWarehouseIdAsync(owner.TenantId);
        var product = await (await owner.Client.PostAsJsonAsync("/api/v1/products", new
        {
            name = "Teflon Tape", unitOfMeasure = "pcs",
            variants = new[] { new { sku = "TT-12", costPrice = 5m, sellingPrice = 9m, reorderLevel = 20m } },
        })).Content.ReadFromJsonAsync<JsonElement>(Json);
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/inventory/stock-in")
        {
            Content = JsonContent.Create(new { warehouseId = warehouse, lines = new[] { new { variantId = product.GetProperty("variants")[0].GetProperty("id").GetGuid(), quantity = 4m, unitCost = 5m } } }),
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        (await owner.Client.SendAsync(request)).EnsureSuccessStatusCode();

        // The owner opts in; the email switch is only offered where it exists.
        var prefs = await (await owner.Client.PutAsJsonAsync("/api/v1/notifications/preferences",
            new { preferences = new[] { new { type = "LowStock", email = true } } })).Content.ReadFromJsonAsync<JsonElement>(Json);
        var low = prefs.EnumerateArray().Single(p => p.GetProperty("type").GetString() == "LowStock");
        low.GetProperty("email").GetBoolean().ShouldBeTrue();
        low.GetProperty("inApp").GetBoolean().ShouldBeTrue(); // untouched by a partial update
        (await owner.Client.PutAsJsonAsync("/api/v1/notifications/preferences",
            new { preferences = new[] { new { type = "UserJoined", email = true } } })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // Someone opted in at a company with nothing low gets nothing.
        var calm = await factory.RegisterCompanyAsync();
        await calm.Client.PutAsJsonAsync("/api/v1/notifications/preferences", new { preferences = new[] { new { type = "LowStock", email = true } } });

        await RunAsync("low-stock-digest");

        var mail = factory.Emails.Sent.Where(m => m.To == owner.Email && m.Subject.Contains("reordering")).ShouldHaveSingleItem();
        mail.Subject.ShouldStartWith("1 item needs reordering");
        mail.TextBody.ShouldContain("Teflon Tape (TT-12)");
        mail.TextBody.ShouldContain("4 available, reorder at 20");
        mail.HtmlBody.ShouldContain("/inventory?low=1");
        factory.Emails.Sent.ShouldNotContain(m => m.To == manager.Email);
        factory.Emails.Sent.ShouldNotContain(m => m.To == calm.Email && m.Subject.Contains("reordering"));
    }

    [Fact]
    public async Task A_job_already_running_elsewhere_is_skipped_not_doubled()
    {
        // Hold the job's advisory lock, as another instance would while running it. Unpooled, so closing it really ends
        // the session (a pooled connection would keep the lock alive in the pool after Close).
        await using var other = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(factory.ConnectionString) { Pooling = false }.ConnectionString);
        await other.OpenAsync();
        await using (var take = new NpgsqlCommand("SELECT pg_advisory_lock(@key)", other))
        {
            take.Parameters.AddWithValue("key", JobLocks.KeyFor("token-cleanup"));
            await take.ExecuteScalarAsync();
        }

        var skipped = await RunAsync("token-cleanup");
        skipped.GetProperty("ran").GetBoolean().ShouldBeFalse();

        await other.CloseAsync(); // the "other instance" finishes
        (await RunAsync("token-cleanup")).GetProperty("ran").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task Job_status_is_visible_to_the_platform_only()
    {
        await RunAsync("notification-retention");
        var admin = await factory.LoginAsync(ApiFactory.SuperAdminEmail, ApiFactory.SuperAdminPassword);
        var jobs = await admin.Client.GetFromJsonAsync<JsonElement>("/api/v1/platform/jobs", Json);
        var retention = jobs.EnumerateArray().Single(j => j.GetProperty("name").GetString() == "notification-retention");
        retention.GetProperty("lastSucceeded").GetBoolean().ShouldBeTrue();
        retention.GetProperty("lastMessage").GetString()!.ShouldStartWith("Deleted");
        retention.GetProperty("schedule").GetString().ShouldBe("Daily at 19:00 UTC");

        (await admin.Client.PostAsync("/api/v1/platform/jobs/no-such-job/run", null)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var owner = await factory.RegisterCompanyAsync();
        (await owner.Client.GetAsync("/api/v1/platform/jobs")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await owner.Client.PostAsync("/api/v1/platform/jobs/token-cleanup/run", null)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
