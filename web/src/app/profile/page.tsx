'use client';

import { useEffect, useState } from 'react';
import Link from 'next/link';
import { useRouter } from 'next/navigation';
import { OnboardingGate } from '@/components/OnboardingGate';
import { useSession } from '@/components/SessionProvider';
import { AcademicProfileForm, ProfileSaveNotice } from '@/components/AcademicProfileForm';
import { ApiError, getProfile, logout } from '@/lib/api';
import { ROUTES } from '@/lib/onboarding';
import { StudentTopbar } from '@/components/ui';
import type { StudentProfileView } from '@/lib/types';

/**
 * The academic profile edit surface for a student who has already completed the
 * onboarding step. It exists because a profile change is not a one-off: the
 * faculty moves students between groups, and the backend converges the calendar
 * onto the new audience (ADR-096) — behaviour no screen could previously reach.
 */
function ProfileEditor() {
  const router = useRouter();
  const { user, setUser } = useSession();
  const [profile, setProfile] = useState<StudentProfileView | null>(null);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [saved, setSaved] = useState<{ resyncRequested: boolean } | null>(null);

  useEffect(() => {
    getProfile()
      .then(setProfile)
      .catch((error) =>
        setLoadError(error instanceof ApiError ? error.message : 'Profil alınamadı.'),
      );
  }, []);

  async function onSignOut() {
    await logout();
    setUser(null);
    router.replace(ROUTES.signIn);
  }

  return (
    <>
      <StudentTopbar subtitle={user?.displayName ?? user?.email ?? undefined} onSignOut={onSignOut} />
      <main id="main" style={{ padding: '32px 0 80px' }}>
        <div className="container" style={{ maxWidth: 720 }}>
          <p style={{ marginBottom: 12 }}>
            <Link className="btn btn-tertiary btn-sm" href={ROUTES.dashboard}>
              ← Panele dön
            </Link>
          </p>

          <section className="card card-content">
            <h1 style={{ fontSize: 22 }}>Akademik profili düzenle</h1>
            <p className="muted" style={{ marginTop: 8 }}>
              Grubun değiştiyse burada güncelle. Takvimin yeni gruba göre yeniden düzenlenir.
            </p>

            {loadError && (
              <div className="error" role="alert" style={{ marginTop: 14 }}>
                {loadError}
              </div>
            )}

            {saved && (
              <div style={{ marginTop: 14 }}>
                <ProfileSaveNotice resyncRequested={saved.resyncRequested} />
              </div>
            )}

            {!loadError && !profile && <p className="loading-note" style={{ marginTop: 14 }}>Yükleniyor…</p>}

            {profile && (
              <AcademicProfileForm
                initial={profile}
                submitLabel="Profili güncelle"
                busyLabel="Kaydediliyor…"
                onSaved={(result) => {
                  setProfile(result.profile);
                  setSaved({ resyncRequested: result.calendarResyncRequested });
                }}
              />
            )}
          </section>
        </div>
      </main>
    </>
  );
}

export default function ProfileEditPage() {
  return (
    // Every state in which the student already has a profile. `ProfileRequired`
    // belongs to the onboarding step, and a suspended account is refused by the
    // backend anyway — offering the form there would be a promise it cannot keep.
    <OnboardingGate
      allow={[
        'CalendarAuthorizationRequired',
        'ReadyForInitialSync',
        'InitialSyncInProgress',
        'Active',
        'ActionRequired',
      ]}
    >
      <ProfileEditor />
    </OnboardingGate>
  );
}
