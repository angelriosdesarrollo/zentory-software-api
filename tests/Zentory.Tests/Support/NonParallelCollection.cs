namespace Zentory.Tests.Support;

// Colección para los tests que modifican estado global del proceso (variables de entorno): xUnit la ejecuta sola,
// después de las colecciones paralelas, así ningún otro host arranca mientras el entorno está alterado.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class NonParallelCollection
{
    public const string Name = "NonParallel";
}
