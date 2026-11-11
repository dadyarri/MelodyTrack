using MelodyTrack.Rewrite.Domain;

namespace MelodyTrack.Rewrite.Application;

public sealed record StoredPasskey(string CredentialId, byte[] PublicKey, uint Counter, byte[] UserHandle)
{
    public override string ToString() => nameof(StoredPasskey);
}

/// <summary>Unverified credential selector; ownership and signature must be checked before access is granted.</summary>
public sealed record PasskeyAssertionIdentity(string CredentialId, byte[] UserHandle)
{
    public override string ToString() => nameof(PasskeyAssertionIdentity);
}

public interface ICredentialSecurity
{
    string HashPassword(string password);
    bool VerifyPassword(string hash, string password);
    string CreateAuthenticatorSecret();
    string ProtectAuthenticator(StaffId staffId, string secret);
    string UnprotectAuthenticator(StaffId staffId, string protectedSecret);
    long? VerifyAuthenticator(string secret, string code, DateTimeOffset now);
    string CreatePasskeyRegistration(StaffId staffId, string login, IReadOnlyList<StoredPasskey> existing);
    Task<StoredPasskey?> VerifyPasskeyRegistrationAsync(StaffId staffId, string optionsJson, string responseJson, CancellationToken cancellationToken);
    string CreatePasskeyAssertion(IReadOnlyList<StoredPasskey> credentials);
    PasskeyAssertionIdentity? ReadPasskeyAssertionIdentity(string responseJson);
    Task<uint?> VerifyPasskeyAssertionAsync(StaffId staffId, string optionsJson, string responseJson, StoredPasskey credential, CancellationToken cancellationToken);
}
