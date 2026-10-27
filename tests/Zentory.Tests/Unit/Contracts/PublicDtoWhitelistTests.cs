using System.Reflection;
using System.Text.RegularExpressions;
using Zentory.Application.ProjectShares.DTOs;
using Zentory.Application.Proposals.DTOs;

namespace Zentory.Tests.Unit.Contracts;

// PLAN-0018 RF-17 (CA-23; SR-1.4): las respuestas públicas por token solo exponen una lista blanca de campos. El test
// recorre las propiedades públicas de cada DTO, incluidas las de los DTO anidados y los elementos de sus listas, y
// exige exactamente la lista blanca: un campo nuevo, aunque sea inocuo, obliga a revisarla aquí.
public sealed partial class PublicDtoWhitelistTests
{
    private static readonly string[] PublicProposalWhitelist =
    [
        "Id", "Title", "Status", "ClientName", "OrganizationName", "OrganizationLogoUrl", "Currency", "TotalAmount",
        "SentAt", "ExpiresAt", "AcceptedAt", "RejectedAt", "ViewCount", "LastViewedAt",
        "Sections[].SectionType", "Sections[].Title", "Sections[].Content", "Sections[].SortOrder", "Sections[].IsVisible",
        "Items[].Description", "Items[].Quantity", "Items[].UnitPrice", "Items[].DiscountPct", "Items[].Total",
        "Items[].SortOrder",
    ];

    private static readonly string[] ProjectSharePublicWhitelist =
    [
        "ProjectName", "ClientName", "Progress", "HealthStatus", "StartDate", "EndDate", "Message",
        "Milestones[].Id", "Milestones[].Name", "Milestones[].Status", "Milestones[].DueDate", "Milestones[].Value",
        "Documents[].Id", "Documents[].Name", "Documents[].Kind", "Documents[].MimeType", "Documents[].Size",
        "Documents[].Url", "Documents[].Source",
    ];

    [Fact]
    public void PLAN0018_CA23_PublicProposalDto_PropiedadesAnidadas_SonExactamenteLaListaBlanca()
        => Assert.Equal(PublicProposalWhitelist.Order(StringComparer.Ordinal), PropertyPaths(typeof(PublicProposalDto)));

    [Fact]
    public void PLAN0018_CA23_ProjectSharePublicDto_PropiedadesAnidadas_SonExactamenteLaListaBlanca()
        => Assert.Equal(ProjectSharePublicWhitelist.Order(StringComparer.Ordinal), PropertyPaths(typeof(ProjectSharePublicDto)));

    [Theory]
    [InlineData(typeof(PublicProposalDto))]
    [InlineData(typeof(ProjectSharePublicDto))]
    public void PLAN0018_CA23_DtoPublico_SinCostosRateCostNitNiEmail(Type dto)
    {
        var forbidden = PropertyPaths(dto).Where(path => IsForbidden(path)).ToList();

        Assert.True(forbidden.Count == 0, $"{dto.Name} expone campos prohibidos: " + string.Join(", ", forbidden));
    }

    [Fact]
    public void PLAN0018_CA23_Recorrido_EncuentraCamposProhibidosEnDtoAnidadosYListas()
    {
        // Control del recorrido y del detector: los campos prohibidos aparecen aunque estén anidados.
        var paths = PropertyPaths(typeof(LeakyDto));

        Assert.Equal(
            new[] { "Member.Email", "Member.Name", "Name", "Rows[].Nit", "Rows[].RateCost", "Rows[].TotalCost" },
            paths);
        Assert.Equal(
            new[] { "Member.Email", "Rows[].Nit", "Rows[].RateCost", "Rows[].TotalCost" },
            paths.Where(path => IsForbidden(path)));
    }

    // ── Apoyo ───────────────────────────────────────────────────────────────────────────────────────────────────

    private sealed record LeakyMember(string Name, string Email);
    private sealed record LeakyRow(decimal RateCost, decimal TotalCost, string Nit);
    private sealed record LeakyDto(string Name, LeakyMember Member, IReadOnlyList<LeakyRow> Rows);

    private static readonly HashSet<string> ForbiddenWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "Cost", "Costs", "Nit", "Email", "Emails",
    };

    // Alguna palabra (PascalCase) de algún segmento del camino nombra un costo (RateCost incluido), un NIT o un email.
    // Por palabras y no por subcadena: UnitPrice no es un NIT.
    private static bool IsForbidden(string path)
        => Word().Matches(path).Any(word => ForbiddenWords.Contains(word.Value));

    [GeneratedRegex("[A-Z][a-z0-9]*|[a-z][a-z0-9]*")]
    private static partial Regex Word();

    // Caminos de todas las propiedades públicas: "Prop", "Nested.Prop" y "List[].Prop". Solo se baja a los tipos de
    // Zentory (los DTO); los tipos del framework (string, decimal, DateTime, Guid…) son hojas.
    private static List<string> PropertyPaths(Type type)
    {
        var paths = new List<string>();
        Collect(type, prefix: "", paths, visiting: []);
        return paths.Order(StringComparer.Ordinal).ToList();
    }

    private static void Collect(Type type, string prefix, List<string> paths, HashSet<Type> visiting)
    {
        if (!visiting.Add(type))
            throw new InvalidOperationException($"Ciclo en {type.Name}: un DTO público no debería referenciarse a sí mismo.");

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                     .Where(p => p.Name != "EqualityContract"))
        {
            var path = prefix + property.Name;
            var element = ElementType(property.PropertyType);
            var target = element ?? Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;

            if (IsZentoryType(target))
                Collect(target, path + (element is null ? "." : "[]."), paths, visiting);
            else
                paths.Add(path);
        }

        visiting.Remove(type);
    }

    private static Type? ElementType(Type type)
    {
        if (type == typeof(string))
            return null;
        if (type.IsArray)
            return type.GetElementType();
        return type.GetInterfaces().Append(type)
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            .Select(i => i.GetGenericArguments()[0])
            .FirstOrDefault();
    }

    private static bool IsZentoryType(Type type)
        => type.Namespace?.StartsWith("Zentory.", StringComparison.Ordinal) == true && !type.IsEnum;
}
