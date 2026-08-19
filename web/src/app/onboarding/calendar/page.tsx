'use client';

import { useEffect, useState } from 'react';
import { useRouter } from 'next/navigation';
import { OnboardingGate } from '@/components/OnboardingGate';
import { useSession } from '@/components/SessionProvider';
import {
  ApiError,
  assessCalendarRebuild,
  authorizeCalendar,
  getCalendarAuthorizationOptions,
  rebuildCalendar,
} from '@/lib/api';
import { requestCalendarAuthorizationCode } from '@/lib/google';
import { ROUTES, routeForOnboardingState } from '@/lib/onboarding';
import { AuthShell, Banner, Brand, Stepper } from '@/components/ui';

const CAN_ACCESS = [
  'Yalnızca kendi oluşturduğu “Sirkadiyen” takvimi',
  'Bu takvimde etkinlik oluşturma, güncelleme, kaldırma',
  'Takvimlerinin yalnızca listesi — aynı takvimi ikinci kez oluşturmamak için',
];
const CANNOT_ACCESS = [
  'Diğer takvimlerindeki etkinlikler — okunmaz, değiştirilmez',
  'Kişisel veya iş takvimlerine yazma',
  'Gmail, Drive veya başka bir Google servisi',
];

/**
 * The way out of a deleted calendar (ADR-116).
 *
 * Before this existed the student had none. Deleting the Sirkadiyen calendar marks the connection
 * unavailable, which drops them out of every writer and lands them on this page — and the only
 * control here was a consent button that does not clear the flag, so consenting returned them to
 * this page indefinitely.
 *
 * It writes no calendar. It discards the event ledger, which described a calendar that no longer
 * exists, and returns the connection to the state initial synchronization starts from; the student
 * then starts that synchronization themselves, exactly as they did the first time (ADR-058).
 */
function RebuildCalendar() {
  const { refresh } = useSession();
  const router = useRouter();
  const [unavailableSince, setUnavailableSince] = useState<string | null>(null);
  const [eligible, setEligible] = useState(false);
  const [confirming, setConfirming] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    // Asked rather than assumed from the onboarding state: the server owns the reason, and a
    // calendar that came back on its own must not be offered a destructive repair.
    void assessCalendarRebuild()
      .then((assessment) => {
        if (cancelled) return;
        setEligible(assessment.outcome === 'Reset');
        setUnavailableSince(assessment.unavailableSinceUtc ?? null);
      })
      .catch(() => { if (!cancelled) setEligible(false); });
    return () => { cancelled = true; };
  }, []);

  async function onRebuild() {
    setBusy(true);
    setError(null);
    try {
      await rebuildCalendar();
      const me = await refresh();
      // Pending initial sync, so the sync screen is where they land and press start.
      router.replace(me ? routeForOnboardingState(me.onboardingState) : ROUTES.sync);
    } catch (caught) {
      setBusy(false);
      setError(caught instanceof ApiError ? caught.message : 'Takvim yeniden kurulamadı.');
    }
  }

  return (
    <div style={{ marginTop: 16 }}>
      <Banner tone="warning">
        <strong>Sirkadiyen takvimin bulunamıyor.</strong> Silinmiş görünüyor
        {unavailableSince ? ` (${new Date(unavailableSince).toLocaleDateString('tr-TR')} tarihinden beri)` : ''}.
        Google erişimin duruyor; eksik olan takvimin kendisi.
      </Banner>

      {eligible && !confirming && (
        <button
          className="btn btn-secondary btn-block"
          type="button"
          style={{ marginTop: 12 }}
          onClick={() => setConfirming(true)}
        >
          Takvimimi yeniden oluştur
        </button>
      )}

      {eligible && confirming && (
        <div className="card card-content" style={{ marginTop: 12 }}>
          <p style={{ fontSize: 14, margin: 0 }}>
            Sirkadiyen sana yeni bir takvim oluşturacak ve derslerini baştan yazacak. Silinen
            takvimdeki eski etkinlikler geri gelmez — o takvim artık yok. Diğer takvimlerine
            dokunulmaz.
          </p>
          <p className="muted" style={{ fontSize: 13, marginTop: 8 }}>
            Bu adımdan sonra senkronizasyonu kendin başlatacaksın.
          </p>
          <div className="cluster" style={{ marginTop: 12 }}>
            <button
              className="btn btn-primary"
              type="button"
              disabled={busy}
              onClick={() => void onRebuild()}
            >
              {busy ? 'Hazırlanıyor…' : 'Evet, yeniden oluştur'}
            </button>
            <button
              className="btn btn-tertiary"
              type="button"
              disabled={busy}
              onClick={() => setConfirming(false)}
            >
              Vazgeç
            </button>
          </div>
        </div>
      )}

      {error && <div className="error" role="alert">{error}</div>}
    </div>
  );
}

function CalendarAuthorization() {
  const router = useRouter();
  const { user, refresh } = useSession();
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  // `ActionRequired` has exactly one cause: the dedicated calendar was proven unreachable, which
  // in practice means the student deleted it. A revoked grant is a different state — it clears
  // `Authorized`, and onboarding reports `CalendarAuthorizationRequired` for that. Calling this
  // one "re-authorize" was wrong and, worse, was a loop: re-consenting does not clear the
  // unavailable flag, so the user was routed straight back here (ADR-116).
  const calendarMissing = user?.onboardingState === 'ActionRequired';

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
        Sirkadiyen hesabında ayrı bir takvim oluşturur; ana takvimine dokunmaz. Takvim, ilk
        eşitleme sırasında otomatik olarak hazırlanır.
      </p>

      {calendarMissing && <RebuildCalendar />}

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
        {busy ? 'Google açılıyor…' : calendarMissing ? 'İzni yenile' : 'Google ile izin ver'}
      </button>

      {error && (
        <div className="error" role="alert" aria-live="polite">
          {error}
        </div>
      )}
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
