using Microsoft.EntityFrameworkCore;
using MRR.Data.Entities;

namespace MRR.Data
{
    public class MRRDbContext : DbContext
    {
        public MRRDbContext(DbContextOptions<MRRDbContext> options) : base(options)
        {
        }

        public DbSet<CommandItem> CommandItems { get; set; } = null!;
        public DbSet<Player> Robots { get; set; } = null!;
        public DbSet<CurrentGameDataEntity> CurrentGameData { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.HasDefaultSchema("rally");

            modelBuilder.Entity<CommandItem>(entity =>
            {
                entity.ToTable("CommandList");
                entity.HasKey(e => e.CommandID);
            });
        }
    }
}