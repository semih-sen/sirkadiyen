'use client';

import { useState } from 'react';
import { acceptSourceDateCorrection, ApiError } from '@/lib/api';
import type {
  RevisionDateAnomalyView,
  RevisionFindingView,
  RevisionState,
} from '@/lib/types';

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
  RecordDateOutOfSequence: {
    label: 'Sıra dışı tarih',
    explains:
      'Bir satırın tarihi, içinde bulunduğu sütunun kronolojik sırasını bozuyor. Bu neredeyse her '
      + 'zaman yılın yanlış yazılmasıdır: belge geçen yılın dosyasından kopyalanmış ve o satırın '
      + 'yılı güncellenmemiştir. Parser, komşu tarihler tek bir okuma bırakıyorsa yılı kendisi '
      + 'düzeltir ve dersi düzeltilmiş tarihte yayımlar — bu bir uyarıdır, revizyonu bekletmez. '
      + 'Birden fazla okuma uyuyorsa ya da hücre kendi yazdığı gün adıyla çelişiyorsa düzeltmez: '
      + 'tarihi belgedeki hâliyle yayımlar, revizyonu bekletir ve olası tarihleri aşağıda listeler. '
      + 'Bir tarihi kabul etmek kaynağı kalıcı olarak düzeltir: bu kaynak o tarihi nerede yazarsa '
      + 'yazsın, bundan sonraki her ayrıştırmada seçtiğiniz tarih okunur. Onaylarsanız değil, '
      + 'kabul edip kaynağı yeniden çektiğinizde düzelir.',
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
  original: 'Belgedeki tarih',
  applied: 'Yayımlanan tarih',
  lowerAnchor: 'Önceki tarih',
  upperAnchor: 'Sonraki tarih',
  reason: 'Sonuç',
  cell: 'Hücre',
  candidates: 'Olası okumalar',
};

function format(value: unknown): string {
  if (value === null || value === undefined) return '—';
  if (typeof value === 'object') return JSON.stringify(value);
  return String(value);
}

/** The parser's own vocabulary for why it withheld a correction, in the reviewer's language. */
const ANOMALY_REASONS: Record<string, string> = {
  repaired: 'Yılı düzeltilip yayımlandı',
  noCandidateFitsTheAnchors: 'Hiçbir yıl komşu tarihlerin arasına oturmuyor',
  severalCandidatesFitTheAnchors: 'Birden fazla yıl uyuyor',
  suspectIsNotBoundedOnBothSides: 'İki yandan sınırlanmıyor',
  anchorBracketTooWideToRead: 'Komşu tarihler arası okumak için fazla geniş',
  candidateContradictsTheStatedWeekday: 'Hücre kendi yazdığı gün adıyla çelişiyor',
};

const CANDIDATE_RULES: Record<string, string> = {
  sequenceYearSubstitution: 'yıl değiştirilerek',
  sequenceWeekdayAlternative: 'gün adına göre',
};

/**
 * Accepting one of the readings the parser listed for an out-of-sequence date (ADR-139).
 *
 * This is the lever the review screen was missing. Approving the revision publishes the date the
 * document states, which is the date nobody believes; rejecting it holds the schedule until the
 * faculty edits a workbook that is not ours. Accepting a candidate corrects the source instead, so
 * the next poll re-parses the same document and reads the date the operator chose.
 *
 * It is deliberately not part of the approve/reject row: it does not settle this revision. The
 * revision stays held, and a re-poll produces a new one that no longer trips the rule.
 */
function DateCorrectionAction({
  sourceId,
  anomaly,
}: {
  sourceId: string;
  anomaly: RevisionDateAnomalyView;
}) {
  const [reason, setReason] = useState('');
  const [busy, setBusy] = useState<string | null>(null);
  const [accepted, setAccepted] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  async function accept(corrected: string) {
    const trimmed = reason.trim();
    if (trimmed.length === 0) {
      setError('Kabul için bir gerekçe girin; bu karar denetim kaydına yazılır.');
      return;
    }
    setBusy(corrected);
    setError(null);
    try {
      await acceptSourceDateCorrection(sourceId, anomaly.original, corrected, trimmed);
      setAccepted(corrected);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Tarih düzeltmesi kaydedilemedi.');
    } finally {
      setBusy(null);
    }
  }

  const field = `date-correction-${sourceId}-${anomaly.original}`;

  return (
    <div className="revision-date-correction">
      <p className="revision-date-correction-head">
        <strong className="mono">{anomaly.original}</strong>
        {anomaly.cell && <small className="mono muted"> · {anomaly.cell}</small>}
        <span className="muted">
          {' '}— {ANOMALY_REASONS[anomaly.reason] ?? anomaly.reason}
          {anomaly.lowerAnchor && anomaly.upperAnchor && (
            <> (komşular: {anomaly.lowerAnchor} … {anomaly.upperAnchor})</>
          )}
        </span>
      </p>

      {accepted ? (
        <p className="muted">
          Kabul edildi: bu kaynak {anomaly.original} yazdığı her yerde {accepted} okunacak.
          Değişikliğin derslere yansıması için kaynağı yeniden çekin.
        </p>
      ) : (
        <>
          <label htmlFor={field}>Kabul gerekçesi (denetim kaydına yazılır)</label>
          <input
            id={field}
            value={reason}
            onChange={(event) => setReason(event.target.value)}
            placeholder="Belgeyi kontrol ettim; satır bir önceki yılın dosyasından kalmış."
          />
          <div className="cluster" style={{ gap: 8 }}>
            {anomaly.candidates.map((candidate) => (
              <button
                key={candidate.value}
                className="btn btn-secondary btn-sm"
                type="button"
                onClick={() => void accept(candidate.value)}
                disabled={busy !== null}
              >
                {busy === candidate.value ? 'Kaydediliyor…' : candidate.value}
                <small className="muted">
                  {' '}({CANDIDATE_RULES[candidate.rule] ?? candidate.rule}
                  {candidate.weekdayMatches === true && ', gün adı uyuyor'}
                  {candidate.weekdayMatches === false && ', gün adı uymuyor'})
                </small>
              </button>
            ))}
          </div>
          {anomaly.candidates.length === 0 && (
            <p className="muted">
              Parser bu tarih için makul bir okuma üretemedi. Doğru tarihi belgeden okuyup kaynak
              sayfasından elle bir düzeltme girmeniz gerekir.
            </p>
          )}
        </>
      )}

      {error && <div className="error" role="alert">{error}</div>}
    </div>
  );
}

/** The anomalies a date-sequence finding carries, or an empty list when its detail is not one. */
function readAnomalies(detail: string): RevisionDateAnomalyView[] {
  try {
    const parsed: unknown = detail ? JSON.parse(detail) : null;
    if (!Array.isArray(parsed)) return [];
    return parsed.filter(
      (entry): entry is RevisionDateAnomalyView =>
        typeof entry === 'object'
        && entry !== null
        && typeof (entry as RevisionDateAnomalyView).original === 'string',
    );
  } catch {
    return [];
  }
}

export function Finding({
  finding,
  sourceId,
}: {
  finding: RevisionFindingView;
  /** The source the revision belongs to. Only the date-sequence action needs it. */
  sourceId?: string;
}) {
  const rule = RULES[finding.rule];
  const severity = SEVERITY[finding.severity] ?? { label: finding.severity, tone: 'var(--muted)' };

  // Only the unresolved half of the rule offers a decision. A repair the parser already applied is
  // reported for visibility; there is nothing left to accept.
  const anomalies = finding.rule === 'RecordDateOutOfSequence' && sourceId
    ? readAnomalies(finding.detail).filter((anomaly) => !anomaly.applied)
    : [];

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

      {anomalies.length > 0 && sourceId && (
        <div className="revision-date-corrections">
          <p className="muted">
            Aşağıdaki tarihlerden birini kabul etmek kaynağı kalıcı olarak düzeltir. Bu revizyonu
            değiştirmez: kabul ettikten sonra kaynağı yeniden çekin, yeni revizyon doğru tarihle
            gelir.
          </p>
          {anomalies.map((anomaly) => (
            <DateCorrectionAction key={anomaly.original} sourceId={sourceId} anomaly={anomaly} />
          ))}
        </div>
      )}
    </section>
  );
}
