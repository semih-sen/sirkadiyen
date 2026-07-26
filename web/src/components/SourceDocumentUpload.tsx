'use client';

// Administrative acquisition from the admin panel (ADR-079, ADR-080).
//
// The endpoint this posts to is authorized by the SuperAdmin policy and protected
// by antiforgery on the backend; this component only carries the browser's session
// cookie and the CSRF header through the typed client. It renders what the backend
// reports and never claims more: an upload is an acquisition, so a successful one
// means "stored as evidence", not "published to calendars". The worker parses,
// validates and publishes it on its next cycle under the same rules as a polled
// source, and a suspicious revision still waits for review.

import { useCallback, useEffect, useRef, useState } from 'react';
import {
  listSourceDocumentUploads,
  listUploadableSources,
  uploadSourceDocument,
  ApiError,
} from '@/lib/api';
import type {
  SourceDocumentUploadAuditEntry,
  SourceDocumentUploadResponse,
  UploadableSourceView,
} from '@/lib/types';

/**
 * Mirrors AdministrativeDocumentUploadService.MaximumDocumentBytes. The backend is
 * the authority; checking here only avoids uploading megabytes to be told no.
 */
const MAXIMUM_DOCUMENT_BYTES = 8 * 1024 * 1024;

const PROGRAM_LABELS: Record<string, string> = {
  Turkish: 'Türkçe',
  English: 'İngilizce',
};

/**
 * One document, and every source it is evidence for.
 *
 * The catalog has one entry per program because a canonical record reaches a
 * student only when its program language matches theirs, but the faculty hands out
 * a single document. The operator picks the document; the fan-out serves the rest.
 */
interface DocumentGroup {
  /** The member the upload is posted to. Any member serves the whole group. */
  primarySourceId: string;
  members: UploadableSourceView[];
}

function groupByDocument(sources: UploadableSourceView[]): DocumentGroup[] {
  const groups = new Map<string, DocumentGroup>();
  for (const source of sources) {
    // A source with no shared group is its own only member, so the ordinary
    // one-document-one-source case needs no special handling.
    const key = source.sharedDocumentGroup ?? source.sourceId;
    const existing = groups.get(key);
    if (existing) {
      existing.members.push(source);
    } else {
      groups.set(key, { primarySourceId: source.sourceId, members: [source] });
    }
  }
  return [...groups.values()];
}

function groupLabel(group: DocumentGroup): string {
  const primary = group.members[0];
  const programs = group.members
    .map((member) => PROGRAM_LABELS[member.programLanguage] ?? member.programLanguage)
    .join(' + ');
  return `Dönem ${primary.classYear} · ${programs} · ${primary.displayName}`;
}

function formatBytes(byteCount: number): string {
  if (byteCount < 1024) {
    return `${byteCount} B`;
  }
  if (byteCount < 1024 * 1024) {
    return `${Math.round(byteCount / 1024)} KB`;
  }
  return `${Math.round((byteCount / (1024 * 1024)) * 10) / 10} MB`;
}

function outcomeLabel(outcome: string): string {
  if (outcome === 'Stored') return 'yeni anlık görüntü kaydedildi';
  if (outcome === 'Unchanged') return 'içerik değişmedi';
  return outcome;
}

/** The message for the failure the backend actually reported. */
function uploadErrorMessage(error: unknown): string {
  if (!(error instanceof ApiError)) {
    return 'Belge yüklenemedi. Ağ bağlantısını kontrol et.';
  }
  switch (error.status) {
    case 401:
      return 'Oturum düştü. Yeniden giriş yapıp tekrar dene.';
    case 403:
      return 'Bu işlem için SuperAdmin yetkisi gerekiyor.';
    case 404:
      return error.message || 'Kaynak yapılandırılmamış.';
    case 409:
      // A freeze is temporary: the same upload succeeds once it is lifted.
      return `${error.message} Dondurmayı kaldırdıktan sonra aynı belgeyi tekrar yükle.`;
    case 413:
      return `Belge çok büyük (en fazla ${formatBytes(MAXIMUM_DOCUMENT_BYTES)}).`;
    default:
      return error.message || 'Belge reddedildi.';
  }
}

function UploadResult({ result }: { result: SourceDocumentUploadResponse }) {
  const stored = result.targets.filter((target) => target.outcome === 'Stored');

  return (
    <div
      style={{
        marginTop: 16,
        padding: '10px 12px',
        background: 'rgba(70, 209, 158, 0.1)',
        border: '1px solid rgba(70, 209, 158, 0.4)',
        borderRadius: 8,
        fontSize: 13,
      }}
    >
      <div style={{ color: 'var(--success)', fontWeight: 600 }}>
        Belge kanıt olarak kaydedildi ({result.targets.length} kaynak)
      </div>
      <ul style={{ margin: '8px 0 0', paddingLeft: 18 }}>
        {result.targets.map((target) => {
          const program = PROGRAM_LABELS[target.programLanguage] ?? target.programLanguage;
          return (
            <li key={target.sourceId} style={{ marginBottom: 2 }}>
              <strong>{target.sourceId}</strong> (Dönem {target.classYear} {program}) —{' '}
              {outcomeLabel(target.outcome)}
            </li>
          );
        })}
      </ul>
      <p className="muted" style={{ margin: '8px 0 0', fontSize: 12 }}>
        {stored.length === 0
          ? 'Hiçbir kaynakta içerik değişmedi, bu yüzden yeni revizyon oluşmayacak. Bu, aynı belgenin tekrar yüklendiği anlamına gelir.'
          : 'Yükleme yalnızca kanıtı saklar. Worker sonraki döngüsünde bu anlık görüntüyü ' +
            'çekilen bir kaynakla aynı kurallara göre işler; şüpheli bir revizyon yine ' +
            'inceleme bekler. Takvimlere yansıdığını revizyon kuyruğundan doğrula.'}
      </p>
      <p className="muted" style={{ margin: '6px 0 0', fontSize: 12, wordBreak: 'break-all' }}>
        Dosya özeti (SHA-256): {result.contentSha256}
      </p>
    </div>
  );
}

function UploadHistory({ entries }: { entries: SourceDocumentUploadAuditEntry[] }) {
  if (entries.length === 0) {
    return (
      <p className="muted" style={{ margin: '10px 0 0', fontSize: 13 }}>
        Bu belge için henüz yükleme yapılmamış.
      </p>
    );
  }

  return (
    <div style={{ marginTop: 10 }}>
      {entries.map((entry) => (
        <div
          key={`${entry.sourceId}-${entry.uploadedAtUtc}-${entry.contentSha256}`}
          className="status-row"
          style={{ fontSize: 13 }}
        >
          <span>
            {new Date(entry.uploadedAtUtc).toLocaleString('tr-TR')} · {entry.fileName}
          </span>
          <span className="value">
            {entry.sourceId} · {formatBytes(entry.byteCount)} · {outcomeLabel(entry.outcome)} ·{' '}
            {entry.uploadedBy}
          </span>
        </div>
      ))}
    </div>
  );
}

export function SourceDocumentUpload() {
  const [groups, setGroups] = useState<DocumentGroup[] | null>(null);
  const [sourceId, setSourceId] = useState('');
  const [file, setFile] = useState<File | null>(null);
  const [isUploading, setIsUploading] = useState(false);
  const [result, setResult] = useState<SourceDocumentUploadResponse | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [history, setHistory] = useState<SourceDocumentUploadAuditEntry[] | null>(null);
  const [historyFailed, setHistoryFailed] = useState(false);
  const fileInput = useRef<HTMLInputElement>(null);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const documents = groupByDocument(await listUploadableSources());
        if (cancelled) {
          return;
        }
        setGroups(documents);
        // Only preselect when there is nothing to choose between: picking the
        // wrong document would attach one semester's evidence to another.
        if (documents.length === 1) {
          setSourceId(documents[0].primarySourceId);
        }
      } catch (err) {
        if (!cancelled) {
          setGroups([]);
          setError(
            err instanceof ApiError ? err.message : 'Yüklenebilir kaynaklar listelenemedi.',
          );
        }
      }
    })();
    return () => {
      cancelled = true;
    };
  }, []);

  const selected =
    groups?.find((group) => group.primarySourceId === sourceId) ?? null;

  /**
   * The audit trail of every source this document serves, newest first. Merging
   * them is what makes an interrupted fan-out visible: one member holding the
   * document and the other not is exactly the failure ADR-080 leaves possible.
   */
  const loadHistory = useCallback(async (group: DocumentGroup | null) => {
    setHistory(null);
    setHistoryFailed(false);
    if (group === null) {
      return;
    }
    try {
      const perSource = await Promise.all(
        group.members.map((member) => listSourceDocumentUploads(member.sourceId)),
      );
      setHistory(
        perSource
          .flat()
          .sort((left, right) => right.uploadedAtUtc.localeCompare(left.uploadedAtUtc))
          .slice(0, 20),
      );
    } catch {
      // The audit trail is context, not the operation: a failure here must not
      // read as an upload failure.
      setHistoryFailed(true);
    }
  }, []);

  useEffect(() => {
    void loadHistory(selected);
  }, [selected, loadHistory]);

  function onFileChange(event: React.ChangeEvent<HTMLInputElement>) {
    setResult(null);
    setError(null);
    setFile(event.target.files?.[0] ?? null);
  }

  async function onUpload() {
    if (!selected || !file) {
      setError('Bir belge ve bir dosya seç.');
      return;
    }
    if (!file.name.toLowerCase().endsWith('.docx')) {
      setError('Yalnızca .docx belgeler yüklenebilir.');
      return;
    }
    if (file.size === 0) {
      setError('Belge boş.');
      return;
    }
    if (file.size > MAXIMUM_DOCUMENT_BYTES) {
      setError(`Belge çok büyük (en fazla ${formatBytes(MAXIMUM_DOCUMENT_BYTES)}).`);
      return;
    }

    setIsUploading(true);
    setError(null);
    setResult(null);
    try {
      const response = await uploadSourceDocument(selected.primarySourceId, file);
      setResult(response);
      setFile(null);
      if (fileInput.current) {
        fileInput.current.value = '';
      }
      await loadHistory(selected);
    } catch (err) {
      setError(uploadErrorMessage(err));
      // A fan-out interrupted between its targets leaves one source with the
      // document and the other without, so the audit trail is reloaded even on
      // failure: it says which targets landed.
      await loadHistory(selected);
    } finally {
      setIsUploading(false);
    }
  }

  return (
    <div>
      <h2 style={{ fontSize: 16, margin: '26px 0 8px' }}>Belge yükleme (elden verilen kaynaklar)</h2>
      <p className="muted" style={{ marginBottom: 0 }}>
        Yayınlanmayan, elden dağıtılan belgeler yalnızca buradan alınır. Yükleme belgeyi
        değiştirilemez kanıt olarak saklar; ayrıştırma, doğrulama ve yayınlama worker’ın
        işidir.
      </p>

      {groups === null ? (
        <p className="muted">Yükleniyor…</p>
      ) : groups.length === 0 ? (
        <p className="muted">Yüklemeyle alınan kaynak yok.</p>
      ) : (
        <>
          <label htmlFor="upload-source">Belge</label>
          <select
            id="upload-source"
            value={sourceId}
            onChange={(event) => {
              setSourceId(event.target.value);
              setResult(null);
              setError(null);
            }}
            disabled={isUploading}
          >
            <option value="">Belge seç…</option>
            {groups.map((group) => (
              <option key={group.primarySourceId} value={group.primarySourceId}>
                {groupLabel(group)}
              </option>
            ))}
          </select>

          {selected && (
            <p className="muted" style={{ margin: '8px 0 0', fontSize: 13 }}>
              Akademik yıl {selected.members[0].academicYear} · beklenen biçim{' '}
              {selected.members[0].documentFormat}
              <br />
              {selected.members.length === 1
                ? `Kaynak: ${selected.members[0].sourceId}`
                : `Tek yükleme şu kaynakların her biri için ayrı bir anlık görüntü oluşturur: ` +
                  selected.members.map((member) => member.sourceId).join(', ')}
            </p>
          )}

          <label htmlFor="upload-file">
            Belge (.docx, en fazla {formatBytes(MAXIMUM_DOCUMENT_BYTES)})
          </label>
          <input
            id="upload-file"
            ref={fileInput}
            type="file"
            accept=".docx,application/vnd.openxmlformats-officedocument.wordprocessingml.document"
            onChange={onFileChange}
            disabled={isUploading}
          />
          {file && (
            <p className="muted" style={{ margin: '6px 0 0', fontSize: 13 }}>
              {file.name} · {formatBytes(file.size)}
            </p>
          )}

          <button
            className="primary"
            type="button"
            onClick={onUpload}
            disabled={isUploading || !selected || !file}
          >
            {isUploading ? 'Yükleniyor…' : 'Belgeyi yükle'}
          </button>

          {result && <UploadResult result={result} />}
          {error && <div className="error">{error}</div>}

          {selected !== null && (
            <>
              <h3 style={{ fontSize: 14, margin: '20px 0 0', color: 'var(--muted)' }}>
                Yükleme geçmişi
              </h3>
              {historyFailed ? (
                <p className="muted" style={{ margin: '10px 0 0', fontSize: 13 }}>
                  Geçmiş yüklenemedi.
                </p>
              ) : history === null ? (
                <p className="muted" style={{ margin: '10px 0 0', fontSize: 13 }}>
                  Yükleniyor…
                </p>
              ) : (
                <UploadHistory entries={history} />
              )}
            </>
          )}
        </>
      )}
    </div>
  );
}
