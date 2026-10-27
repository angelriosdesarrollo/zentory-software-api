using Microsoft.EntityFrameworkCore;
using Zentory.Domain.Entities;
using Zentory.Domain.Entities.Billing;
using Zentory.Domain.Entities.Master;
using Zentory.Infrastructure.Persistence;
using Zentory.Infrastructure.Persistence.Purge;
using Zentory.Tests.Support;
using OrganizationEntity = Zentory.Domain.Entities.Organization;

namespace Zentory.Tests.Unit.Build;

// PLAN-0018 CA-T12-1 (parte de CA-122, que se cierra en T12b; RF-78 4; C-8; ADR purga §2): el catálogo de purga
// clasifica todos los tipos de ZentoryDbContext.Model, cada uno en una sola categoría, y ninguno con OrganizationId
// figura como global. Un tipo nuevo del modelo sin clasificar rompe la suite (R-E6).
public sealed class PurgeCatalogPolicyTests
{
    private static readonly OrganizationDataCatalog Catalog = OrganizationDataCatalog.Default;

    [Fact]
    public void PLAN0018_CA122_ModeloDeZentory_CadaTipoEnExactamenteUnaCategoriaYSinViolaciones()
    {
        using var db = TestDbContextFactory.Create();

        var badlyClassified = db.Model.GetEntityTypes()
            .Where(entity => Catalog.CategoriesOf(entity.ClrType).Count != 1)
            .Select(entity => $"{entity.ClrType.Name} ({Catalog.CategoriesOf(entity.ClrType).Count})")
            .ToList();

        Assert.True(badlyClassified.Count == 0,
            "Tipos sin clasificar o en varias categorías de OrganizationDataCatalog: " + string.Join(", ", badlyClassified));
        var violations = Catalog.FindViolations(db.Model);
        Assert.True(violations.Count == 0, "OrganizationDataCatalog: " + string.Join(" ", violations));
    }

    [Fact]
    public void PLAN0018_CA122_ModeloDeZentory_NingunTipoConOrganizationIdFiguraComoGlobal()
    {
        using var db = TestDbContextFactory.Create();

        var globalWithOrganization = db.Model.GetEntityTypes()
            .Where(entity => entity.FindProperty(OrganizationDataCatalog.OrganizationIdProperty) is not null)
            .Where(entity => Catalog.CategoriesOf(entity.ClrType).Contains(OrganizationDataCategory.Global))
            .Select(entity => entity.ClrType.Name)
            .ToList();

        Assert.Empty(globalWithOrganization);
    }

    [Fact]
    public void PLAN0018_CA122_ModeloConUnTipoDePruebaSinClasificar_LaPoliticaFalla()
    {
        using var db = new ContextWithUnclassifiedProbe(new DbContextOptionsBuilder<ZentoryDbContext>()
            .UseInMemoryDatabase($"zentory-purge-catalog-{Guid.NewGuid():N}").Options);

        Assert.Contains(db.Model.GetEntityTypes(), entity => entity.ClrType == typeof(UnclassifiedProbe));
        Assert.Empty(Catalog.CategoriesOf(typeof(UnclassifiedProbe)));
        Assert.Contains(Catalog.FindViolations(db.Model),
            violation => violation.StartsWith(nameof(UnclassifiedProbe) + ":", StringComparison.Ordinal));
        Assert.Throws<InvalidOperationException>(() => Catalog.CategoryOf(typeof(UnclassifiedProbe)));
    }

    [Fact]
    public void PLAN0018_CA122_TipoEnDosCategorias_EsUnaViolacion()
    {
        using var db = TestDbContextFactory.Create();
        var catalog = With(Catalog, global: Catalog.Global.Append(typeof(Client)));

        Assert.Equal(2, catalog.CategoriesOf(typeof(Client)).Count);
        Assert.Contains(catalog.FindViolations(db.Model),
            violation => violation.StartsWith(nameof(Client) + ": en 2 categorías", StringComparison.Ordinal));
        Assert.Throws<InvalidOperationException>(() => catalog.CategoryOf(typeof(Client)));
    }

    [Fact]
    public void PLAN0018_CA122_TipoConOrganizationIdClasificadoComoGlobal_EsUnaViolacion()
    {
        using var db = TestDbContextFactory.Create();
        var catalog = With(Catalog,
            purgeByOrganization: Catalog.PurgeByOrganization.Where(type => type != typeof(Client)),
            global: Catalog.Global.Append(typeof(Client)));

        Assert.Equal(OrganizationDataCategory.Global, catalog.CategoryOf(typeof(Client)));
        Assert.Contains($"{nameof(Client)}: tiene {OrganizationDataCatalog.OrganizationIdProperty} y figura como global.",
            catalog.FindViolations(db.Model));
    }

    [Fact]
    public void PLAN0018_CA122_Catalogo_HijosPorPadreDesvinculadosYPropiosComoDiceElAdr()
    {
        Assert.Contains(new ParentLink(typeof(ProjectFinancials), typeof(Project), nameof(ProjectFinancials.ProjectId)),
            Catalog.PurgeByParent);
        Assert.Contains(new ParentLink(typeof(PilaVerificationDocument), typeof(PilaVerification),
            nameof(PilaVerificationDocument.PilaVerificationId)), Catalog.PurgeByParent);

        Assert.Equal(
            new[] { typeof(BillingCustomer), typeof(BillingInvoice), typeof(BillingPayment), typeof(BillingCredit),
                typeof(CouponRedemption) }.OrderBy(t => t.Name),
            Catalog.Detach.OrderBy(t => t.Name));

        Assert.Equal(OrganizationDataCategory.Owned, Catalog.CategoryOf(typeof(CollaboratorAccessToken)));
        Assert.Equal(OrganizationDataCategory.Owned, Catalog.CategoryOf(typeof(User)));
        Assert.Equal(OrganizationDataCategory.Owned, Catalog.CategoryOf(typeof(OrganizationEntity)));
        Assert.Equal(OrganizationDataCategory.Global, Catalog.CategoryOf(typeof(LegalDocumentArchive)));
        Assert.Equal(OrganizationDataCategory.Global, Catalog.CategoryOf(typeof(Subscription)));
        Assert.Equal(OrganizationDataCategory.Global, Catalog.CategoryOf(typeof(PaymentMethod)));
        Assert.Equal(OrganizationDataCategory.PurgeByOrganization, Catalog.CategoryOf(typeof(AuditLog)));
        Assert.Equal(OrganizationDataCategory.PurgeByOrganization, Catalog.CategoryOf(typeof(OrganizationMember)));
    }

    [Fact]
    public void PLAN0018_CA122_HijoPorPadreConPadreNoPurgado_EsUnaViolacion()
    {
        using var db = TestDbContextFactory.Create();
        var catalog = With(Catalog,
            purgeByParent: Catalog.PurgeByParent
                .Where(link => link.Child != typeof(ProjectFinancials))
                .Append(new ParentLink(typeof(ProjectFinancials), typeof(Country), "NoExiste")));

        var violations = catalog.FindViolations(db.Model);

        Assert.Contains($"{nameof(ProjectFinancials)}: no tiene la clave de su padre NoExiste.", violations);
        Assert.Contains(violations, violation =>
            violation.StartsWith($"{nameof(ProjectFinancials)}: su padre {nameof(Country)}",
                StringComparison.Ordinal));
    }

    // ── Apoyo ───────────────────────────────────────────────────────────────────────────────────────────────────

    private static OrganizationDataCatalog With(
        OrganizationDataCatalog source,
        IEnumerable<Type>?       purgeByOrganization = null,
        IEnumerable<ParentLink>? purgeByParent       = null,
        IEnumerable<Type>?       global              = null)
        => new(purgeByOrganization ?? source.PurgeByOrganization, purgeByParent ?? source.PurgeByParent, source.Detach,
            source.Owned, global ?? source.Global);

    // Tipo de prueba que un plan futuro podría agregar al modelo sin clasificarlo.
    private sealed class UnclassifiedProbe
    {
        public Guid Id             { get; set; }
        public Guid OrganizationId { get; set; }
    }

    private sealed class ContextWithUnclassifiedProbe(DbContextOptions<ZentoryDbContext> options) : ZentoryDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<UnclassifiedProbe>().HasKey(probe => probe.Id);
        }
    }
}
