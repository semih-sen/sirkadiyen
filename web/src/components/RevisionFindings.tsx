'use client';

import type { RevisionFindingView, RevisionState } from '@/lib/types';

/**
 * What a validation rule means, and what the operator is being asked to decide (ADR-135).
 *
 * The stored finding is written for the record: it names the rule and carries the numbers, in
 * English, because it is evidence and evidence should read the same on every host. What it does
 * not say is what the rule is *for* — and an operator holding a revision back cannot decide from
 * `LowConfidenceRecord` alone whether they are looking at a broken document or a schedule that
 * really did change. That sentence belongs to the screen, so it lives here.
 *
 * Each entry says three things in order: what the check looked at, what a failure usually means,
 * and what approving anyway would do. The third is the one that matters, because approval is the
 * only way a held revision reaches a calendar.
 */
const RULES: Record<string, { label: string; explains: string }> = {
  EmptyRevision: {
    label: 'Boş revizyon',
    explains:
      'Parser bu belgeden hiç ders çıkaramadı. Boş bir revizyon hiçbir koşulda yayımlanamaz: '
      + 'yayımlansaydı bu kaynağın öğrenci takvimlerindeki bütün etkinlikleri silinirdi. Bu yüzden '
      + 'inceleme beklemez, doğrudan reddedilir — onaylanabilecek bir şey yok. Kaynak daha önce '
      + 'ders yayımlamışsa belge veya parser profili değişmiş demektir. Hiç yayımlamamışsa, bu '
      + 'kaynak yalnızca başka kaynakların derslerini zenginleştiren bir yardımcı kaynak olabilir; '
      + 'o durumda boş olması beklenen ve kalıcı sonuçtur, eksik bir yayın yoktur.',
  },
  RecordDateOutsideAcademicYear: {
    label: 'Akademik yıl dışında tarih',
    explains:
      'Bazı derslerin tarihi kaynağın akademik yılının dışına düşüyor. Bu neredeyse her zaman '
      + 'tarihin yanlış okunduğu anlamına gelir — programın gerçekten taşındığı değil. Aşağıdaki '
      + 'listede tarihler belgedekiyle aynıysa sorun kaynağın akademik yıl ayarındadır; '
      + 'değilse parser tarihi yanlış çözmüştür. Onaylarsanız bu dersler o tarihlerle takvime '
      + 'yazılır.',
  },
  ImpossibleLessonDuration: {
    label: 'Olanaksız ders süresi',
    explains:
      'Bir dersin süresi gerçek olamayacak kadar kısa veya uzun. Genellikle bitiş saati okunamamış '
      + 'ya da iki ayrı ders tek hücrede birleşmiştir. Onaylarsanız öğrencinin takviminde o '
      + 'uzunlukta bir etkinlik oluşur.',
  },
  LowConfidenceRecord: {
    label: 'Düşük güvenli alan',
    explains:
      'Parser bazı zorunlu alanları (tarih, saat, başlık, hedef kitle) tahmin ederek çözdü ve bunu '
      + 'kendisi bildirdi. Belge o alanı belirsiz yazmış demektir. Aşağıdaki kayıtları belgeyle '
      + 'karşılaştırın; doğruysa onaylamak güvenlidir.',
  },
  UnknownAudienceSelector: {
    label: 'Tanımsız hedef kitle değeri',
    explains:
      'Kaynak, katalogda bildirmediği bir grup/alt grup değeri yayımlıyor. İki olasılık var: '
      + 'fakülte gerçekten yeni bir grup açtı ve katalog güncellenmeli, ya da değer yanlış okundu. '
      + 'Onaylarsanız ders yalnızca profilinde o değer yazan öğrencilere gider — değer uydurmaysa '
      + 'hiç kimseye gitmez ve ders sessizce kaybolur.',
  },
  DuplicateStableIdentity: {
    label: 'Yinelenen kalıcı kimlik',
    explains:
      'İki kayıt aynı kalıcı kimliği iddia ediyor. Kalıcı kimlik, bir dersin takvimdeki '
      + 'karşılığını bulmakta kullanılan şeydir; ikisi aynıysa diff hangisinin hangi etkinliğe '
      + 'karşılık geldiğini bilemez ve güncelleme yanlış etkinliğe uygulanabilir. Bu genellikle '
      + 'belgede aynı dersin iki kez yazılmasından ya da kimliği ayıran bir alanın okunamamasından '
      + 'gelir.',
  },
  AudienceOverlap: {
    label: 'Aynı kitleye çakışan ders',
    explains:
      'Aynı öğrenci grubuna aynı gün ve saatte birden fazla ders yazılmış. Fakülte gerçekten '
      + 'çakışma yayımlamış olabilir; ama daha sık görülen sebep, bir dersin hedef kitlesinin fazla '
      + 'geniş okunmasıdır. Onaylarsanız öğrencinin takviminde üst üste iki etkinlik görünür.',
  },
  MassDeletion: {
    label: 'Toplu silme',
    explains:
      'Bu revizyon, yayımlanmış revizyona göre çok sayıda dersi ortadan kaldırıyor. Yayımlanırsa o '
      + 'dersler öğrencilerin takviminden silinir. Belge gerçekten kısaldıysa (dönem bitti, program '
      + 'yeniden yazıldı) beklenen bir durumdur; kısalmadıysa belge eksik alınmış ya da eksik '
      + 'okunmuş demektir. Aşağıdaki sayıyı belgeyle karşılaştırmadan onaylamayın.',
  },
};

const SEVERITY: Record<string, { label: string; tone: string }> = {
  Error: { label: 'Hata', tone: 'var(--danger)' },
  Warning: { label: 'Uyarı', tone: 'var(--accent)' },
  Information: { label: 'Bilgi', tone: 'var(--muted)' },
};

/** What a state means for the schedule students actually see. */
export const STATE_EXPLAINS: Record<RevisionState | string, { label: string; explains: string }> = {
  Parsed: {
    label: 'Ayrıştırıldı',
    explains:
      'Belge okundu ama revizyon henüz doğrulamadan geçmedi. Normalde bu durum saniyeler sürer; '
      + 'burada takılı kalmışsa bir worker döngüsü yarıda kesilmiştir. Bir sonraki döngü bunu '
      + 'kendiliğinden alıp doğrular.',
  },
  Validating: {
    label: 'Doğrulanıyor',
    explains: 'Doğrulama şu anda çalışıyor. Bu durum bir anlıktır.',
  },
  ReviewRequired: {
    label: 'İnceleme gerekiyor',
    explains:
      'Doğrulama en az bir hata buldu ve revizyonu beklettiği için burada. Onaylanana kadar '
      + 'hiçbir öğrencinin takvimine yazılmaz.',
  },
  Validated: {
    label: 'Doğrulandı',
    explains: 'Bütün kurallar geçti; revizyon bir sonraki döngüde yayımlanacak.',
  },
  Published: {
    label: 'Yayımlandı',
    explains: 'Bu revizyon yürürlükte: öğrenci takvimleri bunu esas alıyor.',
  },
  Superseded: {
    label: 'Yerine yenisi geçti',
    explains: 'Aynı kaynağın daha yeni bir revizyonu yayımlandığı için bu artık yürürlükte değil.',
  },
  Rejected: {
    label: 'Reddedildi',
    explains:
      'Bu revizyon hiçbir zaman yayımlanmayacak. Reddetme geri alınamaz; kaynağın yeniden parse '
      + 'edilmesi yeni bir revizyon üretir.',
  },
};

export function stateLabel(state: RevisionState | string): string {
  return STATE_EXPLAINS[state]?.label ?? String(state);
}

/**
 * The evidence a rule recorded, rendered as what it actually is.
 *
 * Every rule that names records stores an array of objects — the candidate, its date, its title.
 * The previous renderer ran `String(entry)` over them, so the screen showed a column of
 * `[object Object]` and the evidence was, in practice, absent. A table of the object's own keys
 * shows it without the screen having to know each rule's shape.
 */
function Evidence({ detail }: { detail: string }) {
  let parsed: unknown = null;
  try {
    parsed = detail ? JSON.parse(detail) : null;
  } catch {
    return <pre className="mono revision-evidence-raw">{detail}</pre>;
  }

  if (!Array.isArray(parsed) || parsed.length === 0) {
    return parsed === null ? null : <pre className="mono revision-evidence-raw">{detail}</pre>;
  }

  const rows = parsed as unknown[];
  const objects = rows.filter(
    (row): row is Record<string, unknown> => typeof row === 'object' && row !== null && !Array.isArray(row),
  );

  if (objects.length !== rows.length) {
    return (
      <ul className="muted revision-evidence-list">
        {rows.slice(0, 40).map((row, index) => <li key={index}>{String(row)}</li>)}
      </ul>
    );
  }

  // Union of keys, first-seen order: rules may omit a key on some rows, and dropping those
  // columns would hide exactly the row that differs.
  const columns: string[] = [];
  for (const row of objects) {
    for (const key of Object.keys(row)) {
      if (!columns.includes(key)) columns.push(key);
    }
  }

  return (
    <div className="table-wrap">
      <table className="data-table data-table--stack revision-evidence">
        <thead>
          <tr>{columns.map((column) => <th key={column}>{EVIDENCE_HEADINGS[column] ?? column}</th>)}</tr>
        </thead>
        <tbody>
          {objects.slice(0, 40).map((row, index) => (
            <tr key={index}>
              {columns.map((column) => (
                <td key={column} data-label={EVIDENCE_HEADINGS[column] ?? column} className="mono">
                  {format(row[column])}
                </td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
      {objects.length > 40 && (
        <p className="muted">
          İlk 40 kayıt gösteriliyor; bulgu toplam {objects.length} kayıt taşıyor.
        </p>
      )}
    </div>
  );
}

/** The keys the validator writes, in the reviewer's language. */
const EVIDENCE_HEADINGS: Record<string, string> = {
  candidateId: 'Kayıt',
  date: 'Tarih',
  displayTitle: 'Ders',
  stableIdentity: 'Kalıcı kimlik',
  start: 'Başlangıç',
  end: 'Bitiş',
  minutes: 'Süre (dk)',
  field: 'Alan',
  confidence: 'Güven',
  dimension: 'Boyut',
  value: 'Değer',
  count: 'Adet',
};

function format(value: unknown): string {
  if (value === null || value === undefined) return '—';
  if (typeof value === 'object') return JSON.stringify(value);
  return String(value);
}

export function Finding({ finding }: { finding: RevisionFindingView }) {
  const rule = RULES[finding.rule];
  const severity = SEVERITY[finding.severity] ?? { label: finding.severity, tone: 'var(--muted)' };

  return (
    <section className="revision-finding">
      <div className="cluster revision-finding-head">
        <span className="badge" style={{ color: severity.tone, borderColor: severity.tone }}>
          {severity.label}
        </span>
        <strong>{rule?.label ?? finding.rule}</strong>
        {finding.affectedRecordCount > 0 && (
          <span className="muted">{finding.affectedRecordCount} kayıt etkileniyor</span>
        )}
        <small className="mono muted">{finding.rule}</small>
      </div>

      {rule && <p className="revision-finding-explains">{rule.explains}</p>}

      {/* The stored message carries this revision's own numbers and thresholds, which the
          explanation above cannot: it is written once, and this is one occurrence of it. */}
      <p className="muted revision-finding-message">{finding.message}</p>

      <Evidence detail={finding.detail} />
    </section>
  );
}
