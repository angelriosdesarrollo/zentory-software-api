using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zentory.Domain.Entities;

namespace Zentory.Infrastructure.Persistence.Configurations.Core;

public class OrganizationBankAccountConfiguration : IEntityTypeConfiguration<OrganizationBankAccount>
{
    public void Configure(EntityTypeBuilder<OrganizationBankAccount> builder)
    {
        builder.ToTable("organization_bank_accounts", "core");
        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id).HasColumnName("id");
        builder.Property(b => b.OrganizationId).HasColumnName("organization_id").IsRequired();
        builder.Property(b => b.BankName).HasColumnName("bank_name").HasMaxLength(100).IsRequired();
        builder.Property(b => b.AccountType).HasColumnName("account_type").HasMaxLength(30).IsRequired();
        builder.Property(b => b.AccountNumber).HasColumnName("account_number").HasMaxLength(50).IsRequired();
        builder.Property(b => b.AccountHolder).HasColumnName("account_holder").HasMaxLength(255).IsRequired();
        builder.Property(b => b.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
        builder.Property(b => b.IsDefault).HasColumnName("is_default").HasDefaultValue(false);
        builder.Property(b => b.CreatedAt).HasColumnName("created_at");
        builder.Property(b => b.UpdatedAt).HasColumnName("updated_at");
        builder.Property(b => b.DeletedAt).HasColumnName("deleted_at");
        builder.HasIndex(b => b.OrganizationId).HasDatabaseName("ix_org_bank_accounts_org_id");
    }
}
