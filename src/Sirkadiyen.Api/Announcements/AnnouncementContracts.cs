using Sirkadiyen.Application.Announcements;
using Sirkadiyen.Domain.Announcements;
using Sirkadiyen.Domain.Scheduling.Sources;

namespace Sirkadiyen.Api.Announcements;

/// <summary>
/// What an operator composed, as the browser sends it (ADR-107).
/// </summary>
/// <remarks>
/// The time zone is deliberately absent: every Sirkadiyen schedule is interpreted in
/// <c>Europe/Istanbul</c> and the server applies it, so an announcement cannot be placed at a
/// different moment than the lessons around it (AI_GUIDELINE §16).
/// </remarks>
public sealed record AnnouncementCompositionRequest
{
    public CalendarAnnouncementKind Kind { get; init; } = CalendarAnnouncementKind.Bulk;

    public string? AcademicYear { get; init; }

    public int? ClassYear { get; init; }

    public ProgramLanguage? ProgramLanguage { get; init; }

    public Dictionary<string, string>? Selectors { get; init; }

    /// <summary>Required for a warning, refused for a bulk announcement.</summary>
    public Guid? TargetUserId { get; init; }

    public string? TemplateKey { get; init; }

    public string? Title { get; init; }

    public string? Body { get; init; }

    public string? Location { get; init; }

    public bool IsAllDay { get; init; }

    public DateOnly? LocalDate { get; init; }

    public TimeOnly? StartLocalTime { get; init; }

    public TimeOnly? EndLocalTime { get; init; }

    public int? ReminderMinutesBefore { get; init; }

    public string? CategoryKey { get; init; }

    public string? InternalNote { get; init; }
}

/// <summary>The confirmation step: the composed request plus what binds it (plan §4.3 step 5).</summary>
public sealed record CreateAnnouncementRequest
{
    public AnnouncementCompositionRequest? Announcement { get; init; }

    /// <summary>The hash the preview returned; a mismatch refuses the write.</summary>
    public string? PlanHash { get; init; }

    /// <summary>The phrase the operator typed by hand.</summary>
    public string? ConfirmationPhrase { get; init; }

    /// <summary>Why this is being sent; written to the audit trail.</summary>
    public string? Reason { get; init; }
}

public sealed record UpdateAnnouncementRequest
{
    public AnnouncementCompositionRequest? Announcement { get; init; }

    public string? Reason { get; init; }
}

public sealed record CancelAnnouncementRequest
{
    public string? Reason { get; init; }
}

/// <summary>The categories and warning templates a composer offers, as the server defines them.</summary>
public sealed record AnnouncementCompositionOptions
{
    public required IReadOnlyList<AnnouncementCategoryView> Categories { get; init; }

    public required IReadOnlyList<AnnouncementTemplateView> Templates { get; init; }

    /// <summary>The zone every announcement is interpreted in; not operator-selectable.</summary>
    public required string TimeZoneId { get; init; }

    /// <summary>The earliest local date an announcement may be written for.</summary>
    public required DateOnly EarliestLocalDate { get; init; }
}

public sealed record AnnouncementCategoryView
{
    public required string Key { get; init; }

    public required string Name { get; init; }

    public required string BackgroundColor { get; init; }

    public static AnnouncementCategoryView From(AnnouncementCategory category) => new()
    {
        Key = category.Key,
        Name = category.Name,
        BackgroundColor = category.BackgroundColor,
    };
}

public sealed record AnnouncementTemplateView
{
    public required string Key { get; init; }

    public required string Name { get; init; }

    public required string SuggestedTitle { get; init; }

    public required string SuggestedBody { get; init; }

    public required string CategoryKey { get; init; }

    public static AnnouncementTemplateView From(AnnouncementTemplate template) => new()
    {
        Key = template.Key,
        Name = template.Name,
        SuggestedTitle = template.SuggestedTitle,
        SuggestedBody = template.SuggestedBody,
        CategoryKey = template.CategoryKey,
    };
}
