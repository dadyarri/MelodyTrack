using MelodyTrack.Rewrite.Persistence;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<NpgsqlDataSource>(services =>
{
    var configuration = services.GetRequiredService<IConfiguration>();
    var connectionString = configuration.GetConnectionString("Database");
    if (string.IsNullOrWhiteSpace(connectionString))
    {
        throw new InvalidOperationException("Не задана строка подключения ConnectionStrings:Database.");
    }

    return new NpgsqlDataSourceBuilder(connectionString).MapDomainEnums().Build();
});
builder.Services.AddDatabase();
var app = builder.Build();

app.Run();
