using System.Globalization;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Utils;
using Microsoft.EntityFrameworkCore;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace MelodyTrack.Backend.Services.RecurringTasks;

public interface ITeacherScheduleImageGenerator
{
    Task<byte[]?> GenerateAsync(Ulid teacherId, DateOnly businessDate, string timezone, CancellationToken ct);
}

public class TeacherScheduleImageGenerator(AppDbContext db) : ITeacherScheduleImageGenerator
{
    private static readonly Color BackgroundColor = Color.ParseHex("#f4e8c8");
    private static readonly Color PanelColor = Color.ParseHex("#f8edcf");
    private static readonly Color ElevatedPanelColor = Color.ParseHex("#fbf3dc");
    private static readonly Color AccentColor = Color.ParseHex("#8b6226");
    private static readonly Color TextColor = Color.ParseHex("#33271a");
    private static readonly Color SecondaryTextColor = Color.ParseHex("#746550");
    private static readonly Color BorderSecondaryColor = Color.ParseHex("#e8dcc2");
    private static readonly Color FillAlterColor = Color.ParseHex("#efe1be");
    private static readonly Color TagBackgroundColor = Color.ParseHex("#efe1be");
    private static readonly FontFamily FallbackFontFamily = SystemFonts.Collection.Families
        .OrderByDescending(family => family.Name.Contains("DejaVu Sans", StringComparison.OrdinalIgnoreCase))
        .First();

    public async Task<byte[]?> GenerateAsync(Ulid teacherId, DateOnly businessDate, string timezone, CancellationToken ct)
    {
        var dayStartUtc = DateTimeUtils.ConvertLocalDateToUtc(businessDate, TimeOnly.MinValue, timezone);
        var nextDayStartUtc = DateTimeUtils.ConvertLocalDateToUtc(businessDate.AddDays(1), TimeOnly.MinValue, timezone);

        var teacher = await db.Users
            .AsNoTracking()
            .Where(user => user.Id == teacherId)
            .Select(user => new
            {
                user.Id,
                user.FirstName,
                user.LastName
            })
            .FirstOrDefaultAsync(ct);

        if (teacher is null)
        {
            return null;
        }

        var appointments = await db.Appointments
            .AsNoTracking()
            .Where(appointment =>
                !appointment.IsDeleted
                && appointment.Provider != null
                && appointment.Provider.Id == teacherId
                && appointment.Status == AppointmentStatus.Planned
                && appointment.StartDate >= dayStartUtc
                && appointment.StartDate < nextDayStartUtc)
            .OrderBy(appointment => appointment.StartDate)
            .Select(appointment => new
            {
                appointment.StartDate,
                appointment.EndDate,
                ClientLastName = appointment.Client.LastName,
                ClientFirstName = appointment.Client.FirstName,
                ServiceName = appointment.Service.Name
            })
            .ToListAsync(ct);

        if (appointments.Count == 0)
        {
            return null;
        }

        var teacherName = string.Join(' ', new[] { teacher.LastName, teacher.FirstName }
            .Where(value => !string.IsNullOrWhiteSpace(value)));

        const int width = 1300;
        const int outerPadding = 32;
        const int cardPadding = 32;
        const int scheduleCardHeight = 140;
        const int scheduleCardGap = 16;
        const int headerHeight = 188;
        var height = outerPadding * 2 + headerHeight + appointments.Count * scheduleCardHeight + Math.Max(0, appointments.Count - 1) * scheduleCardGap;

        using var image = new Image<Rgba32>(width, height, BackgroundColor);
        image.Mutate(context =>
        {
            var headerFont = CreateFont(48, FontStyle.Bold);
            var subheaderFont = CreateFont(32, FontStyle.Bold);
            var rowNameFont = CreateFont(28, FontStyle.Bold);
            var rowServiceFont = CreateFont(22, FontStyle.Regular);
            var tagFont = CreateFont(20, FontStyle.Bold);

            context.Fill(PanelColor, new RectangleF(outerPadding, outerPadding, width - outerPadding * 2, height - outerPadding * 2));
            context.Draw(BorderSecondaryColor, 1, new RectangleF(outerPadding, outerPadding, width - outerPadding * 2, height - outerPadding * 2));

            var cursorY = outerPadding + cardPadding;

            context.DrawText(
                new RichTextOptions(headerFont)
                {
                    Origin = new PointF(outerPadding + cardPadding, cursorY)
                },
                $"Расписание на {businessDate:dd.MM.yyyy} ({businessDate.ToString("dddd", CultureInfo.GetCultureInfoByIetfLanguageTag("ru"))})",
                TextColor);

            cursorY += 64;

            context.DrawText(
                new RichTextOptions(subheaderFont)
                {
                    Origin = new PointF(outerPadding + cardPadding, cursorY)
                },
                teacherName,
                TextColor);

            cursorY += 48;

            for (var index = 0; index < appointments.Count; index++)
            {
                var appointment = appointments[index];
                var localStart = DateTimeUtils.ConvertDateToTimezone(appointment.StartDate, timezone);
                var localEnd = DateTimeUtils.ConvertDateToTimezone(appointment.EndDate, timezone);
                var rowTop = cursorY + index * (scheduleCardHeight + scheduleCardGap);
                var cardX = outerPadding + cardPadding;
                var cardWidth = width - outerPadding * 2 - cardPadding * 2;

                context.Fill(ElevatedPanelColor, new RectangleF(cardX, rowTop, cardWidth, scheduleCardHeight));
                context.Draw(BorderSecondaryColor, 1, new RectangleF(cardX, rowTop, cardWidth, scheduleCardHeight));
                context.Fill(AccentColor, new RectangleF(cardX, rowTop, 4, scheduleCardHeight));

                var tagX = cardX + 24;
                var tagY = rowTop + 18;
                var tagWidth = 175;
                var tagHeight = 30;

                context.Fill(TagBackgroundColor, new RectangleF(tagX, tagY, tagWidth, tagHeight));
                context.Draw(FillAlterColor, 1, new RectangleF(tagX, tagY, tagWidth, tagHeight));

                context.DrawText(
                    new RichTextOptions(tagFont)
                    {
                        Origin = new PointF(tagX + 14, tagY + 6)
                    },
                    $"{localStart:HH:mm} - {localEnd:HH:mm}",
                    AccentColor);

                context.DrawText(
                    new RichTextOptions(rowNameFont)
                    {
                        Origin = new PointF(cardX + 24, rowTop + 64),
                        WrappingLength = cardWidth - 48
                    },
                    $"{appointment.ClientLastName} {appointment.ClientFirstName}",
                    TextColor);

                context.DrawText(
                    new RichTextOptions(rowServiceFont)
                    {
                        Origin = new PointF(cardX + 24, rowTop + 104),
                        WrappingLength = cardWidth - 48
                    },
                    appointment.ServiceName,
                    SecondaryTextColor);
            }
        });

        await using var stream = new MemoryStream();
        await image.SaveAsync(stream, new PngEncoder(), ct);
        return stream.ToArray();
    }

    private static Font CreateFont(float size, FontStyle style)
    {
        return FallbackFontFamily.CreateFont(size, style);
    }
}
