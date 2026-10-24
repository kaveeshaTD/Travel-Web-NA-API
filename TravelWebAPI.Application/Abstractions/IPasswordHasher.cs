namespace TravelWebAPI.Application.Abstractions;

public interface IPasswordHasher
{
    /// <summary>Returns a salted, versioned hash. The password itself is never stored.</summary>
    string Hash(string password);

    bool Verify(string password, string hash);

    /// <summary>True when the hash was made with weaker settings than the current ones.</summary>
    bool NeedsRehash(string hash);
}
