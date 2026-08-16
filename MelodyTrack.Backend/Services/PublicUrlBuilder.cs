using MelodyTrack.Core.Configuration;
using Microsoft.Extensions.Options;

namespace MelodyTrack.Backend.Services;

public interface IPublicUrlBuilder
{
    string GetInviteUrl(Ulid code);
    string GetResetPasswordUrl(string token);
    string GetClientPortalAccessUrl(string token);
    string GetCalendarSubscriptionUrl(string token);
}

public sealed class PublicUrlBuilder(IOptions<PublicUrlOptions> options) : IPublicUrlBuilder
{
    public string GetInviteUrl(Ulid code) => BuildAppUrl($"invite/{code}");

    public string GetResetPasswordUrl(string token) => $"{BuildAppUrl("restore")}?code={Uri.EscapeDataString(token)}";

    public string GetClientPortalAccessUrl(string token) => BuildAppUrl($"portal/access/{token}");

    public string GetCalendarSubscriptionUrl(string token) => BuildApiUrl($"calendar-subscriptions/{token}.ics");

    private string BuildAppUrl(string path) => BuildUrl(options.Value.BaseUrl, path);

    private string BuildApiUrl(string path) => BuildUrl(options.Value.BaseUrl, path);

    private static string BuildUrl(string baseUrl, string path) => $"{baseUrl.TrimEnd('/')}/{path}";
}
