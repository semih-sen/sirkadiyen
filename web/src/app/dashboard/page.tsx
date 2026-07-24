'use client';

import { useEffect, useState } from 'react';
import { useRouter } from 'next/navigation';
import { OnboardingGate } from '@/components/OnboardingGate';
import { useSession } from '@/components/SessionProvider';
import { getSyncStatus, logout } from '@/lib/api';
import { ROUTES } from '@/lib/onboarding';
import type { CalendarSyncStatusResponse } from '@/lib/types';

function Dashboard() {
  const router = useRouter();
  const { user, setUser } = useSession();
  const [status, setStatus] = useState<CalendarSyncStatusResponse | null>(null);

  useEffect(() => {
    getSyncStatus()
      .then(setStatus)
      .catch(() => setStatus(null));
  }, []);

  async function onSignOut() {
    await logout();
    setUser(null);
    router.replace(ROUTES.signIn);
  }

  return (
    <div className="card">
      <div className="brand">Sirkadiyen</div>
      <h1>Hazırsın 🎉</h1>
      <p className="muted">
        Ders programın Google takvimine senkronize ediliyor. Değişiklikler otomatik olarak
        takvimine yansır.
      </p>

      <div className="status-row">
        <span>Hesap</span>
        <span className="value">{user?.email}</span>
      </div>
      <div className="status-row">
        <span>Senkronizasyon</span>
        <span className="value">{status?.initialSyncState ?? '—'}</span>
      </div>
      <div className="status-row">
        <span>Takvimdeki etkinlik</span>
        <span className="value">{status?.mappedEventCount ?? 0}</span>
      </div>

      <p style={{ marginTop: 20 }}>
        <button className="link" type="button" onClick={onSignOut}>
          Çıkış yap
        </button>
      </p>
    </div>
  );
}

export default function DashboardPage() {
  return (
    <OnboardingGate allow={['Active']}>
      <Dashboard />
    </OnboardingGate>
  );
}
