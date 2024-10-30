using Microsoft.AspNetCore.Components;

namespace MelodyTrack.Web.Client.Components;

using Models;
using Radzen;

public partial class AddAppointmentDialog : ComponentBase
{
    [Parameter]
    public DateTime StartTime { get; set; }

    [Parameter]
    public DateTime EndTime { get; set; }

    [Inject]
    private DialogService DialogService { get; set; }

    private Appointment model = new();

    protected override void OnParametersSet()
    {
        model.StartTime = StartTime;
        model.EndTime = EndTime;
    }

    private void OnSubmit(Appointment appointment) => DialogService.Close(appointment);
}

