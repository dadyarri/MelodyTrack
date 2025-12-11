var builder = DistributedApplication.CreateBuilder(args);

var db = builder.AddPostgres("db")
    .AddDatabase("melodytrack");

var api = builder.AddProject<Projects.MelodyTrack_Backend>("backend")
    .WithReference(db)
    .WithEnvironment("MELODY_TRACK_DATABASE_URL", db.Resource.ConnectionStringExpression)
    .WithEnvironment("MELODY_TRACK_JWT_SIGNING_KEY", "Rr3dSvuYI1ozNMme38TilJ52nJ9w2zlX7ONNu9ztDpyQG/fVKW+Oj+iDHhY7z4dTVdH3dx5ANqAwWQ1TSentQw==")
    .WaitFor(db);


api.Resource.TryGetUrls(out var backendUrls);

var web = builder.AddProject<Projects.MelodyTrack_Web>("web")
    .WithExternalHttpEndpoints()
    .WithReference(api)
    .WithEnvironment("MELODYTRACK_BACKEND_BASE_ADDRESS", backendUrls?.First().Url)
    .WaitFor(api);


builder.Build().Run();
