namespace IProfiler.Core.Licensing;

/// <summary>Validates the offline, signed license file. No phone-home (CLAUDE.md rule 8).</summary>
public interface ILicenseValidator
{
    LicenseStatus Validate();
}

/// <param name="Reason">Why the license is invalid; null when valid.</param>
public sealed record LicenseStatus(bool IsValid, string? LicensedTo = null, DateOnly? ExpiresOn = null, string? Reason = null);
