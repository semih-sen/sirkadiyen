'use client';

import { useCallback, useEffect, useState, type ReactNode } from 'react';
import { useRouter } from 'next/navigation';
import { AdminShell, type AdminNavKey } from '@/components/AdminShell';
import { useSession } from '@/components/SessionProvider';
import { AuthShell } from '@/components/ui';
import { ApiError, getFreeze, logout } from '@/lib/api';
import { ROUTES, routeForOnboardingState } from '@/lib/onboarding';
import type { OperationalFreezeSnapshot } from '@/lib/types';

export function AdminPageFrame({ active, children }: { active: AdminNavKey; children: ReactNode }) {
  const router = useRouter();
  const { user, loading, setUser } = useSession();
  const [freeze, setFreeze] = useState<OperationalFreezeSnapshot | null>(null);
  const [error, setError] = useState<string | null>(null);

  const loadFreeze = useCallback(async () => {
    try {
      setFreeze(await getFreeze());
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Sistem durumu alınamadı.');
    }
  }, []);

  useEffect(() => {
    if (loading) return;
    if (!user) router.replace(ROUTES.signIn);
    else if (user.role !== 'SuperAdmin') router.replace(routeForOnboardingState(user.onboardingState));
    else void loadFreeze();
  }, [loading, user, router, loadFreeze]);

  async function onSignOut() {
    await logout();
    setUser(null);
    router.replace(ROUTES.signIn);
  }

  if (loading || !user || user.role !== 'SuperAdmin') {
    return <AuthShell><p className="loading-note">Yükleniyor…</p></AuthShell>;
  }

  return (
    <AdminShell active={active} operator={user.email} isFrozen={freeze?.isFrozen ?? false} onSignOut={() => void onSignOut()}>
      {error && <div className="error" role="alert" style={{ marginBottom: 16 }}>{error}</div>}
      {children}
    </AdminShell>
  );
}
