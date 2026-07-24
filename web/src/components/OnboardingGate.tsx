'use client';

import { useEffect } from 'react';
import type { ReactNode } from 'react';
import { useRouter } from 'next/navigation';
import { useSession } from '@/components/SessionProvider';
import { ROUTES, routeForOnboardingState } from '@/lib/onboarding';
import type { OnboardingState } from '@/lib/types';

/**
 * Guards an onboarding step. Redirects to sign-in when there is no session, and
 * to the correct step when the backend's onboarding state does not match the
 * states this page serves. The backend stays authoritative; this only navigates.
 */
export function OnboardingGate({
  allow,
  children,
}: {
  allow: OnboardingState[];
  children: ReactNode;
}) {
  const router = useRouter();
  const { user, loading } = useSession();

  useEffect(() => {
    if (loading) {
      return;
    }
    if (!user) {
      router.replace(ROUTES.signIn);
      return;
    }
    if (!allow.includes(user.onboardingState)) {
      router.replace(routeForOnboardingState(user.onboardingState));
    }
  }, [loading, user, allow, router]);

  if (loading || !user || !allow.includes(user.onboardingState)) {
    return (
      <div className="card">
        <p className="muted">Yükleniyor…</p>
      </div>
    );
  }

  return <>{children}</>;
}
