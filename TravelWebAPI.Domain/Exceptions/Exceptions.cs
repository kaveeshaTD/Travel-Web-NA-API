namespace TravelWebAPI.Domain.Exceptions;

/// <summary>Base type of every error the application raises on purpose. It carries no HTTP knowledge.</summary>
public abstract class AppException(string message, IReadOnlyDictionary<string, string>? fields = null)
    : Exception(message)
{
    /// <summary>Per-field messages (for validation) or extra detail (for conflicts).</summary>
    public IReadOnlyDictionary<string, string> Fields { get; } = fields ?? new Dictionary<string, string>();
}

/// <summary>The input is invalid. <see cref="AppException.Fields"/> names each invalid field.</summary>
public sealed class ValidationException : AppException
{
    public const string DefaultMessage = "Validation failed";

    public ValidationException(IReadOnlyDictionary<string, string> fields)
        : base(DefaultMessage, fields)
    {
    }

    public ValidationException(string field, string message)
        : base(DefaultMessage, new Dictionary<string, string> { [field] = message })
    {
    }
}

/// <summary>The caller is not signed in or the credentials are wrong.</summary>
public sealed class UnauthorizedException(string message = "Sign in required")
    : AppException(message);

/// <summary>The caller is signed in but not allowed to do this.</summary>
public sealed class ForbiddenException(string message = "You are not allowed to do this")
    : AppException(message);

/// <summary>The caller must wait before trying again (account lockout). Answered as 429 with a Retry-After header.</summary>
public sealed class TooManyRequestsException(string message, TimeSpan retryAfter) : AppException(message)
{
    public TimeSpan RetryAfter { get; } = retryAfter;
}

/// <summary>The requested record does not exist.</summary>
public sealed class NotFoundException(string message = "Not found") : AppException(message)
{
    public NotFoundException(string entity, string id) : this($"{entity} '{id}' was not found")
    {
    }
}

/// <summary>The request clashes with existing data (duplicate value, invalid state change).</summary>
public class ConflictException(string message, IReadOnlyDictionary<string, string>? fields = null)
    : AppException(message, fields);

/// <summary>A delete was refused because other records still use the target.</summary>
public sealed class DependentsExistException : ConflictException
{
    public DependentsExistException(string entity, IEnumerable<string> dependents)
        : this(entity, dependents.ToList())
    {
    }

    private DependentsExistException(string entity, List<string> dependents)
        : base($"This {entity} is still used by: {string.Join(", ", dependents)}")
    {
        Dependents = dependents;
    }

    /// <summary>Names of the records that still use the target. Returned to the caller as <c>fields.dependents</c>.</summary>
    public IReadOnlyList<string> Dependents { get; }
}
