'use client';

import { useCallback, useEffect, useState } from 'react';
import { useRouter } from 'next/navigation';
import { OnboardingGate } from '@/components/OnboardingGate';
import { useSession } from '@/components/SessionProvider';
import { getSyncStatus, startSync, ApiError } from '@/lib/api';
import { ROUTES } from '@/lib/onboarding';
import { AuthShell, Banner, Brand, Stepper } from '@/components/ui';
import type { CalendarSyncStatusResponse, GoogleCalendarInitialSyncState } from '@/lib/types';

// The worker pipeline (plan §4.2). The backend exposes one authoritative state
// (Pending → InProgress → Completed) and the real ledger event count, so we drive
// timeline status from that state rather than fabricating per-stage progress.
const STAGES: { key: string; label: string }[] = [
  { key: 'queued', label: 'Kuyrukta' },
  { key: 'acquire', label: 'Kaynaklar okunuyor' },
  { key: 'resolve', label: 'Kişisel dersler çözülüyor' },
  { key: 'calendar', label: 'Özel takvim hazırlanıyor' },
  { key: 'write', label: 'Etkinlikler yazılıyor' },
  { key: 'verify', label: 'Doğrulanıyor' },
];

/** Timeline status for a stage given the real backend state. */
function stageStatus(
  index: number,
  state: GoogleCalendarInitialSyncState | undefined,
): 'done' | 'active' | 'pending' {
  if (state === 'Completed') return 'done';
  if (state === 'InProgress') {
    // Queued is done, the middle stages are the active band, verify waits.
    if (index === 0) return 'done';
    if (index === STAGES.length - 1) return 'pending';
    return index <= 3 ? 'active' : 'pending';
  }
  // Pending / unknown: only the queue is reached.
  return index === 0 ? 'active' : 'pending';
}

function InitialSync() {
  const router = useRouter();
  const { user, refresh } = useSession();
  const [status, setStatus] = useState<CalendarSyncStatusResponse | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const poll = useCallback(async () => {
    try {
      const next = await getSyncStatus();
      setStatus(next);
      if (next?.onboarding.state === 'Active') {
        await refresh();
        router.replace(ROUTES.dashboard);
      }
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Durum alınamadı.');
    }
  }, [refresh, router]);

  // Start intent immediately when the backend says we are ready; the worker does
  // the slow work across cycles, so we then poll for progress.
  useEffect(() => {
    let active = true;
    async function begin() {
      if (user?.onboardingState === 'ReadyForInitialSync') {
        setBusy(true);
        try {
          await startSync();
        } catch (err) {
          if (active) {
            setError(err instanceof ApiError ? err.message : 'Senkronizasyon başlatılamadı.');
          }
        } finally {
          if (active) {
            setBusy(false);
          }
        }
      }
      if (active) {
        await poll();
      }
    }
    void begin();
    return () => {
      active = false;
    };
    // Intentionally run once on mount.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  useEffect(() => {
    const timer = setInterval(() => void poll(), 4000);
    return () => clearInterval(timer);
  }, [poll]);

  const state = status?.initialSyncState;
  // %100 is shown only on a backend Completed (plan §5.7 critical rule).
  const percent = state === 'Completed' ? 100 : state === 'InProgress' ? 99 : busy ? 5 : 0;

  return (
    <AuthShell wide>
      <Brand />
      <div style={{ margin: '20px 0 24px' }}>
        <Stepper activeIndex={3} />
      </div>

      <h1>Takvimin hazırlanıyor</h1>
      <p className="muted" style={{ marginTop: 8 }}>
        Ders programın Google takvimine yazılıyor. Bu işlem birkaç dakika sürebilir; sayfayı açık
        bırakabilir ya da daha sonra geri dönebilirsin. Önceki adımlar sunucuda kayıtlı.
      </p>

      <div
        style={{
          display: 'grid',
          gridTemplateColumns: 'minmax(0, 1.4fr) minmax(0, 1fr)',
          gap: 24,
          marginTop: 24,
        }}
      >
        <ol className="timeline">
          {STAGES.map((stage, index) => {
            const s = stageStatus(index, state);
            return (
              <li key={stage.key} data-status={s}>
                <div className="stage-title">{stage.label}</div>
                <div className="stage-meta">
                  {s === 'done' ? 'Bitti' : s === 'active' ? 'Çalışıyor…' : 'Bekliyor'}
                </div>
              </li>
            );
          })}
        </ol>

        <div className="card card-content" style={{ alignSelf: 'start' }}>
          <div className="summary-row">
            <span>Genel</span>
            <span className="value mono">{percent}%</span>
          </div>
          <div className="summary-row">
            <span>Durum</span>
            <span className="value">{busy ? 'Başlatılıyor…' : (state ?? '—')}</span>
          </div>
          <div className="summary-row">
            <span>
              Yazılan etkinlik <span className="proto-data-tag">defterden</span>
            </span>
            <span className="value mono">{status?.mappedEventCount ?? 0}</span>
          </div>
        </div>
      </div>

      {state !== 'Completed' && (
        <div style={{ marginTop: 20 }}>
          <Banner tone="info">
            İlerleme %99’da bekleyebilir; %100 yalnızca sunucu senkronizasyonu onayladığında görünür.
          </Banner>
        </div>
      )}

      {error && (
        <div className="error" role="alert" aria-live="polite">
          {error}
        </div>
      )}
    </AuthShell>
  );
}

export default function SyncPage() {
  return (
    <OnboardingGate allow={['ReadyForInitialSync', 'InitialSyncInProgress']}>
      <InitialSync />
    </OnboardingGate>
  );
}
