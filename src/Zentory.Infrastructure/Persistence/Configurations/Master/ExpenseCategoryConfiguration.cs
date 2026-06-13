using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zentory.Domain.Entities.Master;

namespace Zentory.Infrastructure.Persistence.Configurations.Master;

public class ExpenseCategoryConfiguration : IEntityTypeConfiguration<ExpenseCategory>
{
    public void Configure(EntityTypeBuilder<ExpenseCategory> builder)
    {
        builder.ToTable("expense_categories", "master");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(e => e.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
        builder.Property(e => e.Slug).HasColumnName("slug").HasMaxLength(50).IsRequired();
        builder.Property(e => e.Type).HasColumnName("type").HasMaxLength(20).IsRequired();
        builder.Property(e => e.IsSystem).HasColumnName("is_system").HasDefaultValue(false);
        builder.HasIndex(e => e.Slug).IsUnique().HasDatabaseName("ix_master_expense_categories_slug");
    }
}
