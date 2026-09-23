using Sirkadiyen.Application.Vault;
using Xunit;

namespace Sirkadiyen.Infrastructure.UnitTests.Vault;

public sealed class VaultLinkNormalizerTests
{
    private static readonly VaultCatalog Catalog = VaultCatalog.Build(
    [
        "Farmakoloji/Otonom_Sinir_Sistemi.md",
        "Dahiliye/Astım.md",
        "Kardiyoloji/Giriş.md",
        "Farmakoloji/Giriş.md",
        "Kardiyoloji/Aritmi_Tipleri.md",
    ]);

    [Fact]
    public void Leaves_exact_links_alone()
    {
        const string Content = "Bkz. [[Otonom_Sinir_Sistemi]], [[Aritmi_Tipleri#Taşiaritmiler|AF]] ve [[Kardiyoloji/Giriş]].";

        VaultLinkNormalization result = VaultLinkNormalizer.Normalize(Content, Catalog);

        Assert.Equal(Content, result.Content);
        Assert.Empty(result.Corrected);
        Assert.Empty(result.Removed);
    }

    /// <summary>The links the end-to-end run actually produced for underscore titles.</summary>
    [Fact]
    public void Corrects_misspelled_targets_and_keeps_the_wording()
    {
        VaultLinkNormalization result = VaultLinkNormalizer.Normalize(
            "[[Otonom Sinir Sistemi]] ile [[aritmi tipleri#Bradiaritmiler|bradikardi]] ve [[Astim]].",
            Catalog);

        Assert.Equal(
            "[[Otonom_Sinir_Sistemi|Otonom Sinir Sistemi]] ile [[Aritmi_Tipleri#Bradiaritmiler|bradikardi]] ve [[Astım|Astim]].",
            result.Content);
        Assert.Equal(["Otonom Sinir Sistemi", "aritmi tipleri", "Astim"], result.Corrected);
    }

    [Fact]
    public void Unwraps_links_to_notes_that_do_not_exist_or_are_ambiguous()
    {
        VaultLinkNormalization result = VaultLinkNormalizer.Normalize(
            "[[Uydurma Not]], [[Yok|görünen]] ve [[Giris]].",
            Catalog);

        Assert.Equal("Uydurma Not, görünen ve Giris.", result.Content);
        Assert.Equal(["Uydurma Not", "Yok", "Giris"], result.Removed);
    }

    [Fact]
    public void Leaves_embeds_alone()
    {
        const string Content = "![[ekg.png]] ve ![[Otonom Sinir Sistemi]]";

        Assert.Equal(Content, VaultLinkNormalizer.Normalize(Content, Catalog).Content);
    }
}
