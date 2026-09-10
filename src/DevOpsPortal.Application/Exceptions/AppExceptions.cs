namespace DevOpsPortal.Application.Exceptions;

public class NotFoundException(string entity, object key) : Exception($"{entity} '{key}' was not found.");

public class ConflictException(string message) : Exception(message);

public class ValidationException(string message) : Exception(message);

public class AuthenticationFailedException(string message = "Invalid username or password.") : Exception(message);
