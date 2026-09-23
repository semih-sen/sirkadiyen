using Sirkadiyen.Application.Vault;
using Xunit;

namespace Sirkadiyen.Infrastructure.UnitTests.Vault;

public sealed class VaultAgentOutputParserTests
{
    [Fact]
    public void Parses_a_bare_object()
    {
        VaultNoteDraft? draft = VaultAgentOutputParser.ParseNoteDraft(
            """{"folder":"Farmakoloji","title":"Beta Blokerler","backlinks":[{"target":"Aritmi","reason":"tedavi"}]}""",
            out string? error);

        Assert.Null(error);
        Assert.NotNull(draft);
        Assert.Equal("Farmakoloji", draft.Folder);
        Assert.Equal("Beta Blokerler", draft.Title);
        Assert.Equal([new VaultBacklinkRequest("Aritmi", "tedavi")], draft.Backlinks);
    }

    [Fact]
    public void Parses_the_last_fenced_block_after_prose()
    {
        const string Output = """
            Notu yazdım. İçinde {süslü parantez} geçen bir cümle.

            ```json
            {"Folder": "", "Title": "X", "Backlinks": [{"target": "  "}, {"target": "Y"},]}
            ```
            """;

        VaultNoteDraft? draft = VaultAgentOutputParser.ParseNoteDraft(Output, out _);

        Assert.NotNull(draft);
        Assert.Equal("", draft.Folder);
        Assert.Equal("X", draft.Title);
        Assert.Equal("Y", Assert.Single(draft.Backlinks).Target);
    }

    [Fact]
    public void Tolerates_missing_backlinks()
    {
        VaultNoteDraft? draft = VaultAgentOutputParser.ParseNoteDraft("""{"title":"X"}""", out _);

        Assert.NotNull(draft);
        Assert.Empty(draft.Backlinks);
        Assert.Null(draft.Folder);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("tamam")]
    [InlineData("{bozuk json")]
    [InlineData("{\"title\": 5}")]
    public void Reports_an_unreadable_answer(string? output)
    {
        Assert.Null(VaultAgentOutputParser.ParseNoteDraft(output, out string? error));
        Assert.NotNull(error);
    }
}
