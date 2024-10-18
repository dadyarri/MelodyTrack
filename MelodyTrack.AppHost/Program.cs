var builder = DistributedApplication.CreateBuilder(args);

var apiService = builder.AddProject<Projects.MelodyTrack_ApiService>("apiservice");

builder.AddProject<Projects.MelodyTrack_Web>("webfrontend")
    .WithExternalHttpEndpoints()
    .WithReference(apiService);

builder.Build().Run();
