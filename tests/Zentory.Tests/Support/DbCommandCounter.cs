#pragma warning disable EF1001 // QueryCompiler e IQueryCompiler son internas de EF: ver CountingQueryCompiler.
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.Internal;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Zentory.Infrastructure.Persistence;

namespace Zentory.Tests.Support;

// Contador de comandos de base de un host de pruebas (PLAN-0018 RNF-1, RF-90 d; CA-133): cuenta cada consulta que EF
// ejecuta (ToList, Any, First, ToDictionary...) y cada SaveChanges, en todos los ZentoryDbContext del host. La suite usa
// InMemory, que no emite DbCommand y no pasa por un DbCommandInterceptor: por eso cuenta en el compilador de consultas
// de EF, el punto por el que pasa toda ejecución, que en PostgreSQL es un comando por consulta (sin Include partidos).
//
// Uso: Install en ConfigureTestServices de un WithWebHostBuilder; Reset antes de la petición medida y Count después.
public sealed class DbCommandCounter
{
    private int _count;

    public int Count => Volatile.Read(ref _count);

    public void Reset() => Interlocked.Exchange(ref _count, 0);

    private void Increment() => Interlocked.Increment(ref _count);

    // Envuelve las opciones de ZentoryDbContext que registró AddInfrastructure, sin cambiar el proveedor ni la base.
    public static void Install(IServiceCollection services, DbCommandCounter counter)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(counter);

        var registered = services.Single(d => d.ServiceType == typeof(DbContextOptions<ZentoryDbContext>));
        var create = registered.ImplementationFactory
            ?? throw new InvalidOperationException("Las opciones de ZentoryDbContext no se registraron con AddDbContext.");
        services.Remove(registered);
        services.Add(new ServiceDescriptor(
            typeof(DbContextOptions<ZentoryDbContext>),
            sp => new DbContextOptionsBuilder<ZentoryDbContext>((DbContextOptions<ZentoryDbContext>)create(sp))
                .ReplaceService<IQueryCompiler, CountingQueryCompiler>()
                .AddInterceptors(new SaveChangesCounter(counter))
                .Options,
            registered.Lifetime));
        services.AddSingleton(counter);
    }

    // AddDbContext deja el contenedor de la aplicación en CoreOptionsExtension: de ahí sale el contador del host.
    private static DbCommandCounter? CounterOf(ICurrentDbContext current)
        => current.Context.GetService<IDbContextOptions>()
            .FindExtension<CoreOptionsExtension>()?
            .ApplicationServiceProvider?
            .GetService<DbCommandCounter>();

    private sealed class SaveChangesCounter(DbCommandCounter counter) : SaveChangesInterceptor
    {
        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
        {
            counter.Increment();
            return result;
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            counter.Increment();
            return ValueTask.FromResult(result);
        }
    }

    // QueryCompiler es API interna de EF (EF1001, suprimida en todo el archivo): se usa solo aquí, en el host de pruebas,
    // para contar ejecuciones.
    private sealed class CountingQueryCompiler : QueryCompiler
    {
        private readonly ICurrentDbContext _currentContext;

        public CountingQueryCompiler(
            IQueryContextFactory                       queryContextFactory,
            ICompiledQueryCache                        compiledQueryCache,
            ICompiledQueryCacheKeyGenerator            compiledQueryCacheKeyGenerator,
            IDatabase                                  database,
            IDiagnosticsLogger<DbLoggerCategory.Query> logger,
            ICurrentDbContext                          currentContext,
            IEvaluatableExpressionFilter               evaluatableExpressionFilter,
            IModel                                     model)
            : base(queryContextFactory, compiledQueryCache, compiledQueryCacheKeyGenerator, database, logger,
                currentContext, evaluatableExpressionFilter, model)
            => _currentContext = currentContext;

        public override TResult Execute<TResult>(Expression query)
        {
            CounterOf(_currentContext)?.Increment();
            return base.Execute<TResult>(query);
        }

        public override TResult ExecuteAsync<TResult>(Expression query, CancellationToken cancellationToken = default)
        {
            CounterOf(_currentContext)?.Increment();
            return base.ExecuteAsync<TResult>(query, cancellationToken);
        }
    }
}
