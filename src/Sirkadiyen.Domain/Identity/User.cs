namespace Sirkadiyen.Domain.Identity;

/// <summary>A Google-authenticated local Sirkadiyen account.</summary>
public sealed class User
{
    public const int MaximumGoogleSubjectLength = 255;

    public const int MaximumEmailLength = 320;

    public const int MaximumDisplayNameLength = 200;

    private User()
    {
        // Materialization constructor.
    }

    public Guid Id { get; private init; }

    /// <summary>The immutable Google <c>sub</c> claim.</summary>
    public string GoogleSubject { get; private init; } = string.Empty;

    public string Email { get; private set; } = string.Empty;

    public string NormalizedEmail { get; private set; } = string.Empty;

    public bool IsEmailVerified { get; private set; }

    public string? DisplayName { get; private set; }

    public UserRole Role { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private init; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public DateTimeOffset LastSignedInAtUtc { get; private set; }

    public uint RowVersion { get; private set; }

    public static User CreateFromGoogle(
        string googleSubject,
        string email,
        bool isEmailVerified,
        string? displayName,
        UserRole role,
        DateTimeOffset atUtc)
    {
        if (!isEmailVerified)
        {
            throw new ArgumentException(
                "A local account requires a Google-verified email.",
                nameof(isEmailVerified));
        }

        googleSubject = RequiredBounded(
            googleSubject,
            MaximumGoogleSubjectLength,
            nameof(googleSubject));
        (email, string normalizedEmail) = NormalizeEmail(email);
        displayName = OptionalBounded(displayName, MaximumDisplayNameLength, nameof(displayName));

        return new User
        {
            Id = Guid.CreateVersion7(),
            GoogleSubject = googleSubject,
            Email = email,
            NormalizedEmail = normalizedEmail,
            IsEmailVerified = true,
            DisplayName = displayName,
            Role = role,
            CreatedAtUtc = atUtc,
            UpdatedAtUtc = atUtc,
            LastSignedInAtUtc = atUtc,
        };
    }

    /// <summary>Refreshes mutable profile fields after another verified sign-in.</summary>
    public void RegisterVerifiedGoogleSignIn(
        string email,
        bool isEmailVerified,
        string? displayName,
        DateTimeOffset atUtc)
    {
        if (!isEmailVerified)
        {
            throw new ArgumentException(
                "A local account requires a Google-verified email.",
                nameof(isEmailVerified));
        }

        (email, string normalizedEmail) = NormalizeEmail(email);
        displayName = OptionalBounded(displayName, MaximumDisplayNameLength, nameof(displayName));

        Email = email;
        NormalizedEmail = normalizedEmail;
        IsEmailVerified = true;
        DisplayName = displayName;
        UpdatedAtUtc = atUtc;
        LastSignedInAtUtc = atUtc;
    }

    /// <summary>
    /// Grants a role without silently demoting an existing operator during
    /// bootstrap sign-in.
    /// </summary>
    public void GrantRole(UserRole role, DateTimeOffset atUtc)
    {
        if (role <= Role)
        {
            return;
        }

        Role = role;
        UpdatedAtUtc = atUtc;
    }

    /// <summary>
    /// Sets the role to an explicit value, allowing both promotion and demotion (ADR-119). Unlike
    /// <see cref="GrantRole"/> — which only ever promotes, so a re-authenticating operator is never
    /// silently demoted at sign-in — this is the deliberate administrative role change and can lower
    /// a role as well as raise it. Returns whether the role actually changed, so the caller can skip
    /// writing an audit record for a no-op.
    /// </summary>
    public bool ChangeRole(UserRole role, DateTimeOffset atUtc)
    {
        if (role == Role)
        {
            return false;
        }

        Role = role;
        UpdatedAtUtc = atUtc;
        return true;
    }

    public static string NormalizeEmailValue(string email) => NormalizeEmail(email).Normalized;

    private static (string Display, string Normalized) NormalizeEmail(string email)
    {
        email = RequiredBounded(email, MaximumEmailLength, nameof(email));

        if (!email.Contains('@', StringComparison.Ordinal)
            || email.StartsWith('@')
            || email.EndsWith('@'))
        {
            throw new ArgumentException("A valid email address is required.", nameof(email));
        }

        return (email, email.ToUpperInvariant());
    }

    private static string RequiredBounded(string value, int maximumLength, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        value = value.Trim();
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            value.Length,
            maximumLength,
            parameterName);
        return value;
    }

    private static string? OptionalBounded(
        string? value,
        int maximumLength,
        string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        value = value.Trim();
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            value.Length,
            maximumLength,
            parameterName);
        return value;
    }
}

public enum UserRole
{
    User = 0,
    SuperAdmin = 1,
}
