'use client';

import { useRouter } from 'next/navigation';
import { OnboardingGate } from '@/components/OnboardingGate';
import { useSession } from '@/components/SessionProvider';
import { logout } from '@/lib/api';
import { ROUTES } from '@/lib/onboarding';
import { AuthShell, Banner, Brand } from '@/components/ui';

function Suspended() {
  const router = useRouter();
  const { setUser } = useSession();

  async function onSignOut() {
    await logout();
    setUser(null);
    router.replace(ROUTES.signIn);
  }

  return (
    <AuthShell>
      <Brand />
      <h1 style={{ marginTop: 18 }}>Hesap askıya alındı</h1>
      <p className="muted" style={{ marginTop: 8 }}>
        Lisansın iptal edilmiş görünüyor, bu yüzden senkronizasyon durduruldu. Takvimine daha önce
        yazılan etkinlikler korunur.
      </p>

      <div style={{ marginTop: 16 }}>
        <Banner tone="danger">
          Bu terminal bir durumdur. Yeniden etkinleştirme için yöneticiyle iletişime geç —
          arayüzden yeniden deneme yapılmaz.
        </Banner>
      </div>

      <p style={{ marginTop: 20 }}>
        <button className="btn btn-tertiary btn-sm" type="button" onClick={onSignOut}>
          Çıkış yap
        </button>
      </p>
    </AuthShell>
  );
}

export default function SuspendedPage() {
  return (
    <OnboardingGate allow={['Suspended']}>
      <Suspended />
    </OnboardingGate>
  );
}
