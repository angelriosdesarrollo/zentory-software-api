using Zentory.Application.Common.Interfaces;
using Zentory.Application.Common.Security;
using Zentory.Domain.Constants;

namespace Zentory.Infrastructure.Services;

// IFinancialVisibility desde la membresía de la petición (PLAN-0018 RF-66 e; D-29). Scoped como IMembershipContext: cada
// petición decide con su propia membresía, leída de la base, y nada se cachea entre peticiones (SR-0018.40). Falla
// cerrado: sin membresía resuelta no se ven montos.
public sealed class FinancialVisibility : IFinancialVisibility
{
    private readonly IMembershipContext _membership;

    public FinancialVisibility(IMembershipContext membership) => _membership = membership;

    public bool CanSeeAmounts
        => _membership.IsResolved && (_membership.Role == OrgRole.Owner || _membership.Role == OrgRole.Admin);

    public decimal? Amount(decimal? value) => CanSeeAmounts ? value : null;
}
