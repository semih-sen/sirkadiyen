'use client';

import { useState } from 'react';
import { useRouter } from 'next/navigation';
import { OnboardingGate } from '@/components/OnboardingGate';
import { useSession } from '@/components/SessionProvider';
import { authorizeCalendar, getCalendarAuthorizationOptions, ApiError } from '@/lib/api';
import { requestCalendarAuthorizationCode } from '@/lib/google';
import { routeForOnboardingState } from '@/lib/onboarding';
import { AuthShell, Banner, Brand, ImplNote, Stepper } from '@/components/ui';

const CAN_ACCESS = [
  'Yalnızca kendi oluşturduğu “Sirkadiyen Ders Programı” takvimi',
  'Bu takvimde etkinlik oluşturma, güncelleme, kaldırma',
  'Google hesap kimliğini doğrulama amaçlı okuma',
];
const CANNOT_ACCESS = [
  'Mevcut kişisel veya iş takvimlerin',
  'Diğer takvimlerdeki etkinliklerin',
  'Gmail, Drive veya başka bir Google servisi',
];

function CalendarAuthorization() {
  const router = useRouter();
  const { user, refresh } = useSession();
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const needsReauth = user?.onboardingState === 'ActionRequired';

  async function onAuthorize() {
    setBusy(true);
    setError(null);
    try {
      // The client ID + scope come from the backend so the consent screen requests
      // exactly the scope the backend will accept (calendar.app.created, ADR-057).
      const options = await getCalendarAuthorizationOptions();
      const code = await requestCalendarAuthorizationCode(options.clientId, options.scope);
      const result = await authorizeCalendar(code);
      const me = await refresh();
      router.replace(routeForOnboardingState(me?.onboardingState ?? result.onboarding.state));
    } catch (err) {
      setBusy(false);
      if (err instanceof ApiError && err.status === 403) {
        setError('Takvim izni verilmedi. Sirkadiyen’in kendi takvimini yönetmesine izin ver.');
      } else {
        setError(err instanceof ApiError ? err.message : 'Takvim izni tamamlanamadı.');
      }
    }
  }

  return (
    <AuthShell wide>
      <Brand />
      <div style={{ margin: '20px 0 24px' }}>
        <Stepper activeIndex={2} />
      </div>

      <h1>Takvim izni</h1>
      <p className="muted" style={{ marginTop: 8 }}>
        Sirkadiyen hesabında ayrı bir takvim oluşturur; ana takvimine dokunmaz. Yetki verildikten
        sonra takvimi senkronizasyon sırasında sunucu oluşturur.
      </p>

      {needsReauth && (
        <div style={{ marginTop: 16 }}>
          <Banner tone="warning">
            Google erişimi iptal edilmiş görünüyor. Senkronizasyonun sürmesi için yeniden
            yetkilendir.
          </Banner>
        </div>
      )}

      <div
        style={{
          display: 'grid',
          gap: 16,
          gridTemplateColumns: 'repeat(auto-fit, minmax(220px, 1fr))',
          margin: '20px 0',
        }}
      >
        <div className="card card-content">
          <h4>✅ Erişebildiği</h4>
          <ul style={{ listStyle: 'none', margin: '12px 0 0', padding: 0, display: 'grid', gap: 8, fontSize: 14 }}>
            {CAN_ACCESS.map((item) => (
              <li key={item} style={{ color: 'var(--fg-ink)' }}>
                {item}
              </li>
            ))}
          </ul>
        </div>
        <div className="card card-content">
          <h4>🚫 Erişemediği</h4>
          <ul style={{ listStyle: 'none', margin: '12px 0 0', padding: 0, display: 'grid', gap: 8, fontSize: 14 }}>
            {CANNOT_ACCESS.map((item) => (
              <li key={item} className="muted">
                {item}
              </li>
            ))}
          </ul>
        </div>
      </div>

      <button className="btn btn-primary btn-block" type="button" onClick={onAuthorize} disabled={busy}>
        {busy ? 'Google açılıyor…' : needsReauth ? 'Yeniden yetkilendir' : 'Google ile izin ver'}
      </button>

      {error && (
        <div className="error" role="alert" aria-live="polite">
          {error}
        </div>
      )}

      <ImplNote>
        <code>GET/POST /api/calendar/authorization</code> popup kod akışı; yalnızca{' '}
        <code>calendar.app.created</code> kapsamı istenir (ADR-057). Takvime yazan taraf .NET
        Worker’dır.
      </ImplNote>
    </AuthShell>
  );
}

export default function CalendarPage() {
  return (
    <OnboardingGate allow={['CalendarAuthorizationRequired', 'ActionRequired']}>
      <CalendarAuthorization />
    </OnboardingGate>
  );
}
