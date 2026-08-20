using Microsoft.EntityFrameworkCore;
using ThreeOfSpades.Api.Domain;

namespace ThreeOfSpades.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Room> Rooms => Set<Room>();
    public DbSet<RoomMember> RoomMembers => Set<RoomMember>();
    public DbSet<GameRecord> Games => Set<GameRecord>();
    public DbSet<GamePlayerRecord> GamePlayers => Set<GamePlayerRecord>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<User>(e =>
        {
            e.HasIndex(x => x.Email).IsUnique();
            e.HasIndex(x => x.UserName);
            e.Property(x => x.Email).HasMaxLength(256);
            e.Property(x => x.UserName).HasMaxLength(32);
        });

        model.Entity<Room>(e =>
        {
            e.HasIndex(x => x.Code).IsUnique();
            e.HasOne(x => x.Owner).WithMany().HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<RoomMember>(e =>
        {
            e.HasKey(x => new { x.RoomId, x.UserId });
            e.HasOne(x => x.Room).WithMany(r => r.Members).HasForeignKey(x => x.RoomId);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId);
        });

        model.Entity<GameRecord>(e =>
        {
            e.HasOne(x => x.Room).WithMany(r => r.Games).HasForeignKey(x => x.RoomId);
        });

        model.Entity<GamePlayerRecord>(e =>
        {
            e.HasOne(x => x.Game).WithMany(g => g.Players).HasForeignKey(x => x.GameId);
        });
    }
}
