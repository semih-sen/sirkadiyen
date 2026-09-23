using Sirkadiyen.Application.Vault;
using Xunit;

namespace Sirkadiyen.Infrastructure.UnitTests.Vault;

public sealed class VaultCatalogTests
{
    private static readonly string[] Paths =
    [
        "Kardiyoloji/Aritmi_Tipleri.md",
        "Kardiyoloji/Alt/Giriş.md",
        "Farmakoloji/Giriş.md",
        "Kök Not.md",
        "Kardiyoloji/ekg.png",
        ".obsidian/workspace.json",
        ".trash/Silinmiş.md",
        "Arşiv/.gizli/Eski.md",
    ];

    [Fact]
    public void Build_lists_only_visible_markdown_notes()
    {
        VaultCatalog catalog = VaultCatalog.Build(Paths);

        Assert.Equal(
            ["Farmakoloji/Giriş.md", "Kardiyoloji/Alt/Giriş.md", "Kardiyoloji/Aritmi_Tipleri.md", "Kök Not.md"],
            catalog.Notes.Select(static note => note.Path));
    }

    [Fact]
    public void Build_links_ambiguous_titles_by_path()
    {
        VaultCatalog catalog = VaultCatalog.Build(Paths);

        Assert.Equal(
            ["Farmakoloji/Giriş", "Kardiyoloji/Alt/Giriş", "Aritmi_Tipleri", "Kök Not"],
            catalog.Notes.Select(static note => note.LinkTarget));
    }

    [Fact]
    public void Build_derives_folders_including_ancestors_but_not_hidden_ones()
    {
        VaultCatalog catalog = VaultCatalog.Build(Paths);

        Assert.Equal(["Farmakoloji", "Kardiyoloji", "Kardiyoloji/Alt"], catalog.Folders);
    }

    [Theory]
    [InlineData("Aritmi_Tipleri")]
    [InlineData("aritmi_tipleri")]
    [InlineData("[[Aritmi_Tipleri]]")]
    [InlineData("Kardiyoloji/Aritmi_Tipleri")]
    [InlineData("Kardiyoloji/Aritmi_Tipleri.md")]
    public void Find_accepts_the_forms_an_agent_may_echo_back(string target)
    {
        VaultCatalog catalog = VaultCatalog.Build(Paths);

        Assert.Equal("Kardiyoloji/Aritmi_Tipleri.md", catalog.Find(target)?.Path);
    }

    [Theory]
    [InlineData("Giriş")]
    [InlineData("Silinmiş")]
    [InlineData("")]
    public void Find_does_not_guess_ambiguous_or_hidden_targets(string target) =>
        Assert.Null(VaultCatalog.Build(Paths).Find(target));

    [Fact]
    public void Build_tolerates_keys_differing_only_in_case()
    {
        VaultCatalog catalog = VaultCatalog.Build(["A/Not.md", "a/Not.md"]);

        Assert.Equal(2, catalog.Notes.Count);
        Assert.True(catalog.IsTitleTaken("not"));
    }
}
