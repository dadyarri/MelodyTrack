using FastEndpoints;
using FastEndpoints.Security;
using FastEndpoints.Swagger;
using MelodyTrack.ApiService.Services;
using MelodyTrack.ApiService.Storage;

var builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire components.
builder.AddServiceDefaults();

builder.Services
    .AddProblemDetails()
    .AddAuthenticationJwtBearer(j =>
    {
        j.SigningKey = "MelodyTrackSecretKey"; // TODO: Change this to a secure key.
    })
    .AddAuthorization()
    .AddFastEndpoints()
    .SwaggerDocument(so =>
    {
        so.DocumentSettings = ds =>
        {
            ds.Title = "MelodyTrack API";
            ds.Description = "API to manage lessons for MelodyTrack CLI";
            ds.Version = "v1";
        };
    });

builder.AddNpgsqlDbContext<AppDbContext>("melodytrack-db");

foreach (var service in typeof(IService).Assembly.GetTypes().Where(t => typeof(IService).IsAssignableFrom(t)))
{
    builder.Services.AddTransient(service);
}

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseExceptionHandler();
app
    .UseAuthentication()
    .UseAuthorization()
    .UseFastEndpoints()
    .UseSwaggerGen()
    .UseSwaggerUi();

app.Run();

