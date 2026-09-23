using Sirkadiyen.Application.Vault;
using Xunit;

namespace Sirkadiyen.Infrastructure.UnitTests.Vault;

public sealed class VaultNoteRequestTests
{
    [Fact]
    public void Normalizes_accepted_input()
    {
        VaultNoteRequest? request = VaultNoteRequest.Create("  Beta blokerler özeti  ", "/Farmakoloji/", "Beta Blokerler.md", out string? error);

        Assert.Null(error);
        Assert.NotNull(request);
        Assert.Equal("Beta blokerler özeti", request.Prompt);
        Assert.Equal("Farmakoloji", request.Folder);
        Assert.Equal("Beta Blokerler", request.Title);
    }

    [Fact]
    public void Leaves_placement_to_the_agent_when_not_given()
    {
        VaultNoteRequest? request = VaultNoteRequest.Create("x", null, "  ", out _);

        Assert.NotNull(request);
        Assert.Null(request.Folder);
        Assert.Null(request.Title);
    }

    [Theory]
    [InlineData("", null, null)]
    [InlineData("x", "../dış", null)]
    [InlineData("x", null, "a#b")]
    public void Refuses_unusable_input(string prompt, string? folder, string? title)
    {
        Assert.Null(VaultNoteRequest.Create(prompt, folder, title, out string? error));
        Assert.NotNull(error);
    }

    [Fact]
    public void Refuses_an_overlong_prompt() =>
        Assert.Null(VaultNoteRequest.Create(new string('a', VaultNoteRequest.MaxPromptLength + 1), null, null, out _));
}
