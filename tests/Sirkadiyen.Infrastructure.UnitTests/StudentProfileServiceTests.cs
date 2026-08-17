using Sirkadiyen.Application.Licensing;
using Sirkadiyen.Application.StudentProfiles;
using Sirkadiyen.Domain.Licensing;
using Sirkadiyen.Domain.Scheduling.Sources;
using Xunit;

namespace Sirkadiyen.Infrastructure.UnitTests;

/// <summary>
/// The service is a thin coordinator, but the two outcome flags it forwards are what the
/// <c>ProfileUpdated</c> audit record is built from (AI_GUIDELINE §19). Dropping either on the way
/// out would make every audit row claim the audience never moved, which is precisely the claim the
/// record exists to make honestly.
/// </summary>
public sealed class StudentProfileServiceTests
{
    private static readonly SupportedProfileSchema Schema = CurrentSupportedProfileSchema.Create();

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(true, false)]
    public async Task ForwardsBothAudienceOutcomeFlagsFromTheStore(
        bool audienceChanged,
        bool calendarResyncRequested)
    {
        // The third case is real, not hypothetical: a student who has not finished onboarding has
        // no calendar to converge, so the audience changed and nothing was queued.
        RecordingProfileStore store = new(audienceChanged, calendarResyncRequested);
        StudentProfileService service = new(
            Schema,
            store,
            new StubLicenseStore(UserLicenseState.Active),
            TimeProvider.System);

        SaveStudentProfileResult result = await service.SaveAsync(
            Guid.NewGuid(),
            Submitted(),
            CancellationToken.None);

        Assert.Equal(SaveStudentProfileOutcome.Saved, result.Outcome);
        Assert.Equal(audienceChanged, result.AudienceChanged);
        Assert.Equal(calendarResyncRequested, result.CalendarResyncRequested);
    }

    [Fact]
    public async Task ReportsNoAudienceChangeWhenTheAccountIsNotActivated()
    {
        // An unactivated account never reaches the store, so there is nothing to audit either.
        RecordingProfileStore store = new(audienceChanged: true, calendarResyncRequested: true);
        StudentProfileService service = new(
            Schema,
            store,
            new StubLicenseStore(UserLicenseState.Suspended),
            TimeProvider.System);

        SaveStudentProfileResult result = await service.SaveAsync(
            Guid.NewGuid(),
            Submitted(),
            CancellationToken.None);

        Assert.Equal(SaveStudentProfileOutcome.ActivationRequired, result.Outcome);
        Assert.False(result.AudienceChanged);
        Assert.False(result.CalendarResyncRequested);
        Assert.False(store.WasCalled);
    }

    private static SubmittedStudentProfile Submitted() => new()
    {
        ClassYear = 1,
        ProgramLanguage = ProgramLanguage.Turkish,
        StudentNumber = "0101240048",
        Selectors = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["practiceGroup"] = "A",
            ["practiceSubgroup"] = "A1",
        },
    };

    private sealed class RecordingProfileStore(bool audienceChanged, bool calendarResyncRequested)
        : IStudentProfileStore
    {
        public bool WasCalled { get; private set; }

        public Task<StudentProfileView?> GetByUserIdAsync(
            Guid userId,
            CancellationToken cancellationToken) =>
            Task.FromResult<StudentProfileView?>(null);

        public Task<bool> ExistsForUserAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task<StudentProfileUpsertResult> UpsertAsync(
            Guid userId,
            string academicYear,
            int classYear,
            ProgramLanguage programLanguage,
            string studentNumber,
            string selectorSchemaVersion,
            IReadOnlyDictionary<string, string> selectors,
            DateTimeOffset atUtc,
            CancellationToken cancellationToken)
        {
            WasCalled = true;
            return Task.FromResult(new StudentProfileUpsertResult
            {
                Profile = new StudentProfileView
                {
                    UserId = userId,
                    AcademicYear = academicYear,
                    ClassYear = classYear,
                    ProgramLanguage = programLanguage,
                    StudentNumber = studentNumber,
                    SelectorSchemaVersion = selectorSchemaVersion,
                    Selectors = selectors,
                    UpdatedAtUtc = atUtc,
                },
                AudienceChanged = audienceChanged,
                CalendarResyncRequested = calendarResyncRequested,
            });
        }
    }

    /// <summary>Answers the one question the service asks; every other member is out of scope.</summary>
    private sealed class StubLicenseStore(UserLicenseState state) : ILicenseStore
    {
        public Task<UserLicenseState> GetUserLicenseStateAsync(
            Guid userId,
            CancellationToken cancellationToken) => Task.FromResult(state);

        public Task SaveCreatedAsync(License license, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<LicenseRedemptionResult> RedeemAsync(
            byte[] codeHash,
            Guid userId,
            string userEmail,
            DateTimeOffset redeemedAtUtc,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<LicenseRevocationResult> RevokeAsync(
            Guid licenseId,
            Guid actorUserId,
            string actorEmail,
            string reason,
            DateTimeOffset revokedAtUtc,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ManualLicenseActivationResult> ActivateManuallyAsync(
            Guid userId,
            Guid actorUserId,
            string actorEmail,
            string reason,
            DateTimeOffset activatedAtUtc,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<UserLicenseSummary?> GetUserLicenseSummaryAsync(
            Guid userId,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
