using FluentValidation;

namespace StockPilot.Application.Common.Validation;

/// <summary>Reusable rules. The frontend's Zod schemas mirror these, but the server is the authority.</summary>
public static class ValidationRules
{
    public const int PasswordMinLength = 8;
    public const int PasswordMaxLength = 128;

    public static IRuleBuilderOptions<T, string> StrongPassword<T>(this IRuleBuilder<T, string> rule) =>
        rule
            .NotEmpty().WithMessage("Password is required.")
            .MinimumLength(PasswordMinLength).WithMessage($"Password must be at least {PasswordMinLength} characters.")
            .MaximumLength(PasswordMaxLength).WithMessage($"Password must be at most {PasswordMaxLength} characters.")
            .Matches("[A-Z]").WithMessage("Password must contain an uppercase letter.")
            .Matches("[a-z]").WithMessage("Password must contain a lowercase letter.")
            .Matches("[0-9]").WithMessage("Password must contain a digit.");

    public static IRuleBuilderOptions<T, string> ValidEmail<T>(this IRuleBuilder<T, string> rule) =>
        rule
            .NotEmpty().WithMessage("Email is required.")
            .MaximumLength(Domain.Identity.User.EmailMaxLength)
            .EmailAddress().WithMessage("Email is not valid.");

    public static IRuleBuilderOptions<T, string> PersonName<T>(this IRuleBuilder<T, string> rule, string label) =>
        rule
            .NotEmpty().WithMessage($"{label} is required.")
            .MaximumLength(Domain.Identity.User.NameMaxLength);
}
