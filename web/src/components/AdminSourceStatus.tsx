'use client';

import { useCallback, useEffect, useState } from 'react';
import { ApiError, getAdminSource, listAdminSources } from '@/lib/api';
import { DetailDrawer, LoadState, Tabs, formatDateTime, statusBadge } from '@/components/AdminData';
import { SourceDocumentUpload } from '@/components/SourceDocumentUpload';
import { SourceCatalogEditor } from '@/components/SourceCatalogEditor';
import type { ParserWarningView, SourceStatusDetail, SourceStatusListItem } from '@/lib/types';

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

  async function open(item: SourceStatusListItem) {
    try {
      setDetail(await getAdminSource(item.sourceId));
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
                <tr key={item.sourceId} onClick={() => void open(item)} style={{ cursor: 'pointer' }}>
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
      {detail && <SourceDetail detail={detail} onClose={() => setDetail(null)} />}
    </section>
  );
}

function SourceDetail({ detail, onClose }: { detail: SourceStatusDetail; onClose: () => void }) {
  return (
    <DetailDrawer title={detail.summary.displayName} subtitle={detail.summary.sourceId} onClose={onClose}>
      <div className="summary-row"><span className="muted">Taşıma</span><strong>{detail.summary.transport}</strong></div>
      <div className="summary-row"><span className="muted">Parser</span><strong>{detail.parserProfile} · {detail.parserProfileVersion}</strong></div>

      <h3 style={{ fontSize: 15, marginTop: 20 }}>Son parse uyarıları</h3>
      {detail.latestParseWarnings.length === 0 ? (
        <p className="muted">Son parser koşusunda saklanmış uyarı bulunmuyor.</p>
      ) : detail.latestParseWarnings.map((warning, index) => (
        <ParserWarning key={`${warning.code}-${warning.candidateId ?? 'run'}-${index}`} warning={warning} />
      ))}

      <h3 style={{ fontSize: 15, marginTop: 20 }}>Son snapshotlar</h3>
      {detail.recentSnapshots.length === 0 ? <p className="muted">Snapshot bulunmuyor.</p> : detail.recentSnapshots.map((snapshot) => (
        <section key={snapshot.snapshotId} style={{ padding: '12px 0', borderBottom: '1px solid var(--border)' }}>
          <div className="cluster" style={{ justifyContent: 'space-between' }}>
            <strong className="mono">{snapshot.snapshotId}</strong>
            <span className={`badge ${snapshot.hasPayload ? 'badge-success' : 'badge-neutral'}`}>{snapshot.hasPayload ? 'Payload saklanıyor' : 'Payload budandı'}</span>
          </div>
          <p className="muted" style={{ fontSize: 12 }}>{formatDateTime(snapshot.acquiredAtUtc)} · {snapshot.worksheetCount} sayfa · {snapshot.cellCount} hücre · {snapshot.diagnosticCount} tanı</p>
          <small className="mono">{snapshot.contentHash}</small>
        </section>
      ))}
    </DetailDrawer>
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
