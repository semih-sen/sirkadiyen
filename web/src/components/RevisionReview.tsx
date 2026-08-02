'use client';

import { useCallback, useEffect, useState } from 'react';
import { approveRevision, getRevision, listRevisions, ApiError } from '@/lib/api';
import type { RevisionFindingView, ScheduleRevisionDetail, ScheduleRevisionSummary } from '@/lib/types';

function severityColor(severity: string): string {
  if (severity === 'Error') return 'var(--danger)';
  if (severity === 'Warning') return 'var(--accent)';
  return 'var(--muted)';
}

function Finding({ finding }: { finding: RevisionFindingView }) {
  // Detail is a JSON string (e.g. the overlap list); render it readably when present.
  let detailLines: string[] = [];
  try {
    const parsed: unknown = finding.detail ? JSON.parse(finding.detail) : null;
    if (Array.isArray(parsed)) {
      detailLines = parsed.map((entry) => String(entry));
    }
  } catch {
    detailLines = finding.detail ? [finding.detail] : [];
  }

  return (
    <div style={{ borderTop: '1px solid var(--border)', padding: '10px 0' }}>
      <div style={{ display: 'flex', gap: 8, alignItems: 'baseline' }}>
        <span style={{ color: severityColor(finding.severity), fontWeight: 600, fontSize: 13 }}>
          {finding.severity}
        </span>
        <span style={{ fontSize: 13 }}>{finding.rule}</span>
      </div>
      <p className="muted" style={{ margin: '4px 0', fontSize: 13 }}>{finding.message}</p>
      {detailLines.length > 0 && (
        <ul className="muted" style={{ margin: '4px 0', paddingLeft: 18, fontSize: 12 }}>
          {detailLines.slice(0, 40).map((line, index) => (
            <li key={index}>{line}</li>
          ))}
        </ul>
      )}
    </div>
  );
}

function RevisionRow({
  summary,
  onApproved,
}: {
  summary: ScheduleRevisionSummary;
  onApproved: () => void;
}) {
  const [detail, setDetail] = useState<ScheduleRevisionDetail | null>(null);
  const [open, setOpen] = useState(false);
  const [reason, setReason] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function toggle() {
    const next = !open;
    setOpen(next);
    if (next && !detail) {
      try {
        setDetail(await getRevision(summary.revisionId));
      } catch (err) {
        setError(err instanceof ApiError ? err.message : 'Bulgular yüklenemedi.');
      }
    }
  }

  async function onApprove() {
    if (reason.trim().length === 0) {
      setError('Onay için bir gerekçe girin.');
      return;
    }
    setBusy(true);
    setError(null);
    try {
      await approveRevision(summary.revisionId, reason.trim());
      onApproved();
    } catch (err) {
      setBusy(false);
      setError(err instanceof ApiError ? err.message : 'Revizyon onaylanamadı.');
    }
  }

  return (
    <div style={{ border: '1px solid var(--border)', borderRadius: 8, padding: 12, marginBottom: 10 }}>
      <button
        type="button"
        onClick={toggle}
        className="link"
        style={{ display: 'flex', justifyContent: 'space-between', width: '100%', color: 'var(--text)' }}
      >
        <span style={{ fontWeight: 600 }}>{summary.sourceId}</span>
        <span className="value" style={{ fontSize: 13 }}>
          {summary.recordCount} kayıt · {open ? '▲' : '▼'}
        </span>
      </button>
      {summary.stateReason && (
        <p className="muted" style={{ margin: '6px 0 0', fontSize: 13 }}>{summary.stateReason}</p>
      )}

      {open && (
        <div style={{ marginTop: 10 }}>
          {detail ? (
            detail.findings.map((finding, index) => <Finding key={index} finding={finding} />)
          ) : (
            <p className="muted">Yükleniyor…</p>
          )}

          <label htmlFor={`reason-${summary.revisionId}`}>Onay gerekçesi (denetim kaydına yazılır)</label>
          <input
            id={`reason-${summary.revisionId}`}
            value={reason}
            onChange={(event) => setReason(event.target.value)}
            placeholder="Kaynağı kontrol ettim; çakışmalar gerçek."
          />
          <button className="primary" type="button" onClick={onApprove} disabled={busy}>
            {busy ? 'Onaylanıyor…' : 'Onayla ve yayınla'}
          </button>
        </div>
      )}

      {error && <div className="error">{error}</div>}
    </div>
  );
}

export function RevisionReview() {
  const [revisions, setRevisions] = useState<ScheduleRevisionSummary[] | null>(null);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    try {
      setRevisions(await listRevisions('ReviewRequired'));
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'İnceleme kuyruğu yüklenemedi.');
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  return (
    <div>
      <h2 style={{ fontSize: 18, margin: '0 0 8px' }}>İnceleme bekleyen revizyonlar</h2>
      {revisions === null && !error && <p className="muted">Yükleniyor…</p>}
      {revisions !== null && revisions.length === 0 && (
        <p className="muted">İnceleme bekleyen revizyon yok.</p>
      )}
      {revisions?.map((summary) => (
        <RevisionRow key={summary.revisionId} summary={summary} onApproved={load} />
      ))}
      {error && <div className="error">{error}</div>}
    </div>
  );
}
