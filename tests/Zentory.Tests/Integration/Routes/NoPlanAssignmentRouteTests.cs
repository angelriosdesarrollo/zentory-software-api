using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.ActionConstraints;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;
using Zentory.Application.Billing.Commands;
using Zentory.Application.Common.Interfaces;
using Zentory.Domain.Constants;
using Zentory.Domain.Entities.Billing;
using Zentory.Infrastructure.Persistence;
using Zentory.Infrastructure.Persistence.Purge;
using Zentory.Tests.Support;
using static Zentory.Tests.Support.AuthSessionApi;

namespace Zentory.Tests.Integration.Routes;

// PLAN-0018 CA-T24-1 (parte de CA-44, se cierra en T30c; RF-39, RF-73, SPEC-0001 CA-11; DEC-0101): ninguna acción de
// los controladores asigna ni cambia el plan ni escribe Subscriptions. Cuatro capas, cada una cubre lo que la otra no:
//
//   A. Comportamiento (host de pruebas). Un interceptor de SaveChanges y de comandos SQL registra todo cambio a una
//      Subscription. Se recorren TODAS las acciones POST/PUT/PATCH/DELETE enumeradas con IActionDescriptorCollectionProvider,
//      con el token de un owner studio y un cuerpo mínimo, y además se compara la tabla entera antes y después. Cubre
//      cualquier forma de escritura que llegue a SaveChanges (Entry, Attach, State, Set<T>, Find(typeof), repositorios
//      genéricos…) en los caminos que un cuerpo mínimo alcanza. No cubre lo que exige un cuerpo válido concreto ni el SQL
//      crudo (InMemory no lo ejecuta): eso lo cubren B y C. El alta (registro y Google) se prueba con cuerpos válidos.
//   B. Escaneo de src/ (código sin comentarios). Todo archivo que nombra Subscription/Subscriptions o la tabla
//      subscriptions (también dentro de SQL) está clasificado: escritores (solo AssignPlanBySeedCommand y
//      DemoScenarioRunner), lectores (sin ninguna forma de escritura ni SaveChanges) o declaraciones (solo líneas de
//      declaración). Un archivo nuevo que la nombre rompe el test hasta clasificarlo.
//   C. Acceso genérico y SQL crudo en src/: Set<T>, Set(typeof), Find(typeof), Model.FindEntityType, operaciones sin
//      tipo del contexto (db.Add/Attach/Update/Remove/Entry) y cualquier SQL crudo (ExecuteSql, SqlQuery, FromSql,
//      comandos ADO) solo existen en la lista cerrada de abajo, y la purga deja Subscription como global (no la toca).
//   D. Alcance: los controladores enumerados no nombran a los escritores, AssignPlanBySeedCommand solo lo envía la CLI y
//      los escenarios demo solo los usan el arranque y db init. Ninguna ruta que cambia estado tiene segmento de plan,
//      suscripción o tier, ni un cuerpo con un campo de plan.
//
// Límite conocido: SQL armado por partes en tiempo de ejecución (p. ej. "subscr" + "iptions") no lo ve B; lo frena C,
// porque ningún archivo de src/ puede ejecutar SQL crudo sin entrar en la lista cerrada.
public sealed class NoPlanAssignmentRouteTests : IClassFixture<ZentoryApiFactory>
{
    private static readonly string[] MutatingVerbs = ["POST", "PUT", "PATCH", "DELETE"];

    private static readonly string[] PlanSegments =
    [
        "plan", "plans", "subscription", "subscriptions", "assign-plan", "upgrade", "downgrade", "billing", "tier",
        "tiers",
    ];

    private static readonly string[] PlanFields =
        ["Plan", "PlanId", "PlanName", "SubscriptionId", "BillingPlanId", "Tier", "TierId"];

    // ── Clasificación de src/ (capa B) ──────────────────────────────────────────────────────────────────────────

    private const string AssignPlanFile = "src/Zentory.Application/Billing/Commands/AssignPlanBySeedCommand.cs";
    private const string DemoRunnerFile = "src/Zentory.Infrastructure/Persistence/Seeding/Demo/DemoScenarioRunner.cs";
    private const string PurgerFile     = "src/Zentory.Infrastructure/Persistence/Purge/OrganizationDataPurger.cs";
    private const string SeedCliFile    = "src/Zentory.API/Cli/SeedCli.cs";

    // Los únicos que escriben Subscriptions (DEC-0101), y por qué ninguna acción llega a ellos.
    private static readonly Dictionary<string, string> Writers = new(StringComparer.Ordinal)
    {
        [AssignPlanFile] = "seed assign-plan: solo lo envía la CLI (SeedCli)",
        [DemoRunnerFile] = "escenarios demo: solo db init --demo y el arranque de Development",
    };

    // Leen Subscriptions para resolver o mostrar el plan; no pueden contener ninguna forma de escritura.
    private static readonly string[] Readers =
    [
        "src/Zentory.Infrastructure/Services/PlanResolutionService.cs",
        "src/Zentory.Application/Organization/Queries/GetOrgPlanQuery.cs",
    ];

    // Declaran el tipo o lo clasifican; cada línea que lo nombra debe ser una declaración.
    private static readonly string[] Declarations =
    [
        "src/Zentory.Domain/Entities/Billing/Subscription.cs",
        "src/Zentory.Application/Common/Interfaces/IZentoryDbContext.cs",
        "src/Zentory.Infrastructure/Persistence/ZentoryDbContext.cs",
        "src/Zentory.Infrastructure/Persistence/Configurations/Billing/SubscriptionConfiguration.cs",
        "src/Zentory.Infrastructure/Persistence/Purge/OrganizationDataCatalog.cs",
    ];

    private static readonly Regex[] DeclarationLines =
    [
        new(@"^\s*(public\s+)?DbSet\s*<\s*Subscription\s*>\s+Subscriptions\s*\{\s*get;\s*(set;\s*)?\}( = default!;)?\s*$"),
        new(@"\btypeof\s*\(\s*Subscription\s*\)\s*,"),
        new(@"\bIEntityTypeConfiguration\s*<\s*Subscription\s*>"),
        new(@"\bEntityTypeBuilder\s*<\s*Subscription\s*>"),
        new(@"builder\.ToTable\(""subscriptions"", ""billing""\);"),
    ];

    // Acceso genérico o SQL crudo permitido (capa C), con el motivo.
    private static readonly Dictionary<string, string> GenericAccess = new(StringComparer.Ordinal)
    {
        [DemoRunnerFile] = "escenarios demo (escritor de la lista)",
        [PurgerFile]     = "purga: recorre OrganizationDataCatalog, que clasifica Subscription como global",
        ["src/Zentory.Application/Auth/Services/AuthSessionIssuer.cs"] =
            "reintento por concurrencia del login: solo reajusta las entradas que el login ya tenía (User, RefreshToken)",
        ["src/Zentory.Infrastructure/Persistence/Interceptors/AuditInterceptor.cs"] =
            "auditoría: lee Model.FindEntityType para el nombre de la tabla; no cambia entradas",
    };

    private static readonly string[] WriterNames =
        [nameof(AssignPlanBySeedCommand), "AssignPlanBySeed", "SeedCli", "DemoScenarioRunner", "DemoScenario", "DemoSeed"];

    private const string OwnerPassword = "Owner-No-Plan-Route-2026!";

    private readonly ZentoryApiFactory _factory;

    private readonly ITestOutputHelper _output;

    public NoPlanAssignmentRouteTests(ZentoryApiFactory factory, ITestOutputHelper output)
    {
        _factory = factory;
        _output  = output;
    }

    // ── A. Comportamiento en el host ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA44_InterceptorDeSubscriptions_EstaConectadoYRegistraUnCambio()
    {
        // Control: sin esto, 0 cambios en el recorrido podría significar solo que el interceptor no está en el pipeline.
        var recorder = new SubscriptionWriteRecorder();
        using var host = WithRecorder(recorder);
        var tenants = await new TenantBuilder(_factory).BuildAsync();

        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
            var subscription = await db.Subscriptions.FirstAsync(s => s.UserId == tenants.A.Owner.UserId);
            db.Entry(subscription).Property(s => s.Currency).CurrentValue = "EUR";
            await db.SaveChangesAsync();
        }

        Assert.Contains(recorder.Writes, write => write.StartsWith(nameof(EntityState.Modified), StringComparison.Ordinal));
    }

    [Fact]
    public async Task PLAN0018_CA44_TodasLasAccionesQueCambianEstado_ConTokenDeOwnerYCuerpoMinimo_NoTocanSubscriptions()
    {
        var recorder = new SubscriptionWriteRecorder();
        using var host = WithRecorder(recorder);
        var tenants = await new TenantBuilder(_factory).ForA(a => a.WithOwnerPassword(OwnerPassword)).BuildAsync();
        var actions = MutatingActions(host);
        var before = await SubscriptionsSnapshotAsync(host);

        var outcomes = new List<string>();
        foreach (var action in actions)
        {
            using var http = host.CreateClient();
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tenants.TokenFor(tenants.A));
            outcomes.Add($"{await SendMinimalAsync(http, action, tenants.A.OrganizationId)} {Describe(action)}");
        }

        // Evidencia del recorrido: cuántas acciones respondieron cada estado.
        foreach (var group in outcomes.GroupBy(o => o.Split(' ')[0]).OrderBy(g => g.Key, StringComparer.Ordinal))
            _output.WriteLine($"{group.Key}: {group.Count()}");
        foreach (var outcome in outcomes) _output.WriteLine(outcome);

        Assert.True(actions.Count > 50, $"Solo se recorrieron {actions.Count} acciones que cambian estado.");
        Assert.Equal(actions.Count, outcomes.Count);
        Assert.True(recorder.Writes.IsEmpty,
            "Acciones que escribieron Subscriptions: " + string.Join(", ", recorder.Writes) +
            "\nRecorrido: " + string.Join("; ", outcomes));
        Assert.True(before == await SubscriptionsSnapshotAsync(host),
            "La tabla Subscriptions cambió durante el recorrido de las acciones.");
    }

    [Fact]
    public async Task PLAN0018_CA44_ProponerYAceptarLaTransferencia_CambianElOwnerYElPlanResueltoSinEscribirSubscriptions()
    {
        // RF-64, RF-83 d (T30c): proponer no cambia nada; aceptar cambia el owner y, en consecuencia, el plan resuelto de la
        // organización (studio del owner anterior → pro del destino), pero ninguna de las dos escribe Subscriptions.
        const string memberPassword = "Miembro-No-Plan-Route-2026!";
        var tenants = await OwnershipTransferApi.OrganizationAsync(_factory, _factory.Services, OwnerPassword, memberPassword,
            configureA: a => a.WithPlan(Plan.Studio));
        var target = tenants.A.Member(OrgRole.Admin);
        await OwnershipTransferApi.SeedOwnSubscriptionAsync(_factory.Services, target, tenants.A.Organization, Plan.Pro);
        var recorder = new SubscriptionWriteRecorder();
        using var host = WithRecorder(recorder);
        var before = await SubscriptionsSnapshotAsync(host);
        using var ownerHttp  = OrganizationDeletionApi.ClientFor(host, tenants, tenants.A);
        using var targetHttp = OrganizationDeletionApi.ClientFor(host, tenants, tenants.A, target);

        var transferId = await OwnershipTransferApi.ProposeTransferIdAsync(ownerHttp, target.UserId, OwnerPassword);
        Assert.Equal(Plan.Studio, await ResolvedPlanAsync(host, tenants.A.OrganizationId));
        using (var accept = await OwnershipTransferApi.AcceptAsync(targetHttp, transferId, memberPassword))
            Assert.Equal(System.Net.HttpStatusCode.NoContent, accept.StatusCode);

        Assert.Equal(Plan.Pro, await ResolvedPlanAsync(host, tenants.A.OrganizationId));
        Assert.True(recorder.Writes.IsEmpty, "La transferencia escribió Subscriptions: " + string.Join(", ", recorder.Writes));
        Assert.True(before == await SubscriptionsSnapshotAsync(host), "La transferencia cambió la tabla Subscriptions.");
    }

    // Plan efectivo de la organización: el del owner actual (RF-73).
    private static async Task<string> ResolvedPlanAsync(WebApplicationFactory<Program> host, Guid organizationId)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        var ownerId = await db.Organizations.Where(o => o.OrganizationId == organizationId).Select(o => o.OwnerId).SingleAsync();
        return await scope.ServiceProvider.GetRequiredService<IPlanResolutionService>().ResolveForOwnerAsync(ownerId);
    }

    [Fact]
    public async Task PLAN0018_CA44_Registro_NoEscribeSubscriptionsNiClienteDeCobroYElPlanResueltoEsFree()
    {
        // DEC-0101: el alta ya no crea la suscripción free en trialing; sin suscripción vigente el plan es free.
        var recorder = new SubscriptionWriteRecorder();
        using var host = WithRecorder(recorder);
        var email = $"alta-{Guid.NewGuid():N}@zentory.test";
        var before = await SubscriptionsSnapshotAsync(host);

        using var http = host.CreateClient();
        using var response = await http.PostAsJsonAsync("/api/v1/auth/register", new
        {
            email,
            password      = OwnerPassword,
            firstName     = "Alta",
            lastName      = "Free",
            orgName       = $"Org alta {Guid.NewGuid():N}",
            legalType     = LegalType.Freelance,
            acceptedTerms = true,
        });

        var session = await ReadSessionAsync(response, System.Net.HttpStatusCode.Created);
        await AssertFreeOwnerWithoutBillingAsync(host, recorder, before, Guid.Parse(session.AccessToken.Subject), session);
    }

    [Fact]
    public async Task PLAN0018_CA44_AltaConGoogle_NoEscribeSubscriptionsNiClienteDeCobroYElPlanResueltoEsFree()
    {
        var recorder = new SubscriptionWriteRecorder();
        var google = new FakeGoogleTokenValidator();
        using var host = WithGoogle(_factory, google).WithWebHostBuilder(b => b.ConfigureTestServices(services =>
            services.AddSingleton<IInterceptor>(recorder)));
        var subject = $"google-{Guid.NewGuid():N}";
        var before = await SubscriptionsSnapshotAsync(host);

        var idToken = google.Register(new GoogleUserInfo(subject, $"alta-g-{Guid.NewGuid():N}@zentory.test", "Alta", "Google",
            EmailVerified: true));
        using var http = host.CreateClient();
        using var response = await http.PostAsJsonAsync(GoogleEndpoint, new { idToken });

        var session = await ReadSessionAsync(response);
        await AssertFreeOwnerWithoutBillingAsync(host, recorder, before, Guid.Parse(session.AccessToken.Subject), session);
    }

    // ── B. Clasificación de quien nombra Subscription en src/ ───────────────────────────────────────────────────

    [Fact]
    public void PLAN0018_CA44_ArchivosDeSrcQueNombranSubscription_EstanTodosClasificados()
    {
        var classified = Writers.Keys.Concat(Readers).Concat(Declarations).ToHashSet(StringComparer.Ordinal);

        var unclassified = SourceFiles("src").Where(file => NamesSubscriptions(Code(file)))
            .Where(file => !classified.Contains(file)).ToList();

        Assert.True(unclassified.Count == 0,
            "Archivos que nombran Subscription o la tabla subscriptions sin clasificar: " + string.Join(", ", unclassified));
    }

    [Fact]
    public void PLAN0018_CA44_EscritoresDeSubscriptions_SonSoloAssignPlanBySeedYLosEscenariosDemo()
    {
        // Las declaraciones las vigila, línea por línea y más estricto, el test de declaraciones.
        var writers = SourceFiles("src")
            .Where(file => !Declarations.Contains(file))
            .Where(file => WritesSubscriptions(Code(file)))
            .Order(StringComparer.Ordinal);

        Assert.Equal(Writers.Keys.Order(StringComparer.Ordinal), writers);
    }

    [Fact]
    public void PLAN0018_CA44_LectoresDeSubscriptions_NoTienenNingunaFormaDeEscrituraNiSaveChanges()
    {
        Assert.All(Readers, file =>
        {
            var code = Code(file);
            Assert.True(NamesSubscriptions(code), $"{file} ya no lee Subscriptions: sácalo de la lista.");
            Assert.False(WritesSubscriptions(code), $"{file} escribe Subscriptions.");
            Assert.DoesNotMatch(new Regex(@"\bSaveChanges(Async)?\s*\("), code);
            Assert.DoesNotMatch(new Regex(@"\bAsTracking\s*\("), code);
        });
    }

    [Fact]
    public void PLAN0018_CA44_Declaraciones_SoloNombranSubscriptionEnLineasDeDeclaracion()
    {
        Assert.All(Declarations.Where(file => !file.EndsWith("/Subscription.cs", StringComparison.Ordinal)), file =>
        {
            var offending = Code(file).Split('\n')
                .Where(line => NamesSubscriptions(line) && !DeclarationLines.Any(pattern => pattern.IsMatch(line)))
                .ToList();
            Assert.True(offending.Count == 0, $"{file} nombra Subscription fuera de una declaración: " +
                string.Join(" | ", offending.Select(l => l.Trim())));
        });
    }

    [Theory]
    [InlineData("_db.Subscriptions.Add(subscription);")]
    [InlineData("db.Subscriptions.Update(s);")]
    [InlineData("_db.Subscriptions.RemoveRange(old);")]
    [InlineData("var s = Subscription.Create(userId, customerId, planId);")]
    [InlineData("db.Set<Subscription>().Add(s);")]
    [InlineData("await _db.Subscriptions.Where(s => s.UserId == id).ExecuteUpdateAsync(u => u.SetProperty(s => s.Status, x));")]
    [InlineData("var sub = await _db.Subscriptions.FirstAsync(ct); sub.Cancel(\"x\");")]
    // Sondas de QA (intento 1):
    [InlineData("var s = await db.Subscriptions.FirstAsync(); db.Subscriptions.Entry(s).Property(p => p.Status).CurrentValue = \"x\";")]
    [InlineData("var s = await db.Subscriptions.FirstAsync(); db.Entry(s).Property(\"Status\").CurrentValue = \"x\";")]
    [InlineData("await db.Database.ExecuteSqlRawAsync(\"UPDATE billing.subscriptions SET status = 'cancelled'\");")]
    [InlineData("var s = await db.FindAsync<Subscription>(id); db.Attach(s); db.Entry(s).State = EntityState.Deleted;")]
    [InlineData("var e = db.Find(typeof(Subscription), id); db.Remove(e!);")]
    [InlineData("db.AddRange(Activator.CreateInstance(typeof(Subscription), true)!);")]
    public void PLAN0018_CA44_DetectorDeEscritura_EncuentraLasFormasConocidas(string code)
    {
        Assert.True(WritesSubscriptions(code), $"El detector no encontró la escritura en: {code}");
    }

    [Theory]
    [InlineData("var plan = await _db.Subscriptions.Where(s => s.UserId == id).ToListAsync(ct);")]
    [InlineData("var rows = await _db.Subscriptions.AnyAsync(s => s.UserId == id, ct);")]
    public void PLAN0018_CA44_DetectorDeEscritura_IgnoraLasLecturas(string code)
    {
        Assert.False(WritesSubscriptions(code), $"El detector tomó una lectura por escritura: {code}");
    }

    // ── C. Acceso genérico y SQL crudo ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void PLAN0018_CA44_AccesoGenericoOSqlCrudoEnSrc_SoloEnLaListaCerrada()
    {
        var found = SourceFiles("src").Where(file => UsesGenericAccess(Code(file))).Order(StringComparer.Ordinal);

        Assert.Equal(GenericAccess.Keys.Order(StringComparer.Ordinal), found);
    }

    [Theory]
    // Sonda de QA: repositorio genérico con Set<T>().
    [InlineData("public Task AddAsync<T>(T e) where T : class { _ctx.Set<T>().Add(e); return _ctx.SaveChangesAsync(); }")]
    [InlineData("_context.Set(type).Add(entity);")]
    [InlineData("var e = await _db.FindAsync(typeof(Subscription), id);")]
    [InlineData("var t = _db.Model.FindEntityType(name);")]
    [InlineData("_db.Entry(entity).State = EntityState.Modified;")]
    [InlineData("dbContext.Remove(entity);")]
    [InlineData("await _db.Database.ExecuteSqlRawAsync(sql);")]
    [InlineData("await Database.ExecuteSqlInterpolatedAsync($\"DELETE FROM x\");")]
    [InlineData("_db.Database.SqlQueryRaw<int>(sql);")]
    [InlineData("var c = connection.CreateCommand(); c.CommandText = sql;")]
    [InlineData("entry.State = EntityState.Deleted;")]
    public void PLAN0018_CA44_DetectorDeAccesoGenerico_EncuentraLasFormasConocidas(string code)
    {
        Assert.True(UsesGenericAccess(code), $"El detector no encontró el acceso genérico en: {code}");
    }

    [Fact]
    public void PLAN0018_CA44_Purga_ClasificaSubscriptionComoGlobalYNoLaToca()
    {
        Assert.Equal([OrganizationDataCategory.Global], OrganizationDataCatalog.Default.CategoriesOf(typeof(Subscription)));
    }

    // ── D. Alcance y rutas ──────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void PLAN0018_CA44_AccionesDelHost_EnumeradasConIActionDescriptorCollectionProvider_NoEstanVacias()
    {
        var actions = ControllerActions(_factory);

        Assert.True(actions.Count > 50, $"Solo se enumeraron {actions.Count} acciones.");
        Assert.Contains(actions, a => a.ControllerTypeInfo.Name == "AuthController");
    }

    [Fact]
    public void PLAN0018_CA44_AccionesQueCambianEstado_NingunaRutaTieneSegmentoDePlanSuscripcionOTier()
    {
        var offending = MutatingActions(_factory)
            .Where(action => Segments(action).Any(segment => PlanSegments.Contains(segment, StringComparer.OrdinalIgnoreCase)))
            .Select(Describe)
            .ToList();

        Assert.True(offending.Count == 0, "Acciones que cambian estado con ruta de plan, suscripción o tier: " +
            string.Join(", ", offending));
    }

    [Fact]
    public void PLAN0018_CA44_AccionesQueCambianEstado_NingunCuerpoOFormularioTraeUnCampoDePlan()
    {
        var offending = MutatingActions(_factory)
            .SelectMany(action => action.Parameters
                .Where(p => p.BindingInfo?.BindingSource is { } source
                    && (source == BindingSource.Body || source == BindingSource.Form))
                .SelectMany(p => PlanProperties(p.ParameterType))
                .Select(property => $"{Describe(action)} → {property}"))
            .ToList();

        Assert.True(offending.Count == 0, "Cuerpos con un campo de plan: " + string.Join(", ", offending));
    }

    [Fact]
    public void PLAN0018_CA44_ControladoresEnumerados_NoNombranAssignPlanBySeedNiLosEscenariosDemo()
    {
        var offending = ControllerFiles()
            .SelectMany(file => WriterNames
                .Where(name => Regex.IsMatch(Code(file), $@"\b{Regex.Escape(name)}"))
                .Select(name => $"{file}: {name}"))
            .ToList();

        Assert.True(offending.Count == 0, "Controladores que alcanzan un escritor de Subscriptions: " +
            string.Join(", ", offending));
    }

    [Fact]
    public void PLAN0018_CA44_AssignPlanBySeedCommand_SoloLoEnviaLaCli()
    {
        var senders = SourceFiles("src")
            .Where(file => file != AssignPlanFile)
            .Where(file => Regex.IsMatch(Code(file), $@"\b{nameof(AssignPlanBySeedCommand)}\b"))
            .ToList();

        Assert.Equal([SeedCliFile], senders);
        Assert.DoesNotContain(ControllerActions(_factory),
            action => action.ControllerTypeInfo.Namespace?.EndsWith(".Cli") == true);
    }

    [Fact]
    public void PLAN0018_CA44_EscenariosDemo_SoloLosUsanElArranqueYDbInit()
    {
        var users = SourceFiles("src")
            .Where(file => !file.StartsWith("src/Zentory.Infrastructure/Persistence/Seeding/", StringComparison.Ordinal))
            .Where(file => Regex.IsMatch(Code(file), @"\bDemoScenarioRunner\b|\bDemoScenarios?\b"))
            .ToList();

        Assert.All(users, file => Assert.DoesNotContain("/Controllers/", file, StringComparison.Ordinal));
    }

    // ── Detectores ──────────────────────────────────────────────────────────────────────────────────────────────

    // Nombra la entidad o su DbSet (identificador) o la tabla subscriptions (en cualquier capitalización, p. ej. SQL).
    private static bool NamesSubscriptions(string code)
        => Regex.IsMatch(code, @"\bSubscriptions?\b") || Regex.IsMatch(code, @"\bsubscriptions\b", RegexOptions.IgnoreCase);

    private static readonly Regex SubscriptionMutator = new(
        $@"\.\s*({string.Join('|', typeof(Subscription)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName)
            .Select(m => m.Name))})\s*\(");

    private static readonly Regex[] WritePatterns =
    [
        new(@"\bSubscriptions\s*\.\s*(Add|AddAsync|AddRange|AddRangeAsync|Update|UpdateRange|Remove|RemoveRange|Attach|AttachRange|Entry|Find|FindAsync|Local)\b"),
        new(@"\bSubscriptions\b[^;]*\.\s*Execute(Update|Delete)(Async)?\s*\("),
        new(@"\bSubscription\s*\.\s*Create\s*\("),
        // Set<Subscription>(), Find<Subscription>, FindAsync<Subscription>, Entry<Subscription>…; la declaración DbSet no.
        new(@"\b(Set|Find|FindAsync|Entry|Add|AddAsync|Attach|Update|Remove)\s*<\s*Subscription\s*>"),
        new(@"\btypeof\s*\(\s*Subscription\s*\)\s*[^,\s]"),
        // typeof(Subscription) como argumento (Find(typeof(Subscription), id), CreateInstance(typeof(Subscription), …));
        // una lista de tipos (typeof(Subscription), typeof(…)) es una declaración.
        new(@"\btypeof\s*\(\s*Subscription\s*\)\s*,\s*(?!typeof\b)\w"),
        // SQL que nombra la tabla.
        new(@"(?i)\b(update|insert\s+into|delete\s+from|truncate|merge\s+into|alter\s+table|drop\s+table)\b[^""]*\bsubscriptions\b"),
    ];

    // Escribe Subscriptions si usa una forma de escritura de su DbSet, crea la entidad, la toma por tipo, la nombra en SQL
    // de escritura, o si la lee y además llama a un mutador de la entidad (salen por reflexión) o cambia una entrada del
    // ChangeTracker (Entry/State/CurrentValue/Attach/Remove sin tipo).
    private static bool WritesSubscriptions(string code)
        => WritePatterns.Any(pattern => pattern.IsMatch(code))
        || (NamesSubscriptions(code) && (SubscriptionMutator.IsMatch(code) || UntypedChangeTracker.IsMatch(code)));

    private static readonly Regex UntypedChangeTracker = new(
        @"\.\s*(Entry|Attach|AttachRange|Remove|RemoveRange|Update|UpdateRange|Add|AddRange|AddAsync|AddRangeAsync)\s*\(|\.State\s*=[^=]|\.CurrentValue\s*=[^=]");

    private static readonly Regex[] GenericPatterns =
    [
        new(@"\bSet\s*<\s*T\w*\s*>"),
        new(@"\bSet\s*\(\s*(typeof\s*\(|\w+\s*\))"),
        new(@"\b(Find|FindAsync)\s*\(\s*typeof\s*\("),
        new(@"\.Model\s*\.\s*(FindEntityType|GetEntityTypes)\s*\("),
        new(@"\b\w*(db|Db|context|Context|ctx|Ctx)\s*\.\s*(Add|AddAsync|AddRange|AddRangeAsync|Attach|AttachRange|Update|UpdateRange|Remove|RemoveRange|Entry)\s*[(<]"),
        new(@"\.State\s*=\s*EntityState\."),
        new(@"\b(ExecuteSql|ExecuteSqlRaw|ExecuteSqlInterpolated|SqlQuery|SqlQueryRaw|FromSql|FromSqlRaw|FromSqlInterpolated)\w*\s*[(<]"),
        new(@"\b(CreateCommand|CommandText|NpgsqlCommand|ExecuteNonQuery|ExecuteScalar|ExecuteReader)\b"),
    ];

    private static bool UsesGenericAccess(string code) => GenericPatterns.Any(pattern => pattern.IsMatch(code));

    // ── Apoyo del recorrido ─────────────────────────────────────────────────────────────────────────────────────

    private WebApplicationFactory<Program> WithRecorder(SubscriptionWriteRecorder recorder)
        => _factory.WithWebHostBuilder(b => b.ConfigureTestServices(services =>
            services.AddSingleton<IInterceptor>(recorder)));

    // Petición con los parámetros de ruta rellenados (guid → la organización A o uno nuevo; int → 1; resto → "x") y,
    // salvo DELETE, un cuerpo JSON vacío. Devuelve el estado o la excepción, que no detienen el recorrido.
    private static async Task<string> SendMinimalAsync(HttpClient http, ControllerActionDescriptor action, Guid organizationId)
    {
        var path = "/" + Regex.Replace(action.AttributeRouteInfo?.Template ?? string.Empty,
            @"\{\*{0,2}(?<name>[^}:=?]+)(?<constraint>:[^}=?]*)?(=[^}]*)?\??\}", match =>
            {
                var constraint = match.Groups["constraint"].Value;
                if (constraint.Contains("int", StringComparison.OrdinalIgnoreCase)
                    || constraint.Contains("long", StringComparison.OrdinalIgnoreCase)) return "1";
                if (constraint.Contains("guid", StringComparison.OrdinalIgnoreCase)
                    || match.Groups["name"].Value.EndsWith("id", StringComparison.OrdinalIgnoreCase))
                    return match.Groups["name"].Value.Contains("org", StringComparison.OrdinalIgnoreCase)
                        ? organizationId.ToString()
                        : Guid.NewGuid().ToString();
                return "x";
            }).TrimStart('~', '/');

        var method = HttpMethods(action).First(m => MutatingVerbs.Contains(m, StringComparer.OrdinalIgnoreCase));
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (!string.Equals(method, "DELETE", StringComparison.OrdinalIgnoreCase))
            request.Content = new StringContent("{}", Encoding.UTF8, "application/json");

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        try
        {
            using var response = await http.SendAsync(request, timeout.Token);
            return ((int)response.StatusCode).ToString();
        }
        catch (Exception exception)
        {
            return exception.GetType().Name;
        }
    }

    // Todas las filas de Subscriptions, serializadas: cualquier cambio de cualquier columna cambia la foto.
    private static async Task<string> SubscriptionsSnapshotAsync(WebApplicationFactory<Program> host)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        var rows = await db.Subscriptions.AsNoTracking().OrderBy(s => s.Id).ToListAsync();
        return JsonSerializer.Serialize(rows);
    }

    private static async Task AssertFreeOwnerWithoutBillingAsync(
        WebApplicationFactory<Program> host, SubscriptionWriteRecorder recorder, string before, Guid userId,
        IssuedSession session)
    {
        Assert.True(recorder.Writes.IsEmpty, "El alta escribió Subscriptions: " + string.Join(", ", recorder.Writes));
        Assert.True(before == await SubscriptionsSnapshotAsync(host), "El alta cambió la tabla Subscriptions.");
        Assert.Equal(Plan.Free, session.Claim("plan"));

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        Assert.False(await db.Subscriptions.AnyAsync(s => s.UserId == userId), "El alta creó una suscripción.");
        var ownedOrganizations = await db.Organizations.Where(o => o.OwnerId == userId).Select(o => o.OrganizationId)
            .ToListAsync();
        Assert.Single(ownedOrganizations);
        Assert.False(await db.BillingCustomers.AnyAsync(c => c.OrganizationId == ownedOrganizations[0]),
            "El alta creó un cliente de cobro.");
        Assert.Equal(Plan.Free,
            await scope.ServiceProvider.GetRequiredService<IPlanResolutionService>().ResolveForOwnerAsync(userId));
    }

    private static List<ControllerActionDescriptor> ControllerActions(WebApplicationFactory<Program> host)
        => host.Services.GetRequiredService<IActionDescriptorCollectionProvider>()
            .ActionDescriptors.Items.OfType<ControllerActionDescriptor>().ToList();

    private static List<ControllerActionDescriptor> MutatingActions(WebApplicationFactory<Program> host)
        => ControllerActions(host)
            .Where(action => HttpMethods(action).Intersect(MutatingVerbs, StringComparer.OrdinalIgnoreCase).Any())
            .ToList();

    // Archivo fuente de cada controlador enumerado: el que declara su clase. Todos deben encontrarse.
    private List<string> ControllerFiles()
    {
        var sources = SourceFiles("src/Zentory.API").Select(file => (File: file, Code: Code(file))).ToList();
        return ControllerActions(_factory)
            .Select(action => action.ControllerTypeInfo.Name)
            .Distinct(StringComparer.Ordinal)
            .Select(name => sources.FirstOrDefault(s => Regex.IsMatch(s.Code, $@"\bclass\s+{name}\b")).File
                ?? throw new InvalidOperationException($"No se encontró el archivo del controlador {name}."))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private static IEnumerable<string> HttpMethods(ControllerActionDescriptor action)
        => action.ActionConstraints?.OfType<HttpMethodActionConstraint>().SelectMany(c => c.HttpMethods) ?? [];

    private static IEnumerable<string> Segments(ControllerActionDescriptor action)
        => (action.AttributeRouteInfo?.Template ?? string.Empty)
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(segment => !segment.StartsWith('{'));

    private static IEnumerable<string> PlanProperties(Type type)
        => type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => PlanFields.Contains(p.Name, StringComparer.OrdinalIgnoreCase))
            .Select(p => $"{type.Name}.{p.Name}");

    private static string Describe(ControllerActionDescriptor action)
        => $"{string.Join('|', HttpMethods(action))} {action.AttributeRouteInfo?.Template} " +
           $"({action.ControllerTypeInfo.Name}.{action.ActionName})";

    private static IEnumerable<string> SourceFiles(string folder)
        => Directory.GetFiles(Path.Combine(RepoRoot.Path, folder), "*.cs", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(RepoRoot.Path, path).Replace('\\', '/'))
            .Where(file => !file.Split('/').Any(part => part is "bin" or "obj" or "Migrations"));

    // Código sin comentarios de línea, para que un comentario que nombra un escritor no cuente.
    private static string Code(string file)
        => string.Join("\n", File.ReadAllLines(Path.Combine(RepoRoot.Path, file))
            .Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal))
            .Select(line => Regex.Replace(line, @"\s//\s.*$", string.Empty)));
}
