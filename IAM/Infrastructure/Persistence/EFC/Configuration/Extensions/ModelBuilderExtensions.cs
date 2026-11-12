using Hampcoders.Electrolink.API.IAM.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.IAM.Domain.Model.Entities;
using Hampcoders.Electrolink.API.IAM.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace Hampcoders.Electrolink.API.IAM.Infrastructure.Persistence.EFC.Configuration.Extensions;

public static class ModelBuilderExtensions
{
    public static void ApplyIamConfiguration(this ModelBuilder builder)
    {
        builder.Entity<User>().HasKey(u => u.Id);

        builder.Entity<User>()
            .Property(u => u.Id)
            .HasConversion(
                id => id.Value,
                value => UserId.From(value))
            .IsRequired()
            .ValueGeneratedOnAdd();

        builder.Entity<User>()
            .Property(u => u.PasswordHash)
            .HasConversion(
                ph => ph.Value,
                value => HashedPassword.FromHash(value))
            .HasColumnName("PasswordHash")
            .IsRequired();

        builder.Entity<User>()
            .OwnsOne(u => u.Email, e =>
            {
                e.WithOwner()
                    .HasForeignKey("Id");
                e.HasKey("Id");

                e.Property(x => x.Value)
                    .HasColumnName("Email")
                    .IsRequired();

                e.HasIndex(x => x.Value)
                    .IsUnique();
            });

        builder.Entity<User>()
            .OwnsOne(u => u.PasswordResetToken, prt =>
            {
                prt.WithOwner().HasForeignKey("Id");
                prt.Property(x => x.Value)
                    .HasColumnName("password_reset_token")
                    .HasMaxLength(100);
                prt.Property(x => x.ExpiresAt)
                    .HasColumnName("password_reset_token_expires_at");
            });
    
        builder.Entity<RefreshToken>(rt =>
        {
            rt.ToTable("refresh_tokens");
            rt.HasKey(x => x.Id);
            rt.Property(x => x.Id).HasMaxLength(60);
            rt.Property(x => x.UserId)
                .HasConversion(id => id.Value, value => UserId.From(value))
                .HasMaxLength(60)
                .IsRequired();
            rt.Property(x => x.TokenHash).HasMaxLength(128).IsRequired();
            rt.Property(x => x.ReplacedByTokenHash).HasMaxLength(128);
            rt.HasIndex(x => x.TokenHash).IsUnique();
            rt.HasIndex(x => x.UserId);
        });
    }
}
