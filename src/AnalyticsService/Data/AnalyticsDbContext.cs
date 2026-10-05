using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;

namespace AnalyticsService.Data;

public partial class AnalyticsDbContext : DbContext
{
    public AnalyticsDbContext(DbContextOptions<AnalyticsDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<ClickEvent> ClickEvents { get; set; }

    public virtual DbSet<LinkStat> LinkStats { get; set; }

    public virtual DbSet<VwLinkClickSummary> VwLinkClickSummaries { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ClickEvent>(entity =>
        {
            entity.HasIndex(e => new { e.Code, e.ClickedAtUtc }, "IX_ClickEvents_Code_ClickedAtUtc");

            entity.Property(e => e.ClickedAtUtc).HasPrecision(3);
            entity.Property(e => e.Code)
                .HasMaxLength(16)
                .UseCollation("Latin1_General_100_BIN2");
            entity.Property(e => e.Referrer).HasMaxLength(512);
            entity.Property(e => e.UserAgent).HasMaxLength(512);
        });

        modelBuilder.Entity<LinkStat>(entity =>
        {
            entity.HasKey(e => e.Code);

            entity.Property(e => e.Code)
                .HasMaxLength(16)
                .UseCollation("Latin1_General_100_BIN2");
            entity.Property(e => e.LastClickedAtUtc).HasPrecision(3);
            entity.Property(e => e.UpdatedAtUtc).HasPrecision(3);
        });

        modelBuilder.Entity<VwLinkClickSummary>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("vw_LinkClickSummary");

            entity.Property(e => e.Code)
                .HasMaxLength(16)
                .UseCollation("Latin1_General_100_BIN2");
            entity.Property(e => e.LastClickedAtUtc).HasPrecision(3);
            entity.Property(e => e.UpdatedAtUtc).HasPrecision(3);
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
