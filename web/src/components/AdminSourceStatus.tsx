'use client';

import { useCallback, useEffect, useState } from 'react';
import {
  ApiError,
  getAdminSource,
  listAdminSources,
  pruneSnapshotPayload,
  requestSourcePoll,
} from '@/lib/api';
import { DetailDrawer, LoadState, Tabs, formatDateTime, statusBadge } from '@/components/AdminData';
import { SourceDocumentUpload } from '@/components/SourceDocumentUpload';
import { SourceCatalogEditor } from '@/components/SourceCatalogEditor';
import type {
  ParserWarningView,
  SourceSnapshotSummary,
  SourceStatusDetail,
  SourceStatusListItem,
} from '@/lib/types';

export function AdminSourceWorkspace() {
  const [tab, setTab] = useState('status');

  return (
    <>
      <Tabs
        value={tab}
        onChange={setTab}
        items={[
          { value: 'status', label: 'Kaynak durumu' },
          { value: 'upload', label: 'Belge yükleme' },
          { value: 'catalog', label: 'Kaynak kataloğu' },
        ]}
      />
      {tab === 'status' && <SourceStatus />}
      {tab === 'upload' && (
        <section className="card admin-workspace-card"><SourceDocumentUpload /></section>
      )}
      {tab === 'catalog' && <SourceCatalogEditor />}
    </>
  );
}

function SourceStatus() {
  const [items, setItems] = useState<SourceStatusListItem[] | null>(null);
  const [detail, setDetail] = useState<SourceStatusDetail | null>(null);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    setError(null);
    try {
      setItems(await listAdminSources());
    } catch (caught) {
      setError(caught instanceof ApiError ? caught.message : 'Kaynak durumları alınamadı.');
    }
  }, []);

  useEffect(() => { void load(); }, [load]);

  async function open(sourceId: string) {
    try {
      setDetail(await getAdminSource(sourceId));
    } catch (caught) {
      setError(caught instanceof ApiError ? caught.message : 'Kaynak detayı alınamadı.');
    }
  }

  return (
    <section className="card admin-workspace-card">
      <p className="muted" style={{ marginBottom: 14 }}>
        Bu görünüm yalnız saklanmış pipeline kanıtlarını okur; poll veya parse başlatmaz.
      </p>
      <LoadState
        loading={items === null && !error}
        error={error}
        empty={items?.length === 0}
        onRetry={() => void load()}
      />
      {items && items.length > 0 && (
        <div className="table-wrap">
          <table className="data-table data-table--stack">
            <thead><tr><th>Kaynak</th><th>Program</th><th>Son poll</th><th>Parse</th><th>Uyarı/Hata</th><th>Revizyon</th></tr></thead>
            <tbody>
              {items.map((item) => (
                <tr key={item.sourceId} onClick={() => void open(item.sourceId)} style={{ cursor: 'pointer' }}>
                  <td><strong>{item.displayName}</strong><small className="mono muted" style={{ display: 'block' }}>{item.sourceId}</small></td>
                  <td>Dönem {item.classYear} · {item.programLanguage}</td>
                  <td>{formatDateTime(item.lastPolledAtUtc)}{!item.isPollingEnabled && <small className="muted" style={{ display: 'block' }}>Polling kapalı</small>}</td>
                  <td><span className={`badge ${statusBadge(item.latestParseRunStatus ?? 'unknown')}`}>{item.latestParseRunStatus ?? 'Veri yok'}</span></td>
                  <td>{item.latestParseWarningCount ?? 0} / {item.latestParseErrorCount ?? 0}</td>
                  <td><span className={`badge ${statusBadge(item.latestRevisionState ?? 'unknown')}`}>{item.latestRevisionState ?? 'Veri yok'}</span></td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
      {detail && (
        <SourceDetail
          detail={detail}
          onClose={() => setDetail(null)}
          onReload={() => open(detail.summary.sourceId)}
        />
      )}
    </section>
  );
}

function SourceDetail({
  detail,
  onClose,
  onReload,
}: {
  detail: SourceStatusDetail;
  onClose: () => void;
  onReload: () => Promise<void>;
}) {
  return (
    <DetailDrawer title={detail.summary.displayName} subtitle={detail.summary.sourceId} onClose={onClose}>
      <div className="summary-row"><span className="muted">Taşıma</span><strong>{detail.summary.transport}</strong></div>
      <div className="summary-row"><span className="muted">Parser</span><strong>{detail.parserProfile} · {detail.parserProfileVersion}</strong></div>

      <PollControls sourceId={detail.summary.sourceId} />

      <h3 style={{ fontSize: 15, marginTop: 20 }}>Son parse uyarıları</h3>
      {detail.latestParseWarnings.length === 0 ? (
        <p className="muted">Son parser koşusunda saklanmış uyarı bulunmuyor.</p>
      ) : detail.latestParseWarnings.map((warning, index) => (
        <ParserWarning key={`${warning.code}-${warning.candidateId ?? 'run'}-${index}`} warning={warning} />
      ))}

      <h3 style={{ fontSize: 15, marginTop: 20 }}>Son snapshotlar</h3>
      <p className="muted" style={{ fontSize: 12, marginTop: -4 }}>
        Bir snapshot değişmez kanıttır. “Payload’ı buda”, yalnızca büyük normalize içeriği siler;
        kimlik, hash, sayımlar ve tüm parse/revizyon/diff izi kalır.
      </p>
      {detail.recentSnapshots.length === 0 ? <p className="muted">Snapshot bulunmuyor.</p> : detail.recentSnapshots.map((snapshot) => (
        <SnapshotRow key={snapshot.snapshotId} snapshot={snapshot} onReload={onReload} />
      ))}
    </DetailDrawer>
  );
}

/**
 * Queues an immediate poll of this source (ADR-127). "Force" reparses even an unchanged document,
 * needed after a profile or configuration change; the plain poll only reparses if the document
 * changed. The worker acquires and parses on its next cycle, so the effect is not instantaneous.
 */
function PollControls({ sourceId }: { sourceId: string }) {
  const [busy, setBusy] = useState<'plain' | 'force' | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  async function poll(force: boolean) {
    setBusy(force ? 'force' : 'plain');
    setMessage(null);
    setError(null);
    try {
      await requestSourcePoll(sourceId, force);
      setMessage(
        force
          ? 'Force yeniden çekme kuyruğa alındı; worker bir sonraki döngüsünde yeniden parse edecek.'
          : 'Çekme isteği kuyruğa alındı; worker bir sonraki döngüsünde çekecek.',
      );
    } catch (caught) {
      setError(caught instanceof ApiError ? caught.message : 'Çekme isteği kuyruğa alınamadı.');
    } finally {
      setBusy(null);
    }
  }

  return (
    <div style={{ marginTop: 14, borderTop: '1px solid var(--border)', paddingTop: 12 }}>
      <div className="cluster" style={{ gap: 8 }}>
        <button type="button" className="btn btn-sm" disabled={busy !== null} onClick={() => void poll(false)}>
          {busy === 'plain' ? 'Kuyruğa alınıyor…' : 'Şimdi çek'}
        </button>
        <button type="button" className="btn btn-sm" disabled={busy !== null} onClick={() => void poll(true)}>
          {busy === 'force' ? 'Kuyruğa alınıyor…' : 'Force ile çek'}
        </button>
      </div>
      <p className="muted" style={{ fontSize: 12, marginTop: 6 }}>
        “Force”, belge değişmese bile yeni bir parse koşusu açar (profil/ayar değişiklikleri için).
      </p>
      {message && <p className="muted" style={{ fontSize: 12, marginTop: 4 }}>{message}</p>}
      {error && <p className="error-text" style={{ fontSize: 12, marginTop: 4 }}>{error}</p>}
    </div>
  );
}

function SnapshotRow({
  snapshot,
  onReload,
}: {
  snapshot: SourceSnapshotSummary;
  onReload: () => Promise<void>;
}) {
  const [confirming, setConfirming] = useState(false);
  const [reason, setReason] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function prune() {
    if (reason.trim().length === 0) {
      setError('Bir gerekçe gerekli.');
      return;
    }
    setBusy(true);
    setError(null);
    try {
      await pruneSnapshotPayload(snapshot.snapshotId, reason.trim());
      setConfirming(false);
      setReason('');
      await onReload();
    } catch (caught) {
      setError(caught instanceof ApiError ? caught.message : 'Payload budanamadı.');
    } finally {
      setBusy(false);
    }
  }

  return (
    <section style={{ padding: '12px 0', borderBottom: '1px solid var(--border)' }}>
      <div className="cluster" style={{ justifyContent: 'space-between' }}>
        <strong className="mono">{snapshot.snapshotId}</strong>
        <span className={`badge ${snapshot.hasPayload ? 'badge-success' : 'badge-neutral'}`}>{snapshot.hasPayload ? 'Payload saklanıyor' : 'Payload budandı'}</span>
      </div>
      <p className="muted" style={{ fontSize: 12 }}>{formatDateTime(snapshot.acquiredAtUtc)} · {snapshot.worksheetCount} sayfa · {snapshot.cellCount} hücre · {snapshot.diagnosticCount} tanı</p>
      <small className="mono">{snapshot.contentHash}</small>
      {!snapshot.hasPayload && snapshot.payloadPrunedAtUtc && (
        <p className="muted" style={{ fontSize: 12, marginTop: 6 }}>
          Payload {formatDateTime(snapshot.payloadPrunedAtUtc)} tarihinde budandı.
        </p>
      )}
      {snapshot.hasPayload && !confirming && (
        <button
          type="button"
          className="btn btn-tertiary btn-sm"
          style={{ marginTop: 8 }}
          onClick={() => { setConfirming(true); setError(null); }}
        >
          Payload’ı buda
        </button>
      )}
      {snapshot.hasPayload && confirming && (
        <div style={{ marginTop: 8 }}>
          <label className="muted" style={{ fontSize: 12, display: 'block', marginBottom: 4 }}>
            Gerekçe (denetim kaydına yazılır)
          </label>
          <textarea
            className="text-input"
            rows={2}
            value={reason}
            disabled={busy}
            onChange={(event) => setReason(event.target.value)}
            placeholder="Örn. eski snapshot, depolama alanını geri kazan"
          />
          <div className="cluster" style={{ gap: 8, marginTop: 8 }}>
            <button type="button" className="btn btn-danger btn-sm" disabled={busy} onClick={() => void prune()}>
              {busy ? 'Budanıyor…' : 'Payload’ı buda'}
            </button>
            <button
              type="button"
              className="btn btn-tertiary btn-sm"
              disabled={busy}
              onClick={() => { setConfirming(false); setReason(''); setError(null); }}
            >
              Vazgeç
            </button>
          </div>
        </div>
      )}
      {error && <p className="error-text" style={{ marginTop: 6 }}>{error}</p>}
    </section>
  );
}

function ParserWarning({ warning }: { warning: ParserWarningView }) {
  const badge = warning.severity === 'Error'
    ? 'badge-danger'
    : warning.severity === 'Warning'
      ? 'badge-warning'
      : 'badge-neutral';

  return (
    <section style={{ padding: '12px 0', borderBottom: '1px solid var(--border)' }}>
      <div className="cluster" style={{ justifyContent: 'space-between' }}>
        <strong>{warning.code}</strong>
        <span className={`badge ${badge}`}>{warning.severity}</span>
      </div>
      <p style={{ marginTop: 6 }}>{warning.message}</p>
      {warning.candidateId && <small className="mono muted">Aday: {warning.candidateId}</small>}
      {warning.evidence && (
        <p className="muted" style={{ fontSize: 12, marginTop: 6 }}>
          {warning.evidence.sheetTitle} · {warning.evidence.range} · {warning.evidence.extractionRule}
          {warning.evidence.rawText ? ` · “${warning.evidence.rawText}”` : ''}
        </p>
      )}
    </section>
  );
}
