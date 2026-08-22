'use client';

import { useCallback, useEffect, useState } from 'react';
import {
  approveRevision,
  getRevision,
  listRevisions,
  listRecentRevisions,
  rejectRevision,
  ApiError,
} from '@/lib/api';
import { Tabs, formatDateTime } from '@/components/AdminData';
import type {
  RevisionFindingView,
  RevisionState,
  ScheduleRevisionDetail,
  ScheduleRevisionSummary,
} from '@/lib/types';

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

/**
 * The queue an operator is working.
 *
 * Rejected is included because rejection is terminal: once a revision leaves the review queue the
 * only surface that can still answer "why did this never reach a calendar" is this one.
 */
type RevisionTab = RevisionState | 'history';

const QUEUES: { value: RevisionTab; label: string }[] = [
  { value: 'ReviewRequired', label: 'İnceleme bekleyen' },
  { value: 'Rejected', label: 'Reddedilen' },
  { value: 'history', label: 'Geçmiş' },
];

function ReviewActions({
  summary,
  onSettled,
}: {
  summary: ScheduleRevisionSummary;
  onSettled: () => void;
}) {
  const [approvalReason, setApprovalReason] = useState('');
  const [rejectionReason, setRejectionReason] = useState('');
  const [confirmingRejection, setConfirmingRejection] = useState(false);
  const [busy, setBusy] = useState<'approve' | 'reject' | null>(null);
  const [error, setError] = useState<string | null>(null);

  async function run(action: 'approve' | 'reject') {
    const reason = (action === 'approve' ? approvalReason : rejectionReason).trim();
    if (reason.length === 0) {
      setError(action === 'approve' ? 'Onay için bir gerekçe girin.' : 'Reddetme için bir gerekçe girin.');
      return;
    }
    setBusy(action);
    setError(null);
    try {
      if (action === 'approve') {
        await approveRevision(summary.revisionId, reason);
      } else {
        await rejectRevision(summary.revisionId, reason);
      }
      onSettled();
    } catch (err) {
      setBusy(null);
      setError(
        err instanceof ApiError
          ? err.message
          : action === 'approve'
            ? 'Revizyon onaylanamadı.'
            : 'Revizyon reddedilemedi.',
      );
    }
  }

  return (
    <div style={{ marginTop: 16 }}>
      <label htmlFor={`approve-${summary.revisionId}`}>
        Onay gerekçesi (denetim kaydına yazılır)
      </label>
      <input
        id={`approve-${summary.revisionId}`}
        value={approvalReason}
        onChange={(event) => setApprovalReason(event.target.value)}
        placeholder="Kaynağı kontrol ettim; çakışmalar gerçek."
      />
      <button
        className="btn btn-primary"
        type="button"
        onClick={() => void run('approve')}
        disabled={busy !== null}
      >
        {busy === 'approve' ? 'Onaylanıyor…' : 'Onayla ve yayınla'}
      </button>

      <div style={{ borderTop: '1px solid var(--border)', marginTop: 20, paddingTop: 16 }}>
        {!confirmingRejection ? (
          <>
            <p className="muted" style={{ fontSize: 13, margin: '0 0 10px' }}>
              Parse hatalıysa revizyon reddedilir. Bu işlem geri alınamaz ve revizyon hiçbir
              takvime ulaşmaz. Düzeltme yolu geri alma değil, kaynağı düzeltip bu revizyonun
              üzerine yeni bir revizyon yayınlamaktır.
            </p>
            <button
              className="btn btn-secondary btn-sm"
              type="button"
              onClick={() => setConfirmingRejection(true)}
            >
              Revizyonu reddet
            </button>
          </>
        ) : (
          <>
            <label htmlFor={`reject-${summary.revisionId}`}>
              Reddetme gerekçesi (kalıcı; denetim kaydına yazılır)
            </label>
            <input
              id={`reject-${summary.revisionId}`}
              value={rejectionReason}
              onChange={(event) => setRejectionReason(event.target.value)}
              placeholder="Kaynak tabloda tarih sütunu kaymış; düzeltilmiş belge beklenecek."
            />
            <div className="cluster" style={{ gap: 8 }}>
              <button
                className="btn btn-danger"
                type="button"
                onClick={() => void run('reject')}
                disabled={busy !== null}
              >
                {busy === 'reject' ? 'Reddediliyor…' : 'Reddetmeyi onayla'}
              </button>
              <button
                className="btn btn-tertiary btn-sm"
                type="button"
                onClick={() => setConfirmingRejection(false)}
                disabled={busy !== null}
              >
                Vazgeç
              </button>
            </div>
          </>
        )}
      </div>

      {error && <div className="error" role="alert">{error}</div>}
    </div>
  );
}

function SettledRecord({ detail }: { detail: ScheduleRevisionDetail }) {
  if (detail.rejectedBy) {
    return (
      <div style={{ marginTop: 16, borderTop: '1px solid var(--border)', paddingTop: 12 }}>
        <div className="summary-row"><span className="muted">Reddeden</span><strong>{detail.rejectedBy}</strong></div>
        <div className="summary-row"><span className="muted">Tarih</span><strong>{formatDateTime(detail.rejectedAtUtc)}</strong></div>
        <p style={{ marginTop: 8 }}>{detail.rejectionReason}</p>
      </div>
    );
  }
  if (detail.approvedBy) {
    return (
      <div style={{ marginTop: 16, borderTop: '1px solid var(--border)', paddingTop: 12 }}>
        <div className="summary-row"><span className="muted">Onaylayan</span><strong>{detail.approvedBy}</strong></div>
        <div className="summary-row"><span className="muted">Tarih</span><strong>{formatDateTime(detail.approvedAtUtc)}</strong></div>
        <p style={{ marginTop: 8 }}>{detail.approvalReason}</p>
      </div>
    );
  }
  return null;
}

function RevisionRow({
  summary,
  actionable,
  showState,
  onSettled,
}: {
  summary: ScheduleRevisionSummary;
  actionable: boolean;
  showState?: boolean;
  onSettled: () => void;
}) {
  const [detail, setDetail] = useState<ScheduleRevisionDetail | null>(null);
  const [open, setOpen] = useState(false);
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

  return (
    <div style={{ border: '1px solid var(--border)', borderRadius: 8, padding: 12, marginBottom: 10 }}>
      <button
        type="button"
        onClick={() => void toggle()}
        className="link"
        style={{ display: 'flex', justifyContent: 'space-between', width: '100%', color: 'var(--text)' }}
      >
        <span style={{ fontWeight: 600 }}>{summary.sourceId}</span>
        <span className="value" style={{ fontSize: 13 }}>
          {showState && <span className="badge badge-neutral" style={{ marginRight: 8 }}>{summary.state}</span>}
          {summary.recordCount} kayıt · {open ? '▲' : '▼'}
        </span>
      </button>
      {summary.stateReason && (
        <p className="muted" style={{ margin: '6px 0 0', fontSize: 13 }}>{summary.stateReason}</p>
      )}

      {open && (
        <div style={{ marginTop: 10 }}>
          {detail ? (
            <>
              {detail.findings.map((finding, index) => <Finding key={index} finding={finding} />)}
              {actionable
                ? <ReviewActions summary={summary} onSettled={onSettled} />
                : <SettledRecord detail={detail} />}
            </>
          ) : (
            <p className="muted">Yükleniyor…</p>
          )}
        </div>
      )}

      {error && <div className="error" role="alert">{error}</div>}
    </div>
  );
}

export function RevisionReview() {
  const [queue, setQueue] = useState<RevisionTab>('ReviewRequired');
  const [revisions, setRevisions] = useState<ScheduleRevisionSummary[] | null>(null);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    setRevisions(null);
    setError(null);
    try {
      setRevisions(
        queue === 'history' ? await listRecentRevisions() : await listRevisions(queue),
      );
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'İnceleme kuyruğu yüklenemedi.');
    }
  }, [queue]);

  useEffect(() => {
    void load();
  }, [load]);

  const actionable = queue === 'ReviewRequired';

  return (
    <div>
      <Tabs
        value={String(queue)}
        onChange={(value) => setQueue(value as RevisionTab)}
        items={QUEUES.map((item) => ({ value: String(item.value), label: item.label }))}
      />
      {queue === 'history' && (
        <p className="muted" style={{ margin: '10px 0 4px', fontSize: 13 }}>
          Her durumdaki en yeni revizyonlar (yayımlanan, geçersiz kılınan, reddedilen dahil), en yeni
          önce. Salt görüntüleme.
        </p>
      )}
      {revisions === null && !error && <p className="loading-note">Yükleniyor…</p>}
      {revisions !== null && revisions.length === 0 && (
        <p className="muted">
          {actionable
            ? 'İnceleme bekleyen revizyon yok.'
            : queue === 'history'
              ? 'Revizyon geçmişi boş.'
              : 'Reddedilmiş revizyon yok.'}
        </p>
      )}
      {revisions?.map((summary) => (
        <RevisionRow
          key={summary.revisionId}
          summary={summary}
          actionable={actionable}
          showState={queue === 'history'}
          onSettled={() => void load()}
        />
      ))}
      {error && <div className="error" role="alert">{error}</div>}
    </div>
  );
}
