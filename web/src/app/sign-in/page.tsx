'use client';

import { useEffect, useRef, useState } from 'react';
import Link from 'next/link';
import { useRouter } from 'next/navigation';
import { useSession } from '@/components/SessionProvider';
import { signInWithGoogle, ApiError } from '@/lib/api';
import { renderGoogleSignInButton } from '@/lib/google';
import { routeForUser } from '@/lib/onboarding';
import { AuthShell, Brand } from '@/components/ui';

const AUTH_CLIENT_ID = process.env.NEXT_PUBLIC_GOOGLE_AUTH_CLIENT_ID ?? '';

export default function SignInPage() {
  const router = useRouter();
  const { user, loading, setUser } = useSession();
  const buttonRef = useRef<HTMLDivElement>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  // Already signed in: send the user to wherever they belong (admin or onboarding).
  useEffect(() => {
    if (!loading && user) {
      router.replace(routeForUser(user));
    }
  }, [loading, user, router]);

  useEffect(() => {
    if (loading || user || !buttonRef.current) {
      return;
    }
    if (!AUTH_CLIENT_ID) {
      setError('NEXT_PUBLIC_GOOGLE_AUTH_CLIENT_ID yapılandırılmamış.');
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
        router.replace(routeForUser(signedIn));
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
    <AuthShell>
      <Brand />
      <h1 style={{ marginTop: 18 }}>Giriş yap</h1>
      <p className="muted" style={{ marginTop: 8 }}>
        Devam etmek için Google hesabınla giriş yap. Şifre kullanılmaz; hesabını yalnızca Google
        doğrular. Takvim izni ayrı bir adımda istenir.
      </p>

      <div style={{ marginTop: 24, minHeight: 52 }}>
        <div ref={buttonRef} style={{ display: busy ? 'none' : 'block' }} />
        {busy && (
          <p className="muted" style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
            <span className="spinner" aria-hidden="true" /> Oturum başlatılıyor…
          </p>
        )}
      </div>

      {error && (
        <div className="error" role="alert" aria-live="polite">
          {error}
        </div>
      )}

      <p className="muted" style={{ marginTop: 24, fontSize: 13 }}>
        Devam ederek{' '}
        <Link href="/gizlilik" style={{ color: 'var(--fg)', fontWeight: 600 }}>
          Gizlilik Politikası
        </Link>{' '}
        ve{' '}
        <Link href="/kosullar" style={{ color: 'var(--fg)', fontWeight: 600 }}>
          Kullanım Koşulları
        </Link>
        ’nı kabul etmiş olursun.
      </p>
    </AuthShell>
  );
}
