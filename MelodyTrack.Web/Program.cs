using MelodyTrack.Common.Utils;
using MudBlazor.Services;
using MelodyTrack.Web.Components;
using MelodyTrack.Web.Components.ApiClient;

var builder = WebApplication.CreateBuilder(args);
var backendBaseAddress = EnvironmentUtils.GetRequiredEnvironmentVariable("MELODYTRACK_BACKEND_BASE_ADDRESS");

// Add MudBlazor services
builder.Services.AddMudServices();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddHttpClient("mt", (_, client) =>
{
    client.BaseAddress = new Uri(backendBaseAddress);
});
builder.Services.AddScoped<ApiUtils>();
builder.Services.AddScoped<AuthApi>();
builder.Services.AddScoped<ClientsApi>();
builder.Services.AddScoped<ExpensesApi>();
builder.Services.AddScoped<PaymentsApi>();
builder.Services.AddScoped<ScheduleApi>();
builder.Services.AddScoped<ServicesApi>();
builder.Services.AddScoped<Api>();

builder.WebHost.UseStaticWebAssets();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseAntiforgery();
app.UseStaticFiles();
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();