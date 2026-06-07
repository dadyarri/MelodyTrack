using MelodyTrack.Backend.Services;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace MelodyTrack.Backend.Data.ValueConverters;

public sealed class EncryptedRequiredStringConverter(IPersonalDataProtector protector) : ValueConverter<string, string>(
    value => protector.Encrypt(value),
    value => protector.Decrypt(value));
