namespace MelodyTrack.Codegen;

public static class SourceGenerationHelper
{
    public const string Attribute = """
                                    namespace MelodyTrack.Codegen
                                    {
                                        [System.AttributeUsage(System.AttributeTargets.Class)]
                                        public sealed class GenerateQueryExtensionsAttribute : System.Attribute
                                        {
                                            public Type EntityType { get; }
                                            public GenerateQueryExtensionsAttribute(Type entityType) {
                                                EntityType = entityType;
                                            }
                                        }
                                    }
                                    """;
}