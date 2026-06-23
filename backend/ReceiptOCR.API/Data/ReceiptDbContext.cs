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
        
        // Settings, Expenses, SystemLogs tabloları da buraya eklenebilir
        // Şimdilik sadece Auth için Users tablosuna odaklanıyoruz.

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            
            modelBuilder.Entity<User>(entity =>
            {
                entity.ToTable("Users");
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.Username).IsUnique();
            });
        }
    }
}
