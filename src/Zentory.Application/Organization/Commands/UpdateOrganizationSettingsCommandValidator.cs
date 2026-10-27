using FluentValidation;
using Zentory.Application.Organization.Queries;
using Zentory.Domain.Constants;

namespace Zentory.Application.Organization.Commands;

// Lista blanca de PATCH /organization/settings (PLAN-0018 RF-89, D-46, SR-0018.45). El validador corre antes del
// handler (ValidationBehavior), así que una sola clave fuera de la lista deja la petición entera sin efecto.
public sealed class UpdateOrganizationSettingsCommandValidator : AbstractValidator<UpdateOrganizationSettingsCommand>
{
    public const int MaxKeys = 100;

    // Comparación ordinal: las filas se guardan con la clave tal cual llega, así que "Bank.Name" sería otra clave.
    public static readonly IReadOnlySet<string> AllowedKeys = new HashSet<string>(
        DefaultOrgSettings.All.Keys.Concat(OrgSettingKey.Profile).Concat(OrgSettingKey.Bank),
        StringComparer.Ordinal);

    public UpdateOrganizationSettingsCommandValidator()
    {
        RuleFor(x => x.Settings)
            .NotNull()
            .WithMessage("El cuerpo debe ser un objeto con las claves a actualizar.");

        RuleFor(x => x.Settings)
            .Must(s => s.Count <= MaxKeys)
            .WithMessage($"Se permiten como máximo {MaxKeys} claves por petición.")
            .When(x => x.Settings is not null);

        // Con más de MaxKeys basta el error de cantidad: no se lista cada clave de una petición abusiva.
        RuleForEach(x => x.Settings.Keys)
            .Must(key => AllowedKeys.Contains(key))
            .WithName(nameof(UpdateOrganizationSettingsCommand.Settings))
            .WithMessage("La clave de configuración no está permitida.")
            .When(x => x.Settings is not null && x.Settings.Count <= MaxKeys);
    }
}
