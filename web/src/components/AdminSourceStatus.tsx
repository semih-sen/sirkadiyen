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
import { Banner } from '@/components/ui';
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
  // A source the catalog no longer declares is kept whole in the database but is not one of the
  // pipeline's working parts any more: nothing polls it, so every column on its row is frozen at
  // the day it was dropped and can only ever read as stale. It is listed separately rather than
  // beside the sources being worked on (ADR-155).
  const active = (items ?? []).filter((item) => !item.retiredAtUtc);
  const retired = (items ?? []).filter((item) => item.retiredAtUtc);
  // Lifted to the top of the screen because a failing acquisition is invisible in every other
  // column: those describe the last state the source reached before it started failing (ADR-137).
  const failing = active.filter((item) => item.lastPollFailureAtUtc);
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
      {failing.length > 0 && (
        <Banner tone="danger">
          <strong>{failing.length} kaynağın belgesi alınamıyor.</strong>{' '}
          {failing.map((item) => item.sourceId).join(', ')} — bu kaynaklar yeni bir program
          yayımlamıyor; öğrencilerin takviminde son başarılı okumadaki hâli duruyor. Satıra
          tıklayıp sebebi okuyun.
        </Banner>
      )}
      {items && active.length > 0 && (
        <div className="table-wrap">
          <table className="data-table data-table--stack">
            <thead><tr><th>Kaynak</th><th>Program</th><th>Son poll</th><th>Parse</th><th>Uyarı/Hata</th><th>Revizyon</th></tr></thead>
            <tbody>
              {active.map((item) => (
                <tr key={item.sourceId} onClick={() => void open(item.sourceId)} style={{ cursor: 'pointer' }}>
                  <td><strong>{item.displayName}</strong><small className="mono muted" style={{ display: 'block' }}>{item.sourceId}</small></td>
                  <td>Dönem {item.classYear} · {item.programLanguage}</td>
                  <td>
                    {formatDateTime(item.lastPolledAtUtc)}
                    {!item.isPollingEnabled && <small className="muted" style={{ display: 'block' }}>Polling kapalı</small>}
                    {/* A failing source's other columns all describe the state before the
                        failure, so the row has to say so where the poll time is read. */}
                    {item.lastPollFailureAtUtc && (
                      <small className="source-failing">
                        {describeFailingSince(item.lastPollFailureAtUtc)}{' '}
                        {item.transport === 'AdministrativeUpload' ? 'işlenemiyor' : 'alınamıyor'}
                      </small>
                    )}
                  </td>
                  <td>
                    <span className={`badge ${statusBadge(item.latestParseRunStatus ?? 'unknown')}`}>{item.latestParseRunStatus ?? 'Veri yok'}</span>
                    {/* A failed run stores no warnings, so the reason has to sit next to the badge;
                        the full text is in the detail drawer. */}
                    {item.latestParseRunStatus === 'Failed' && item.latestParseFailureReason && (
                      <small className="source-failure-reason mono" style={{ display: 'block', marginTop: 4 }}>
                        {truncateReason(item.latestParseFailureReason)}
                      </small>
                    )}
                  </td>
                  <td>{item.latestParseWarningCount ?? 0} / {item.latestParseErrorCount ?? 0}</td>
                  <td>
                    {/* A companion source's revision is empty and refused every cycle, by design.
                        Shown as the plain "Rejected" every other source's failure carries, it is a
                        permanent red badge — and a badge that is always red is one nobody reads
                        (ADR-156). Any other state is shown as it is: a companion that suddenly
                        published something is exactly what an operator must see. */}
                    {!item.publishesSchedule && (item.latestRevisionState ?? 'Rejected') === 'Rejected' ? (
                      <>
                        <span className="badge badge-neutral">Yayımlamaz</span>
                        <small className="muted" style={{ display: 'block' }}>companion</small>
                      </>
                    ) : (
                      <span className={`badge ${statusBadge(item.latestRevisionState ?? 'unknown')}`}>{item.latestRevisionState ?? 'Veri yok'}</span>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
      {retired.length > 0 && (
        <RetiredSources sources={retired} onOpen={(sourceId) => void open(sourceId)} />
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

/**
 * The sources the catalog has dropped, kept out of the operational table and reachable underneath
 * it (ADR-155).
 *
 * Collapsed, because nobody is working on these: they are here so that "where did G2-VERTICAL-SPRING
 * go" has an answer on the screen that answers it, and so their evidence stays one click away.
 */
function RetiredSources({
  sources,
  onOpen,
}: {
  sources: SourceStatusListItem[];
  onOpen: (sourceId: string) => void;
}) {
  return (
    <details style={{ marginTop: 18 }}>
      <summary className="muted" style={{ cursor: 'pointer' }}>
        Katalogdan çıkarılmış {sources.length} kaynak
      </summary>
      <p className="muted" style={{ fontSize: 12, marginTop: 8 }}>
        Bu kaynaklar artık katalogda tanımlı değil; poll edilmiyorlar. Satırları, snapshotları,
        revizyonları ve yayımladıkları etkinlikler silinmedi — kanıt olarak duruyor.
      </p>
      <ul style={{ listStyle: 'none', margin: '8px 0 0', padding: 0 }}>
        {sources.map((item) => (
          <li key={item.sourceId} style={{ padding: '6px 0' }}>
            <button
              type="button"
              className="btn btn-tertiary btn-sm"
              onClick={() => onOpen(item.sourceId)}
            >
              {item.displayName}
            </button>{' '}
            <small className="mono muted">{item.sourceId}</small>{' '}
            <small className="muted">
              · {item.retiredAtUtc ? `${formatDateTime(item.retiredAtUtc)} tarihinde çıkarıldı` : 'çıkarıldı'}
            </small>
          </li>
        ))}
      </ul>
    </details>
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

      {!detail.summary.publishesSchedule && (
        <Banner tone="neutral">
          <strong>Bu kaynak kendi programını yayımlamaz.</strong>{' '}
          Belgesini başka bir kaynak yardımcı kanıt olarak okur; kendi revizyonu boş olur ve
          reddedilir. Bu beklenen sonuçtur — hiçbir takvimden bir şey eksilmiyor, belgenin
          söylediği bilgi onu okuyan kaynağın etkinliklerine yazılıyor.
        </Banner>
      )}

      {detail.summary.retiredAtUtc && (
        <Banner tone="neutral">
          <strong>Bu kaynak katalogdan çıkarılmış.</strong>{' '}
          {formatDateTime(detail.summary.retiredAtUtc)} tarihinden beri poll edilmiyor. Aşağıdaki
          bilgiler o tarihe kadarki son durumu gösteriyor; hiçbir kaydı silinmedi.
        </Banner>
      )}

      {detail.summary.lastPollFailureAtUtc && (
        <Banner tone="danger">
          <strong>
            {detail.summary.transport === 'AdministrativeUpload'
              ? 'Yüklenen belge işlenemiyor.'
              : 'Belge alınamıyor.'}
          </strong>{' '}
          {describeFailingSince(detail.summary.lastPollFailureAtUtc)} her döngüde başarısız oluyor.
          {detail.summary.lastPolledAtUtc && (
            <> Son başarılı okuma: {formatDateTime(detail.summary.lastPolledAtUtc)}.</>
          )}
          <p className="mono source-failure-reason">{detail.summary.lastPollFailureReason}</p>
          <span className="muted">
            Aşağıdaki parse, revizyon ve snapshot bilgileri bu son başarılı okumaya ait; bu kaynak
            o tarihten beri yeni bir program yayımlamıyor.
          </span>
        </Banner>
      )}

      {/* A failed parse stores no response, so it produces no warning rows below; its cause lives
          only on the run itself and has to be shown on its own. */}
      {detail.summary.latestParseRunStatus === 'Failed' && detail.summary.latestParseFailureReason && (
        <Banner tone="danger">
          <strong>Parse başarısız oldu.</strong>{' '}
          {detail.summary.latestParseRunAtUtc && (
            <>Son deneme: {formatDateTime(detail.summary.latestParseRunAtUtc)}.</>
          )}
          <p className="mono source-failure-reason">{detail.summary.latestParseFailureReason}</p>
          <span className="muted">
            Başarısız koşu bir parser yanıtı saklamaz; bu yüzden aşağıda uyarı listelenmez.
            Öğrencilerin takviminde son başarılı revizyon duruyor.
          </span>
        </Banner>
      )}

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

/**
 * How long the source has been failing, in the unit the reader is deciding with.
 *
 * "4 gündür" is the sentence that makes this actionable; a timestamp alone reads as just another
 * date on a screen already full of them, which is how four days of failure went unnoticed.
 */
/**
 * A one-line preview of a parse failure for the table cell, where the full exception would blow the
 * row height apart. The whole reason is still shown, untrimmed, in the detail drawer.
 */
function truncateReason(reason: string): string {
  const firstLine = reason.split('\n', 1)[0].trim();
  return firstLine.length > 120 ? `${firstLine.slice(0, 119)}…` : firstLine;
}

function describeFailingSince(failedAtUtc: string): string {
  const failedAt = new Date(failedAtUtc);
  if (Number.isNaN(failedAt.getTime())) return 'Bir süredir';

  const minutes = Math.max(0, Math.floor((Date.now() - failedAt.getTime()) / 60000));
  if (minutes < 60) return `${minutes} dakikadır`;
  const hours = Math.floor(minutes / 60);
  if (hours < 24) return `${hours} saattir`;
  return `${Math.floor(hours / 24)} gündür`;
}
