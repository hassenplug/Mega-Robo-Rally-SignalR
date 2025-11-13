using Microsoft.EntityFrameworkCore;
using MRR.Data.Entities;

namespace MRR.Data
{
    public class MRRDbContext : DbContext
    {
        public MRRDbContext(DbContextOptions<MRRDbContext> options) : base(options)
        {
        }

        public DbSet<PendingCommandEntity> PendingCommands { get; set; } = null!;
        public DbSet<Robot> Robots { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Configure default schema if needed
            modelBuilder.HasDefaultSchema("rally");

            // Configure PendingCommands
            modelBuilder.Entity<PendingCommandEntity>(entity =>
            {
                entity.ToTable("CommandList");
                entity.HasKey(e => e.CommandID);
            });
        }
    }
}