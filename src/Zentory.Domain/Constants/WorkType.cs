namespace Zentory.Domain.Constants;

/// <summary>
/// The organization's primary line of work — mirrors the frontend's PROJECT_TYPE
/// (lib/constants/project-types.ts). Drives onboarding copy/shortcuts today;
/// nullable and optional, not required to create an organization.
/// </summary>
public static class WorkType
{
    public const string Software     = "software";
    public const string ObraPublica  = "obra_publica";
    public const string Consultoria  = "consultoria";
    public const string Marketing    = "marketing";
    public const string Otro         = "otro";

    public static readonly string[] All = { Software, ObraPublica, Consultoria, Marketing, Otro };
}
