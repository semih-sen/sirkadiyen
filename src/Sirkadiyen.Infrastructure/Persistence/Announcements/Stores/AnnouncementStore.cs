using Microsoft.EntityFrameworkCore;
using Npgsql;
using Sirkadiyen.Application.Announcements;
using Sirkadiyen.Application.Common;
using Sirkadiyen.Domain.Announcements;
using Sirkadiyen.Domain.GoogleCalendar;
using Sirkadiyen.Domain.Identity;
using Sirkadiyen.Domain.StudentProfiles;
using Sirkadiyen.Infrastructure.Persistence.Licensing.Stores;

namespace Sirkadiyen.Infrastructure.Persistence.Announcements.Stores;

/// <summary>
/// Persists announcements and their delivery ledger in PostgreSQL (ADR-107).
/// </summary>
public sealed class AnnouncementStore(SirkadiyenDbContext dbContext) : IAnnouncementStore
{
    /// <summary>PostgreSQL's unique-violation SQLSTATE.</summary>
    private const string UniqueViolation = "23505";

    public async Task<AnnouncementSummary?> FindByCampaignKeyAsync(
        string campaignKey,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(campaignKey);

        CalendarAnnouncement? announcement = await dbContext.CalendarAnnouncements
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.CampaignKey == campaignKey,
                cancellationToken);

        return announcement is null
            ? null
            : await SummarizeAsync(announcement, cancellationToken);
    }

    public Task<AnnouncementCreateStoreResult> AddAsync(
        CalendarAnnouncement announcement,
        IReadOnlyList<CalendarAnnouncementDelivery> deliveries,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(announcement);
        ArgumentNullException.ThrowIfNull(deliveries);

        return RetriableTransaction.ExecuteAsync(dbContext, async () =>
        {
            // The announcement and every delivery row commit together. A partial write would
            // leave a campaign whose recipient set is smaller than the count the operator
            // confirmed, and nothing would ever notice.
            dbContext.CalendarAnnouncements.Add(announcement);
            dbContext.CalendarAnnouncementDeliveries.AddRange(deliveries);

            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException exception)
                when (exception.InnerException is PostgresException
                {
                    SqlState: UniqueViolation,
                })
            {
                // Another operator confirmed the same announcement first. The unique index on the
                // campaign key is the real deduplication guarantee; the earlier application check
                // only makes the common case cheap (plan §4.4).
                dbContext.ChangeTracker.Clear();
                AnnouncementSummary? existing = await FindByCampaignKeyAsync(
                    announcement.CampaignKey,
                    cancellationToken);

                return existing is null
                    ? throw new InvalidOperationException(
                        "The announcement campaign key collided but no existing announcement "
                        + "could be read back.")
                    : new AnnouncementCreateStoreResult
                    {
                        AlreadyExisted = true,
                        Announcement = existing,
                    };
            }

            return new AnnouncementCreateStoreResult
            {
                AlreadyExisted = false,
                Announcement = await SummarizeAsync(announcement, cancellationToken),
            };
        });
    }

    public async Task<IReadOnlyList<AnnouncementSummary>> ListAsync(
        CalendarAnnouncementKind? kind,
        CalendarAnnouncementStatus? status,
        Guid? targetUserId,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        IQueryable<CalendarAnnouncement> query = dbContext.CalendarAnnouncements.AsNoTracking();
        if (kind is { } requiredKind)
        {
            query = query.Where(announcement => announcement.Kind == requiredKind);
        }

        if (status is { } requiredStatus)
        {
            query = query.Where(announcement => announcement.Status == requiredStatus);
        }

        if (targetUserId is { } requiredTarget)
        {
            query = query.Where(announcement => announcement.TargetUserId == requiredTarget);
        }

        List<CalendarAnnouncement> announcements = await query
            .OrderByDescending(announcement => announcement.CreatedAtUtc)
            .ThenByDescending(announcement => announcement.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);

        Dictionary<Guid, AnnouncementDeliveryCounts> counts = await CountsForAsync(
            [.. announcements.Select(announcement => announcement.Id)],
            cancellationToken);

        return
        [
            .. announcements.Select(announcement => Summarize(
                announcement,
                counts.GetValueOrDefault(announcement.Id, EmptyCounts))),
        ];
    }

    public async Task<AnnouncementDetail?> FindAsync(
        Guid announcementId,
        CancellationToken cancellationToken)
    {
        CalendarAnnouncement? announcement = await dbContext.CalendarAnnouncements
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == announcementId, cancellationToken);
        if (announcement is null)
        {
            return null;
        }

        List<AnnouncementExclusionGroup> exclusions = await dbContext.CalendarAnnouncementDeliveries
            .AsNoTracking()
            .Where(delivery => delivery.CalendarAnnouncementId == announcementId
                && delivery.SkipReason != null)
            .GroupBy(delivery => delivery.SkipReason!.Value)
            .Select(group => new AnnouncementExclusionGroup
            {
                Reason = group.Key,
                Count = group.Count(),
            })
            .OrderBy(group => group.Reason)
            .ToListAsync(cancellationToken);

        return new AnnouncementDetail
        {
            Summary = await SummarizeAsync(announcement, cancellationToken),
            Body = announcement.Body,
            Location = announcement.Location,
            TimeZoneId = announcement.TimeZoneId,
            ReminderMinutesBefore = announcement.ReminderMinutesBefore,
            CategoryKey = announcement.CategoryKey,
            TemplateKey = announcement.TemplateKey,
            InternalNote = announcement.InternalNote,
            AudienceAcademicYear = announcement.AudienceAcademicYear,
            AudienceClassYear = announcement.AudienceClassYear,
            AudienceProgramLanguage = announcement.AudienceProgramLanguage,
            AudienceSelectors = announcement.AudienceSelectors,
            TargetUserId = announcement.TargetUserId,
            CreationReason = announcement.CreationReason,
            LastUpdatedBy = announcement.LastUpdatedBy,
            LastUpdateReason = announcement.LastUpdateReason,
            UpdatedAtUtc = announcement.UpdatedAtUtc,
            PlanHash = announcement.PlanHash,
            DeliveryAttempts = announcement.DeliveryAttempts,
            Exclusions = exclusions,
        };
    }

    public async Task<PagedResult<AnnouncementDeliveryView>> ListDeliveriesAsync(
        Guid announcementId,
        CalendarAnnouncementDeliveryState? state,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(page);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageSize);

        IQueryable<CalendarAnnouncementDelivery> query = dbContext.CalendarAnnouncementDeliveries
            .AsNoTracking()
            .Where(delivery => delivery.CalendarAnnouncementId == announcementId);
        if (state is { } requiredState)
        {
            query = query.Where(delivery => delivery.State == requiredState);
        }

        int totalCount = await query.CountAsync(cancellationToken);
        List<AnnouncementDeliveryView> items =
            await (from delivery in query
                   join user in dbContext.Users.AsNoTracking()
                       on delivery.UserId equals user.Id
                   orderby user.Email, delivery.UserId
                   select new AnnouncementDeliveryView
                   {
                       UserId = delivery.UserId,
                       Email = user.Email,
                       DisplayName = user.DisplayName,
                       State = delivery.State,
                       SkipReason = delivery.SkipReason,
                       AppliedContentVersion = delivery.AppliedContentVersion,
                       FailureReason = delivery.FailureReason,
                       UpdatedAtUtc = delivery.UpdatedAtUtc,
                   })
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(cancellationToken);

        return new PagedResult<AnnouncementDeliveryView>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
        };
    }

    public Task<UpdateAnnouncementResult> UpdateContentAsync(
        Guid announcementId,
        AnnouncementContent content,
        string updatedBy,
        string reason,
        DateTimeOffset atUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);

        return RetriableTransaction.ExecuteAsync(dbContext, async () =>
        {
            CalendarAnnouncement? announcement = await dbContext.CalendarAnnouncements
                .SingleOrDefaultAsync(
                    candidate => candidate.Id == announcementId,
                    cancellationToken);
            if (announcement is null)
            {
                return new UpdateAnnouncementResult
                {
                    Outcome = UpdateAnnouncementOutcome.NotFound,
                };
            }

            if (announcement.Status is CalendarAnnouncementStatus.Cancelling
                or CalendarAnnouncementStatus.Cancelled)
            {
                return new UpdateAnnouncementResult
                {
                    Outcome = UpdateAnnouncementOutcome.Cancelled,
                    Detail = "İptal edilmiş bir duyuru düzenlenemez; yenisini oluşturun.",
                };
            }

            announcement.UpdateContent(content, updatedBy, reason, atUtc);

            // Every copy already written is now a version behind. Re-opening them in the same
            // transaction as the content change is what stops a crash from leaving recipients
            // holding text nobody would ever correct.
            List<CalendarAnnouncementDelivery> written =
                await dbContext.CalendarAnnouncementDeliveries
                    .Where(delivery => delivery.CalendarAnnouncementId == announcementId
                        && (delivery.State == CalendarAnnouncementDeliveryState.Written
                            || delivery.State == CalendarAnnouncementDeliveryState.Failed))
                    .ToListAsync(cancellationToken);
            foreach (CalendarAnnouncementDelivery delivery in written)
            {
                delivery.ReopenForPatch(atUtc);
            }

            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                return new UpdateAnnouncementResult
                {
                    Outcome = UpdateAnnouncementOutcome.ConcurrentChange,
                };
            }

            return new UpdateAnnouncementResult
            {
                Outcome = UpdateAnnouncementOutcome.Updated,
                Announcement = await SummarizeAsync(announcement, cancellationToken),
            };
        });
    }

    public Task<CancelAnnouncementResult> RequestCancellationAsync(
        Guid announcementId,
        string cancelledBy,
        string reason,
        DateTimeOffset atUtc,
        CancellationToken cancellationToken) =>
        RetriableTransaction.ExecuteAsync(dbContext, async () =>
        {
            CalendarAnnouncement? announcement = await dbContext.CalendarAnnouncements
                .SingleOrDefaultAsync(
                    candidate => candidate.Id == announcementId,
                    cancellationToken);
            if (announcement is null)
            {
                return new CancelAnnouncementResult
                {
                    Outcome = CancelAnnouncementOutcome.NotFound,
                };
            }

            if (announcement.Status is CalendarAnnouncementStatus.Cancelled)
            {
                return new CancelAnnouncementResult
                {
                    Outcome = CancelAnnouncementOutcome.AlreadyCancelled,
                    Announcement = await SummarizeAsync(announcement, cancellationToken),
                };
            }

            announcement.RequestCancellation(cancelledBy, reason, atUtc);

            // Recipients not yet written to are skipped rather than left pending: cancelling
            // means nobody else receives it, and a pending row would be a promise to deliver.
            List<CalendarAnnouncementDelivery> pending =
                await dbContext.CalendarAnnouncementDeliveries
                    .Where(delivery => delivery.CalendarAnnouncementId == announcementId
                        && delivery.State == CalendarAnnouncementDeliveryState.Pending)
                    .ToListAsync(cancellationToken);
            foreach (CalendarAnnouncementDelivery delivery in pending)
            {
                delivery.MarkRemoved(atUtc);
            }

            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                return new CancelAnnouncementResult
                {
                    Outcome = CancelAnnouncementOutcome.ConcurrentChange,
                };
            }

            return new CancelAnnouncementResult
            {
                Outcome = CancelAnnouncementOutcome.CancellationRequested,
                Announcement = await SummarizeAsync(announcement, cancellationToken),
            };
        });

    // ---- Delivery worker ---------------------------------------------------

    public async Task<IReadOnlyList<AnnouncementDispatchCandidate>> ListDispatchableAsync(
        DateTimeOffset nowUtc,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        List<CalendarAnnouncement> announcements = await dbContext.CalendarAnnouncements
            .AsNoTracking()
            .Where(announcement =>
                (announcement.Status == CalendarAnnouncementStatus.Queued
                    || announcement.Status == CalendarAnnouncementStatus.Delivering
                    || announcement.Status == CalendarAnnouncementStatus.Cancelling)
                && (announcement.NextAttemptAtUtc == null
                    || announcement.NextAttemptAtUtc <= nowUtc))
            .OrderBy(announcement => announcement.CreatedAtUtc)
            .ThenBy(announcement => announcement.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return
        [
            .. announcements.Select(announcement => new AnnouncementDispatchCandidate
            {
                AnnouncementId = announcement.Id,
                Kind = announcement.Kind,
                Status = announcement.Status,
                ContentVersion = announcement.ContentVersion,
                DeliveryAttempts = announcement.DeliveryAttempts,
                Title = announcement.Title,
                Body = announcement.Body,
                Location = announcement.Location,
                IsAllDay = announcement.IsAllDay,
                LocalDate = announcement.LocalDate,
                StartLocalTime = announcement.StartLocalTime,
                EndLocalTime = announcement.EndLocalTime,
                TimeZoneId = announcement.TimeZoneId,
                ReminderMinutesBefore = announcement.ReminderMinutesBefore,
                CategoryKey = announcement.CategoryKey,
            }),
        ];
    }

    public async Task<IReadOnlyList<AnnouncementDeliveryTarget>> ListDeliveryTargetsAsync(
        Guid announcementId,
        CalendarAnnouncementDeliveryState state,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        List<TargetRow> rows =
            await (from delivery in dbContext.CalendarAnnouncementDeliveries.AsNoTracking()
                   where delivery.CalendarAnnouncementId == announcementId
                       && delivery.State == state
                   join connection in dbContext.GoogleCalendarConnections.AsNoTracking()
                       on delivery.UserId equals connection.UserId into connections
                   from connection in connections.DefaultIfEmpty()
                   join profile in dbContext.StudentProfiles.AsNoTracking()
                       on delivery.UserId equals profile.UserId into profiles
                   from profile in profiles.DefaultIfEmpty()
                   orderby delivery.UserId
                   select new TargetRow
                   {
                       Delivery = delivery,
                       Connection = connection,
                       Profile = profile,
                       HasActiveLicense =
                           ActiveLicenseQuery.UserIds(dbContext).Contains(delivery.UserId),
                   })
                .Take(limit)
                .ToListAsync(cancellationToken);

        return [.. rows.Select(ToTarget)];
    }

    public Task MarkDeliveryWrittenAsync(
        Guid deliveryId,
        string googleEventId,
        int contentVersion,
        DateTimeOffset atUtc,
        CancellationToken cancellationToken) =>
        MutateDeliveryAsync(
            deliveryId,
            delivery => delivery.MarkWritten(googleEventId, contentVersion, atUtc),
            cancellationToken);

    public Task MarkDeliverySkippedAsync(
        Guid deliveryId,
        AnnouncementExclusionReason reason,
        DateTimeOffset atUtc,
        CancellationToken cancellationToken) =>
        MutateDeliveryAsync(
            deliveryId,
            delivery => delivery.MarkSkipped(reason, atUtc),
            cancellationToken);

    public Task MarkDeliveryFailedAsync(
        Guid deliveryId,
        string reason,
        DateTimeOffset atUtc,
        CancellationToken cancellationToken) =>
        MutateDeliveryAsync(
            deliveryId,
            delivery => delivery.MarkFailed(reason, atUtc),
            cancellationToken);

    public Task MarkDeliveryRemovedAsync(
        Guid deliveryId,
        DateTimeOffset atUtc,
        CancellationToken cancellationToken) =>
        MutateDeliveryAsync(
            deliveryId,
            delivery => delivery.MarkRemoved(atUtc),
            cancellationToken);

    public Task ApplyDispatchOutcomeAsync(
        Guid announcementId,
        AnnouncementDispatchTransition transition,
        DateTimeOffset atUtc,
        CancellationToken cancellationToken) =>
        MutateAnnouncementAsync(
            announcementId,
            announcement =>
            {
                switch (transition)
                {
                    case AnnouncementDispatchTransition.Started:
                        announcement.MarkDelivering(atUtc);
                        break;
                    case AnnouncementDispatchTransition.Completed:
                        announcement.MarkDelivered(atUtc);
                        break;
                    case AnnouncementDispatchTransition.DeferredForBudget:
                        announcement.DeferForBudget(atUtc);
                        break;
                    case AnnouncementDispatchTransition.Cancelled:
                        announcement.MarkCancelled(atUtc);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(
                            nameof(transition),
                            transition,
                            "Unknown announcement dispatch transition.");
                }
            },
            cancellationToken);

    public Task DeferAfterFailureAsync(
        Guid announcementId,
        string reason,
        DateTimeOffset nextAttemptAtUtc,
        DateTimeOffset atUtc,
        CancellationToken cancellationToken) =>
        MutateAnnouncementAsync(
            announcementId,
            announcement => announcement.DeferAfterFailure(reason, nextAttemptAtUtc, atUtc),
            cancellationToken);

    public Task MarkDeliveryRunFailedAsync(
        Guid announcementId,
        string reason,
        DateTimeOffset atUtc,
        CancellationToken cancellationToken) =>
        MutateAnnouncementAsync(
            announcementId,
            announcement => announcement.MarkFailed(reason, atUtc),
            cancellationToken);

    // ---- Projection --------------------------------------------------------

    private static readonly AnnouncementDeliveryCounts EmptyCounts = new()
    {
        Pending = 0,
        Written = 0,
        Skipped = 0,
        Removed = 0,
        Failed = 0,
    };

    private static AnnouncementDeliveryTarget ToTarget(TargetRow row)
    {
        // The eligibility rule is restated over the row rather than reused from the audience read
        // store, because the question is different: that one asks who to address, this one asks
        // whether this already-addressed recipient can still be written to right now.
        AnnouncementExclusionReason? exclusion =
            !row.HasActiveLicense ? AnnouncementExclusionReason.LicenseInactive
            : row.Connection is null ? AnnouncementExclusionReason.NoCalendarConnection
            : row.Connection.Status is not GoogleCalendarConnectionStatus.Authorized
                ? AnnouncementExclusionReason.CalendarAuthorizationRevoked
            : row.Connection.ManagedCalendarUnavailableAtUtc is not null
                ? AnnouncementExclusionReason.ManagedCalendarUnavailable
            : string.IsNullOrWhiteSpace(row.Connection.ManagedCalendarId)
                ? AnnouncementExclusionReason.InitialSyncIncomplete
            : null;

        return new AnnouncementDeliveryTarget
        {
            DeliveryId = row.Delivery.Id,
            UserId = row.Delivery.UserId,
            ProtectedRefreshToken = exclusion is null ? row.Connection!.ProtectedRefreshToken : null,
            // A cancellation has to reach the calendar the copy was written to, which is the one
            // recorded on the row rather than whichever calendar the connection points at now.
            ManagedCalendarId = row.Delivery.GoogleCalendarId ?? row.Connection?.ManagedCalendarId,
            ClassYear = row.Profile?.ClassYear,
            ProgramLanguage = row.Profile?.ProgramLanguage,
            CurrentExclusion = exclusion,
            GoogleEventId = row.Delivery.GoogleEventId,
            AppliedContentVersion = row.Delivery.AppliedContentVersion,
        };
    }

    private async Task<AnnouncementSummary> SummarizeAsync(
        CalendarAnnouncement announcement,
        CancellationToken cancellationToken)
    {
        Dictionary<Guid, AnnouncementDeliveryCounts> counts =
            await CountsForAsync([announcement.Id], cancellationToken);
        return Summarize(
            announcement,
            counts.GetValueOrDefault(announcement.Id, EmptyCounts));
    }

    private static AnnouncementSummary Summarize(
        CalendarAnnouncement announcement,
        AnnouncementDeliveryCounts counts) => new()
        {
            AnnouncementId = announcement.Id,
            Kind = announcement.Kind,
            CampaignKey = announcement.CampaignKey,
            Title = announcement.Title,
            Status = announcement.Status,
            ContentVersion = announcement.ContentVersion,
            LocalDate = announcement.LocalDate,
            IsAllDay = announcement.IsAllDay,
            StartLocalTime = announcement.StartLocalTime,
            EndLocalTime = announcement.EndLocalTime,
            RecipientCount = announcement.RecipientCount,
            Counts = counts,
            CreatedBy = announcement.CreatedBy,
            CreatedAtUtc = announcement.CreatedAtUtc,
            CompletedAtUtc = announcement.CompletedAtUtc,
            LastFailureReason = announcement.LastFailureReason,
            CancelledBy = announcement.CancelledBy,
            CancellationReason = announcement.CancellationReason,
        };

    /// <remarks>
    /// The counters are always read from the delivery rows, never stored on the announcement, so
    /// the number an operator sees cannot disagree with the ledger it claims to summarize.
    /// </remarks>
    private async Task<Dictionary<Guid, AnnouncementDeliveryCounts>> CountsForAsync(
        IReadOnlyCollection<Guid> announcementIds,
        CancellationToken cancellationToken)
    {
        if (announcementIds.Count == 0)
        {
            return [];
        }

        List<StateCount> grouped = await dbContext.CalendarAnnouncementDeliveries
            .AsNoTracking()
            .Where(delivery => announcementIds.Contains(delivery.CalendarAnnouncementId))
            .GroupBy(delivery => new { delivery.CalendarAnnouncementId, delivery.State })
            .Select(group => new StateCount(
                group.Key.CalendarAnnouncementId,
                group.Key.State,
                group.Count()))
            .ToListAsync(cancellationToken);

        return grouped
            .GroupBy(row => row.AnnouncementId)
            .ToDictionary(
                group => group.Key,
                group => new AnnouncementDeliveryCounts
                {
                    Pending = Count(group, CalendarAnnouncementDeliveryState.Pending),
                    Written = Count(group, CalendarAnnouncementDeliveryState.Written),
                    Skipped = Count(group, CalendarAnnouncementDeliveryState.Skipped),
                    Removed = Count(group, CalendarAnnouncementDeliveryState.Removed),
                    Failed = Count(group, CalendarAnnouncementDeliveryState.Failed),
                });

        static int Count(
            IEnumerable<StateCount> rows,
            CalendarAnnouncementDeliveryState state) =>
            rows.Where(row => row.State == state).Sum(row => row.Count);
    }

    private sealed record StateCount(
        Guid AnnouncementId,
        CalendarAnnouncementDeliveryState State,
        int Count);

    private async Task MutateDeliveryAsync(
        Guid deliveryId,
        Action<CalendarAnnouncementDelivery> mutate,
        CancellationToken cancellationToken)
    {
        CalendarAnnouncementDelivery? delivery = await dbContext.CalendarAnnouncementDeliveries
            .SingleOrDefaultAsync(candidate => candidate.Id == deliveryId, cancellationToken);
        if (delivery is null)
        {
            return;
        }

        mutate(delivery);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task MutateAnnouncementAsync(
        Guid announcementId,
        Action<CalendarAnnouncement> mutate,
        CancellationToken cancellationToken)
    {
        CalendarAnnouncement? announcement = await dbContext.CalendarAnnouncements
            .SingleOrDefaultAsync(candidate => candidate.Id == announcementId, cancellationToken);
        if (announcement is null)
        {
            return;
        }

        mutate(announcement);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private sealed class TargetRow
    {
        public required CalendarAnnouncementDelivery Delivery { get; init; }

        public GoogleCalendarConnection? Connection { get; init; }

        public StudentProfile? Profile { get; init; }

        public required bool HasActiveLicense { get; init; }
    }
}
