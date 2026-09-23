using Sirkadiyen.Application.Vault;
using Xunit;

namespace Sirkadiyen.Infrastructure.UnitTests.Vault;

public sealed class VaultBacklinkEditCheckTests
{
    private const string Original = """
        ---
        tags: [kardiyoloji]
        ---
        # Hipertansiyon Tedavi Protokolleri

        İlk basamakta ACE inhibitörleri tercih edilir.
        Kalp hızı kontrolü gereken hastalarda farklı ajanlar kullanılır.

        ## Kaynaklar
        - Ders notu
        """;

    [Fact]
    public void Accepts_a_link_inserted_into_a_sentence()
    {
        string edited = Original.Replace(
            "farklı ajanlar kullanılır.", "[[Beta Blokerler]] gibi farklı ajanlar kullanılır.", StringComparison.Ordinal);

        Assert.Null(VaultBacklinkEditCheck.Evaluate(Original, edited, "Beta Blokerler"));
    }

    [Theory]
    [InlineData("[[beta blokerler|β-blokerler]]")]
    [InlineData("[[Beta Blokerler#Endikasyonlar]]")]
    public void Accepts_alias_and_heading_links(string link) =>
        Assert.Null(VaultBacklinkEditCheck.Evaluate(Original, Original + "\n" + link + "\n", "Beta Blokerler"));

    [Fact]
    public void Refuses_an_edit_without_the_link()
    {
        string? reason = VaultBacklinkEditCheck.Evaluate(Original, Original + "\n[[Başka Not]]\n", "Beta Blokerler");

        Assert.NotNull(reason);
    }

    [Fact]
    public void Refuses_a_rewritten_note()
    {
        const string Rewritten = """
            # Hipertansiyon

            ACE inhibitörleri ve [[Beta Blokerler]] kullanılır. Kaynak: ders notu, ek bilgiler ve uzun bir açıklama.
            """;

        Assert.NotNull(VaultBacklinkEditCheck.Evaluate(Original, Rewritten, "Beta Blokerler"));
    }

    [Fact]
    public void Tolerates_changed_line_endings()
    {
        string edited = Original.ReplaceLineEndings("\r\n") + "\r\n- [[Beta Blokerler]]\r\n";

        Assert.Null(VaultBacklinkEditCheck.Evaluate(Original, edited, "Beta Blokerler"));
    }
}
