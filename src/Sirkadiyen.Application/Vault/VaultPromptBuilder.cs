using System.Text;

namespace Sirkadiyen.Application.Vault;

/// <summary>
/// Writes the prompts a job gives the agent. The standing rules (tone, formatting) live in the
/// workspace's <c>CLAUDE.md</c>; these carry only what is specific to one job: the request, the
/// catalog, the files to touch, and the exact answer format the job parses.
/// </summary>
public static class VaultPromptBuilder
{
    /// <summary>The file the note-writing run writes and the job uploads.</summary>
    public const string NoteFileName = "note.md";

    /// <summary>The workspace folder existing notes are copied into for the backlink run.</summary>
    public const string BacklinkFolderName = "backlinks";

    /// <summary>
    /// The shape of the note-writing run's answer, enforced by the agent when it supports structured
    /// output. The prompt still spells the format out, and the parser still tolerates prose around it,
    /// so an agent that ignores the schema degrades to the text path rather than failing.
    /// </summary>
    public const string NoteOutputSchema = """
        {"type":"object","properties":{"folder":{"type":"string"},"title":{"type":"string"},"backlinks":{"type":"array","items":{"type":"object","properties":{"target":{"type":"string"},"reason":{"type":"string"}},"required":["target"]}}},"required":["folder","title","backlinks"]}
        """;

    public static string BuildNotePrompt(VaultNoteRequest request, VaultCatalog catalog, int maxBacklinks)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(catalog);

        StringBuilder prompt = new();
        prompt.AppendLine("Görev: Obsidian vault'um için yeni bir not yaz.");
        prompt.AppendLine();
        prompt.AppendLine("Kullanıcı isteği:");
        prompt.AppendLine("<istek>");
        prompt.AppendLine(request.Prompt);
        prompt.AppendLine("</istek>");
        prompt.AppendLine();
        prompt.AppendLine($"Notu bu dizindeki `{NoteFileName}` dosyasına yaz. Başka hiçbir dosya oluşturma veya değiştirme.");
        prompt.AppendLine();

        prompt.AppendLine("Mevcut notlar (atıf hedefleri; [[...]] içinde tam olarak bu yazımla kullan):");
        AppendList(prompt, catalog.Notes.Select(static note => note.LinkTarget));
        prompt.AppendLine();
        prompt.AppendLine("Mevcut klasörler:");
        AppendList(prompt, catalog.Folders);
        prompt.AppendLine();

        if (request.Folder is not null)
        {
            prompt.AppendLine(request.Folder.Length == 0
                ? "Not vault'un köküne konacak."
                : $"Not '{request.Folder}' klasörüne konacak.");
        }

        if (request.Title is not null)
        {
            prompt.AppendLine($"Notun adı '{request.Title}' olacak.");
        }

        if (request.Folder is not null || request.Title is not null)
        {
            prompt.AppendLine();
        }

        prompt.AppendLine("Flashcard'lar (Spaced Repetition eklentisi; sözdizimi kalıcı kurallarda):");
        prompt.AppendLine("- Frontmatter'dan sonra, başlıktan önce tek başına bir satırda deste etiketi yaz ve altına bir boş satır bırak.");
        prompt.AppendLine(request.Folder is not null && VaultFlashcards.DeckForFolder(request.Folder) is { } deck
            ? $"- Deste etiketi: `{deck}`"
            : $"- Deste etiketini notu koyduğun klasörden türet (`{VaultFlashcards.DeckTag}/klasor/alt-klasor`); kökteki bir not için konuyu anlatan tek bir alt deste seç.");
        prompt.AppendLine("- Metindeki sınav için önemli, kısa ve kesin bilgileri ==vurgu== ile işaretle; her vurgu bir cloze kartı olur.");
        prompt.AppendLine($"- Notun en sonuna `{VaultFlashcards.SectionHeading}` bölümü ekle ve oraya notta cevabı bulunan Soru::Cevap kartları yaz.");
        prompt.AppendLine();

        prompt.AppendLine("Kurallar:");
        prompt.AppendLine("- Metinde, ilgili olduklarında yalnızca yukarıdaki listede bulunan notlara [[Not Adı]] biçiminde atıf yap. Listede olmayan bir nota bağlantı verme.");
        prompt.AppendLine("- Bağlantı hedefini listedeki yazımla karakteri karakterine aynı yaz: alt çizgiyi boşluğa çevirme, harfleri değiştirme. Metinde farklı görünmesini istersen [[Hedef|görünen metin]] kullan.");
        if (maxBacklinks > 0)
        {
            prompt.AppendLine($"- Yeni nota geri atıf yapması gereken mevcut notları \"backlinks\" altında belirt: yalnızca gerçekten ilgili olanlar, en fazla {maxBacklinks} tane.");
        }

        prompt.AppendLine();
        prompt.AppendLine("Notu yazdıktan sonra yanıtın yalnızca şu JSON nesnesi olsun:");
        prompt.AppendLine("""{"folder": "...", "title": "...", "backlinks": [{"target": "...", "reason": "..."}]}""");
        prompt.AppendLine("- folder: mevcut klasörlerden biri; vault kökü için \"\".");
        prompt.AppendLine("- title: .md uzantısı olmadan not adı; / \\ : * ? \" < > | # ^ [ ] karakterlerini içermesin.");
        prompt.AppendLine(maxBacklinks > 0
            ? "- target: mevcut notlar listesindeki yazımla birebir aynı; reason: bu notun yeni nota neden atıf yapması gerektiği."
            : "- backlinks: her zaman boş dizi [].");

        return prompt.ToString();
    }

    public static string BuildBacklinkPrompt(string newNoteLinkTarget, IReadOnlyList<VaultBacklinkTarget> targets)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newNoteLinkTarget);
        ArgumentNullException.ThrowIfNull(targets);

        StringBuilder prompt = new();
        prompt.AppendLine("Görev: Mevcut notlara, yeni yazılan nota geri atıf ekle.");
        prompt.AppendLine();
        prompt.AppendLine($"Yeni not: [[{newNoteLinkTarget}]]");
        prompt.AppendLine($"Yeni notun içeriği `{NoteFileName}` dosyasında; bağlam için okuyabilirsin, değiştirme.");
        prompt.AppendLine();
        prompt.AppendLine("Düzenlenecek dosyalar:");
        foreach (VaultBacklinkTarget target in targets)
        {
            string reason = string.IsNullOrWhiteSpace(target.Reason) ? string.Empty : $" - gerekçe: {target.Reason}";
            prompt.AppendLine($"- `{target.WorkspaceFile}` (not: {target.Note.Title}){reason}");
        }

        prompt.AppendLine();
        prompt.AppendLine("Kurallar:");
        prompt.AppendLine($"- Her dosyada konuyla en ilgili yere [[{newNoteLinkTarget}]] bağlantısını bağlamı bozmadan ekle: uygun bir cümlenin içine ya da ilgili bölümün sonuna kısa bir satır olarak.");
        prompt.AppendLine("- Dosyanın geri kalanını olduğu gibi bırak: mevcut metni silme, yeniden yazma, biçimlendirmesini veya frontmatter'ını değiştirme.");
        prompt.AppendLine("- Listede olmayan hiçbir dosyayı oluşturma veya değiştirme.");
        prompt.AppendLine("- Bitirdiğinde yalnızca \"tamam\" yaz.");

        return prompt.ToString();
    }

    /// <summary>
    /// The prompt for adding flashcards to an existing note (ADR-170). The note's copy is the workspace's
    /// <see cref="NoteFileName"/>, edited in place; the answer is free text, since everything the job
    /// needs afterwards is read from the edited file.
    /// </summary>
    /// <param name="deck">The deck the note's folder maps to, or null for a note in the vault root.</param>
    public static string BuildFlashcardPrompt(VaultNote note, string? deck)
    {
        ArgumentNullException.ThrowIfNull(note);

        StringBuilder prompt = new();
        prompt.AppendLine("Görev: Mevcut bir nota Spaced Repetition flashcard'ları ekle.");
        prompt.AppendLine();
        prompt.AppendLine($"Not: `{NoteFileName}` (vault'taki yeri: {note.Path}, adı: {note.Title})");
        prompt.AppendLine("Önce dosyanın tamamını oku ve konuyu kavra; ardından aynı dosyayı yerinde düzenle.");
        prompt.AppendLine();
        prompt.AppendLine("Yapılacaklar:");
        prompt.AppendLine(deck is null
            ? $"1. Deste etiketi: frontmatter'dan sonra (frontmatter yoksa dosyanın ilk satırına), başlıktan önce tek başına bir satıra notun konusunu anlatan bir deste yaz (`{VaultFlashcards.DeckTag}/konu`) ve altına bir boş satır bırak."
            : $"1. Deste etiketi: frontmatter'dan sonra (frontmatter yoksa dosyanın ilk satırına), başlıktan önce tek başına bir satıra `{deck}` yaz ve altına bir boş satır bırak.");
        prompt.AppendLine("2. Cloze: notun mevcut cümlelerinde sınav için önemli, kısa ve kesin bilgileri (terim, sayı, ilaç, mekanizma) ==...== ile vurgula; bir paragraf ya da liste bloğunda en fazla 3 vurgu.");
        prompt.AppendLine($"3. Soru kartları: dosyanın en sonuna `{VaultFlashcards.SectionHeading}` bölümü ekle; notta cevabı bulunan, konuyu kavramaya yönelik Soru::Cevap kartları yaz (her biri ayrı satırda).");
        prompt.AppendLine();
        prompt.AppendLine("Kurallar:");
        prompt.AppendLine("- Notun metnini değiştirme: cümleleri yeniden yazma, silme, kısaltma ya da yerini değiştirme; yazım hatalarını bile düzeltme. Değişiklik yalnızca yukarıdaki üç ekleme olsun; gerekirse başlık satırlarının altına boş satır ekleyebilirsin.");
        prompt.AppendLine("- Frontmatter'a dokunma, yeni [[bağlantı]] ekleme, `<!--SR:...-->` yorumlarını olduğu gibi bırak. Notta zaten olan ==vurgu==ları kaldırma.");
        prompt.AppendLine("- Vurguyu başlıklara, tablolara, kod bloklarına ve [[bağlantı]]ların içine koyma; ipucu ya da numaralı cloze sözdizimi kullanma.");
        prompt.AppendLine("- Başka hiçbir dosya oluşturma veya değiştirme.");
        prompt.AppendLine("- Bitirdiğinde yalnızca \"tamam\" yaz.");

        return prompt.ToString();
    }

    private static void AppendList(StringBuilder prompt, IEnumerable<string> items)
    {
        bool any = false;
        foreach (string item in items)
        {
            prompt.Append("- ").AppendLine(item);
            any = true;
        }

        if (!any)
        {
            prompt.AppendLine("(yok)");
        }
    }
}

/// <param name="Note">The existing note being edited.</param>
/// <param name="WorkspaceFile">Its copy's path relative to the workspace, as the agent is told it.</param>
/// <param name="Reason">Why the agent proposed linking it, passed back so the edit lands in the right place.</param>
public sealed record VaultBacklinkTarget(VaultNote Note, string WorkspaceFile, string? Reason);
