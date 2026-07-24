'use client';

import { useEffect, useRef, useState } from 'react';
import { useRouter } from 'next/navigation';
import { useSession } from '@/components/SessionProvider';
import { signInWithGoogle, ApiError } from '@/lib/api';
import { renderGoogleSignInButton } from '@/lib/google';
import { routeForOnboardingState } from '@/lib/onboarding';

const AUTH_CLIENT_ID = process.env.NEXT_PUBLIC_GOOGLE_AUTH_CLIENT_ID ?? '';

export default function SignInPage() {
  const router = useRouter();
  const { user, loading, setUser } = useSession();
  const buttonRef = useRef<HTMLDivElement>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  // Already signed in: send the user to wherever their onboarding stands.
  useEffect(() => {
    if (!loading && user) {
      router.replace(routeForOnboardingState(user.onboardingState));
    }
  }, [loading, user, router]);

  useEffect(() => {
    if (loading || user || !buttonRef.current) {
      return;
    }
    if (!AUTH_CLIENT_ID) {
      setError('NEXT_PUBLIC_GOOGLE_AUTH_CLIENT_ID is not configured.');
      return;
    }

    let cancelled = false;
    renderGoogleSignInButton(buttonRef.current, AUTH_CLIENT_ID, async (credential) => {
      if (cancelled) {
        return;
      }
      setBusy(true);
      setError(null);
      try {
        const signedIn = await signInWithGoogle(credential);
        setUser(signedIn);
        router.replace(routeForOnboardingState(signedIn.onboardingState));
      } catch (err) {
        setBusy(false);
        setError(
          err instanceof ApiError
            ? err.message
            : 'Google ile giriş tamamlanamadı. Lütfen tekrar deneyin.',
        );
      }
    }).catch(() => setError('Google Identity Services yüklenemedi.'));

    return () => {
      cancelled = true;
    };
  }, [loading, user, router, setUser]);

  return (
    <div className="card">
      <div className="brand">Sirkadiyen</div>
      <h1>Giriş yap</h1>
      <p className="muted">
        Devam etmek için Google hesabınla giriş yap. Şifre kullanılmaz; hesabını yalnızca Google
        doğrular.
      </p>

      <div ref={buttonRef} style={{ minHeight: 44, display: busy ? 'none' : 'block' }} />
      {busy && <p className="muted">Oturum başlatılıyor…</p>}

      {error && <div className="error">{error}</div>}
    </div>
  );
}
