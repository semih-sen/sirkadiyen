using Sirkadiyen.Application.Vault;
using Xunit;

namespace Sirkadiyen.Infrastructure.UnitTests.Vault;

public sealed class VaultPathPolicyTests
{
    [Theory]
    [InlineData(null, "")]
    [InlineData("  ", "")]
    [InlineData("Kardiyoloji", "Kardiyoloji")]
    [InlineData("/Tıp\\Farmakoloji/", "Tıp/Farmakoloji")]
    public void NormalizeFolder_accepts_usable_folders(string? input, string expected)
    {
        Assert.Equal(expected, VaultPathPolicy.NormalizeFolder(input, out string? error));
        Assert.Null(error);
    }

    [Theory]
    [InlineData("../dışarı")]
    [InlineData("Tıp/../..")]
    [InlineData(".obsidian")]
    [InlineData("Tıp//Farmakoloji")]
    [InlineData("Kardiyo:loji")]
    public void NormalizeFolder_refuses_folders_that_escape_or_hide(string input)
    {
        Assert.Null(VaultPathPolicy.NormalizeFolder(input, out string? error));
        Assert.NotNull(error);
    }

    [Theory]
    [InlineData("Beta Blokerler", "Beta Blokerler")]
    [InlineData("Beta_Blokerler.md", "Beta_Blokerler")]
    public void NormalizeTitle_accepts_with_or_without_extension(string input, string expected) =>
        Assert.Equal(expected, VaultPathPolicy.NormalizeTitle(input, out _));

    [Theory]
    [InlineData("Beta#Bloker")]
    [InlineData("a/b")]
    [InlineData("[[Link]]")]
    [InlineData(".gizli")]
    public void NormalizeTitle_refuses_what_Obsidian_cannot_link(string input) =>
        Assert.Null(VaultPathPolicy.NormalizeTitle(input, out _));

    [Theory]
    [InlineData("Beta Blokerler: Özet", "Beta Blokerler Özet")]
    [InlineData("  [[Aritmi]] #tip.md ", "Aritmi tip")]
    [InlineData("...", null)]
    [InlineData(null, null)]
    public void SanitizeTitle_rewrites_instead_of_refusing(string? input, string? expected) =>
        Assert.Equal(expected, VaultPathPolicy.SanitizeTitle(input));

    [Fact]
    public void Place_makes_the_title_unique_across_the_whole_vault()
    {
        VaultCatalog catalog = VaultCatalog.Build(["Kardiyoloji/Beta Blokerler.md", "Farmakoloji/beta blokerler_2.md"]);

        VaultNotePlacement placement = VaultPathPolicy.Place(catalog, "Farmakoloji", folderMustExist: true, "Beta Blokerler");

        Assert.Equal("Farmakoloji/Beta Blokerler_3.md", placement.Path);
        Assert.Equal("Beta Blokerler_3", placement.Title);
        Assert.Null(placement.Warning);
    }

    [Fact]
    public void Place_falls_back_to_the_root_for_an_unknown_proposed_folder()
    {
        VaultCatalog catalog = VaultCatalog.Build(["Kardiyoloji/Aritmi.md"]);

        VaultNotePlacement placement = VaultPathPolicy.Place(catalog, "Uydurma", folderMustExist: true, "Yeni");

        Assert.Equal("Yeni.md", placement.Path);
        Assert.NotNull(placement.Warning);
    }

    [Fact]
    public void Place_creates_a_folder_the_user_named()
    {
        VaultCatalog catalog = VaultCatalog.Build(["Kardiyoloji/Aritmi.md"]);

        VaultNotePlacement placement = VaultPathPolicy.Place(catalog, "Yeni Klasör", folderMustExist: false, "Not");

        Assert.Equal("Yeni Klasör/Not.md", placement.Path);
        Assert.Null(placement.Warning);
    }
}
