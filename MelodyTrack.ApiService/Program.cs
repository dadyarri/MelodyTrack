using System.Globalization;
using FastEndpoints;
using FastEndpoints.Security;
using FastEndpoints.Swagger;
using MelodyTrack.ApiService.Configuration;
using MelodyTrack.ApiService.Services;
using MelodyTrack.ApiService.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, lc) =>
{
    lc.WriteTo.Console(formatProvider: CultureInfo.InvariantCulture);
});

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

builder.Services.AddDbContextPool<AppDbContext>(options =>
{
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default"));
});

builder.Services.Configure<SecurityConfiguration>(builder.Configuration.GetRequiredSection("Security"));
builder.Services.AddSingleton(resolver => resolver.GetRequiredService<IOptions<SecurityConfiguration>>().Value);

var services = typeof(IService).Assembly.GetTypes()
    .Where(t => typeof(IService).IsAssignableFrom(t) && !t.IsInterface);
foreach (var service in services)
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
