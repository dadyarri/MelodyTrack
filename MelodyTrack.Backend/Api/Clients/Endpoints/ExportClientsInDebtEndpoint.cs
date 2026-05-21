using ClosedXML.Excel;
using FastEndpoints;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Clients.Endpoints;

public class ExportClientsInDebtEndpoint(AppDbContext db)
    : Ep.NoReq.Res<Results<FileContentHttpResult, UnauthorizedHttpResult>>
{
    private const string ExcelContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public override void Configure()
    {
        Get("/clients/inDebt/export");
    }

    public override async Task<Results<FileContentHttpResult, UnauthorizedHttpResult>> ExecuteAsync(CancellationToken ct)
    {
        var clients = await db.Clients
            .AsNoTracking()
            .OrderBy(e => e.LastName)
            .ThenBy(e => e.FirstName)
            .Include(e => e.Contacts)
            .ToListAsync(ct);

        var totalPaymentsByClient = await db.Payments
            .AsNoTracking()
            .GroupBy(e => e.Client.Id)
            .Select(e => new
            {
                ClientId = e.Key,
                TotalPayments = e.Sum(item => item.Amount)
            })
            .ToDictionaryAsync(e => e.ClientId, e => e.TotalPayments, ct);

        var totalServiceCostByClient = await db.Appointments
            .AsNoTracking()
            .Where(e => (e.Status == AppointmentStatus.Completed || e.Status == AppointmentStatus.Burned) && !e.IsDeleted)
            .Join(db.ServicePriceHistory,
                appointment => appointment.Service.Id,
                price => price.Service.Id,
                (appointment, price) => new
                {
                    ClientId = appointment.Client.Id,
                    price.Price
                })
            .GroupBy(e => e.ClientId)
            .Select(e => new
            {
                ClientId = e.Key,
                TotalCost = e.Sum(item => item.Price)
            })
            .ToDictionaryAsync(e => e.ClientId, e => e.TotalCost, ct);

        var debtors = clients
            .Select(client => new
            {
                Client = client,
                Balance = totalPaymentsByClient.GetValueOrDefault(client.Id) - totalServiceCostByClient.GetValueOrDefault(client.Id)
            })
            .Where(item => item.Balance < 0)
            .OrderBy(item => item.Balance)
            .ToList();

        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Debtors");

        sheet.Cell(1, 1).Value = "Фамилия";
        sheet.Cell(1, 2).Value = "Имя";
        sheet.Cell(1, 3).Value = "Отчество";
        sheet.Cell(1, 4).Value = "Телефон";
        sheet.Cell(1, 5).Value = "Telegram";
        sheet.Cell(1, 6).Value = "VK";
        sheet.Cell(1, 7).Value = "Баланс";

        for (var index = 0; index < debtors.Count; index++)
        {
            var row = index + 2;
            var debtor = debtors[index];

            sheet.Cell(row, 1).Value = debtor.Client.LastName;
            sheet.Cell(row, 2).Value = debtor.Client.FirstName;
            sheet.Cell(row, 3).Value = debtor.Client.Patronymic;
            sheet.Cell(row, 4).Value = debtor.Client.Contacts.Phone;
            sheet.Cell(row, 5).Value = debtor.Client.Contacts.Telegram;
            sheet.Cell(row, 6).Value = debtor.Client.Contacts.Vk;
            sheet.Cell(row, 7).Value = debtor.Balance;
            sheet.Cell(row, 7).Style.NumberFormat.Format = "#,##0.00";
        }

        sheet.Row(1).Style.Font.Bold = true;
        sheet.Columns().AdjustToContents();
        sheet.SheetView.FreezeRows(1);

        await using var stream = new MemoryStream();
        workbook.SaveAs(stream);

        var fileName = $"debtors_{DateTime.UtcNow:yyyyMMdd_HHmmss}.xlsx";
        return TypedResults.File(stream.ToArray(), ExcelContentType, fileName);
    }
}
