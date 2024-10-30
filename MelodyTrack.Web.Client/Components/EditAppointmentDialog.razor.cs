using Microsoft.AspNetCore.Components;

namespace MelodyTrack.Web.Client.Components;

using Models;
using Radzen;

public partial class EditAppointmentDialog : ComponentBase
{

    [Inject]
    private DialogService DialogService { get; set; }

    [Parameter]
    public Appointment Appointment { get; set; }

    private Appointment model = new();

    protected override void OnParametersSet() => model = Appointment;

    private void OnSubmit(Appointment data) => DialogService.Close(data);
}

