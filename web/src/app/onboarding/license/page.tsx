'use client';

import { useState } from 'react';
import type { FormEvent } from 'react';
import { useRouter } from 'next/navigation';
import { OnboardingGate } from '@/components/OnboardingGate';
import { useSession } from '@/components/SessionProvider';
import { redeemLicense, logout, ApiError } from '@/lib/api';
import { routeForOnboardingState } from '@/lib/onboarding';
import { AuthShell, Brand, Stepper } from '@/components/ui';
import { LICENSE_REQUEST_MESSAGE, OPERATORS, whatsappLink } from '@/lib/contact';

/** Auto-format toward SRK-XXXXX-XXXXX (ported from prototype bindLicenseInput). */
function formatLicense(raw: string): string {
  const cleaned = raw.toUpperCase().replace(/[^A-Z0-9]/g, '');
  const body = cleaned.replace(/^SRK/, '').slice(0, 10);
  let formatted = 'SRK';
  if (body.length > 0) formatted += '-' + body.slice(0, 5);
  if (body.length > 5) formatted += '-' + body.slice(5, 10);
  return formatted;
}

function LicenseForm() {
  const router = useRouter();
  const { refresh, setUser } = useSession();
  const [code, setCode] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function onSubmit(event: FormEvent) {
    event.preventDefault();
    setBusy(true);
    setError(null);
    try {
      const result = await redeemLicense(code.trim());
      const me = await refresh();
      router.replace(routeForOnboardingState(me?.onboardingState ?? result.onboarding.state));
    } catch (err) {
      setBusy(false);
      // K3: never retain a plaintext code after a failure — clear the field.
      setCode('');
      setError(
        err instanceof ApiError
          ? err.message
          : 'Lisans doğrulanamadı. Kodu kontrol edip tekrar deneyin.',
      );
    }
  }

  async function onSignOut() {
    await logout();
    setUser(null);
    router.replace('/sign-in');
  }

  return (
    <AuthShell>
      <Brand />
      <div style={{ margin: '20px 0 24px' }}>
        <Stepper activeIndex={0} />
      </div>

      <h1>Lisans kodu</h1>
      <p className="muted" style={{ marginTop: 8 }}>
        Hesabını etkinleştirmek için sana verilen tek kullanımlık lisans kodunu gir.
      </p>

      <form onSubmit={onSubmit} style={{ marginTop: 24 }}>
        <div className="field">
          <label htmlFor="code">Lisans kodu</label>
          <input
            id="code"
            className="text-input license-input"
            value={code}
            onChange={(event) => setCode(formatLicense(event.target.value))}
            placeholder="SRK-XXXXX-XXXXX"
            autoComplete="off"
            spellCheck={false}
            aria-invalid={error ? true : undefined}
            required
          />
          <p className="field-hint">Kod büyük harfe çevrilir ve tireler otomatik eklenir.</p>
        </div>
        <button
          className="btn btn-primary btn-block"
          type="submit"
          disabled={busy || code.trim().length < 4}
        >
          {busy ? 'Doğrulanıyor…' : 'Etkinleştir'}
        </button>
      </form>

      {error && (
        <div className="error" role="alert" aria-live="polite">
          {error}
        </div>
      )}

      <LicenseRequest />

      <p style={{ marginTop: 20 }}>
        <button className="btn btn-tertiary btn-sm" type="button" onClick={onSignOut}>
          Çıkış yap
        </button>
      </p>
    </AuthShell>
  );
}

/**
 * The way out of the one dead end this step has: a student who reaches it without a code.
 *
 * Codes are handed out person to person rather than sold here, so the screen has to say who to
 * ask. The link opens WhatsApp with the request already written — the point is that nobody has to
 * work out how to phrase it, or which of the two people to bother.
 */
function LicenseRequest() {
  return (
    <div className="license-request">
      <p className="muted">Lisans kodun yok mu? WhatsApp’tan isteyebilirsin.</p>
      <div className="license-request__actions">
        {OPERATORS.map((operator) => (
          <a
            key={operator.phoneDigits}
            className="btn btn-secondary btn-sm"
            href={whatsappLink(operator, LICENSE_REQUEST_MESSAGE)}
            target="_blank"
            rel="noopener noreferrer"
          >
            {operator.name.split(' ').slice(-2).join(' ')} ile WhatsApp
          </a>
        ))}
      </div>
    </div>
  );
}

export default function LicensePage() {
  return (
    <OnboardingGate allow={['LicenseRequired']}>
      <LicenseForm />
    </OnboardingGate>
  );
}
