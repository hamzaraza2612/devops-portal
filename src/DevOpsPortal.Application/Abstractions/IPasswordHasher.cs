namespace DevOpsPortal.Application.Abstractions;

public interface IPasswordHasher
{
    string Hash(string password);

    /// <summary>Verifies a password against a stored hash. Never logs or returns the plaintext.</summary>
    bool Verify(string hash, string providedPassword);
}
