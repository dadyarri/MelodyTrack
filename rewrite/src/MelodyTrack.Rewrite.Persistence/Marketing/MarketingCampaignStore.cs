using CSharpFunctionalExtensions;
using MelodyTrack.Rewrite.Application;
using MelodyTrack.Rewrite.Domain;
using MelodyTrack.Rewrite.Domain.Rules;

namespace MelodyTrack.Rewrite.Persistence.Marketing;

/// <summary>Сохраняет исходные коды и обеспечивает уникальность их нормализованных ключей.</summary>
public sealed class MarketingCampaignStore(Database database) : IMarketingCampaignStore
{
    private const string Select = """
        SELECT id AS "Id", code AS "Code", normalized_code AS "NormalizedCode", activation_starts_at AS "StartsAt", activation_ends_at AS "EndsAt",
               discount_kind::text AS "DiscountKind", discount_value AS "DiscountValue", terms_kind::text AS "TermsKind",
               lesson_count AS "LessonCount", period_ticks AS "PeriodTicks",
               returning_gap_ticks AS "ReturningGapTicks", is_stopped AS "IsStopped"
        FROM public.marketing_campaigns
        """;

    /// <summary>Читает кампанию без замены исторических условий современной механикой.</summary>
    public async Task<Result<MarketingCampaign?, ErrorList>> FindAsync(MarketingCampaignId id, CancellationToken cancellationToken)
    {
        var row = await database.Query(Select + " WHERE id = @id").WithParameter("id", id.Value)
            .GetSingleOrDefaultAsync<CampaignRow>(cancellationToken);
        if (row is null) return Result.Success<MarketingCampaign?, ErrorList>(null);
        var restored = Restore(row);
        return restored.IsFailure ? restored.Error : Result.Success<MarketingCampaign?, ErrorList>(restored.Value);
    }

    /// <summary>Читает коды всех сохранённых кампаний, включая остановленные и истёкшие.</summary>
    public Task<IReadOnlyList<string>> GetCodesAsync(CancellationToken cancellationToken) =>
        database.Query("SELECT code FROM public.marketing_campaigns ORDER BY code").GetAllAsync<string>(cancellationToken);

    /// <summary>Атомарно вставляет кампанию или возвращает тот же неизменный факт по его ID.</summary>
    public async Task<Result<MarketingCampaign, ErrorList>> InsertAsync(MarketingCampaign campaign, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var row = Capture(campaign);
        if (row.StartsAt >= row.EndsAt) return ErrorList.Single(new ModelError.InvalidValue(), "activationPeriod");
        await using var transaction = await database.BeginTransactionAsync(cancellationToken);
        var inserted = await transaction.Query("""
            INSERT INTO public.marketing_campaigns(id, code, normalized_code, activation_starts_at, activation_ends_at,
                discount_kind, discount_value, terms_kind, lesson_count, period_ticks, returning_gap_ticks, is_stopped)
            VALUES (@id, @code, @normalizedCode, @startsAt, @endsAt, @discountKind::public.marketing_discount_kind, @discountValue,
                @termsKind::public.marketing_terms_kind, @lessonCount, @periodTicks, @returningGapTicks, @isStopped)
            ON CONFLICT DO NOTHING RETURNING id
            """).WithParameter("id", row.Id).WithParameter("code", row.Code).WithParameter("normalizedCode", row.NormalizedCode)
            .WithParameter("startsAt", row.StartsAt).WithParameter("endsAt", row.EndsAt)
            .WithParameter("discountKind", row.DiscountKind).WithParameter("discountValue", row.DiscountValue)
            .WithParameter("termsKind", row.TermsKind).WithParameter("lessonCount", row.LessonCount)
            .WithParameter("periodTicks", row.PeriodTicks)
            .WithParameter("returningGapTicks", row.ReturningGapTicks).WithParameter("isStopped", row.IsStopped)
            .GetSingleOrDefaultAsync<Guid?>(cancellationToken);
        if (inserted is null)
        {
            var previous = await transaction.Query(Select + " WHERE id = @id").WithParameter("id", row.Id)
                .GetSingleOrDefaultAsync<CampaignRow>(cancellationToken);
            if (previous is null) return ErrorList.Single(new ModelError.Duplicate(), "code");
            if (!ValidKey(previous)) return ErrorList.Single(new ModelError.InvalidState(), "code");
            if (!SameFact(row, previous)) return ErrorList.Single(new ModelError.Duplicate(), "id");
            return Restore(previous);
        }
        await transaction.CommitAsync(cancellationToken);
        return Restore(row);
    }

    private static DateTime StorageTime(DateTimeOffset value) =>
        new(value.UtcTicks - value.UtcTicks % 10, DateTimeKind.Utc);

    private static CampaignRow Capture(MarketingCampaign campaign) => new()
    {
        Id = campaign.Id.Value,
        Code = campaign.Code,
        NormalizedCode = MarketingCodeRules.Normalize(campaign.Code),
        StartsAt = StorageTime(campaign.ActivationPeriod.StartsAt),
        EndsAt = StorageTime(campaign.ActivationPeriod.EndsAt),
        DiscountKind = campaign.Discount switch { Discount.Percentage => "percentage", Discount.Fixed => "fixed" },
        DiscountValue = campaign.Discount switch { Discount.Percentage percentage => percentage.Value, Discount.Fixed fixedDiscount => fixedDiscount.Amount.Amount },
        TermsKind = campaign.Terms switch
        {
            DiscountTerms.Payment => "payment", DiscountTerms.Lessons => "lessons",
            DiscountTerms.Period => "period"
        },
        LessonCount = (campaign.Terms as DiscountTerms.Lessons)?.Count,
        PeriodTicks = (campaign.Terms as DiscountTerms.Period)?.Duration.Ticks,
        ReturningGapTicks = campaign.ReturningClientGap?.Ticks,
        IsStopped = campaign.IsStopped
    };

    private static bool SameFact(CampaignRow expected, CampaignRow stored) =>
        expected.Id == stored.Id && string.Equals(expected.NormalizedCode, stored.NormalizedCode, StringComparison.Ordinal)
        && expected.StartsAt == stored.StartsAt && expected.EndsAt == stored.EndsAt
        && expected.DiscountKind == stored.DiscountKind && expected.DiscountValue == stored.DiscountValue
        && expected.TermsKind == stored.TermsKind && expected.LessonCount == stored.LessonCount
        && expected.PeriodTicks == stored.PeriodTicks && expected.ReturningGapTicks == stored.ReturningGapTicks;

    private static Result<MarketingCampaign, ErrorList> Restore(CampaignRow row)
    {
        if (!ValidKey(row)) return ErrorList.Single(new ModelError.InvalidState(), "code");
        var period = TimeInterval.Create(new DateTimeOffset(row.StartsAt, TimeSpan.Zero), new DateTimeOffset(row.EndsAt, TimeSpan.Zero));
        if (period.IsFailure) return period.Error;
        Discount discount;
        if (row.DiscountKind == "percentage")
        {
            var value = Discount.Percentage.Create(row.DiscountValue);
            if (value.IsFailure) return value.Error;
            discount = value.Value;
        }
        else if (row.DiscountKind == "fixed")
        {
            var value = Money.Create(row.DiscountValue);
            if (value.IsFailure) return value.Error;
            discount = new Discount.Fixed(value.Value);
        }
        else return ErrorList.Single(new ModelError.InvalidValue(), "discount");

        DiscountTerms terms;
        switch (row.TermsKind)
        {
            case "payment": terms = new DiscountTerms.Payment(); break;
            case "lessons" when row.LessonCount is { } count:
            {
                var value = DiscountTerms.Lessons.Create(count);
                if (value.IsFailure) return value.Error;
                terms = value.Value;
                break;
            }
            case "period" when row.PeriodTicks is { } ticks:
            {
                var value = DiscountTerms.Period.Create(TimeSpan.FromTicks(ticks));
                if (value.IsFailure) return value.Error;
                terms = value.Value;
                break;
            }
            default: return ErrorList.Single(new ModelError.InvalidValue(), "terms");
        }
        var campaign = MarketingCampaignRules.Restore(new(row.Id), row.Code, period.Value, discount, terms,
            row.ReturningGapTicks is { } gap ? TimeSpan.FromTicks(gap) : null);
        if (campaign.IsFailure) return campaign.Error;
        return row.IsStopped ? campaign.Value.Stop() : campaign;
    }

    private static bool ValidKey(CampaignRow row) =>
        string.Equals(row.NormalizedCode, MarketingCodeRules.Normalize(row.Code), StringComparison.Ordinal);

    private sealed class CampaignRow
    {
        public Guid Id { get; init; }
        public string Code { get; init; } = "";
        public string NormalizedCode { get; init; } = "";
        public DateTime StartsAt { get; init; }
        public DateTime EndsAt { get; init; }
        public string DiscountKind { get; init; } = "";
        public decimal DiscountValue { get; init; }
        public string TermsKind { get; init; } = "";
        public int? LessonCount { get; init; }
        public long? PeriodTicks { get; init; }
        public long? ReturningGapTicks { get; init; }
        public bool IsStopped { get; init; }
    }
}
