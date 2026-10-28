using IProfiler.Core.Licensing;

namespace IProfiler.Infrastructure.Fakes;

public sealed class FakeLicenseValidator : ILicenseValidator
{
    public LicenseStatus Validate() => new(true, LicensedTo: "Development (fake license)");
}
