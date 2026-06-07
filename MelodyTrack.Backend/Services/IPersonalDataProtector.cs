namespace MelodyTrack.Backend.Services;

public interface IPersonalDataProtector
{
    string Encrypt(string plaintext);
    string Decrypt(string storedValue);
    bool IsEncrypted(string storedValue);
}
