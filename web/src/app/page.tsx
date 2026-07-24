'use client';

import { useEffect } from 'react';
import { useRouter } from 'next/navigation';
import { useSession } from '@/components/SessionProvider';
import { ROUTES, routeForOnboardingState } from '@/lib/onboarding';

export default function HomePage() {
  const router = useRouter();
  const { user, loading } = useSession();

  useEffect(() => {
    if (loading) {
      return;
    }
    router.replace(user ? routeForOnboardingState(user.onboardingState) : ROUTES.signIn);
  }, [loading, user, router]);

  return (
    <div className="card">
      <div className="brand">Sirkadiyen</div>
      <p className="muted">Yönlendiriliyor…</p>
    </div>
  );
}
