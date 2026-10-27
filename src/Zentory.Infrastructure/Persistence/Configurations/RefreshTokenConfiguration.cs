using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zentory.Domain.Entities;

namespace Zentory.Infrastructure.Persistence.Configurations;

public class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("refresh_tokens");

        builder.HasKey(rt => rt.Id);
        builder.Property(rt => rt.Id).HasColumnName("id");

        builder.Property(rt => rt.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(rt => rt.TokenHash).HasColumnName("token_hash").HasMaxLength(64).IsRequired();
        builder.Property(rt => rt.ExpiresAt).HasColumnName("expires_at").IsRequired();
        // Token de concurrencia (PLAN-0018 SR-0018.13): la revocación es UPDATE ... WHERE is_revoked = false, así que de
        // dos rotaciones del mismo token solo una afecta la fila y la otra recibe DbUpdateConcurrencyException.
        builder.Property(rt => rt.IsRevoked).HasColumnName("is_revoked").HasDefaultValue(false).IsConcurrencyToken();
        builder.Property(rt => rt.CreatedAt).HasColumnName("created_at");
        // Sesión del token (PLAN-0018 RF-90 a, D-47; M2c): sin FK ni índice (el token se busca por su hash) y sin
        // relleno, así los tokens anteriores quedan con null y son inválidos. No es token de concurrencia.
        builder.Property(rt => rt.SessionId).HasColumnName("session_id").IsRequired(false);

        builder.HasIndex(rt => rt.TokenHash).IsUnique().HasDatabaseName("ix_refresh_tokens_token_hash");
        builder.HasIndex(rt => rt.UserId).HasDatabaseName("ix_refresh_tokens_user_id");
    }
}
