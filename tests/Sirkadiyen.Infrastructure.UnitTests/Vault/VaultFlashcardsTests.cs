using Sirkadiyen.Application.Vault;
using Xunit;

namespace Sirkadiyen.Infrastructure.UnitTests.Vault;

public sealed class VaultFlashcardsTests
{
    private const string Original = """
        ---
        tags:
          - farmakoloji
        ---
        # Beta Blokerler

        Beta blokerler astımda kontrendikedir.
        Kardiyoselektif olanlar β1 reseptörlerine seçicidir.

        ## Yan etkiler

        - Bradikardi
        - Hipogliseminin maskelenmesi
        """;

    private const string Converted = """
        ---
        tags:
          - farmakoloji
        ---
        #flashcards/farmakoloji

        # Beta Blokerler

        Beta blokerler ==astım==da kontrendikedir.
        Kardiyoselektif olanlar ==β1== reseptörlerine seçicidir.

        ## Yan etkiler

        - ==Bradikardi==
        - Hipogliseminin maskelenmesi

        ## Flashcards

        Beta blokerlerin kontrendike olduğu solunum hastalığı nedir?::Astım
        Başlıca yan etkiler nelerdir?
        ?
        Bradikardi, hipogliseminin maskelenmesi
        """;

    [Theory]
    [InlineData("Farmakoloji", "#flashcards/farmakoloji")]
    [InlineData("Farmakoloji/Otonom Sinir Sistemi", "#flashcards/farmakoloji/otonom-sinir-sistemi")]
    [InlineData("Dönem 3/İç Hastalıkları", "#flashcards/donem-3/ic-hastaliklari")]
    [InlineData("01/Kardiyoloji", "#flashcards/kardiyoloji")]
    [InlineData("", null)]
    public void Maps_a_folder_to_its_deck(string folder, string? deck) =>
        Assert.Equal(deck, VaultFlashcards.DeckForFolder(folder));

    [Fact]
    public void Counts_the_cards_the_plugin_finds()
    {
        Assert.Equal(new VaultFlashcardSummary("#flashcards/farmakoloji", 3, 2), VaultFlashcards.Summarize(Converted));
        Assert.Empty(VaultFlashcards.Problems(Converted));
    }

    [Fact]
    public void Skips_code_and_reads_a_frontmatter_deck()
    {
        const string content = """
            ---
            tags: [farmakoloji, flashcards/farmakoloji]
            ---
            Bir ==vurgu== ve `kod::içinde` bir ayraç.

            ```
            ==kod bloğu== ve soru::cevap
            ```
            """;

        Assert.Equal(new VaultFlashcardSummary("#flashcards/farmakoloji", 1, 0), VaultFlashcards.Summarize(content));
        Assert.Empty(VaultFlashcards.Problems(content));
    }

    [Fact]
    public void Ignores_a_frontmatter_property_that_only_shares_the_name()
    {
        const string content = "---\nflashcards: true\n---\n# Not\n";

        Assert.Null(VaultFlashcards.Summarize(content).Deck);
    }

    [Fact]
    public void Reports_a_deck_that_comes_after_the_first_card()
    {
        string content = "Bir ==vurgu==.\n\n#flashcards/konu\n";

        Assert.Contains(VaultFlashcards.Problems(content), static problem => problem.Contains("ilk karttan sonra", StringComparison.Ordinal));
    }

    [Fact]
    public void Reports_a_deck_sharing_its_line_with_text()
    {
        string content = "#flashcards/konu Soru::Cevap\n";

        Assert.Contains(VaultFlashcards.Problems(content), static problem => problem.Contains("aynı satırda", StringComparison.Ordinal));
    }

    [Fact]
    public void Reports_a_note_without_deck_or_cards()
    {
        Assert.Equal(2, VaultFlashcards.Problems("# Not\n\nDüz metin.\n").Count);
    }

    [Fact]
    public void Separates_the_deck_tag_from_the_text_below_it()
    {
        Assert.Equal("#flashcards/konu\n\n# Başlık\n", VaultFlashcards.SeparateDeckTag("#flashcards/konu\n# Başlık\n"));
        Assert.Equal("#flashcards/konu\n\n# Başlık\n", VaultFlashcards.SeparateDeckTag("#flashcards/konu\n\n# Başlık\n"));
    }

    [Fact]
    public void Accepts_a_conversion_that_only_added_cards()
    {
        Assert.Null(VaultFlashcards.EvaluateConversion(Original, Converted));
    }

    [Fact]
    public void Refuses_a_conversion_that_filed_no_cards()
    {
        string withoutDeck = Converted.Replace("#flashcards/farmakoloji\n", string.Empty, StringComparison.Ordinal);

        Assert.Contains("deste etiketi yok", VaultFlashcards.EvaluateConversion(Original, withoutDeck), StringComparison.Ordinal);
    }

    [Fact]
    public void Refuses_a_conversion_that_touched_the_frontmatter()
    {
        string retagged = Converted.Replace("  - farmakoloji", "  - kardiyoloji", StringComparison.Ordinal);

        Assert.Contains("frontmatter", VaultFlashcards.EvaluateConversion(Original, retagged), StringComparison.Ordinal);
    }

    [Fact]
    public void Refuses_a_conversion_that_rewrote_the_text()
    {
        string rewritten = Converted
            .Replace("Beta blokerler ==astım==da kontrendikedir.", "Astımda ==beta bloker== verilmez.", StringComparison.Ordinal)
            .Replace("Kardiyoselektif olanlar", "Selektif olanlar", StringComparison.Ordinal);

        Assert.Contains("Orijinal satırlardan", VaultFlashcards.EvaluateConversion(Original, rewritten), StringComparison.Ordinal);
    }
}
