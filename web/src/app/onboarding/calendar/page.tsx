'use client';

import { useState } from 'react';
import { useRouter } from 'next/navigation';
import { OnboardingGate } from '@/components/OnboardingGate';
import { useSession } from '@/components/SessionProvider';
import { authorizeCalendar, getCalendarAuthorizationOptions, ApiError } from '@/lib/api';
import { requestCalendarAuthorizationCode } from '@/lib/google';
import { routeForOnboardingState } from '@/lib/onboarding';

function CalendarAuthorization() {
  const router = useRouter();
  const { refresh } = useSession();
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

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
    <div className="card">
      <div className="brand">Sirkadiyen</div>
      <div className="steps">
        <div className="step done" />
        <div className="step done" />
        <div className="step current" />
        <div className="step" />
      </div>
      <h1>Takvim izni</h1>
      <p className="muted">
        Sirkadiyen yalnızca kendi oluşturduğu takvime erişir; ana takvimine dokunmaz. Ders
        programını buraya yazabilmek için izin ver.
      </p>

      <button className="primary" type="button" onClick={onAuthorize} disabled={busy}>
        {busy ? 'Google açılıyor…' : 'Google ile izin ver'}
      </button>

      {error && <div className="error">{error}</div>}
    </div>
  );
}

export default function CalendarPage() {
  return (
    <OnboardingGate allow={['CalendarAuthorizationRequired', 'ActionRequired']}>
      <CalendarAuthorization />
    </OnboardingGate>
  );
}
