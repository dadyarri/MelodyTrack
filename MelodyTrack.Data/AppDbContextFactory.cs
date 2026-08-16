using MelodyTrack.Data.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MelodyTrack.Backend.Data;

public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("Database__ConnectionString")
                               ?? Environment.GetEnvironmentVariable("MELODY_TRACK_DATABASE_URL")
                               ?? throw new InvalidOperationException("Database connection string is required for design-time operations.");
        var piiMasterKeyVersion = Environment.GetEnvironmentVariable("PersonalData__CurrentKeyVersion")
                                  ?? Environment.GetEnvironmentVariable("MELODY_TRACK_PII_MASTER_KEY_VERSION")
                                  ?? "v1";
        var piiMasterKey = Environment.GetEnvironmentVariable("PersonalData__CurrentKey")
                           ?? Environment.GetEnvironmentVariable("MELODY_TRACK_PII_MASTER_KEY")
                           ?? throw new InvalidOperationException("Personal-data key is required for design-time operations.");
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
