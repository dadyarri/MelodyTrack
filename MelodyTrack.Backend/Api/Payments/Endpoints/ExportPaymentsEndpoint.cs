using ClosedXML.Excel;
using FastEndpoints;
using MelodyTrack.Backend.Api.Payments.Requests;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Extensions;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Payments.Endpoints;

public class ExportPaymentsEndpoint(AppDbContext db) : Ep.Req<GetPaymentsPaginatedRequest>.Res<Results<FileContentHttpResult, UnauthorizedHttpResult>>
{
    private const string ExcelContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public override void Configure()
    {
        Get("/payments/export");
    }

    public override async Task<Results<FileContentHttpResult, UnauthorizedHttpResult>> ExecuteAsync(GetPaymentsPaginatedRequest req, CancellationToken ct)
    {
        var paymentsQuery = db.Payments
            .AsNoTracking()
            .Include(e => e.Client)
            .Include(e => e.Service)
            .ApplyFuzzySearchFilters(req)
            .ApplyDateRangeFilter(e => e.Date, req.Start, req.End);

        if (!string.IsNullOrWhiteSpace(req.Search))
        {
            var search = req.Search.Trim().ToLower();
            var pattern = $"%{search}%";

            paymentsQuery = paymentsQuery.Where(e =>
                EF.Functions.ILike(e.Description, pattern)
                || EF.Functions.ILike((e.Client.LastName + " " + e.Client.FirstName + " " + (e.Client.Patronymic ?? "")).Trim(), pattern)
                || (e.Service != null && EF.Functions.ILike(e.Service.Name, pattern)));
        }

        if (req.ClientId.HasValue)
        {
            paymentsQuery = paymentsQuery.Where(e => e.Client.Id == req.ClientId.Value);
        }

        if (req.ServiceId.HasValue)
        {
            paymentsQuery = paymentsQuery.Where(e => e.Service != null && e.Service.Id == req.ServiceId.Value);
        }

        var payments = await paymentsQuery
            .OrderByDescending(e => e.Date)
            .ThenBy(e => e.Client.LastName)
            .ThenBy(e => e.Client.FirstName)
            .ToListAsync(ct);

        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Payments");

        sheet.Cell(1, 1).Value = "Дата";
        sheet.Cell(1, 2).Value = "Клиент";
        sheet.Cell(1, 3).Value = "Услуга";
        sheet.Cell(1, 4).Value = "Сумма";
        sheet.Cell(1, 5).Value = "Описание";

        for (var index = 0; index < payments.Count; index++)
        {
            var row = index + 2;
            var payment = payments[index];

            sheet.Cell(row, 1).Value = payment.Date;
            sheet.Cell(row, 1).Style.DateFormat.Format = "dd.MM.yyyy HH:mm";
            sheet.Cell(row, 2).Value = $"{payment.Client.LastName} {payment.Client.FirstName} {payment.Client.Patronymic}".Trim();
            sheet.Cell(row, 3).Value = payment.Service?.Name;
            sheet.Cell(row, 4).Value = payment.Amount;
            sheet.Cell(row, 4).Style.NumberFormat.Format = "#,##0.00";
            sheet.Cell(row, 5).Value = payment.Description;
        }

        sheet.Row(1).Style.Font.Bold = true;
        sheet.Columns().AdjustToContents();
        sheet.SheetView.FreezeRows(1);

        await using var stream = new MemoryStream();
        workbook.SaveAs(stream);

        var fileName = $"payments_{DateTime.UtcNow:yyyyMMdd_HHmmss}.xlsx";
        return TypedResults.File(stream.ToArray(), ExcelContentType, fileName);
    }
}
