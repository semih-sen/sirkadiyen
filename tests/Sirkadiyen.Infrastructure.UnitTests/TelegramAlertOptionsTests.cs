using Sirkadiyen.Application.Notifications;
using Sirkadiyen.Infrastructure.Notifications;
using Xunit;

namespace Sirkadiyen.Infrastructure.UnitTests;

public sealed class TelegramAlertOptionsTests
{
    [Theory]
    [InlineData("5027475773,1176903009")]
    [InlineData("5027475773, 1176903009")]
    [InlineData("5027475773;1176903009")]
    [InlineData("5027475773 1176903009")]
    [InlineData("\n5027475773\n1176903009\n")]
    public void ChatIdsAreReadFromAnyOfTheSeparatorsSomeoneWouldActuallyType(string configured)
    {
        Assert.Equal(
            [5027475773L, 1176903009L],
            TelegramAlertOptions.ParseChatIds(configured));
    }

    [Fact]
    public void AGroupChatIdIsNegativeSoTheSignIsData()
    {
        Assert.Equal([-1001234567890L], TelegramAlertOptions.ParseChatIds("-1001234567890"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NoConfiguredChatIsAnEmptyListRatherThanAFailure(string? configured)
    {
        Assert.Empty(TelegramAlertOptions.ParseChatIds(configured));
    }

    [Fact]
    public void AChatIdThatIsNotANumberIsRefusedRatherThanSilentlySkipped()
    {
        // A mistyped id would otherwise leave one operator quietly unreachable, which is the
        // failure mode alerting exists to prevent.
        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(
            () => TelegramAlertOptions.ParseChatIds("5027475773,@semih"));

        Assert.Contains("@semih", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheChannelIsOffUnlessBothATokenAndARecipientAreConfigured()
    {
        Assert.False(new TelegramAlertOptions().IsConfigured);
        Assert.False(new TelegramAlertOptions { BotToken = "t" }.IsConfigured);
        Assert.False(new TelegramAlertOptions { ChatIds = [1] }.IsConfigured);
        Assert.True(new TelegramAlertOptions { BotToken = "t", ChatIds = [1] }.IsConfigured);
    }

    [Fact]
    public void TheDescriptionSaysWhatIsConfiguredWithoutSayingTheToken()
    {
        // It exists to be logged at startup. A credential in a log line is the thing
        // AI_GUIDELINE §15 forbids, and startup logs are the easiest place to leak one.
        string description = new TelegramAlertOptions
        {
            BotToken = "1234567:secret",
            ChatIds = [1, 2],
            MinimumSeverity = OperatorAlertSeverity.Warning,
        }.Describe();

        Assert.DoesNotContain("secret", description, StringComparison.Ordinal);
        Assert.Contains("2 chat(s)", description, StringComparison.Ordinal);
        Assert.Contains("Warning", description, StringComparison.Ordinal);
    }

    [Fact]
    public void ARepeatedChatIdIsRefusedBecauseItWouldDeliverEveryAlertTwice()
    {
        Assert.Throws<InvalidOperationException>(
            () => new TelegramAlertOptions { ChatIds = [1, 1] }.Validate());
    }

    [Fact]
    public void ANonPositiveTimeoutIsRefused()
    {
        Assert.Throws<InvalidOperationException>(
            () => new TelegramAlertOptions { Timeout = TimeSpan.Zero }.Validate());
    }

    [Fact]
    public void AZeroCooldownIsAllowedBecauseItMeansSuppressNothing()
    {
        new TelegramAlertOptions { RepeatCooldown = TimeSpan.Zero }.Validate();
    }
}
