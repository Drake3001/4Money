using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using ZPI4M.Receipt.Models;

namespace ZPI4M.Receipt.Database;

public partial class ReceiptDbContext : DbContext
{
    public ReceiptDbContext(DbContextOptions<ReceiptDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<Models.Receipt> Receipts { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Models.Receipt>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("receipt_pkey");

            entity.ToTable("receipt", "receipt");

            entity.Property(e => e.Id)
                .ValueGeneratedNever()
                .HasColumnName("_id");
            entity.Property(e => e.ExtractionResult)
                .HasColumnType("jsonb")
                .HasColumnName("extraction_result");
            entity.Property(e => e.S3Link).HasColumnName("s3_link");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
