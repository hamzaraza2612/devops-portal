namespace DevOpsPortal.Application.Dtos.Auth;

public record LoginRequest(string Username, string Password);

public record LoginResponse(string Token, DateTimeOffset ExpiresAt, Users.UserDto User);
