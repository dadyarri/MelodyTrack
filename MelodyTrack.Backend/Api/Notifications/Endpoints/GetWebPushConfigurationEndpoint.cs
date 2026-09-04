using MelodyTrack.Backend.Api.Auth;
using MelodyTrack.Backend.Api.Notifications.Responses;
using MelodyTrack.Core.Configuration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

namespace MelodyTrack.Backend.Api.Notifications.Endpoints;

[ApiEndpoint(ApiMethod.Get, "/notifications/push/configuration")]
public sealed class GetWebPushConfigurationEndpoint
{
    [Authorize(Policy = AuthorizationPolicies.StaffOrClientPortal)]
    public static Task<Ok<WebPushConfigurationResponse>> HandleAsync(
        IOptions<WebPushOptions> options,
        CancellationToken ct)
    {
        var value = options.Value;
        return Task.FromResult(TypedResults.Ok(new WebPushConfigurationResponse
        {
            Enabled = value.Enabled,
            PublicKey = value.Enabled ? value.PublicKey : null
        }));
    }
}
