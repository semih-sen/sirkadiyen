using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sirkadiyen.Application.GoogleCalendar;
using Sirkadiyen.Application.Scheduling.Diffing;
using Sirkadiyen.Domain.Scheduling.Diffing;
using Sirkadiyen.Domain.Scheduling.Publication;
using Sirkadiyen.Infrastructure.Google;
using Sirkadiyen.Infrastructure.Persistence;
using Sirkadiyen.Infrastructure.Security;

// A one-off maintenance command for the incident where a schedule revision's diff was never
// calculated, so the deletions it carried never reached the calendars that held them (the "drog"
// lessons a bulk "drog -> ilaç" rename dropped). It has two commands, both DRY-RUN by default;
// nothing is written unless --apply is passed.
//
//   neutralize    Record a discarded tombstone diff for each skipped revision, so the (now
//                 isolation-fixed) worker never recomputes and dispatches its stale content.
//                 MUST be run before the fix is deployed.
//   remove-strays Delete the leftover calendar events whose lesson is no longer published at all,
//                 for the named users, and drop their ledger rows.
//
// Configuration is read from the same environment the worker uses:
//   SIRKADIYEN_DATABASE__CONNECTION_STRING
//   SIRKADIYEN_DATAPROTECTION__KEY_RING_PATH        (must be the shared key ring; tokens are sealed with it)
//   SIRKADIYEN_GOOGLE__CALENDAR_CLIENT_ID
//   SIRKADIYEN_GOOGLE__CALENDAR_CLIENT_SECRET

// The four revisions whose diff was skipped (G3-TR-A/B annual), oldest first.
string[] defaultRevisions =
[
    "01a05957-0cb0-7f63-8877-04725ec18d62",
    "01a061eb-a72a-7d6f-a560-b1ff7ddbf56a",
    "01a06adf-50cc-7dce-aa46-93aaf2000e8e",
    "01a07a9f-1cc8-7a3f-8b4c-544641871f12",
];

// The two students left holding stranded "drog" events.
string[] defaultUsers =
[
    "01a01775-c886-72ea-85e8-f850cf851409",
    "01a05743-3dba-7ef1-8bf4-fd213a898a7c",
];

if (args.Length == 0)
{
    Console.Error.WriteLine("Usage: neutralize | remove-strays  [--apply] [--revisions <ids>] [--users <ids>]");
    return 1;
}

string command = args[0];
bool apply = args.Contains("--apply", StringComparer.Ordinal);
Guid[] revisions = ParseIds(ValueOf("--revisions") ?? string.Join(',', defaultRevisions));
Guid[] users = ParseIds(ValueOf("--users") ?? string.Join(',', defaultUsers));

CancellationTokenSource cancellation = new();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

ServiceProvider provider = BuildProvider();
try
{
    return command switch
    {
        "neutralize" => await NeutralizeAsync(provider, revisions, apply, cancellation.Token),
        "remove-strays" => await RemoveStraysAsync(provider, users, apply, cancellation.Token),
        _ => Fail($"Unknown command '{command}'."),
    };
}
finally
{
    await provider.DisposeAsync();
}

static async Task<int> NeutralizeAsync(
    ServiceProvider provider,
    Guid[] revisions,
    bool apply,
    CancellationToken cancellationToken)
{
    await using AsyncServiceScope scope = provider.CreateAsyncScope();
    SirkadiyenDbContext db = scope.ServiceProvider.GetRequiredService<SirkadiyenDbContext>();
    SemanticScheduleDiffer differ = new(new SemanticDiffOptions());
    DateTimeOffset now = DateTimeOffset.UtcNow;

    Console.WriteLine($"== neutralize ({(apply ? "APPLY" : "dry-run")}) — {revisions.Length} revision(s) ==");
    foreach (Guid revisionId in revisions)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (await db.ScheduleDiffs.AnyAsync(diff => diff.CurrentRevisionId == revisionId, cancellationToken))
        {
            Console.WriteLine($"  {revisionId}: already has a diff — skipped.");
            continue;
        }

        ScheduleRevision? revision = await db.ScheduleRevisions.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == revisionId, cancellationToken);
        if (revision is null)
        {
            Console.WriteLine($"  {revisionId}: not found — skipped.");
            continue;
        }

        ScheduleRevision? previous = await db.ScheduleRevisions.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.SupersededByRevisionId == revisionId, cancellationToken);

        List<CanonicalScheduleRecord> previousRecords = previous is null
            ? []
            : await RecordsAsync(db, previous.Id, cancellationToken);
        List<CanonicalScheduleRecord> currentRecords = await RecordsAsync(db, revisionId, cancellationToken);

        IReadOnlyList<ScheduleDiffEntry> entries = differ.Diff(previousRecords, currentRecords);
        ScheduleDiff diff = ScheduleDiff.CreateNeutralized(
            revision.ScheduleSourceId,
            revision.SourceId,
            previous?.Id,
            revisionId,
            entries,
            "maintenance:stray-drog-cleanup",
            "Diff skipped during publication, so its deletions were lost; recomputing and dispatching "
                + "it now would write stale point-in-time content over newer content. Neutralized: the "
                + "lost deletions are repaired directly by remove-strays.",
            now);

        Console.WriteLine(
            $"  {revisionId}: {diff.DeletedCount} deleted, {diff.CreatedCount} created, "
            + $"{diff.UpdatedCount} updated → tombstone (Discarded).");

        if (apply)
        {
            db.ScheduleDiffs.Add(diff);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                Console.WriteLine("    neutralized.");
            }
            catch (DbUpdateException exception)
            {
                // A concurrent worker won the unique (CurrentRevisionId) race: leave its diff in place.
                db.ChangeTracker.Clear();
                Console.WriteLine($"    another writer stored a diff first — skipped ({Root(exception)}).");
            }
        }
    }

    Console.WriteLine(apply ? "Done." : "Dry-run only. Re-run with --apply to write.");
    return 0;
}

static async Task<int> RemoveStraysAsync(
    ServiceProvider provider,
    Guid[] users,
    bool apply,
    CancellationToken cancellationToken)
{
    await using AsyncServiceScope scope = provider.CreateAsyncScope();
    SirkadiyenDbContext db = scope.ServiceProvider.GetRequiredService<SirkadiyenDbContext>();
    ICalendarTokenProtector tokenProtector = scope.ServiceProvider.GetRequiredService<ICalendarTokenProtector>();
    IUserCalendarClient calendarClient = scope.ServiceProvider.GetRequiredService<IUserCalendarClient>();

    Console.WriteLine($"== remove-strays ({(apply ? "APPLY" : "dry-run")}) — {users.Length} user(s) ==");
    int strays = 0;
    int removed = 0;
    foreach (Guid userId in users)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var connection = await db.GoogleCalendarConnections.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.UserId == userId, cancellationToken);
        if (connection is null || string.IsNullOrWhiteSpace(connection.ProtectedRefreshToken))
        {
            Console.WriteLine($"  {userId}: no calendar connection — skipped.");
            continue;
        }

        var mappings = await db.UserCalendarEventMappings
            .Where(mapping => mapping.UserId == userId)
            .ToListAsync(cancellationToken);

        foreach (var mapping in mappings)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // A stray is a ledger row whose lesson is no longer in any Published revision of its
            // source — the "retired" case the cohort repair refuses to touch. A still-published
            // lesson (even one merely no longer applicable to this user) is never removed here.
            bool stillPublished = await db.ScheduleRevisions
                .Where(revision => revision.State == RevisionState.Published)
                .Join(
                    db.CanonicalScheduleRecords,
                    revision => revision.Id,
                    record => record.ScheduleRevisionId,
                    (revision, record) => record)
                .AnyAsync(
                    record => record.SourceId == mapping.SourceId
                        && record.StableIdentity == mapping.StableIdentity,
                    cancellationToken);
            if (stillPublished)
            {
                continue;
            }

            strays++;
            Console.WriteLine(
                $"  {userId}: STRAY {mapping.SourceId.Value} / {mapping.StableIdentity} "
                + $"event {mapping.GoogleEventId} on {mapping.GoogleCalendarId}");

            if (!apply)
            {
                continue;
            }

            CalendarAccess access = new()
            {
                RefreshToken = tokenProtector.Unprotect(connection.ProtectedRefreshToken),
            };
            CalendarEventDeleteOutcome outcome = await calendarClient.DeleteEventAsync(
                access,
                mapping.GoogleCalendarId,
                mapping.GoogleEventId,
                cancellationToken);

            db.UserCalendarEventMappings.Remove(mapping);
            await db.SaveChangesAsync(cancellationToken);
            removed++;
            Console.WriteLine($"    Google: {outcome}; ledger row removed.");
        }
    }

    Console.WriteLine(
        $"{strays} stray event(s) found{(apply ? $", {removed} removed." : ". Dry-run only; re-run with --apply.")}");
    return 0;
}

static async Task<List<CanonicalScheduleRecord>> RecordsAsync(
    SirkadiyenDbContext db,
    Guid revisionId,
    CancellationToken cancellationToken) =>
    await db.CanonicalScheduleRecords.AsNoTracking()
        .Where(record => record.ScheduleRevisionId == revisionId)
        .ToListAsync(cancellationToken);

static ServiceProvider BuildProvider()
{
    string connectionString = RequireConfig("SIRKADIYEN_DATABASE__CONNECTION_STRING");
    string keyRingPath = RequireConfig("SIRKADIYEN_DATAPROTECTION__KEY_RING_PATH");
    string clientId = RequireConfig("SIRKADIYEN_GOOGLE__CALENDAR_CLIENT_ID");
    string clientSecret = RequireConfig("SIRKADIYEN_GOOGLE__CALENDAR_CLIENT_SECRET");

    ServiceCollection services = new();
    services.AddSirkadiyenPersistence(connectionString);
    services.AddSirkadiyenDataProtection(keyRingPath);
    services.AddSingleton<ICalendarTokenProtector, DataProtectionCalendarTokenProtector>();
    services.AddSingleton(new GoogleCalendarAuthorizationOptions
    {
        ClientId = clientId,
        ClientSecret = clientSecret,
    });
    services.AddSingleton(new GoogleCalendarThrottleOptions());
    services.AddSingleton<IUserCalendarClient, GoogleCalendarClient>();
    return services.BuildServiceProvider();
}

static string? ValueOf(string name)
{
    int index = Array.IndexOf(Environment.GetCommandLineArgs(), name);
    string[] all = Environment.GetCommandLineArgs();
    return index >= 0 && index + 1 < all.Length ? all[index + 1] : null;
}

static Guid[] ParseIds(string value) =>
    [.. value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(Guid.Parse)];

static string RequireConfig(string name) =>
    Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
        ? value
        : throw new InvalidOperationException($"Missing required environment variable {name}.");

static int Fail(string message)
{
    Console.Error.WriteLine(message);
    return 1;
}

static string Root(Exception exception)
{
    Exception current = exception;
    while (current.InnerException is not null)
    {
        current = current.InnerException;
    }

    return current.Message;
}
