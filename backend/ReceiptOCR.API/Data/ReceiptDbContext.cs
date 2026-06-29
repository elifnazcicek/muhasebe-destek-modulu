using Microsoft.EntityFrameworkCore;
using ReceiptOCR.API.Models;

namespace ReceiptOCR.API.Data
{
    public class ReceiptDbContext : DbContext
    {
        public ReceiptDbContext(DbContextOptions<ReceiptDbContext> options) : base(options)
        {
        }

        public DbSet<User> Users { get; set; }
        public DbSet<Expense> Expenses { get; set; }
        public DbSet<SystemLog> SystemLogs { get; set; }
        public DbSet<ErrorLog> ErrorLogs { get; set; }
        public DbSet<Setting> Settings { get; set; }
        public DbSet<PasswordReset> PasswordResets { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            
            modelBuilder.Entity<User>(entity =>
            {
                entity.ToTable("Users");
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.Username).IsUnique();
            });

            modelBuilder.Entity<Expense>(entity =>
            {
                entity.ToTable("Expenses");
                entity.HasKey(e => e.Id);
            });



            modelBuilder.Entity<SystemLog>(entity =>
            {
                entity.ToTable("SystemLogs");
                entity.HasKey(e => e.Id);
            });

            modelBuilder.Entity<ErrorLog>(entity =>
            {
                entity.ToTable("ErrorLogs");
                entity.HasKey(e => e.Id);
            });

            modelBuilder.Entity<Setting>(entity =>
            {
                entity.ToTable("Settings");
                entity.HasKey(e => e.Key);
            });

            modelBuilder.Entity<PasswordReset>(entity =>
            {
                entity.ToTable("PasswordResets");
                entity.HasKey(e => e.Id);
            });
        }
    }
}
