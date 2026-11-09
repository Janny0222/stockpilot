using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using StockPilot.Application.Common.Abstractions;
using StockPilot.Application.Common.Options;
using StockPilot.Domain.Identity;
using StockPilot.Infrastructure.Identity;

namespace StockPilot.UnitTests.Infrastructure;

public sealed class SecureTokenFactoryTests
{
    private readonly SecureTokenFactory _factory = new();

    [Fact]
    public void Creates_unique_256_bit_url_safe_tokens()
    {
        var tokens = Enumerable.Range(0, 100).Select(_ => _factory.Create().RawToken).ToList();

        tokens.ShouldBeUnique();
        tokens.ShouldAllBe(t => t.Length == 43 && !t.Contains('+') && !t.Contains('/') && !t.Contains('='));
    }

    [Fact]
    public void Hash_is_deterministic_sha256_hex_and_never_equals_the_raw_token()
    {
        var (raw, hash) = _factory.Create();

        _factory.Hash(raw).ShouldBe(hash);
        hash.Length.ShouldBe(64);
        hash.ShouldNotBe(raw);
    }
}

public sealed class PasswordHasherTests
{
    private readonly PasswordHasher _hasher = new();

    [Fact]
    public void Verifies_correct_password_and_rejects_wrong_one()
    {
        var hash = _hasher.Hash("Corr3ct-Horse");

        _hasher.Verify(hash, "Corr3ct-Horse").ShouldBe(PasswordCheck.Success);
        _hasher.Verify(hash, "corr3ct-horse").ShouldBe(PasswordCheck.Failed);
    }

    [Fact]
    public void Same_password_hashes_differently_each_time_because_of_the_salt()
    {
        _hasher.Hash("Same-Passw0rd").ShouldNotBe(_hasher.Hash("Same-Passw0rd"));
    }
}

public sealed class JwtAccessTokenServiceTests
{
    [Fact]
    public void Token_contains_identity_claims_but_no_permissions_or_pii()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 9, 0, 0, 0, TimeSpan.Zero));
        var service = new JwtAccessTokenService(
            Options.Create(new JwtOptions { SigningKey = new string('k', 48) }),
            Options.Create(new AuthOptions()),
            clock);
        var tenantId = Guid.CreateVersion7();
        var user = User.CreateTenantUser(tenantId, SystemRoles.ManagerId, "jose@x.io", "Jose", "Cruz");

        var token = service.Create(user);
        var jwt = new JsonWebToken(token.Token);

        jwt.GetClaim(StockPilotClaims.UserId).Value.ShouldBe(user.Id.ToString());
        jwt.GetClaim(StockPilotClaims.TenantId).Value.ShouldBe(tenantId.ToString());
        jwt.GetClaim(StockPilotClaims.RoleId).Value.ShouldBe(SystemRoles.ManagerId.ToString());
        jwt.GetClaim(StockPilotClaims.SecurityStamp).Value.ShouldBe(user.SecurityStamp);
        jwt.Claims.ShouldNotContain(c => c.Value.Contains("jose@x.io"));
        jwt.Alg.ShouldBe("HS256");
        token.ExpiresAt.ShouldBe(clock.GetUtcNow().AddMinutes(15));
    }

    [Fact]
    public void Platform_admin_token_has_no_tenant_claim()
    {
        var service = new JwtAccessTokenService(
            Options.Create(new JwtOptions { SigningKey = new string('k', 48) }),
            Options.Create(new AuthOptions()),
            TimeProvider.System);

        var jwt = new JsonWebToken(service.Create(User.CreatePlatformAdmin("a@x.io", "A", "B")).Token);

        jwt.TryGetClaim(StockPilotClaims.TenantId, out _).ShouldBeFalse();
    }

    [Fact]
    public void Signing_key_shorter_than_256_bits_fails_validation()
    {
        var options = new JwtOptions { SigningKey = "too-short" };
        var results = options.Validate(new System.ComponentModel.DataAnnotations.ValidationContext(options));
        results.ShouldNotBeEmpty();
    }
}
