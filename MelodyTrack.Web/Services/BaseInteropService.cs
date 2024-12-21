using MelodyTrack.Web.Exceptions;
using Microsoft.JSInterop;

namespace MelodyTrack.Web.Services;

public abstract class BaseInteropService(IJSRuntime jsRuntime)
{
    // Inheritors must specify the module's path
    protected abstract string ModuleName { get; }

    private IJSObjectReference? _module;

    protected virtual Task ExtendInitializationAsync()
    {
        return Task.CompletedTask; // Default implementation does nothing
    }

    // Initialize the JavaScript module
    public async Task InitializeAsync()
    {
        if (string.IsNullOrWhiteSpace(ModuleName))
        {
            throw new InvalidJavascriptModuleException("Module name cannot be null or empty.");
        }

        _module = await jsRuntime.InvokeAsync<IJSObjectReference>("import", $"./js/{ModuleName}.js");

        InvalidJavascriptModuleException.ThrowIfNull(_module, ModuleName);

        await ExtendInitializationAsync();
    }

    // Retrieve the module or throw an exception if not initialized
    protected IJSObjectReference GetModuleOrThrow()
    {
        InvalidJavascriptModuleException.ThrowIfNull(_module, ModuleName);
        return _module!;
    }
}