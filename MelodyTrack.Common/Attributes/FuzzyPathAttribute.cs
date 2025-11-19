namespace MelodyTrack.Common.Attributes;

[AttributeUsage(AttributeTargets.Property)]
public sealed class FuzzyPathAttribute : Attribute
{

    public FuzzyPathAttribute(Type rootEntityType, params string[] pathParts)
    {
        EntityType = rootEntityType ?? throw new ArgumentNullException(nameof(rootEntityType));
        Path = pathParts.Length > 0
            ? string.Join('.', pathParts)
            : throw new ArgumentException("At least one path part is required.", nameof(pathParts));
    }

    /// <summary>
    ///     Entity type
    /// </summary>
    public Type EntityType { get; set; }

    /// <summary>
    ///     Path on the entity
    /// </summary>
    public string Path { get; }
}