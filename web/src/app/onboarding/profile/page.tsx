'use client';

import { useRouter } from 'next/navigation';
import { OnboardingGate } from '@/components/OnboardingGate';
import { useSession } from '@/components/SessionProvider';
import { AcademicProfileForm } from '@/components/AcademicProfileForm';
import { MealMenuCard } from '@/components/MealMenuCard';
import { routeForOnboardingState } from '@/lib/onboarding';
import { AuthShell, Brand, Stepper } from '@/components/ui';

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

      {/* Profilin bir parçası değil (kendi anahtarıyla ayrıca kaydedilir) ama diğer
          alanlarla aynı `.field` görünümünde: isteğe bağlı ve tamamen tersine
          çevrilebilir, onboarding'i tıkamaz (ADR-150). */}
      <MealMenuCard variant="field" />
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
