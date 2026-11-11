using Hampcoders.Electrolink.API.Subscriptions.Domain.Model.Aggregates;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hampcoders.Electrolink.API.Subscriptions.Infrastructure.Persistence.EFC.Configuration;

public class ContactRequestConfiguration : IEntityTypeConfiguration<ContactRequest>
{
    public void Configure(EntityTypeBuilder<ContactRequest> builder)
    {
        builder.ToTable("sp_contact_requests");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasMaxLength(40);
        builder.Property(c => c.Name).HasMaxLength(120).IsRequired();
        builder.Property(c => c.Company).HasMaxLength(160).IsRequired();
        builder.Property(c => c.Email).HasMaxLength(254).IsRequired();
        builder.Property(c => c.Phone).HasMaxLength(30);
        builder.Property(c => c.Message).HasMaxLength(2000).IsRequired();
        builder.Property(c => c.Status).HasMaxLength(20).IsRequired();
        builder.Property(c => c.SourceIp).HasMaxLength(45);
        builder.HasIndex(c => new { c.Email, c.ReceivedAtUtc });
        builder.HasIndex(c => c.ReceivedAtUtc);
    }
}
