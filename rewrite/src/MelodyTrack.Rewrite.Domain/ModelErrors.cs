namespace MelodyTrack.Rewrite.Domain;

public closed record ModelError(string Code) : DomainError(Code)
{
    public sealed record InvalidValue() : ModelError("model.invalid_value");
    public sealed record InvalidState() : ModelError("model.invalid_state");
    public sealed record Forbidden() : ModelError("model.forbidden");
    public sealed record Duplicate() : ModelError("model.duplicate");
    public sealed record WrongOwner() : ModelError("model.wrong_owner");
    public sealed record NotFound() : ModelError("model.not_found");
    public sealed record DecisionRequired() : ModelError("model.decision_required");
}

public closed record FinanceError(string Code) : DomainError(Code)
{
    public sealed record AlreadySettled() : FinanceError("finance.already_settled");
    public sealed record InconsistentSettlement() : FinanceError("finance.inconsistent_settlement");
    public sealed record SubscriptionUsed() : FinanceError("finance.subscription_used");
    public sealed record NoCoverage() : FinanceError("finance.no_coverage");
    public sealed record AmbiguousCoverage() : FinanceError("finance.ambiguous_coverage");
    public sealed record AmbiguousDiscount() : FinanceError("finance.ambiguous_discount");
    public sealed record MissingPrice() : FinanceError("finance.missing_price");
    public sealed record CampaignUnavailable() : FinanceError("finance.campaign_unavailable");
    public sealed record DiscountUnavailable() : FinanceError("finance.discount_unavailable");
}

public closed record LearningError(string Code) : DomainError(Code)
{
    public sealed record TopicLocked() : LearningError("learning.topic_locked");
    public sealed record DependentTopicsExist() : LearningError("learning.dependent_topics_exist");
    public sealed record ActiveEnrollmentExists() : LearningError("learning.active_enrollment_exists");
    public sealed record ConfirmRelockingRequired() : LearningError("learning.confirm_relocking_required");
}
