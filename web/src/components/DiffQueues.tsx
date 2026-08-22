'use client';

import { useCallback, useEffect, useState } from 'react';
import {
  ApiError,
  getDiff,
  listDiffs,
  listDiffsByDispatchState,
  listRecentDiffs,
  releaseDiff,
  retryDiff,
  discardDiff,
} from '@/lib/api';
import { LoadState, Tabs, formatDateTime } from '@/components/AdminData';
import type {
  ScheduleDiffDetail,
  ScheduleDiffEntryView,
  ScheduleDiffRecordView,
  ScheduleDiffSummary,
} from '@/lib/types';

/**
 * The two operator queues over the same route, and why they are separate screens.
 *
 * `held` lists diffs by review state: a diff the safety rules stopped before any calendar was
 * touched. `failed` lists them by dispatch state, which is orthogonal — a diff whose fan-out
 * failed terminally is still Ready or Released, so the review-state filter can never show it
 * (ADR-042, ADR-097). Merging them into one list would hide that a released diff can still fail.
 */
type QueueKey = 'held' | 'failed' | 'history';

const QUEUES: { value: QueueKey; label: string }[] = [
  { value: 'held', label: 'Bekletilen diff’ler' },
  { value: 'failed', label: 'Başarısız dağıtım' },
  { value: 'history', label: 'Geçmiş' },
];

function changeBadge(change: string): string {
  if (change === 'Deleted') return 'badge-danger';
  if (change === 'Ambiguous') return 'badge-warning';
  if (change === 'Created') return 'badge-success';
  return 'badge-neutral';
}

function formatWhen(record: ScheduleDiffRecordView): string {
  if (record.isAllDay) return `${record.localDate} · tüm gün`;
  if (!record.startLocalTime) return record.localDate;
  return `${record.localDate} · ${record.startLocalTime}${record.endLocalTime ? `–${record.endLocalTime}` : ''}`;
}

function RecordLine({ label, record }: { label: string; record: ScheduleDiffRecordView }) {
  return (
    <div style={{ marginTop: 4 }}>
      <span className="muted" style={{ fontSize: 12 }}>{label}: </span>
      <span style={{ fontSize: 13 }}>{record.displayTitle}</span>
      <small className="muted" style={{ display: 'block' }}>
        {formatWhen(record)}
        {record.location ? ` · ${record.location}` : ''}
        {record.instructor ? ` · ${record.instructor}` : ''}
        {record.audienceSelectors ? ` · ${record.audienceSelectors}` : ''}
      </small>
    </div>
  );
}

function DiffEntry({ entry }: { entry: ScheduleDiffEntryView }) {
  return (
    <div style={{ borderTop: '1px solid var(--border)', padding: '10px 0' }}>
      <div className="cluster" style={{ gap: 8, justifyContent: 'space-between' }}>
        <span className={`badge ${changeBadge(entry.change)}`}>{entry.change}</span>
        <small className="muted">
          {entry.match}
          {entry.matchScore != null ? ` · ${entry.matchScore}` : ''}
        </small>
      </div>
      {entry.previous && <RecordLine label="Önceki" record={entry.previous} />}
      {entry.current && <RecordLine label="Yeni" record={entry.current} />}
    </div>
  );
}

function Counts({ summary }: { summary: ScheduleDiffSummary }) {
  return (
    <small className="muted" style={{ display: 'block', marginTop: 4 }}>
      +{summary.createdCount} · ~{summary.updatedCount} · −{summary.deletedCount}
      {summary.ambiguousCount > 0 ? ` · ${summary.ambiguousCount} belirsiz` : ''}
      {` · ${summary.previousRecordCount} → ${summary.currentRecordCount} kayıt`}
    </small>
  );
}

/**
 * One reason-gated action button. Every operator action here — release, retry, discard — records who
 * did it and why, so the shape is shared: a required reason, a busy state, and a stated error.
 */
function ReasonAction({
  title,
  hint,
  label,
  busyLabel,
  placeholder,
  primary,
  perform,
  onSettled,
  inputId,
}: {
  title: string;
  hint: string;
  label: string;
  busyLabel: string;
  placeholder: string;
  primary: boolean;
  perform: (reason: string) => Promise<void>;
  onSettled: () => void;
  inputId: string;
}) {
  const [reason, setReason] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function run() {
    const trimmed = reason.trim();
    if (trimmed.length === 0) {
      setError('Bir gerekçe girin; denetim kaydına yazılır.');
      return;
    }
    setBusy(true);
    setError(null);
    try {
      await perform(trimmed);
      onSettled();
    } catch (caught) {
      setBusy(false);
      setError(caught instanceof ApiError ? caught.message : 'İşlem tamamlanamadı.');
    }
  }

  return (
    <div style={{ marginTop: 16, borderTop: '1px solid var(--border)', paddingTop: 14 }}>
      <p className="muted" style={{ fontSize: 13, margin: '0 0 10px' }}>{hint}</p>
      <label htmlFor={inputId}>{title} (denetim kaydına yazılır)</label>
      <input
        id={inputId}
        value={reason}
        onChange={(event) => setReason(event.target.value)}
        placeholder={placeholder}
      />
      <button
        className={primary ? 'btn btn-primary' : 'btn'}
        type="button"
        onClick={() => void run()}
        disabled={busy}
      >
        {busy ? busyLabel : label}
      </button>
      {error && <div className="error" role="alert">{error}</div>}
    </div>
  );
}

/**
 * The actions a diff in this queue accepts, with each refusal made explicit.
 *
 * A held diff can always be discarded (ADR-127), and released only when it is not an ambiguity hold
 * (ADR-042). A failed diff can be retried only while its dispatch has failed terminally. Where an
 * action does not apply it is shown as a stated reason, not a disabled button, so the operator knows
 * it is wrong here rather than merely unavailable.
 */
function DiffAction({
  queue,
  summary,
  onSettled,
}: {
  queue: QueueKey;
  summary: ScheduleDiffSummary;
  onSettled: () => void;
}) {
  if (queue === 'history') {
    return null;
  }

  if (queue === 'failed') {
    return summary.isDispatchRetriable ? (
      <ReasonAction
        title="Yeniden deneme gerekçesi"
        hint="Yeniden deneme yeni bir yetki vermez; aynı değişmez diff, aynı fikir üretmeyen (idempotent) dağıtımla yeniden işlenir. Başarısızlığın nedeni giderilmediyse tekrar başarısız olur."
        label="Dağıtımı yeniden dene"
        busyLabel="Yeniden deneniyor…"
        placeholder="Google API kotası aşılmıştı; kota yenilendi."
        primary
        perform={(reason) => retryDiff(summary.scheduleDiffId, reason).then(() => undefined)}
        onSettled={onSettled}
        inputId={`diff-retry-${summary.scheduleDiffId}`}
      />
    ) : (
      <p className="muted" style={{ fontSize: 13, marginTop: 14 }}>
        Bu diff’in dağıtımı kalıcı olarak başarısız değil, bu yüzden yeniden denenecek bir şey yok:
        bekleyen bir diff zaten kuyrukta, dağıtılmış olan ise tamamlanmış.
      </p>
    );
  }

  // Held queue: release (when unambiguous) and discard (always).
  return (
    <>
      {summary.isReleasable ? (
        <ReasonAction
          title="Serbest bırakma gerekçesi"
          hint="Serbest bırakmak sorumluluğu üstlenmektir: diff bu andan sonra öğrenci takvimlerine uygulanır. Silmeler dahil, yukarıdaki değişikliklerin tamamı geçerlidir."
          label="Serbest bırak ve dağıt"
          busyLabel="Serbest bırakılıyor…"
          placeholder="Kaynakta gerçekten bu kadar ders kaldırılmış; fakülte teyit etti."
          primary
          perform={(reason) => releaseDiff(summary.scheduleDiffId, reason).then(() => undefined)}
          onSettled={onSettled}
          inputId={`diff-release-${summary.scheduleDiffId}`}
        />
      ) : (
        <p className="muted" style={{ fontSize: 13, marginTop: 14 }}>
          Bu diff belirsizlik nedeniyle bekletiliyor ve serbest bırakılamaz. Serbest bırakmak,
          etkilenen takvimlerde eski dersi bırakıp yerine yenisini hiç yazmamak anlamına gelirdi.
          Belirsizliği kaynak çözmeli; bu diff’i reddedip düzeltilmiş bir revizyonu bekleyebilirsin.
        </p>
      )}
      <ReasonAction
        title="Reddetme gerekçesi"
        hint="Reddetmek diff’i kalıcı olarak atar: takvime hiç ulaşmaz. Bayat ya da hatalı bir diff için kullanılır; düzeltmeyi bir sonraki (yeniden çekilmiş) revizyon yapar."
        label="Reddet"
        busyLabel="Reddediliyor…"
        placeholder="Profil 1.1.0’a geçmeden önce üretilmiş bayat diff; yeniden parse edilecek."
        primary={false}
        perform={(reason) => discardDiff(summary.scheduleDiffId, reason).then(() => undefined)}
        onSettled={onSettled}
        inputId={`diff-discard-${summary.scheduleDiffId}`}
      />
    </>
  );
}

function DiffRow({
  queue,
  summary,
  onSettled,
}: {
  queue: QueueKey;
  summary: ScheduleDiffSummary;
  onSettled: () => void;
}) {
  const [detail, setDetail] = useState<ScheduleDiffDetail | null>(null);
  const [open, setOpen] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function toggle() {
    const next = !open;
    setOpen(next);
    if (next && !detail) {
      try {
        setDetail(await getDiff(summary.scheduleDiffId));
      } catch (caught) {
        setError(caught instanceof ApiError ? caught.message : 'Diff ayrıntısı yüklenemedi.');
      }
    }
  }

  return (
    <div style={{ border: '1px solid var(--border)', borderRadius: 8, padding: 12, marginBottom: 10 }}>
      <button
        type="button"
        onClick={() => void toggle()}
        className="link"
        style={{ display: 'block', width: '100%', textAlign: 'left', color: 'var(--text)' }}
      >
        <span className="cluster" style={{ justifyContent: 'space-between' }}>
          <strong>{summary.sourceId}</strong>
          <span className="cluster" style={{ gap: 8 }}>
            <span className="badge badge-neutral">{summary.state}</span>
            <span className={`badge ${summary.calendarDispatchState === 'Failed' ? 'badge-danger' : 'badge-neutral'}`}>
              {summary.calendarDispatchState}
            </span>
            <span aria-hidden="true">{open ? '▲' : '▼'}</span>
          </span>
        </span>
        <Counts summary={summary} />
      </button>

      <small className="muted" style={{ display: 'block', marginTop: 4 }}>
        {formatDateTime(summary.createdAtUtc)}
      </small>

      {(queue === 'held' || queue === 'history') && summary.holdReason && (
        <p style={{ margin: '8px 0 0', fontSize: 13 }}>{summary.holdReason}</p>
      )}

      {summary.discardedAtUtc && (
        <small className="muted" style={{ display: 'block', marginTop: 6, fontSize: 12 }}>
          Reddedildi: {summary.discardedBy ?? '—'}, {formatDateTime(summary.discardedAtUtc)}
          {summary.discardReason ? ` · ${summary.discardReason}` : ''}
        </small>
      )}

      {summary.releasedAtUtc && (
        <small className="muted" style={{ display: 'block', marginTop: 6, fontSize: 12 }}>
          Serbest bırakıldı: {summary.releasedBy ?? '—'}, {formatDateTime(summary.releasedAtUtc)}
          {summary.releaseReason ? ` · ${summary.releaseReason}` : ''}
        </small>
      )}

      {queue === 'failed' && (
        <>
          {summary.dispatchFailureReason && (
            <p style={{ margin: '8px 0 0', fontSize: 13 }}>{summary.dispatchFailureReason}</p>
          )}
          {/* A diff retried repeatedly is the signal: the failure is not transient. */}
          <small
            className={summary.dispatchRetryCount > 0 ? undefined : 'muted'}
            style={{ display: 'block', marginTop: 6, fontSize: 12 }}
          >
            {summary.dispatchAttempts} deneme · operatör yeniden denemesi: {summary.dispatchRetryCount}
            {summary.lastDispatchRetriedAtUtc
              ? ` · son: ${summary.lastDispatchRetriedBy ?? '—'}, ${formatDateTime(summary.lastDispatchRetriedAtUtc)}`
              : ''}
          </small>
          {summary.lastDispatchRetryReason && (
            <small className="muted" style={{ display: 'block', fontSize: 12 }}>
              Son gerekçe: {summary.lastDispatchRetryReason}
            </small>
          )}
        </>
      )}

      {open && (
        <div style={{ marginTop: 10 }}>
          {detail ? (
            <>
              {detail.entries.length === 0 ? (
                <p className="muted" style={{ fontSize: 13 }}>Takvimi değiştiren giriş yok.</p>
              ) : (
                detail.entries.map((entry, index) => (
                  <DiffEntry key={`${entry.change}-${entry.current?.recordId ?? entry.previous?.recordId ?? index}`} entry={entry} />
                ))
              )}
              {detail.actionableEntryCount > detail.entries.length && (
                <p className="muted" style={{ fontSize: 12, marginTop: 8 }}>
                  {detail.actionableEntryCount} girişin ilk {detail.entries.length} tanesi gösteriliyor.
                </p>
              )}
              <DiffAction queue={queue} summary={summary} onSettled={onSettled} />
            </>
          ) : (
            <p className="loading-note">Yükleniyor…</p>
          )}
        </div>
      )}

      {error && <div className="error" role="alert">{error}</div>}
    </div>
  );
}

export function DiffQueues() {
  const [queue, setQueue] = useState<QueueKey>('held');
  const [diffs, setDiffs] = useState<ScheduleDiffSummary[] | null>(null);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    setDiffs(null);
    setError(null);
    try {
      if (queue === 'held') setDiffs(await listDiffs('Held'));
      else if (queue === 'failed') setDiffs(await listDiffsByDispatchState('Failed'));
      else setDiffs(await listRecentDiffs());
    } catch (caught) {
      setError(caught instanceof ApiError ? caught.message : 'Diff kuyruğu yüklenemedi.');
    }
  }, [queue]);

  useEffect(() => { void load(); }, [load]);

  return (
    <div>
      <Tabs
        value={queue}
        onChange={(value) => setQueue(value as QueueKey)}
        items={QUEUES.map((item) => ({ value: item.value, label: item.label }))}
      />
      <p className="muted" style={{ marginBottom: 14, fontSize: 13 }}>
        {queue === 'held'
          ? 'Güvenlik eşiklerinin durdurduğu diff’ler. Hiçbiri takvime ulaşmadı; serbest bırakılana kadar da ulaşmaz.'
          : queue === 'failed'
            ? 'Dağıtımı kalıcı olarak başarısız olmuş diff’ler. İnceleme durumları hâlâ Ready/Released olduğu için bekletilen kuyrukta görünmezler.'
            : 'Her durumdaki en yeni diff’ler (serbest bırakılan, reddedilen, dağıtılan dahil). Salt görüntüleme; bir kaynağın geçmişte ne yaptığını gösterir.'}
      </p>
      <LoadState
        loading={diffs === null && !error}
        error={error}
        empty={diffs?.length === 0}
        onRetry={() => void load()}
      />
      {diffs?.map((summary) => (
        <DiffRow key={summary.scheduleDiffId} queue={queue} summary={summary} onSettled={() => void load()} />
      ))}
    </div>
  );
}
