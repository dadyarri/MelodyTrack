using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace MelodyTrack.Backend.Data.ValueConverters;

public class UlidToBytesConverter : ValueConverter<Ulid, byte[]>
{
    private static readonly ConverterMappingHints DefaultHints = new(size: 16);

    public UlidToBytesConverter() : this(null)
    {
    }

    public UlidToBytesConverter(ConverterMappingHints? mappingHints)
        : base(
            x => x.ToByteArray(),
            x => new Ulid(x),
            DefaultHints.With(mappingHints))
    {
    }
}