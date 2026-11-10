using MelodyTrack.Rewrite.Api;
using MelodyTrack.Rewrite.Application;
using MelodyTrack.Rewrite.Persistence;
using MelodyTrack.Rewrite.Persistence.Access;
using MelodyTrack.Rewrite.Persistence.Clients;
using MelodyTrack.Rewrite.Persistence.Finance;
using MelodyTrack.Rewrite.Persistence.Marketing;
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
builder.Services.AddPersonalDataProtection();
builder.Services.AddSingleton<IClientNotesStore, ClientNotesStore>();
builder.Services.AddSingleton<ClientNotesService>();
builder.Services.AddSingleton<IExpenseStore, ExpenseStore>();
builder.Services.AddSingleton<ExpenseService>();
builder.Services.AddSingleton<IMarketingCampaignStore, MarketingCampaignStore>();
builder.Services.AddSingleton<MarketingCampaignService>();
builder.Services.AddCurrentStaffAccess();
builder.Services.AddBrowserAccess();
builder.Services.AddScoped<IStaffAccessTokenStore, StaffAccessTokenStore>();
builder.Services.AddStaffAuthentication();
builder.Services.AddSingleton<StaffPolicies>();
builder.Services.AddCredentialSecurity();
builder.Services.AddStaffCredentialFlows();
builder.Services.AddScoped<IClientAuthenticationStore, ClientAuthenticationStore>();
builder.Services.AddClientAuthentication();
var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.MapStaffCredentialEndpoints();
app.MapClientAuthenticationEndpoints();
app.MapStaffSessionEndpoints();
app.MapStaffPoliciesEndpoints();
app.MapClientNotesEndpoints();
app.MapExpensesEndpoints();
app.Run();
