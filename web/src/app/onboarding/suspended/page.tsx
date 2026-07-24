'use client';

import { useRouter } from 'next/navigation';
import { OnboardingGate } from '@/components/OnboardingGate';
import { useSession } from '@/components/SessionProvider';
import { logout } from '@/lib/api';
import { ROUTES } from '@/lib/onboarding';

function Suspended() {
  const router = useRouter();
  const { setUser } = useSession();

  async function onSignOut() {
    await logout();
    setUser(null);
    router.replace(ROUTES.signIn);
  }

  return (
    <div className="card">
      <div className="brand">Sirkadiyen</div>
      <h1>Hesap askıya alındı</h1>
      <p className="muted">
        Lisansın iptal edilmiş görünüyor, bu yüzden senkronizasyon durduruldu. Yardım için yöneticiyle
        iletişime geç.
      </p>
      <button className="link" type="button" onClick={onSignOut}>
        Çıkış yap
      </button>
    </div>
  );
}

export default function SuspendedPage() {
  return (
    <OnboardingGate allow={['Suspended']}>
      <Suspended />
    </OnboardingGate>
  );
}
