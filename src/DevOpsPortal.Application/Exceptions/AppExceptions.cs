namespace DevOpsPortal.Application.Exceptions;

public class NotFoundException(string entity, object key) : Exception($"{entity} '{key}' was not found.");

public class ConflictException(string message) : Exception(message);

public class ValidationException(string message) : Exception(message);

public class AuthenticationFailedException(string message = "Invalid username or password.") : Exception(message);

/// <summary>Authenticated, but lacking the specific permission for this action — used
/// where the required permission is data-dependent (e.g. which environment) and so
/// can't be expressed as a single static [RequirePermission] attribute.</summary>
public class ForbiddenException(string message) : Exception(message);

/// <summary>Internal signal only — thrown and caught within IDeploymentExecutor to
/// mark a Deployment Failed with a clear reason. Never surfaces across the HTTP
/// boundary (the executor runs in the background worker, not a request).</summary>
public class DeploymentExecutionException(string message) : Exception(message);
