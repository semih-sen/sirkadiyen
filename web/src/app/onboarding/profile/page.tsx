'use client';

import { useRouter } from 'next/navigation';
import { OnboardingGate } from '@/components/OnboardingGate';
import { useSession } from '@/components/SessionProvider';
import { AcademicProfileForm } from '@/components/AcademicProfileForm';
import { routeForOnboardingState } from '@/lib/onboarding';
import { AuthShell, Brand, ImplNote, Stepper } from '@/components/ui';

function ProfileStep() {
  const router = useRouter();
  const { refresh } = useSession();

  return (
    <AuthShell wide>
      <Brand />
      <div style={{ margin: '20px 0 24px' }}>
        <Stepper activeIndex={1} />
      </div>

      <h1>Akademik profil</h1>

      <AcademicProfileForm
        submitLabel="Devam et"
        busyLabel="Kaydediliyor…"
        onSaved={async (result) => {
          const me = await refresh();
          router.replace(routeForOnboardingState(me?.onboardingState ?? result.onboarding.state));
        }}
      />

      <ImplNote>
        Alanlar <code>GET /api/profile/options</code> şemasından dinamik üretilir;{' '}
        <code>PUT /api/profile</code> ile kaydedilir (ADR-055, ADR-056).
      </ImplNote>
    </AuthShell>
  );
}

export default function ProfilePage() {
  return (
    <OnboardingGate allow={['ProfileRequired']}>
      <ProfileStep />
    </OnboardingGate>
  );
}
