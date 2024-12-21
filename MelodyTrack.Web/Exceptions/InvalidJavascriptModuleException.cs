using Microsoft.JSInterop;

namespace MelodyTrack.Web.Exceptions;

public class InvalidJavascriptModuleException : Exception
{
    public InvalidJavascriptModuleException(string message) : base(message) { }

    public static void ThrowIfNull(IJSObjectReference? module, string moduleName)
    {
        if (module is null)
        {
            throw new InvalidJavascriptModuleException($"The JavaScript module '{moduleName}' is not initialized. Please call InitializeAsync first.");
        }
    }
}
