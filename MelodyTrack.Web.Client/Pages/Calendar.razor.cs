namespace MelodyTrack.Web.Client.Pages;

using Components;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Models;
using Radzen;
using Radzen.Blazor;

public partial class Calendar
{
    [Inject] protected IJSRuntime JSRuntime { get; set; }

    [Inject] protected NavigationManager NavigationManager { get; set; }

    [Inject] protected DialogService DialogService { get; set; }

    [Inject] protected TooltipService TooltipService { get; set; }

    [Inject] protected ContextMenuService ContextMenuService { get; set; }

    [Inject] protected NotificationService NotificationService { get; set; }

    RadzenScheduler<Appointment> scheduler;
    Dictionary<DateTime, string> events = new Dictionary<DateTime, string>();

    IList<Appointment> appointments = new List<Appointment>
    {
        new Appointment
        {
            Service = "Вокал (Даниил Голубев)",
            StartTime = DateTime.Now.AddHours(-3),
            EndTime = DateTime.Now.AddHours(-2),
        },
        new Appointment
        {
            Service = "Гитара (Александр Запруднов)",
            StartTime = DateTime.Now.AddHours(-2),
            EndTime = DateTime.Now.AddHours(-1),
        },
        new Appointment
        {
            Service = "Вокал (Даниил Голубев)",
            StartTime = DateTime.Now.AddDays(3),
            EndTime = DateTime.Now.AddDays(3).AddHours(1),
        }
    };

    void OnSlotRender(SchedulerSlotRenderEventArgs args)
    {
        if (args.View.Text == "Month" && args.Start.Date == DateTime.Today)
        {
            args.Attributes["style"] =
                "background: var(--rz-scheduler-highlight-background-color, rgba(255,220,40,.2));";
        }

        // Highlight working hours (10-22)
        if (args.View.Text is "Week" or "Day" && args.Start.Hour is > 10 and < 22 &&
            args.Start.DayOfWeek != DayOfWeek.Monday && args.Start.DayOfWeek != DayOfWeek.Tuesday)
        {
            args.Attributes["style"] =
                "background: var(--rz-scheduler-highlight-background-color, rgba(255,220,40,.2));";
        }
    }

    async Task OnSlotSelect(SchedulerSlotSelectEventArgs args)
    {
        if (args.View.Text != "Year")
        {
            Appointment data = await DialogService.OpenAsync<AddAppointmentDialog>("Добавить приём",
                new Dictionary<string, object> { { "StartTime", args.Start }, { "EndTime", args.End } });

            if (data != null)
            {
                appointments.Add(data);
                await scheduler.Reload();
            }
        }
    }

    async Task OnAppointmentSelect(SchedulerAppointmentSelectEventArgs<Appointment> args)
    {
        var copy = new Appointment
        {
            StartTime = args.Data.StartTime, EndTime = args.Data.EndTime, Service = args.Data.Service
        };

        var data = await DialogService.OpenAsync<EditAppointmentDialog>("Редактировать приём",
            new Dictionary<string, object> { { "Appointment", copy } });

        if (data != null)
        {
            // Update the appointment
            args.Data.StartTime = data.Start;
            args.Data.EndTime = data.End;
            args.Data.Service = data.Text;
        }

        await scheduler.Reload();
    }

    void OnAppointmentRender(SchedulerAppointmentRenderEventArgs<Appointment> args)
    {
        // Never call StateHasChanged in AppointmentRender - would lead to infinite loop

        if (args.Data.Service.Contains("Вокал"))
        {
            args.Attributes["style"] = "background: red";
        }
    }

    async Task OnAppointmentMove(SchedulerAppointmentMoveEventArgs args)
    {
        var draggedAppointment = appointments.FirstOrDefault(x => x == args.Appointment.Data);

        if (draggedAppointment != null)
        {
            draggedAppointment.StartTime += args.TimeSpan;

            draggedAppointment.EndTime += args.TimeSpan;

            await scheduler.Reload();
        }
    }
}
