using Sirkadiyen.Application.Administration;
using Sirkadiyen.Application.Auditing;
using Sirkadiyen.Application.Onboarding;

namespace Sirkadiyen.Api.Administration;

public sealed record AdminUserDetailResponse
{
    public required AdminUserDetail User { get; init; }

    public required OnboardingState OnboardingState { get; init; }

    public required IReadOnlyList<AuditEventView> RecentSignIns { get; init; }
}
