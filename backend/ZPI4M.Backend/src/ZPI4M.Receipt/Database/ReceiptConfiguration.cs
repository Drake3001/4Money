using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Text;
using ZPI4M.Receipt.Models;

namespace ZPI4M.Receipt.Database
{
    public static class ReceiptConfiguration
    {
        public static void Configure(ModelBuilder modelBuilder)
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
        }
    }
}
