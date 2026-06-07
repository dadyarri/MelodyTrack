using MelodyTrack.Backend.Services;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace MelodyTrack.Backend.Data.ValueConverters;

public sealed class EncryptedStringConverter(IPersonalDataProtector protector) : ValueConverter<string?, string?>(
    value => string.IsNullOrWhiteSpace(value) ? value : protector.Encrypt(value),
    value => string.IsNullOrWhiteSpace(value) ? value : protector.Decrypt(value))
{
}
