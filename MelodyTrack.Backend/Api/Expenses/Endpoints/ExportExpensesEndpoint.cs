using ClosedXML.Excel;
using FastEndpoints;
using MelodyTrack.Backend.Api.Expenses.Requests;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Extensions;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Expenses.Endpoints;

public class ExportExpensesEndpoint(AppDbContext db) : Ep.Req<GetExpensesPaginatedRequest>.Res<Results<FileContentHttpResult, UnauthorizedHttpResult>>
{
    private const string ExcelContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public override void Configure()
    {
        Get("/expenses/export");
    }

    public override async Task<Results<FileContentHttpResult, UnauthorizedHttpResult>> ExecuteAsync(GetExpensesPaginatedRequest req, CancellationToken ct)
    {
        var expensesQuery = db.Expenses
            .AsNoTracking()
            .ApplyDateRangeFilter(e => e.Date, req.Start, req.End);

        if (!string.IsNullOrWhiteSpace(req.Search))
        {
            var pattern = $"%{req.Search.Trim().ToLower()}%";
            expensesQuery = expensesQuery.Where(e => EF.Functions.ILike(e.Description, pattern));
        }

        var expenses = await expensesQuery
            .OrderByDescending(e => e.Date)
            .ToListAsync(ct);

        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Expenses");

        sheet.Cell(1, 1).Value = "Дата";
        sheet.Cell(1, 2).Value = "Описание";
        sheet.Cell(1, 3).Value = "Сумма";

        for (var index = 0; index < expenses.Count; index++)
        {
            var row = index + 2;
            var expense = expenses[index];

            sheet.Cell(row, 1).Value = expense.Date;
            sheet.Cell(row, 1).Style.DateFormat.Format = "dd.MM.yyyy HH:mm";
            sheet.Cell(row, 2).Value = expense.Description;
            sheet.Cell(row, 3).Value = expense.Amount;
            sheet.Cell(row, 3).Style.NumberFormat.Format = "#,##0.00";
        }

        sheet.Row(1).Style.Font.Bold = true;
        sheet.Columns().AdjustToContents();
        sheet.SheetView.FreezeRows(1);

        await using var stream = new MemoryStream();
        workbook.SaveAs(stream);

        var fileName = $"expenses_{DateTime.UtcNow:yyyyMMdd_HHmmss}.xlsx";
        return TypedResults.File(stream.ToArray(), ExcelContentType, fileName);
    }
}
