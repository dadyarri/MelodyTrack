using MelodyTrack.Common.Utils;
using MelodyTrack.Web.Auth;
using MelodyTrack.Web.Components;
using MelodyTrack.Web.Components.ApiClient;
using Microsoft.AspNetCore.Components.Authorization;
using MudBlazor.Services;

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
    client.DefaultRequestHeaders.UserAgent.ParseAdd("MelodyTrackWeb/2.0");
});
builder.Services.AddScoped<ApiUtils>();
builder.Services.AddScoped<AuthApi>();
builder.Services.AddScoped<ClientsApi>();
builder.Services.AddScoped<ExpensesApi>();
builder.Services.AddScoped<PaymentsApi>();
builder.Services.AddScoped<ScheduleApi>();
builder.Services.AddScoped<ServicesApi>();
builder.Services.AddScoped<UsersApi>();
builder.Services.AddScoped<Api>();

builder.Services.AddAuthentication("bearer")
    .AddBearerToken("bearer");
builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<AuthenticationStateProvider, CustomAuthenticationStateProvider>();

builder.WebHost.UseStaticWebAssets();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseAntiforgery();
app.UseStaticFiles();
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();