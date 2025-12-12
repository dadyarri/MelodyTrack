var builder = DistributedApplication.CreateBuilder(args);

var db = builder.AddPostgres("db")
    .WithDataVolume(isReadOnly: false)
    .AddDatabase("melodytrack");

var migrator = builder
    .AddProject<Projects.MelodyTrack_MigratorService>("migrator")
    .WithReference(db)
    .WithEnvironment("MELODYTRACK_V1_DATABASE_URL", Environment.GetEnvironmentVariable("MELODYTRACK_V1_DATABASE_URL"))
    .WaitFor(db);

var api = builder.AddProject<Projects.MelodyTrack_Backend>("backend")
    .WithExternalHttpEndpoints()
    .WithReference(db)
    .WithEnvironment("MELODY_TRACK_JWT_SIGNING_KEY", Environment.GetEnvironmentVariable("MELODY_TRACK_JWT_SIGNING_KEY"))
    .WaitForCompletion(migrator)
    .WaitFor(db);

builder.AddProject<Projects.MelodyTrack_Web>("web")
    .WithExternalHttpEndpoints()
    .WithReference(api)
    .WaitFor(api);


builder.Build().Run();
