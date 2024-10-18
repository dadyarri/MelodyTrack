var builder = DistributedApplication.CreateBuilder(args);

var postgresDb = builder
    .AddPostgres("postgresdb")
    .WithPgWeb()
    .AddDatabase("melodytrack-db");

var apiService = builder
    .AddProject<Projects.MelodyTrack_ApiService>("apiservice")
    .WithReference(postgresDb);

builder.AddProject<Projects.MelodyTrack_Web>("webfrontend")
    .WithExternalHttpEndpoints()
    .WithReference(apiService);

builder.Build().Run();
