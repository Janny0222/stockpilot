using StockPilot.Domain.Common;
using StockPilot.Domain.Identity;

namespace StockPilot.UnitTests.Domain;

public sealed class UserTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 8, 0, 0, TimeSpan.Zero);

    private static User NewUser() =>
        User.CreateTenantUser(Guid.CreateVersion7(), SystemRoles.OwnerId, "  Maria.Santos@Example.COM ", "Maria", "Santos");

    [Fact]
    public void Email_is_normalized()
    {
        NewUser().Email.ShouldBe("maria.santos@example.com");
    }

    [Fact]
    public void Tenant_user_requires_a_tenant()
    {
        Should.Throw<ArgumentException>(() => User.CreateTenantUser(Guid.Empty, SystemRoles.OwnerId, "a@b.c", "A", "B"));
    }

    [Fact]
    public void Platform_admin_has_no_tenant()
    {
        var admin = User.CreatePlatformAdmin("admin@x.io", "P", "A");
        admin.TenantId.ShouldBeNull();
        admin.IsPlatformAdmin.ShouldBeTrue();
        admin.RoleId.ShouldBe(SystemRoles.SuperAdminId);
    }

    [Fact]
    public void Locks_out_after_max_failed_attempts_and_resets_counter()
    {
        var user = NewUser();

        for (var i = 0; i < 4; i++)
        {
            user.RecordFailedLogin(Now, maxAttempts: 5, TimeSpan.FromMinutes(15));
        }

        user.IsLockedOut(Now).ShouldBeFalse();
        user.RecordFailedLogin(Now, maxAttempts: 5, TimeSpan.FromMinutes(15));

        user.IsLockedOut(Now).ShouldBeTrue();
        user.IsLockedOut(Now.AddMinutes(16)).ShouldBeFalse();
        user.AccessFailedCount.ShouldBe(0);
    }

    [Fact]
    public void Successful_login_clears_lockout_state()
    {
        var user = NewUser();
        user.RecordFailedLogin(Now, 5, TimeSpan.FromMinutes(15));
        user.RecordSuccessfulLogin(Now);

        user.AccessFailedCount.ShouldBe(0);
        user.LockoutEnd.ShouldBeNull();
        user.LastLoginAt.ShouldBe(Now);
    }

    [Fact]
    public void Password_change_and_deactivation_rotate_the_security_stamp()
    {
        var user = NewUser();
        var stamp = user.SecurityStamp;

        user.SetPasswordHash("hash");
        user.SecurityStamp.ShouldNotBe(stamp);

        stamp = user.SecurityStamp;
        user.Deactivate();
        user.SecurityStamp.ShouldNotBe(stamp);
        user.IsActive.ShouldBeFalse();
    }

    [Fact]
    public void Upgrading_the_hash_keeps_sessions_alive()
    {
        var user = NewUser();
        user.SetPasswordHash("old");
        var stamp = user.SecurityStamp;

        user.UpgradePasswordHash("new");

        user.PasswordHash.ShouldBe("new");
        user.SecurityStamp.ShouldBe(stamp);
    }
}

public sealed class RefreshTokenTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void New_token_is_active_until_expiry()
    {
        var token = RefreshToken.Issue(Guid.NewGuid(), "h", Guid.NewGuid(), Now, TimeSpan.FromDays(7), "1.2.3.4", "ua");

        token.IsActive(Now).ShouldBeTrue();
        token.IsActive(Now.AddDays(7)).ShouldBeFalse();
    }

    [Fact]
    public void Revoke_is_idempotent_and_keeps_the_first_reason()
    {
        var token = RefreshToken.Issue(Guid.NewGuid(), "h", Guid.NewGuid(), Now, TimeSpan.FromDays(7), null, null);
        var replacement = Guid.NewGuid();

        token.Revoke(Now, RefreshTokenRevokeReason.Rotated, replacement);
        token.Revoke(Now.AddMinutes(1), RefreshTokenRevokeReason.ReuseDetected);

        token.IsActive(Now).ShouldBeFalse();
        token.RevokeReason.ShouldBe(RefreshTokenRevokeReason.Rotated);
        token.ReplacedByTokenId.ShouldBe(replacement);
        token.RevokedAt.ShouldBe(Now);
    }

    [Fact]
    public void Very_long_user_agents_are_truncated()
    {
        var token = RefreshToken.Issue(Guid.NewGuid(), "h", Guid.NewGuid(), Now, TimeSpan.FromDays(1), null, new string('x', 2000));
        token.UserAgent!.Length.ShouldBe(512);
    }
}

public sealed class UserTokenTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Can_be_consumed_only_once()
    {
        var token = UserToken.Create(Guid.NewGuid(), UserTokenPurpose.PasswordReset, "h", Now, TimeSpan.FromHours(1));

        token.Consume(Now);

        Should.Throw<DomainException>(() => token.Consume(Now)).Code.ShouldBe("TOKEN_INVALID");
    }

    [Fact]
    public void Cannot_be_consumed_after_expiry()
    {
        var token = UserToken.Create(Guid.NewGuid(), UserTokenPurpose.EmailVerification, "h", Now, TimeSpan.FromHours(1));

        Should.Throw<DomainException>(() => token.Consume(Now.AddHours(1)));
    }
}

public sealed class SlugTests
{
    [Theory]
    [InlineData("Acme Trading Co.", "acme-trading-co")]
    [InlineData("  Northwind   Supply!! ", "northwind-supply")]
    [InlineData("ÄÖÜ", "company")]
    [InlineData("123 Hardware", "123-hardware")]
    public void Creates_url_safe_slugs(string input, string expected)
    {
        Slug.From(input).ShouldBe(expected);
    }

    [Fact]
    public void Slugs_are_length_limited()
    {
        Slug.From(new string('a', 200)).Length.ShouldBe(Slug.MaxLength);
    }
}
