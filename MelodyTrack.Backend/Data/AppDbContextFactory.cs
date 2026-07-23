using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MelodyTrack.Backend.Data;

public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var connectionString = EnvironmentUtils.GetRequiredEnvironmentVariable("MELODY_TRACK_DATABASE_URL");
        var piiMasterKeyVersion = Environment.GetEnvironmentVariable("MELODY_TRACK_PII_MASTER_KEY_VERSION") ?? "v1";
        var piiMasterKey = EnvironmentUtils.GetRequiredEnvironmentVariable("MELODY_TRACK_PII_MASTER_KEY");
        var piiMasterKeys = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [piiMasterKeyVersion] = piiMasterKey
        };

        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseNpgsql(connectionString);

        var protector = new PersonalDataProtector(piiMasterKeyVersion, piiMasterKeys);
        return new AppDbContext(optionsBuilder.Options, protector);
    }
}
