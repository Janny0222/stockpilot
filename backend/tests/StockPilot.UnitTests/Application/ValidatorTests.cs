using StockPilot.Application.Features.Auth;

namespace StockPilot.UnitTests.Application;

public sealed class RegisterCompanyValidatorTests
{
    private readonly RegisterCompanyValidator _validator = new();

    private static RegisterCompanyCommand Valid() =>
        new("Acme Trading", "Maria", "Santos", "maria@acme.test", "Str0ngPass!", "PHP", "Asia/Manila");

    [Fact]
    public void Accepts_a_valid_registration()
    {
        _validator.Validate(Valid()).IsValid.ShouldBeTrue();
    }

    [Theory]
    [InlineData("short1A")]       // too short
    [InlineData("alllowercase1")] // no uppercase
    [InlineData("ALLUPPERCASE1")] // no lowercase
    [InlineData("NoDigitsHere")]  // no digit
    public void Rejects_weak_passwords(string password)
    {
        var result = _validator.Validate(Valid() with { Password = password });

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == nameof(RegisterCompanyCommand.Password));
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("")]
    public void Rejects_invalid_emails(string email)
    {
        _validator.Validate(Valid() with { Email = email }).IsValid.ShouldBeFalse();
    }

    [Theory]
    [InlineData("PH")]
    [InlineData("PESO")]
    [InlineData("12A")]
    public void Rejects_invalid_currency_codes(string currency)
    {
        _validator.Validate(Valid() with { Currency = currency }).IsValid.ShouldBeFalse();
    }

    [Theory]
    [InlineData("Asia/Manila", true)]
    [InlineData("UTC", true)]
    [InlineData("America/Argentina/Buenos_Aires", true)]
    [InlineData("Robert'); DROP TABLE tenants;--", false)]
    public void Validates_time_zone_shape(string zone, bool valid)
    {
        _validator.Validate(Valid() with { TimeZone = zone }).IsValid.ShouldBe(valid);
    }
}

public sealed class ResetPasswordValidatorTests
{
    [Fact]
    public void Requires_token_and_strong_password()
    {
        var result = new ResetPasswordValidator().Validate(new ResetPasswordCommand("", "weak"));

        result.Errors.Select(e => e.PropertyName).Distinct()
            .ShouldBe([nameof(ResetPasswordCommand.Token), nameof(ResetPasswordCommand.NewPassword)], ignoreOrder: true);
    }
}
