namespace MelodyTrack.Backend.Api.Tasks.Responses;

public class GetRecurringTaskRulesResponse
{
    public required List<RecurringTaskRuleDto> Rules { get; set; }
}
