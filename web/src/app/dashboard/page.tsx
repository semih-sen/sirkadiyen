'use client';

import { useEffect, useState } from 'react';
import { useRouter } from 'next/navigation';
import { OnboardingGate } from '@/components/OnboardingGate';
import { useSession } from '@/components/SessionProvider';
import { getProfile, getSyncStatus, logout } from '@/lib/api';
import { ROUTES } from '@/lib/onboarding';
import { Banner, ImplNote, StudentTopbar } from '@/components/ui';
import { DepartmentColorEditor } from '@/components/DepartmentColorEditor';
import type { CalendarSyncStatusResponse, StudentProfileView } from '@/lib/types';

const PROGRAM_LABELS: Record<string, string> = { Turkish: 'Türkçe', English: 'İngilizce' };

const SELECTOR_LABELS: Record<string, string> = {
  practiceGroup: 'Uygulama grubu',
  practiceSubgroup: 'Uygulama alt grubu',
  anatomyGroup: 'Anatomi grubu',
  curriculumGroup: 'Müfredat grubu',
};

function Dashboard() {
  const router = useRouter();
  const { user, setUser } = useSession();
  const [status, setStatus] = useState<CalendarSyncStatusResponse | null>(null);
  const [profile, setProfile] = useState<StudentProfileView | null>(null);

  useEffect(() => {
    getSyncStatus()
      .then(setStatus)
      .catch(() => setStatus(null));
    getProfile()
      .then(setProfile)
      .catch(() => setProfile(null));
  }, []);

  async function onSignOut() {
    await logout();
    setUser(null);
    router.replace(ROUTES.signIn);
  }

  const synced = status?.initialSyncState === 'Completed';
  const subtitle = profile
    ? `${user?.displayName ?? user?.email} · Dönem ${profile.classYear}`
    : (user?.displayName ?? user?.email ?? undefined);

  return (
    <>
      <StudentTopbar subtitle={subtitle} onSignOut={onSignOut} />

      <main id="main" style={{ padding: '32px 0 80px' }}>
        <div className="container">
          {/* 1. Sync health — derived strictly from the authoritative sync state. */}
          <Banner tone={synced ? 'info' : 'warning'}>
            <strong>{synced ? 'Takvimin güncel.' : 'Senkronizasyon tamamlanmadı.'}</strong>
            <div className="muted" style={{ marginTop: 2, fontSize: 13.5 }}>
              {synced
                ? `Takvimindeki yönetilen etkinlik sayısı: ${status?.mappedEventCount ?? 0}.`
                : `Durum: ${status?.initialSyncState ?? '—'}. Worker etkinlikleri yazmayı sürdürüyor.`}
            </div>
          </Banner>

          <div
            style={{
              display: 'grid',
              gridTemplateColumns: 'minmax(0, 2fr) minmax(0, 1fr)',
              gap: 24,
              marginTop: 24,
              alignItems: 'start',
            }}
          >
            {/* Left column */}
            <div className="stack" style={{ gap: 20 }}>
              <section className="card card-content">
                <h3 style={{ fontSize: 16 }}>Senkronizasyon durumu</h3>
                <div style={{ marginTop: 10 }}>
                  <div className="summary-row">
                    <span className="muted">Hesap</span>
                    <strong>{user?.email}</strong>
                  </div>
                  <div className="summary-row">
                    <span className="muted">İlk senkronizasyon</span>
                    <strong>{status?.initialSyncState ?? '—'}</strong>
                  </div>
                  <div className="summary-row">
                    <span className="muted">Takvimdeki etkinlik</span>
                    <strong className="mono">{status?.mappedEventCount ?? 0}</strong>
                  </div>
                </div>
                <ImplNote>
                  <code>GET /api/calendar/sync</code>. Takvime yazan taraf .NET Worker’dır;
                  değişiklikler kaynak güncellendikçe otomatik yansır.
                </ImplNote>
              </section>

              {/* Target-state modules with no backend yet — shown honestly, no fabricated data. */}
              <section className="card card-content">
                <h3 style={{ fontSize: 16 }}>Sıradaki dersler ve program değişiklikleri</h3>
                <p className="muted" style={{ marginTop: 8, fontSize: 14 }}>
                  Sıradaki dersler, son program değişiklikleri ve senkronizasyon geçmişi bu alanda
                  gösterilecek. Bu veriler için henüz bir arka uç uç noktası yok — bkz.{' '}
                  <code>GAPS.md</code>.
                </p>
                <span className="badge badge-neutral" style={{ marginTop: 12 }}>
                  Yakında
                </span>
              </section>

              <section className="card card-content">
                <h3 style={{ fontSize: 16 }}>Bir sorun mu var?</h3>
                <p className="muted" style={{ marginTop: 8, fontSize: 14 }}>
                  Takviminde eksik veya hatalı bir ders görüyorsan onarım/mutabakat talebi
                  oluşturabilirsin. Bu denetlenen bir işlemdir; talebin zaman damgası ve sonucu
                  kaydedilir.
                </p>
                <button className="btn btn-secondary btn-sm" type="button" disabled aria-disabled="true" style={{ marginTop: 14 }}>
                  Onarım talebi oluştur (arka uç bekleniyor)
                </button>
              </section>

              <section className="card card-content">
                <DepartmentColorEditor mode="user" />
              </section>
            </div>

            {/* Right column */}
            <div className="stack" style={{ gap: 20 }}>
              <section className="card card-content">
                <h3 style={{ fontSize: 15 }}>Akademik profil</h3>
                {profile ? (
                  <div style={{ marginTop: 10 }}>
                    <div className="summary-row">
                      <span className="muted">Dönem</span>
                      <strong>{profile.classYear}</strong>
                    </div>
                    <div className="summary-row">
                      <span className="muted">Program dili</span>
                      <strong>{PROGRAM_LABELS[profile.programLanguage] ?? profile.programLanguage}</strong>
                    </div>
                    {Object.entries(profile.selectors).map(([key, value]) => (
                      <div className="summary-row" key={key}>
                        <span className="muted">{SELECTOR_LABELS[key] ?? key}</span>
                        <strong>{value}</strong>
                      </div>
                    ))}
                  </div>
                ) : (
                  <p className="muted" style={{ marginTop: 10, fontSize: 13.5 }}>
                    Profil yükleniyor…
                  </p>
                )}
              </section>

              <section className="card card-content">
                <h3 style={{ fontSize: 15 }}>Google Calendar bağlantısı</h3>
                <div className="cluster" style={{ justifyContent: 'space-between', marginTop: 12 }}>
                  <span className={`badge ${status?.hasManagedCalendar ? 'badge-success' : 'badge-neutral'}`}>
                    {status?.hasManagedCalendar ? 'Yönetilen takvim hazır' : 'Takvim bekleniyor'}
                  </span>
                </div>
                <p className="muted" style={{ marginTop: 10, fontSize: 13 }}>
                  Sirkadiyen yalnızca kendi oluşturduğu takvimi yönetir; kişisel takvimlerine
                  dokunmaz.
                </p>
              </section>

              <section className="card card-content" style={{ opacity: 0.75 }}>
                <h3 style={{ fontSize: 15 }}>Bildirimler · Makaleler</h3>
                <p className="muted" style={{ marginTop: 8, fontSize: 13 }}>
                  Kullanıcı bildirimleri ve içerik alanı planlanıyor.
                </p>
                <span className="badge badge-neutral" style={{ marginTop: 10 }}>
                  Yakında
                </span>
              </section>

            </div>
          </div>
        </div>
      </main>
    </>
  );
}

export default function DashboardPage() {
  return (
    <OnboardingGate allow={['Active']}>
      <Dashboard />
    </OnboardingGate>
  );
}
