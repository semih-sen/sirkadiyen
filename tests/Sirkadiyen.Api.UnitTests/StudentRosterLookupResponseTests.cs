using Sirkadiyen.Api.StudentRosters;
using Sirkadiyen.Application.StudentRosters;
using Sirkadiyen.Domain.Scheduling.Sources;
using Xunit;

namespace Sirkadiyen.Api.UnitTests;

/// <summary>
/// Covers what the lookup is allowed to tell the browser (ADR-085).
/// </summary>
public sealed class StudentRosterLookupResponseTests
{
    [Fact]
    public void TheResponseCarriesTheSuggestionAndWhatIsStillOwed()
    {
        StudentRosterLookupResponse response = StudentRosterLookupResponse.From(
            new StudentRosterLookupResult
            {
                Outcome = StudentRosterLookupOutcome.Matched,
                StudentNumber = "0101250001",
                RosterId = "G2-TR-ROSTER",
                GivenName = "HAY*******",
                FamilyName = "KIY***",
                AcademicYear = "2026-2027",
                ClassYear = 2,
                ProgramLanguage = ProgramLanguage.Turkish,
                SuggestedSelectors = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["practiceGroup"] = "A",
                },
                DimensionsRequiringInput = ["anatomyGroup"],
                Notices =
                [
                    new StudentRosterLookupNotice
                    {
                        Code = StudentRosterLookupNoticeCode.DimensionNotStatedByRoster,
                        Dimension = "anatomyGroup",
                        Message = "The list does not state 'anatomyGroup'.",
                    },
                ],
            });

        Assert.Equal("Matched", response.Outcome);
        Assert.Equal("HAY*******", response.GivenName);
        Assert.Equal("A", response.SuggestedSelectors["practiceGroup"]);
        Assert.Equal(["anatomyGroup"], response.DimensionsRequiringInput);
        Assert.Equal("DimensionNotStatedByRoster", Assert.Single(response.Notices).Code);
        Assert.False(response.SomeListsUnreadable);
    }

    [Fact]
    public void AnAmbiguousNumberNeverTellsTheStudentWhichListsClaimIt()
    {
        // Which faculty document a student appears in is an operator diagnostic,
        // not something the browser needs, and naming both would say more about
        // the other student than about this one.
        StudentRosterLookupResponse response = StudentRosterLookupResponse.From(
            new StudentRosterLookupResult
            {
                Outcome = StudentRosterLookupOutcome.Ambiguous,
                StudentNumber = "0101240080",
                ConflictingRosterIds = ["G2-TR-ROSTER", "G3-TR-ROSTER"],
            });

        Assert.Equal("Ambiguous", response.Outcome);
        Assert.Null(response.GivenName);
        Assert.Null(response.ClassYear);
        Assert.Empty(response.SuggestedSelectors);
    }

    [Fact]
    public void AMissSaysWhetherAListCouldNotBeReadWithoutNamingIt()
    {
        StudentRosterLookupResponse response = StudentRosterLookupResponse.From(
            new StudentRosterLookupResult
            {
                Outcome = StudentRosterLookupOutcome.NotFound,
                StudentNumber = "0101250001",
                UnreadableRosterIds = ["G2-TR-ROSTER"],
            });

        Assert.Equal("NotFound", response.Outcome);
        Assert.True(response.SomeListsUnreadable);
    }
}
