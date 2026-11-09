using MelodyTrack.Rewrite.Application.Operations;
using MelodyTrack.Rewrite.Domain.Rules;
using Xunit;
using static MelodyTrack.Rewrite.Domain.Tests.TestData;

namespace MelodyTrack.Rewrite.Domain.Tests;

public sealed class ExpenseOperationsTests
{
    private static Actor Admin => new Actor.Staff(TestData.StaffId, StaffRole.Administrator);
    private static Actor Super => new Actor.Staff(TestData.StaffId, StaffRole.Superuser);
    private static Actor WithGrant(StaffRole role, StaffPermission permission, PermissionGrant grant) =>
        WithGrants(role, (permission, grant));
    private static Actor WithGrants(StaffRole role, params (StaffPermission Permission, PermissionGrant Grant)[] grants) =>
        new Actor.Staff(TestData.StaffId, role, StaffPermissions.Resolve(role,
            InitialPermissionPolicies.ForRole(role).Grants, grants.ToDictionary(item => item.Permission, item => item.Grant)));

    [Fact]
    public void Edit_AdministratorPersonalDeny_OverridesDefaultCorrectionPermission()
    {
        var expense = Ok(ExpenseRules.Create(new(Guid(100)), "Материалы", Ok(Money.Create(100)), Date(2), null));
        var denied = WithGrant(StaffRole.Administrator, StaffPermission.ExpensesCorrect, PermissionGrant.Denied);

        var result = expense.Edit("Материалы", Ok(Money.Create(200)), Date(2), null, "Исправление", denied, Utc(2, 12));

        AssertError(new ModelError.Forbidden(), result);
    }

    [Fact]
    public void Create_GrantedTeacherTodayScope_UsesMoscowDate()
    {
        var teacher = WithGrant(StaffRole.Teacher, StaffPermission.ExpensesRecord,
            new(true, PermissionDataScope.Available, PermissionTimeScope.Today));
        var now = Utc(1, 21);

        var created = Ok(ExpenseOperations.Create(new(Guid(100)), "Материалы", Ok(Money.Create(123.45m)), Date(2), null, teacher, now));

        Assert.Equal(TestData.StaffId, created.AuthorId);
        Assert.Equal(123.45m, created.After!.Amount.Amount);
        AssertError(new ModelError.Forbidden(), ExpenseOperations.Create(new(Guid(101)), "Материалы", Ok(Money.Create(100)),
            Date(1), null, teacher, now));
    }

    [Fact]
    public void Edit_GrantedTeacher_AllowsCorrectionInGrantedScope()
    {
        var teacher = WithGrant(StaffRole.Teacher, StaffPermission.ExpensesCorrect,
            new(true, PermissionDataScope.Available, PermissionTimeScope.Any));
        var expense = Ok(ExpenseRules.Create(new(Guid(100)), "Материалы", Ok(Money.Create(100)), Date(1), null));

        var changed = Ok(expense.Edit("Материалы", Ok(Money.Create(200)), Date(1), null, null, teacher, Utc(2, 12)));

        Assert.Equal(200m, changed.After!.Amount.Amount);
        Assert.Equal(expense, changed.Before);
    }

    [Fact]
    public void Edit_AdministratorGrantedAnyTime_DoesNotBypassSubjectDayBoundary()
    {
        var admin = WithGrant(StaffRole.Administrator, StaffPermission.ExpensesCorrect,
            new(true, PermissionDataScope.Available, PermissionTimeScope.Any));
        var expense = Ok(ExpenseRules.Create(new(Guid(100)), "Материалы", Ok(Money.Create(100)), Date(1), null));

        AssertError(new ModelError.Forbidden(), expense.Edit("Материалы", Ok(Money.Create(200)), Date(2), null,
            "Исправление", admin, Utc(2, 12)));
    }

    [Fact]
    public void Create_OwnScopeWithoutExpenseOwner_FailsClosed()
    {
        var teacher = WithGrant(StaffRole.Teacher, StaffPermission.ExpensesRecord,
            new(true, PermissionDataScope.Own, PermissionTimeScope.Any));

        var result = ExpenseOperations.Create(new(Guid(100)), "Материалы", Ok(Money.Create(100)), Date(2), null, teacher, Utc(2, 12));

        AssertError(new ModelError.Forbidden(), result);
    }

    [Fact]
    public void List_GrantedTeacherMonthScope_FiltersHistoricalPeriod()
    {
        var teacher = WithGrant(StaffRole.Teacher, StaffPermission.ExpensesRead,
            new(true, PermissionDataScope.Available, PermissionTimeScope.CurrentMonth));
        var old = Ok(ExpenseRules.Create(new(Guid(100)), "Материалы", Ok(Money.Create(100)), new DateOnly(2025, 12, 31), null));
        var current = Ok(ExpenseRules.Create(new(Guid(101)), "Материалы", Ok(Money.Create(200)), Date(2), null));

        var listed = Ok(ExpenseOperations.List([old, current], old.Date, current.Date, null, teacher, Utc(2, 12)));

        Assert.Equal(current, Assert.Single(listed));
        AssertError(new ModelError.Forbidden(), ExpenseOperations.Export([current], teacher, Utc(2, 12)));
    }

    [Fact]
    public void Export_GrantedTeacherTodayScope_RequiresClockAndFiltersRows()
    {
        var grant = new PermissionGrant(true, PermissionDataScope.Available, PermissionTimeScope.Today);
        var teacher = WithGrants(StaffRole.Teacher, (StaffPermission.ExpensesExport, grant), (StaffPermission.ExpensesRead, grant));
        var old = Ok(ExpenseRules.Create(new(Guid(100)), "Материалы", Ok(Money.Create(100)), Date(1), null));
        var current = Ok(ExpenseRules.Create(new(Guid(101)), "Материалы", Ok(Money.Create(200)), Date(2), null));

        AssertError(new ModelError.Forbidden(), ExpenseOperations.Export([old, current], teacher));
        var exported = Ok(ExpenseOperations.Export([old, current], teacher, Utc(1, 21)));

        Assert.Equal(current, Assert.Single(exported));
    }

    [Fact]
    public void Export_ExplicitReadDeny_DoesNotLeakRowsThroughExportGrant()
    {
        var teacher = WithGrants(StaffRole.Teacher,
            (StaffPermission.ExpensesExport, new(true, PermissionDataScope.Available, PermissionTimeScope.Any)),
            (StaffPermission.ExpensesRead, PermissionGrant.Denied));
        var expense = Ok(ExpenseRules.Create(new(Guid(100)), "Материалы", Ok(Money.Create(100)), Date(2), null));

        AssertError(new ModelError.Forbidden(), ExpenseOperations.Export([expense], teacher, Utc(2, 12)));
    }

    [Fact]
    public void Export_BroaderExportGrant_PreservesReadTimeScope()
    {
        var teacher = WithGrants(StaffRole.Teacher,
            (StaffPermission.ExpensesExport, new(true, PermissionDataScope.Available, PermissionTimeScope.Any)),
            (StaffPermission.ExpensesRead, new(true, PermissionDataScope.Available, PermissionTimeScope.Today)));
        var old = Ok(ExpenseRules.Create(new(Guid(100)), "Материалы", Ok(Money.Create(100)), Date(1), null));
        var current = Ok(ExpenseRules.Create(new(Guid(101)), "Материалы", Ok(Money.Create(200)), Date(2), null));

        var exported = Ok(ExpenseOperations.Export([old, current], teacher, Utc(2, 12)));

        Assert.Equal(current, Assert.Single(exported));
    }

    [Fact]
    public void Delete_ExplicitTeacherGrant_AllowsGrantedAction()
    {
        var teacher = WithGrant(StaffRole.Teacher, StaffPermission.ExpensesDelete,
            new(true, PermissionDataScope.Available, PermissionTimeScope.Any));
        var expense = Ok(ExpenseRules.Create(new(Guid(100)), "Материалы", Ok(Money.Create(100)), Date(1), null));

        var deleted = Ok(expense.Delete(null, teacher, Utc(2, 12)));

        Assert.Equal(expense, deleted.Before);
        Assert.Null(deleted.After);
    }

    [Fact]
    public void Delete_AdministratorToday_DeniesAbsentDefaultPermission()
    {
        var expense = Ok(ExpenseRules.Create(new(Guid(100)), "Материалы", Ok(Money.Create(100)), Date(2), null));

        AssertError(new ModelError.Forbidden(), expense.Delete("Удаление", Admin, Utc(2, 12)));
    }

    [Fact]
    public void List_SuperuserPeriod_ReturnsNewestExpensesFirst()
    {
        var old = Ok(ExpenseRules.Create(new(Guid(100)), "Материалы", Ok(Money.Create(100)), Date(1), null));
        var current = Ok(ExpenseRules.Create(new(Guid(101)), "Материалы", Ok(Money.Create(200)), Date(2), null));

        var result = Ok(ExpenseOperations.List([old, current], Date(1), Date(2), null, Super, Utc(2, 12)));

        Assert.Equal(new[] { current, old }, result);
    }

    [Fact]
    public void Create_AdministratorAtMoscowDayBoundary_PreservesKopecks()
    {
        var now = Utc(1, 21);

        var change = Ok(ExpenseOperations.Create(new(Guid(100)), "Материалы", Ok(Money.Create(123.45m)),
            Date(2), null, Admin, now));

        Assert.Equal(123.45m, change.After!.Amount.Amount);
        Assert.Equal(TestData.StaffId, change.AuthorId);
        Assert.Null(change.Before);
        AssertError(new ModelError.Forbidden(), ExpenseOperations.Create(new(Guid(101)), "Материалы",
            Ok(Money.Create(123.45m)), Date(1), null, Admin, now));
    }

    [Fact]
    public void Edit_AdministratorMovesHistoricalExpenseToToday_RejectsChange()
    {
        var expense = Ok(ExpenseRules.Create(new(Guid(100)), "Материалы", Ok(Money.Create(100)), Date(1), null));

        var result = expense.Edit("Материалы", Ok(Money.Create(200)), Date(2), null, "Исправление", Admin, Utc(2, 12));

        AssertError(new ModelError.Forbidden(), result);
    }

    [Fact]
    public void Edit_AdministratorWithoutComment_RequiresExplanation()
    {
        var expense = Ok(ExpenseRules.Create(new(Guid(100)), "Материалы", Ok(Money.Create(100)), Date(2), null));

        AssertError(new CatalogueError.Required("comment"), expense.Edit("Материалы", Ok(Money.Create(200)),
            Date(2), null, " ", Admin, Utc(2, 12)));
        var change = Ok(expense.Edit("Материалы", Ok(Money.Create(200)), Date(2), null, "Исправление", Admin, Utc(2, 12)));
        Assert.Equal(expense, change.Before);
        Assert.Equal(200m, change.After!.Amount.Amount);
    }

    [Fact]
    public void List_Administrator_ExcludesHistoricalExpensesAndCannotExport()
    {
        var old = Ok(ExpenseRules.Create(new(Guid(100)), "Материалы", Ok(Money.Create(100)), Date(1), null));
        var current = Ok(ExpenseRules.Create(new(Guid(101)), "Материалы", Ok(Money.Create(200)), Date(2), null));

        var result = Ok(ExpenseOperations.List([old, current], Date(1), Date(2), "матер", Admin, Utc(2, 12)));

        Assert.Equal(current, Assert.Single(result));
        AssertError(new ModelError.Forbidden(), ExpenseOperations.Export([old, current], Admin));
        Assert.Equal(2, Ok(ExpenseOperations.Export([old, current], Super)).Length);
    }

    [Fact]
    public void Delete_SuperuserHistoricalExpense_PreservesFactInChangeHistory()
    {
        var expense = Ok(ExpenseRules.Create(new(Guid(100)), "Материалы", Ok(Money.Create(100)), Date(1), null));

        var change = Ok(expense.Delete(null, Super, Utc(20, 12)));

        Assert.Equal(expense, change.Before);
        Assert.Null(change.After);
    }
}
