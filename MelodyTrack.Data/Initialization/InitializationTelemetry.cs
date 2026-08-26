using System.Diagnostics;

namespace MelodyTrack.Data.Initialization;

internal static class InitializationTelemetry
{
    internal const string ActivitySourceName = "MelodyTrack.Init";

    private static readonly ActivitySource ActivitySource = new(ActivitySourceName);

    public static Activity? StartActivity(string name) => ActivitySource.StartActivity(name);

    public static async Task RunStepAsync(string name, Func<Task> operation)
    {
        using var activity = StartActivity(name);

        try
        {
            await operation();
            activity?.SetStatus(ActivityStatusCode.Ok);
        }
        catch (Exception exception)
        {
            MarkFailed(activity, exception);
            throw;
        }
    }

    public static async Task<T> RunStepAsync<T>(string name, Func<Task<T>> operation)
    {
        using var activity = StartActivity(name);

        try
        {
            var result = await operation();
            activity?.SetStatus(ActivityStatusCode.Ok);
            return result;
        }
        catch (Exception exception)
        {
            MarkFailed(activity, exception);
            throw;
        }
    }

    public static void MarkFailed(Activity? activity, Exception exception)
    {
        activity?.SetTag("error.type", exception.GetType().FullName);
        activity?.SetStatus(ActivityStatusCode.Error);
    }
}
