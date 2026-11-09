using System.Collections.Immutable;
using CSharpFunctionalExtensions;

namespace MelodyTrack.Rewrite.Domain.Rules;

public static class SchoolPublicContentRules
{
    public static Result<SchoolPublicContent, ErrorList> Create(string? name, string? description, string? callNumber, ExternalLink messenger)
    {
        var title = Validation.Required("name", name);
        var text = Validation.Required("description", description);
        var phone = Validation.Required("callNumber", callNumber);
        var validation = Validation.Collect(Validation.Check(title), Validation.Check(text), Validation.Check(phone));
        return validation.IsFailure ? validation.Error : new SchoolPublicContent(title.Value, text.Value, phone.Value, messenger);
    }
}

public static class TeacherPublicProfileRules
{
    public static Result<TeacherPublicProfile, ErrorList> Create(StaffId staffId, string? name, ExternalLink? photo, string? description, string? experience, IEnumerable<string> directions)
    {

        var title = Validation.Required("name", name);
        var validation = Validation.Collect(Validation.Identifier(staffId.Value, "staffId"), Validation.Check(title));
        if (validation.IsFailure) return validation.Error;
        return new TeacherPublicProfile(staffId, title.Value, photo, description?.Trim() ?? "", experience?.Trim() ?? "",
            directions.Where(item => !string.IsNullOrWhiteSpace(item)).Select(item => item.Trim()).ToImmutableArray());
    }

    public static bool IsListed(this TeacherPublicProfile model, Staff staff) => staff.Id == model.StaffId && staff.ParticipatesInScheduling();
}

public static class PublicServiceRules
{
    public static PublicService? At(Service service, DateTimeOffset now) => service.IsPublic && !service.IsArchived && service.PublicPrice is { } price
        ? new(service.Id, service.PublicName, service.Duration, price) : null;
}
