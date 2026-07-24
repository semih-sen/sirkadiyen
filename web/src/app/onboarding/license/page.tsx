'use client';

import { useState } from 'react';
import type { FormEvent } from 'react';
import { useRouter } from 'next/navigation';
import { OnboardingGate } from '@/components/OnboardingGate';
import { useSession } from '@/components/SessionProvider';
import { redeemLicense, logout, ApiError } from '@/lib/api';
import { routeForOnboardingState } from '@/lib/onboarding';

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
    <div className="card">
      <div className="brand">Sirkadiyen</div>
      <div className="steps">
        <div className="step current" />
        <div className="step" />
        <div className="step" />
        <div className="step" />
      </div>
      <h1>Lisans kodu</h1>
      <p className="muted">
        Hesabını etkinleştirmek için sana verilen tek kullanımlık lisans kodunu gir.
      </p>

      <form onSubmit={onSubmit}>
        <label htmlFor="code">Lisans kodu</label>
        <input
          id="code"
          value={code}
          onChange={(event) => setCode(event.target.value)}
          placeholder="SRK-XXXXX-XXXXX"
          autoComplete="off"
          spellCheck={false}
          required
        />
        <button className="primary" type="submit" disabled={busy || code.trim().length === 0}>
          {busy ? 'Doğrulanıyor…' : 'Etkinleştir'}
        </button>
      </form>

      {error && <div className="error">{error}</div>}

      <p style={{ marginTop: 20 }}>
        <button className="link" type="button" onClick={onSignOut}>
          Çıkış yap
        </button>
      </p>
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
