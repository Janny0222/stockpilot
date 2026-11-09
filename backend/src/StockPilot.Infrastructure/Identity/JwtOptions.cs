using System.ComponentModel.DataAnnotations;
using System.Text;

namespace StockPilot.Infrastructure.Identity;

/// <summary>Bound from "Jwt". The signing key MUST come from an environment variable / user-secrets.</summary>
public sealed class JwtOptions : IValidatableObject
{
    public const string SectionName = "Jwt";

    /// <summary>A key that ships in appsettings.Development.json; refused outside Development.</summary>
    public const string DevelopmentKeyMarker = "DEV-ONLY";

    [Required] public string Issuer { get; set; } = "stockpilot-api";
    [Required] public string Audience { get; set; } = "stockpilot-app";
    [Required] public string SigningKey { get; set; } = string.Empty;

    /// <summary>The same key object for issuing (JwtAccessTokenService) and validating (JwtBearer).</summary>
    public Microsoft.IdentityModel.Tokens.SymmetricSecurityKey CreateSigningKey() => new(Encoding.UTF8.GetBytes(SigningKey));

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        // HS256 needs at least 256 bits of key material.
        if (Encoding.UTF8.GetByteCount(SigningKey) < 32)
        {
            yield return new ValidationResult("Jwt:SigningKey must be at least 32 bytes.", [nameof(SigningKey)]);
        }
    }
}
