using Sirkadiyen.Application.Vault;
using Xunit;

namespace Sirkadiyen.Infrastructure.UnitTests.Vault;

public sealed class VaultFlashcardRequestTests
{
    [Theory]
    [InlineData("Farmakoloji/Beta Blokerler.md", "Farmakoloji/Beta Blokerler.md")]
    [InlineData("  /Kök Not.md ", "Kök Not.md")]
    [InlineData("Klasör\\Alt/Not: özet.md", "Klasör/Alt/Not: özet.md")]
    public void Accepts_an_existing_notes_path(string path, string expected)
    {
        VaultFlashcardRequest? request = VaultFlashcardRequest.Create(path, out string? error);

        Assert.Null(error);
        Assert.Equal(expected, request?.NotePath);
        Assert.Equal(VaultJobKind.Flashcards, request?.Kind);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Farmakoloji/resim.png")]
    [InlineData("../dışarı.md")]
    [InlineData(".obsidian/ayar.md")]
    [InlineData("Klasör//Not.md")]
    public void Refuses_what_cannot_be_a_note(string? path)
    {
        Assert.Null(VaultFlashcardRequest.Create(path, out string? error));
        Assert.NotNull(error);
    }
}
