using Microsoft.Extensions.Configuration;

var builder = DistributedApplication.CreateBuilder(args);

const int backendPort = 5000;
const int frontendPort = 5173;

var enableSqlParameterLogging = builder.Configuration.GetValue<bool>("Development:EnableSqlParameterLogging");
var sqlCommandLogLevel = enableSqlParameterLogging ? "Information" : "Warning";

var postgres = builder.AddPostgres("postgres")
    .WithImageTag("16-alpine")
    .WithDataVolume("melodytrack-postgres-data");
var database = postgres.AddDatabase("melodytrack");

var initializer = builder.AddProject<Projects.MelodyTrack_Init>("init", launchProfileName: "development")
    .WithReference(database)
    .WithEnvironment("Database__ConnectionString", database)
    .WithEnvironment("Database__EnableSensitiveDataLogging", enableSqlParameterLogging.ToString())
    .WithEnvironment("Logging__LogLevel__Microsoft.EntityFrameworkCore.Database.Command", sqlCommandLogLevel)
    .WaitFor(database);

var backend = builder.AddProject<Projects.MelodyTrack_Backend>("backend", launchProfileName: "http")
    .WithHttpEndpoint(targetPort: backendPort, port: backendPort, name: "http", isProxied: false)
    .WithReference(database)
    .WithEnvironment("Database__ConnectionString", database)
    .WithEnvironment("Database__EnableSensitiveDataLogging", enableSqlParameterLogging.ToString())
    .WithEnvironment("Http__PathBase", "/api")
    .WithHttpHealthCheck("/health")
    .WaitForCompletion(initializer);

var web = builder.AddViteApp("web", "../MelodyTrack.Web")
    .WithHttpEndpoint(targetPort: frontendPort, port: frontendPort, name: "http", env: "PORT", isProxied: false)
    .WithReference(backend)
    .WithEnvironment("MELODY_TRACK_API_PROXY_TARGET", backend.GetEndpoint("http"))
    .WaitFor(backend)
    .WithExternalHttpEndpoints();

initializer.WithEnvironment("PublicUrl__BaseUrl", web.GetEndpoint("http"));
backend.WithEnvironment("PublicUrl__BaseUrl", web.GetEndpoint("http"));

builder.Build().Run();
