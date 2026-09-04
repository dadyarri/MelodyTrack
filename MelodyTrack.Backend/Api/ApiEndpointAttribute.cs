namespace MelodyTrack.Backend.Api;

public enum ApiMethod
{
    Get,
    Post,
    Put,
    Delete,
    Patch
}

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ApiEndpointAttribute(ApiMethod method, string route) : Attribute
{
    public ApiMethod Method { get; } = method;

    public string Route { get; } = route;
}
