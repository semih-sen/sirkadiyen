using System.Globalization;
using Sirkadiyen.Application.Notifications;
using Sirkadiyen.Application.Operations;
using Sirkadiyen.Domain.Scheduling.Diffing;
using Sirkadiyen.Domain.Scheduling.Publication;
using Sirkadiyen.Domain.Scheduling.Sources;

namespace Sirkadiyen.Worker.Notifications;

/// <summary>
/// Every alert the worker can raise, written in one place (ADR-144).
/// </summary>
/// <remarks>
/// <para>
/// The stages that detect these conditions already state them in the journal, in English, for
/// whoever is reading logs. An alert is read on a phone by the person who operates the faculty's
/// schedules, so it is written in Turkish and says what happens next rather than what happened
/// internally. Keeping the wording here rather than inline keeps the stages readable and lets the
/// messages be tested without a worker.
/// </para>
/// <para>
/// The dedupe key is part of each message's meaning, not an implementation detail. A key naming
/// one revision, diff or parse run is unique and always delivered; a key naming a source or a
/// standing condition is suppressed for the configured cooldown, because that condition is
/// re-detected every cycle until a person fixes it.
/// </para>
/// </remarks>
internal static class WorkerAlerts
{
    /// <summary>
    /// A poll produced a revision — the event the operator asked to be told about. ADR-141 means
    /// an unchanged document produces none, so this fires only on a real change.
    /// </summary>
    /// <remarks>
    /// The validation state decides the severity, because it decides what happens next: a
    /// validated revision publishes itself, one held for review reaches no calendar until somebody
    /// approves it, and a rejected one never will.
    /// </remarks>
    public static OperatorAlert RevisionCreated(
        SourceId sourceId,
        Guid revisionId,
        RevisionState? state,
        int? findingCount)
    {
        (OperatorAlertSeverity severity, string title, string detail) = state switch
        {
            RevisionState.ReviewRequired => (
                OperatorAlertSeverity.Warning,
                "Yeni revizyon incelemeyi bekliyor",
                "Doğrulama en az bir hata bulduğu için revizyon karantinada. Onaylanana kadar "
                + "hiçbir takvime yazılmaz."),
            RevisionState.Rejected => (
                OperatorAlertSeverity.Warning,
                "Yeni revizyon reddedildi",
                "Revizyon doğrulamayı geçemedi ve yayınlanmayacak. Kaynak belge düzeltilip "
                + "yeniden okunmalı."),
            RevisionState.Validated or RevisionState.Published => (
                OperatorAlertSeverity.Info,
                "Yeni revizyon oluştu",
                "Doğrulamayı geçti; yayınlanıp fark hesabına girecek."),
            _ => (
                OperatorAlertSeverity.Warning,
                "Yeni revizyon doğrulanamadı",
                "Revizyon oluştu ama doğrulama tamamlanmadı. Bir sonraki döngü yeniden deneyecek."),
        };

        return new OperatorAlert
        {
            Title = title,
            Severity = severity,
            Detail = detail,
            DedupeKey = $"revision-created:{revisionId}",
            Fields =
            [
                SourceField(sourceId),
                new OperatorAlertField("Revizyon", revisionId.ToString()),
                new OperatorAlertField("Durum", state?.ToString() ?? "bilinmiyor"),
                new OperatorAlertField("Doğrulama bulgusu", Number(findingCount)),
            ],
        };
    }

    /// <summary>A source could not be acquired at all: no snapshot, no parse run, no revision.</summary>
    /// <remarks>
    /// Keyed by source rather than by occurrence. An unreadable document fails on every cycle, so
    /// without that this would be the same message every fifteen minutes until somebody fixes the
    /// sharing permission.
    /// </remarks>
    public static OperatorAlert SourcePollFailed(SourceId sourceId, Exception failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        return new OperatorAlert
        {
            Title = "Kaynak okunamadı",
            Severity = OperatorAlertSeverity.Error,
            Detail = "Belge indirilemediği için bu döngüde anlık görüntü, ayrıştırma ve revizyon "
                + "oluşmadı. Kaynağın paylaşım izni ve adresi kontrol edilmeli.",
            DedupeKey = $"source-poll-failed:{sourceId.Value}",
            Fields =
            [
                SourceField(sourceId),
                new OperatorAlertField("Hata", failure.GetType().Name),
                new OperatorAlertField("Ayrıntı", failure.Message),
            ],
        };
    }

    /// <summary>
    /// A source stopped tracking its discovery folder and is re-reading the catalogued document.
    /// </summary>
    /// <remarks>
    /// The one success worth alerting on (ADR-133): polling keeps reporting healthy while the
    /// source quietly freezes on last week's file, so nothing else would ever say so.
    /// </remarks>
    public static OperatorAlert SourceDiscoveryFallback(SourceId sourceId, string failure) =>
        new()
        {
            Title = "Kaynak klasörü okunamadı, eski belge kullanılıyor",
            Severity = OperatorAlertSeverity.Warning,
            Detail = "Poll başarılı görünüyor ama yeni yayınlanan belgeler görülmüyor: klasör "
                + "listelenemediği için katalogdaki belge okunmaya devam ediyor.",
            DedupeKey = $"source-discovery-fallback:{sourceId.Value}",
            Fields = [SourceField(sourceId), new OperatorAlertField("Sebep", failure)],
        };

    /// <summary>A parse run was reopened because the worker that started it died mid-parse.</summary>
    public static OperatorAlert ParseRunRecovered(SourceId sourceId, Guid? parseRunId) =>
        new()
        {
            Title = "Yarım kalan ayrıştırma kurtarıldı",
            Severity = OperatorAlertSeverity.Warning,
            Detail = "Önceki worker ayrıştırma sırasında durmuş. Bu döngü çalışmayı devraldı; "
                + "tekrarlanıyorsa worker'ın neden durduğuna bakılmalı.",
            DedupeKey = $"parse-run-recovered:{parseRunId?.ToString() ?? sourceId.Value}",
            Fields =
            [
                SourceField(sourceId),
                new OperatorAlertField("Ayrıştırma", parseRunId?.ToString() ?? "bilinmiyor"),
            ],
        };

    /// <summary>A revision an earlier cycle left in <c>Parsed</c> could not be validated.</summary>
    /// <remarks>
    /// Keyed by revision: it is retried every cycle and would otherwise repeat forever, but a
    /// second stuck revision is a different fault and must still be heard.
    /// </remarks>
    public static OperatorAlert RevisionValidationFailed(Guid revisionId, Exception? failure) =>
        new()
        {
            Title = "Revizyon doğrulanamıyor",
            Severity = OperatorAlertSeverity.Error,
            Detail = "Doğrulama bu revizyonda hata verdi ve revizyon atlandı. Her döngüde yeniden "
                + "denenecek, ama düzeltilene kadar yayınlanamaz.",
            DedupeKey = $"revision-validation-failed:{revisionId}",
            Fields =
            [
                new OperatorAlertField("Revizyon", revisionId.ToString()),
                new OperatorAlertField("Hata", failure?.GetType().Name ?? "bilinmiyor"),
                new OperatorAlertField("Ayrıntı", failure?.Message ?? "-"),
            ],
        };

    /// <summary>A diff was calculated: what actually changes in students' calendars.</summary>
    /// <remarks>
    /// A held diff is the alert that matters — it reaches no calendar until an operator acts — so
    /// it is a warning carrying the hold reason, while an ordinary one is information carrying the
    /// counts.
    /// </remarks>
    public static OperatorAlert DiffCalculated(ScheduleDiff diff)
    {
        ArgumentNullException.ThrowIfNull(diff);
        bool held = diff.State is ScheduleDiffState.Held;
        List<OperatorAlertField> fields =
        [
            SourceField(diff.SourceId),
            new OperatorAlertField("Fark", diff.Id.ToString()),
            new OperatorAlertField("Revizyon", diff.CurrentRevisionId.ToString()),
            new OperatorAlertField(
                "Değişiklik",
                $"{diff.CreatedCount} yeni, {diff.UpdatedCount} güncellenen, "
                + $"{diff.DeletedCount} silinen"),
            new OperatorAlertField(
                "Diğer",
                $"{diff.UnchangedCount} değişmeyen, {diff.AmbiguousCount} belirsiz"),
        ];

        if (held)
        {
            fields.Add(new OperatorAlertField("Tutulma sebebi", diff.HoldReason ?? "belirtilmedi"));
        }

        return new OperatorAlert
        {
            Title = held ? "Fark tutuldu, takvime yazılmıyor" : "Fark hesaplandı",
            Severity = held ? OperatorAlertSeverity.Warning : OperatorAlertSeverity.Info,
            Detail = !held
                ? null
                : diff.IsReleasable
                    ? "Panelden incelenip serbest bırakılana kadar hiçbir takvime yazılmaz."
                    : "Belirsiz eşleşme içerdiği için serbest bırakılamaz; yalnızca kaynak "
                        + "belgede düzeltilebilir.",
            DedupeKey = $"diff:{diff.Id}",
            Fields = fields,
        };
    }

    /// <summary>The pipeline has work waiting for a person (ADR-143's outbound half).</summary>
    /// <remarks>
    /// One message for the whole report rather than one per kind, because a blocked pipeline is a
    /// single situation and five separate notifications describing it is how a channel stops being
    /// read. The dedupe key is constant, so the cooldown decides how often a surviving stall is
    /// repeated.
    /// </remarks>
    public static OperatorAlert PipelineStalled(PipelineStallReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        List<OperatorAlertField> fields = [];
        AddStall(fields, "İnceleme bekleyen revizyon", report.RevisionsAwaitingReview);
        AddStall(fields, "Doğrulanamayan revizyon", report.RevisionsStuckBeforeValidation);
        AddStall(fields, "Serbest bırakılmayı bekleyen fark", report.DiffsAwaitingRelease);
        AddStall(fields, "Takvime yazılamayan fark", report.FailedDispatches);
        AddStall(fields, "Okunmayı bırakmış kaynak", report.SourcesNotPolled);

        return new OperatorAlert
        {
            Title = "Boru hattı bekliyor",
            Severity = OperatorAlertSeverity.Warning,
            Detail = "Aşağıdaki işler bir kişinin kararını bekliyor. Hiçbiri kendiliğinden "
                + "çözülmez ve bekledikleri sürece öğrenci takvimlerine ulaşmazlar.",
            DedupeKey = "pipeline-stalled",
            Fields = fields,
        };
    }

    /// <summary>A whole worker stage threw instead of processing its batch.</summary>
    /// <remarks>
    /// Keyed by stage, so a stage failing every cycle says so once per cooldown rather than
    /// filling the channel with the fault it is already writing to the journal each pass.
    /// </remarks>
    public static OperatorAlert StageFailed(string stage, Exception failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        return new OperatorAlert
        {
            Title = "Worker aşaması hata verdi",
            Severity = OperatorAlertSeverity.Error,
            Detail = "Bu aşama bu döngüde hiç iş yapamadı. Bir sonraki döngüde yeniden denenir; "
                + "tekrar ediyorsa sunucu günlüklerine bakılmalı.",
            DedupeKey = $"stage-failed:{stage}",
            Fields =
            [
                new OperatorAlertField("Aşama", stage),
                new OperatorAlertField("Hata", failure.GetType().Name),
                new OperatorAlertField("Ayrıntı", failure.Message),
            ],
        };
    }

    private static void AddStall(List<OperatorAlertField> fields, string label, StalledWork work)
    {
        if (work.Count == 0)
        {
            return;
        }

        string oldest = work.OldestSinceUtc is { } since
            ? since.UtcDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " UTC"
            : "bilinmiyor";
        string source = work.OldestSourceId is { Length: > 0 } sourceId
            ? $" ({sourceId})"
            : string.Empty;
        fields.Add(new OperatorAlertField(label, $"{work.Count} adet, en eskisi {oldest}{source}"));
    }

    private static OperatorAlertField SourceField(SourceId sourceId) => new("Kaynak", sourceId.Value);

    private static string Number(int? value) =>
        value?.ToString(CultureInfo.InvariantCulture) ?? "bilinmiyor";
}
