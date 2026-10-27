using System.Text;
using Zentory.Application.Common.Security;
using Zentory.Domain.Constants;

namespace Zentory.Tests.Unit.Security;

// PLAN-0018 (RF-26, SR-0018.14; criterio CA-T4A-4, parte de CA-81): límites de PasswordPolicy, y OrgRole.Assignable
// (RF-45, adelantado de T27), que el mismo criterio de la tarea agrupa.
public sealed class PasswordPolicyTests
{
    [Theory]
    [InlineData(7, false)]
    [InlineData(8, true)]
    [InlineData(128, true)]
    [InlineData(129, false)]
    public void PLAN0018_CA81_PasswordPolicy_ReglaDeLargo_RechazaSieteYCientoVeintinueveYAceptaOchoYCientoVeintiocho(
        int length, bool expected)
    {
        Assert.Equal(expected, PasswordPolicy.HasValidLength(new string('a', length)));
    }

    [Theory]
    [InlineData(7, false)]
    [InlineData(8, true)]
    [InlineData(72, true)]
    [InlineData(73, false)]   // ASCII: 73 bytes, por encima del tope de BCrypt.
    [InlineData(128, false)]  // Pasa la regla de largo pero no la de bytes (128 > 72).
    [InlineData(129, false)]
    public void PLAN0018_CA81_PasswordPolicy_ContrasenaAscii_ExigeLargoYTopeDeBytes(int length, bool expected)
    {
        Assert.Equal(expected, PasswordPolicy.IsSatisfiedBy(new string('a', length)));
    }

    [Fact]
    public void PLAN0018_CA81_PasswordPolicy_SetentaYDosBytesMultibyte_LaAceptaYSetentaYTres_LaRechaza()
    {
        // "ñ" ocupa 2 bytes en UTF-8: 36 son 72 bytes; con un carácter ASCII más son 73.
        var seventyTwoBytes   = new string('ñ', 36);
        var seventyThreeBytes = seventyTwoBytes + "a";

        Assert.Equal(PasswordPolicy.MaxUtf8Bytes, Encoding.UTF8.GetByteCount(seventyTwoBytes));
        Assert.Equal(PasswordPolicy.MaxUtf8Bytes + 1, Encoding.UTF8.GetByteCount(seventyThreeBytes));
        Assert.True(PasswordPolicy.IsSatisfiedBy(seventyTwoBytes));
        Assert.False(PasswordPolicy.IsSatisfiedBy(seventyThreeBytes));
    }

    [Fact]
    public void PLAN0018_CA81_PasswordPolicy_Nula_LaRechaza()
    {
        Assert.False(PasswordPolicy.IsSatisfiedBy(null));
    }

    [Fact]
    public void PLAN0018_CA81_OrgRoleAssignable_ContieneExactamenteAdminYMember()
    {
        Assert.Equal([OrgRole.Admin, OrgRole.Member], OrgRole.Assignable.OrderBy(r => r, StringComparer.Ordinal));
        Assert.DoesNotContain(OrgRole.Owner, OrgRole.Assignable);
    }
}
