using System.Text;

namespace Sirkadiyen.Application.Vault;

/// <summary>
/// Writes the two prompts a job gives the agent. The standing rules (tone, formatting) live in the
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
