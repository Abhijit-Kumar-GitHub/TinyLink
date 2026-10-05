using Microsoft.EntityFrameworkCore;

namespace LinkService.Data;

public class LinkDbContext(DbContextOptions<LinkDbContext> options) : DbContext(options)
{
    public DbSet<Link> Links => Set<Link>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var link = modelBuilder.Entity<Link>();
        // Binary collation: short codes are case-sensitive, unlike SQL Server's default.
        link.Property(l => l.Code).HasMaxLength(16).IsRequired().UseCollation("Latin1_General_100_BIN2");
        link.HasIndex(l => l.Code).IsUnique();
        link.Property(l => l.OriginalUrl).HasMaxLength(2048).IsRequired();
        link.Property(l => l.CreatedAtUtc)
            .HasConversion(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
        link.HasIndex(l => l.CreatedAtUtc);
    }
}
