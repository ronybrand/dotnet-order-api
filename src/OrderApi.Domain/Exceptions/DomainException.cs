namespace OrderApi.Domain.Exceptions;

public abstract class DomainException(string message, string code, IDictionary<string, object?>? parameters = null)
    : Exception(message)
{
    public string Code { get; } = code;
    public IDictionary<string, object?> Parameters { get; } = parameters ?? new Dictionary<string, object?>();
}

public sealed class NotFoundDomainException(string message, string code, IDictionary<string, object?>? parameters = null)
    : DomainException(message, code, parameters);

public sealed class ValidationDomainException(string message, string code, IDictionary<string, object?>? parameters = null)
    : DomainException(message, code, parameters);

public sealed class ConflictDomainException(string message, string code, IDictionary<string, object?>? parameters = null)
    : DomainException(message, code, parameters);
