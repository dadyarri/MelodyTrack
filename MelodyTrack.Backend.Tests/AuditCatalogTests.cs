using MelodyTrack.Core.Auditing;
using Shouldly;

namespace MelodyTrack.Backend.Tests;

public sealed class AuditCatalogTests
{
    [Fact]
    public void AllCategories_CatalogDefinitions_HaveUniqueCodesAndRussianLabels()
    {
        var codes = AuditCatalog.AllCategories.Select(category => category.Code).ToArray();

        codes.Distinct().Count().ShouldBe(codes.Length);
        AuditCatalog.AllCategories.ShouldAllBe(category => !string.IsNullOrWhiteSpace(category.Label));
        AuditCatalog.AllCategories.ShouldAllBe(category => category.Label != category.Code);
    }

    [Fact]
    public void AllEvents_CatalogDefinitions_HaveUniqueCodesAndRussianLabels()
    {
        var codes = AuditCatalog.AllEvents.Select(auditEvent => auditEvent.Code).ToArray();

        codes.Distinct().Count().ShouldBe(codes.Length);
        AuditCatalog.AllEvents.ShouldAllBe(auditEvent => !string.IsNullOrWhiteSpace(auditEvent.Label));
        AuditCatalog.AllEvents.ShouldAllBe(auditEvent => auditEvent.Label != auditEvent.Code);
        AuditCatalog.AllEvents.ShouldAllBe(auditEvent => AuditCatalog.AllCategories.Contains(auditEvent.Category));
    }

    [Fact]
    public void FindActionCodes_RussianLabelSearch_ReturnsStableCode()
    {
        var matches = AuditCatalog.FindActionCodes("восстановление пароля");

        matches.ShouldContain(AuditCatalog.Events.PasswordResetLinkCreated.Code);
    }
}
