namespace TechBazar.Application.Common;

public class NotFoundException(string message) : Exception(message);

public class ConflictException(string message) : Exception(message);

/// <summary>Bad credentials / invalid or expired refresh token (maps to HTTP 401).</summary>
public class AuthenticationFailedException(string message) : Exception(message);
