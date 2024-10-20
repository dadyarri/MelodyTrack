namespace MelodyTrack.ApiService.Storage;

using MelodyTrack.ApiService.Storage.Entities;
using Microsoft.EntityFrameworkCore;

public class AppDbContext : DbContext
{
    public virtual DbSet<BannedToken> BannedTokens { get; set; }
    public virtual DbSet<RefreshToken> RefreshTokens { get; set; }
    public virtual DbSet<ResetToken> ResetTokens { get; set; }
    public virtual DbSet<User> Users { get; set; }

    public AppDbContext(DbContextOptions<AppDbContext> options)
    {
    }
}
