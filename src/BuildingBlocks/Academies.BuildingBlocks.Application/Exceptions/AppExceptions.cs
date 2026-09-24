namespace Academies.BuildingBlocks.Application.Exceptions;

/// <summary>Base for expected failures; the global exception handler maps each one to a status code.</summary>
public abstract class AppException(string message) : Exception(message);

/// <summary>404.</summary>
public sealed class NotFoundException(string entity, object key)
    : AppException($"{entity} '{key}' was not found.");

/// <summary>409: duplicate or conflicting state.</summary>
public sealed class ConflictException(string message) : AppException(message);

/// <summary>422: a business rule rejected the request (e.g. plan limit reached).</summary>
public sealed class BusinessRuleException(string message, string? code = null) : AppException(message)
{
    public string? Code { get; } = code;
}

/// <summary>403: authenticated but not allowed to touch this record.</summary>
public sealed class ForbiddenAccessException(string message = "You do not have access to this resource.")
    : AppException(message);

/// <summary>401: bad credentials or token.</summary>
public sealed class UnauthorizedException(string message = "Invalid credentials.") : AppException(message);
