using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Zentory.Domain.Constants;
using Zentory.Domain.Entities;
using Zentory.Domain.Entities.Billing;
using Zentory.Infrastructure.Migrations;
using Zentory.Infrastructure.Persistence;
using Zentory.Tests.Support;
using OrganizationEntity = Zentory.Domain.Entities.Organization;

namespace Zentory.Tests.Integration.OrganizationDeletion;

// PLAN-0018 CA-T11-4 (parte de CA-75; RF-12, RF-65 b; ADR purga §1) y el modelo de M0c (RF-9, C-6, D-45): Organization con
// el filtro DeletedAt == null y su sello de concurrencia, User.OwnershipStamp, la facturación del owner con OrganizationId
// nullable, la migración AddOrganizationSoftDelete tal como la generó la herramienta y ExecuteInTransactionAsync.
public sealed class OrganizationSoftDeleteModelTests
{
    [Fact]
    public void PLAN0018_CA75_Organization_TieneElFiltroGlobalDeletedAtIgualANull()
    {
        using var db = TestDbContextFactory.Create();

        var filter = db.Model.FindEntityType(typeof(OrganizationEntity))!.GetQueryFilter();

        Assert.NotNull(filter);
        var body = Assert.IsAssignableFrom<BinaryExpression>(filter.Body);
        Assert.Equal(ExpressionType.Equal, body.NodeType);
        Assert.Equal(nameof(OrganizationEntity.DeletedAt), Assert.IsAssignableFrom<MemberExpression>(body.Left).Member.Name);
        Assert.Null(Assert.IsAssignableFrom<ConstantExpression>(StripConvert(body.Right)).Value);
    }

    [Fact]
    public async Task PLAN0018_CA75_Organization_EliminadaNoLaVeNingunaLecturaConFiltros_SoloIgnoreQueryFilters()
    {
        await using var db = TestDbContextFactory.Create();
        var active  = OrganizationEntity.Create($"Activa {Guid.NewGuid():N}", LegalType.Empresa);
        var deleted = OrganizationEntity.Create($"Eliminada {Guid.NewGuid():N}", LegalType.Empresa);
        deleted.MarkDeleted(Guid.NewGuid(), DateTime.UtcNow);
        db.Organizations.AddRange(active, deleted);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        Assert.Equal([active.OrganizationId], await db.Organizations.Select(o => o.OrganizationId).ToListAsync());
        Assert.False(await db.Organizations.AnyAsync(o => o.OrganizationId == deleted.OrganizationId));
        Assert.True(await db.Organizations.IgnoreQueryFilters().AnyAsync(o => o.OrganizationId == deleted.OrganizationId));
    }

    [Fact]
    public void PLAN0018_CA102_Organization_MarkDeletedAsignaDeletedAtDeletedByYRegeneraElSello()
    {
        var organization = OrganizationEntity.Create("Org", LegalType.Empresa);
        var stamp = organization.ConcurrencyStamp;
        var by = Guid.NewGuid();
        var at = new DateTime(2026, 10, 25, 12, 0, 0, DateTimeKind.Utc);

        organization.MarkDeleted(by, at);

        Assert.Equal(at, organization.DeletedAt);
        Assert.Equal(by, organization.DeletedBy);
        Assert.Null(organization.PurgeStartedAt);
        Assert.NotEqual(stamp, organization.ConcurrencyStamp);

        // Cambiar el owner también lo regenera: una eliminación con el sello leído antes falla (RF-65 b2).
        var afterDelete = organization.ConcurrencyStamp;
        organization.SetOwner(Guid.NewGuid());
        Assert.NotEqual(afterDelete, organization.ConcurrencyStamp);
    }

    [Fact]
    public void PLAN0018_CA102_SellosDeConcurrencia_ConcurrencyStampYOwnershipStampSonTokensDeConcurrencia()
    {
        using var db = TestDbContextFactory.Create();

        var organization = db.Model.FindEntityType(typeof(OrganizationEntity))!;
        Assert.True(organization.FindProperty(nameof(OrganizationEntity.ConcurrencyStamp))!.IsConcurrencyToken);
        Assert.Equal("concurrency_stamp", organization.FindProperty(nameof(OrganizationEntity.ConcurrencyStamp))!.GetColumnName());
        Assert.Contains(organization.GetIndexes(), index =>
            index.GetDatabaseName() == "ix_organizations_deleted_at"
            && index.Properties.Single().Name == nameof(OrganizationEntity.DeletedAt));

        var user = db.Model.FindEntityType(typeof(User))!.FindProperty(nameof(User.OwnershipStamp))!;
        Assert.True(user.IsConcurrencyToken);
        Assert.Equal("ownership_stamp", user.GetColumnName());

        // TouchOwnership regenera el sello (D-45); el resto de escrituras de User no lo toca.
        var owner = User.Create("owner@example.test", "Ana", "Owner", OrgRole.Owner);
        var stamp = owner.OwnershipStamp;
        owner.SetLastActiveOrganization(Guid.NewGuid());
        Assert.Equal(stamp, owner.OwnershipStamp);
        owner.TouchOwnership();
        Assert.NotEqual(stamp, owner.OwnershipStamp);
    }

    [Theory]
    [InlineData(typeof(BillingCustomer))]
    [InlineData(typeof(BillingInvoice))]
    [InlineData(typeof(BillingPayment))]
    [InlineData(typeof(BillingCredit))]
    [InlineData(typeof(CouponRedemption))]
    public void PLAN0018_CA107_FacturacionDelOwner_OrganizationIdNullable(Type billingType)
    {
        // C-6: la purga desvincula la facturación del owner (OrganizationId = null), así que M0c vuelve nullable la columna.
        using var db = TestDbContextFactory.Create();

        var property = db.Model.FindEntityType(billingType)!.FindProperty("OrganizationId")!;

        Assert.True(property.IsNullable);
        Assert.Equal(typeof(Guid?), property.ClrType);
    }

    [Fact]
    public void PLAN0018_CA8_MigracionAddOrganizationSoftDelete_ConservaElDefaultValueDeRellenoYVuelveNullableLaFacturacion()
    {
        var operations = new AddOrganizationSoftDelete().UpOperations;

        var added = operations.OfType<AddColumnOperation>().ToDictionary(op => $"{op.Table}.{op.Name}");
        Assert.Equal(
            ["organizations.concurrency_stamp", "organizations.deleted_at", "organizations.deleted_by",
             "organizations.purge_started_at", "users.ownership_stamp"],
            added.Keys.Order(StringComparer.Ordinal));
        foreach (var stamp in new[] { "organizations.concurrency_stamp", "users.ownership_stamp" })
        {
            Assert.False(added[stamp].IsNullable);
            Assert.Equal(Guid.Empty, added[stamp].DefaultValue);
        }
        Assert.All(new[] { "organizations.deleted_at", "organizations.deleted_by", "organizations.purge_started_at" },
            column => Assert.True(added[column].IsNullable));

        var altered = operations.OfType<AlterColumnOperation>().ToList();
        Assert.Equal(
            ["coupon_redemptions", "credits", "customers", "invoices", "payments"],
            altered.Select(op => op.Table).Order(StringComparer.Ordinal));
        Assert.All(altered, op =>
        {
            Assert.Equal("billing", op.Schema);
            Assert.Equal("organization_id", op.Name);
            Assert.True(op.IsNullable);
        });

        var index = Assert.Single(operations.OfType<CreateIndexOperation>());
        Assert.Equal("ix_organizations_deleted_at", index.Name);
        // Solo DDL generado: ni SQL a mano ni operaciones fuera de las esperadas.
        Assert.Empty(operations.OfType<SqlOperation>());
        Assert.Equal(added.Count + altered.Count + 1, operations.Count);
    }

    [Fact]
    public async Task PLAN0018_CA102_ExecuteInTransactionAsync_SinTransaccionesEnInMemory_EjecutaYPropagaLaExcepcion()
    {
        await using var db = TestDbContextFactory.Create();
        var uow = new UnitOfWork(db);
        var organization = OrganizationEntity.Create($"Tx {Guid.NewGuid():N}", LegalType.Empresa);

        await uow.ExecuteInTransactionAsync(async ct =>
        {
            db.Organizations.Add(organization);
            await uow.SaveChangesAsync(ct);
        });
        Assert.True(await db.Organizations.AnyAsync(o => o.OrganizationId == organization.OrganizationId));

        var failure = new InvalidOperationException("falla dentro de la operación");
        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            uow.ExecuteInTransactionAsync(_ => throw failure));
        Assert.Same(failure, thrown);
    }

    private static Expression StripConvert(Expression expression)
        => expression is UnaryExpression { NodeType: ExpressionType.Convert } unary ? StripConvert(unary.Operand) : expression;
}
