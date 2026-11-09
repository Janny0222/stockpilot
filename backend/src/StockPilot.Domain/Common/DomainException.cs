namespace StockPilot.Domain.Common;

/// <summary>
/// A business rule was violated (e.g. "insufficient stock", "cannot approve a draft").
/// <see cref="Code"/> is a stable, machine-readable identifier the frontend can switch on;
/// the message is for humans. The API maps this to HTTP 422.
/// </summary>
public class DomainException : Exception
{
    public DomainException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    public DomainException() : this("DOMAIN_ERROR", "A business rule was violated.") { }

    public DomainException(string message) : this("DOMAIN_ERROR", message) { }

    public DomainException(string message, Exception innerException)
        : base(message, innerException)
    {
        Code = "DOMAIN_ERROR";
    }

    public string Code { get; }
}
