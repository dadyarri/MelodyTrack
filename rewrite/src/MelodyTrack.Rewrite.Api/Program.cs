using System.Reflection;
using MelodyTrack.Rewrite.Api;
using MelodyTrack.Rewrite.Application;
using MelodyTrack.Rewrite.Persistence;
using MelodyTrack.Rewrite.Persistence.Access;
using MelodyTrack.Rewrite.Persistence.Clients;
using MelodyTrack.Rewrite.Persistence.Finance;
using MelodyTrack.Rewrite.Persistence.Marketing;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);
var isOpenApiGeneration = string.Equals(Assembly.GetEntryAssembly()?.GetName().Name,
    "GetDocument.Insider", StringComparison.Ordinal);
builder.Services.AddRewriteOpenApi();
builder.Services.AddReleaseChangelog();
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
builder.Services.AddPersonalDataProtection(validateOnStart: !isOpenApiGeneration);
builder.Services.AddSingleton<IClientNotesStore, ClientNotesStore>();
builder.Services.AddSingleton<ClientNotesService>();
builder.Services.AddSingleton<IClientDirectoryStore, ClientDirectoryStore>();
builder.Services.AddSingleton<ClientDirectoryService>();
builder.Services.AddSingleton<IExpenseStore, ExpenseStore>();
builder.Services.AddSingleton<ExpenseService>();
builder.Services.AddSingleton<IMarketingCampaignStore, MarketingCampaignStore>();
builder.Services.AddSingleton<MarketingCampaignService>();
builder.Services.AddCurrentStaffAccess();
builder.Services.AddBrowserAccess();
builder.Services.AddScoped<IStaffAccessTokenStore, StaffAccessTokenStore>();
builder.Services.AddStaffAuthentication();
builder.Services.AddSingleton<StaffPolicies>();
builder.Services.AddCredentialSecurity(validateOnStart: !isOpenApiGeneration);
builder.Services.AddStaffCredentialFlows();
builder.Services.AddScoped<IClientAuthenticationStore, ClientAuthenticationStore>();
builder.Services.AddClientAuthentication();
var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.MapReleaseEndpoints();
app.MapStaffCredentialEndpoints();
app.MapClientAuthenticationEndpoints();
app.MapStaffSessionEndpoints();
app.MapStaffPoliciesEndpoints();
app.MapClientNotesEndpoints();
app.MapClientDirectoryEndpoints();
app.MapExpensesEndpoints();
app.Run();
