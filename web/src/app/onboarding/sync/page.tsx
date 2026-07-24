'use client';

import { useCallback, useEffect, useState } from 'react';
import { useRouter } from 'next/navigation';
import { OnboardingGate } from '@/components/OnboardingGate';
import { useSession } from '@/components/SessionProvider';
import { getSyncStatus, startSync, ApiError } from '@/lib/api';
import { ROUTES } from '@/lib/onboarding';
import type { CalendarSyncStatusResponse } from '@/lib/types';

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

  return (
    <div className="card">
      <div className="brand">Sirkadiyen</div>
      <div className="steps">
        <div className="step done" />
        <div className="step done" />
        <div className="step done" />
        <div className="step current" />
      </div>
      <h1>Takvimin hazırlanıyor</h1>
      <p className="muted">
        Ders programın Google takvimine yazılıyor. Bu işlem birkaç dakika sürebilir; sayfayı açık
        bırakabilir ya da daha sonra geri dönebilirsin.
      </p>

      <div className="status-row">
        <span>Durum</span>
        <span className="value">{busy ? 'Başlatılıyor…' : (status?.initialSyncState ?? '—')}</span>
      </div>
      <div className="status-row">
        <span>Yazılan etkinlik</span>
        <span className="value">{status?.mappedEventCount ?? 0}</span>
      </div>

      {error && <div className="error">{error}</div>}
    </div>
  );
}

export default function SyncPage() {
  return (
    <OnboardingGate allow={['ReadyForInitialSync', 'InitialSyncInProgress']}>
      <InitialSync />
    </OnboardingGate>
  );
}
