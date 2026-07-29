using FastEndpoints;
using FluentValidation;

namespace MelodyTrack.Backend.Api.Releases.Requests;

public sealed class GetReleasesValidator : Validator<GetReleasesRequest>
{
    public GetReleasesValidator()
    {
        RuleFor(request => request.Page).GreaterThan(0);
        RuleFor(request => request.PageSize).InclusiveBetween(1, 50);
    }
}
