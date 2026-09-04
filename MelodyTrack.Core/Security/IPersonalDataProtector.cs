namespace MelodyTrack.Core.Security;

public interface IPersonalDataProtector
{
    string Encrypt(string plaintext);
    string Decrypt(string storedValue);
    string NormalizeEmail(string email);
    string HashEmailBlindIndex(string email);
    bool IsEncrypted(string storedValue);
    bool ShouldReencrypt(string storedValue);
}
