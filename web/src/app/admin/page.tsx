'use client';

import { useCallback, useEffect, useState } from 'react';
import { useRouter } from 'next/navigation';
import { useSession } from '@/components/SessionProvider';
import { activateUser, getFreeze, logout, setFreeze, ApiError } from '@/lib/api';
import { ROUTES, routeForOnboardingState } from '@/lib/onboarding';
import { RevisionReview } from '@/components/RevisionReview';
import { SourceDocumentUpload } from '@/components/SourceDocumentUpload';
import { AdminShell, AdminSectionTitle } from '@/components/AdminShell';
import { AuthShell, Banner, ImplNote } from '@/components/ui';
import type { OperationalFreezeSnapshot } from '@/lib/types';

function FreezeCard({
  freeze,
  onChanged,
}: {
  freeze: OperationalFreezeSnapshot | null;
  onChanged: (next: OperationalFreezeSnapshot) => void;
}) {
  const [reason, setReason] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function onToggle() {
    if (!freeze) {
      return;
    }
    if (reason.trim().length === 0) {
      setError('Bir gerekçe girin.');
      return;
    }
    setBusy(true);
    setError(null);
    try {
      const result = await setFreeze(!freeze.isFrozen, reason.trim());
      onChanged(result.state);
      setReason('');
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Freeze durumu değiştirilemedi.');
    } finally {
      setBusy(false);
    }
  }

  return (
    <section className="card">
      <AdminSectionTitle>Operasyonel dondurma</AdminSectionTitle>
      {freeze ? (
        <>
          <div className="summary-row">
            <span className="muted">Durum</span>
            <span className={`badge ${freeze.isFrozen ? 'badge-warning' : 'badge-success'}`}>
              {freeze.isFrozen ? 'Donduruldu' : 'Aktif'}
            </span>
          </div>
          {freeze.changedBy && (
            <div className="summary-row">
              <span className="muted">Son değişiklik</span>
              <span className="value">
                {freeze.changedBy}
                {freeze.changedAtUtc
                  ? ` · ${new Date(freeze.changedAtUtc).toLocaleString('tr-TR')}`
                  : ''}
              </span>
            </div>
          )}
          {freeze.reason && (
            <div className="summary-row">
              <span className="muted">Gerekçe</span>
              <span className="value">{freeze.reason}</span>
            </div>
          )}

          <div className="field" style={{ marginTop: 16 }}>
            <label htmlFor="freeze-reason">Gerekçe (denetim kaydına yazılır)</label>
            <input
              id="freeze-reason"
              className="text-input"
              value={reason}
              onChange={(event) => setReason(event.target.value)}
              placeholder={freeze.isFrozen ? 'Neden çözülüyor?' : 'Neden donduruluyor?'}
            />
          </div>
          <button
            className={`btn ${freeze.isFrozen ? 'btn-primary' : 'btn-danger'} btn-block`}
            type="button"
            onClick={onToggle}
            disabled={busy}
          >
            {busy ? 'İşleniyor…' : freeze.isFrozen ? 'Dondurmayı kaldır' : 'Pipeline’ı dondur'}
          </button>
          {error && (
            <div className="error" role="alert">
              {error}
            </div>
          )}
        </>
      ) : (
        <p className="muted">Yükleniyor…</p>
      )}
      <ImplNote>
        <code>GET/POST /api/operations/freeze</code> (SuperAdmin, CSRF korumalı, audit’li — ADR-034).
      </ImplNote>
    </section>
  );
}

function AdminPanel() {
  const router = useRouter();
  const { user, refresh, setUser } = useSession();

  const [freeze, setFreezeState] = useState<OperationalFreezeSnapshot | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [activating, setActivating] = useState(false);

  const loadFreeze = useCallback(async () => {
    try {
      setFreezeState(await getFreeze());
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Freeze durumu alınamadı.');
    }
  }, []);

  useEffect(() => {
    void loadFreeze();
  }, [loadFreeze]);

  async function onActivateSelf() {
    if (!user) {
      return;
    }
    setActivating(true);
    setError(null);
    try {
      await activateUser(user.userId, 'SuperAdmin self-activation for student testing');
      const me = await refresh();
      router.replace(routeForOnboardingState(me?.onboardingState ?? 'ProfileRequired'));
    } catch (err) {
      setActivating(false);
      setError(err instanceof ApiError ? err.message : 'Hesap etkinleştirilemedi.');
    }
  }

  async function onSignOut() {
    await logout();
    setUser(null);
    router.replace(ROUTES.signIn);
  }

  return (
    <AdminShell active="dashboard" operator={user?.email} isFrozen={freeze?.isFrozen ?? false}>
      <div className="cluster" style={{ justifyContent: 'space-between', gap: 12, marginBottom: 20 }}>
        <div>
          <h1 style={{ fontSize: 26 }}>Genel bakış</h1>
          <p className="muted" style={{ marginTop: 6 }}>
            SuperAdmin olarak giriş yaptın ({user?.email}). Öğrenci lisansı gerekmez.
          </p>
        </div>
        <button className="btn btn-tertiary btn-sm" type="button" onClick={onSignOut}>
          Çıkış yap
        </button>
      </div>

      {error && (
        <div className="error" role="alert" style={{ marginBottom: 16 }}>
          {error}
        </div>
      )}

      <div
        style={{
          display: 'grid',
          gridTemplateColumns: 'repeat(auto-fit, minmax(340px, 1fr))',
          gap: 20,
          alignItems: 'start',
        }}
      >
        <FreezeCard freeze={freeze} onChanged={setFreezeState} />

        {/* Pipeline order: acquisition first, then what publication review does with it.
            These two components render their own section headings. */}
        <section className="card">
          <SourceDocumentUpload />
        </section>

        <section className="card">
          <RevisionReview />
        </section>

        <section className="card">
          <AdminSectionTitle>Kendi takvimini test et</AdminSectionTitle>
          <p className="muted" style={{ fontSize: 14 }}>
            Öğrenci akışını (profil → takvim izni → senkronizasyon) kendi hesabınla test etmek için
            kendini etkinleştirebilirsin. Bu, denetlenen bir manuel etkinleştirme kaydı oluşturur.
          </p>
          <button
            className="btn btn-secondary btn-sm"
            type="button"
            onClick={onActivateSelf}
            disabled={activating}
            style={{ marginTop: 12 }}
          >
            {activating ? 'Etkinleştiriliyor…' : 'Kendi öğrenci hesabımı etkinleştir →'}
          </button>
          <ImplNote>
            <code>POST /api/admin/users/&#123;id&#125;/activate</code> (ADR-053 manuel etkinleştirme).
          </ImplNote>
        </section>
      </div>

      <div style={{ marginTop: 24 }}>
        <Banner tone="info">
          Kalan operatör yüzeyleri — finans, kullanıcı yönetimi, toplu etkinlik, kullanıcı uyarısı,
          kaynak panosu, sunucu izleme, erişim kayıtları — henüz arka uca sahip değil. Ayrıntılı
          liste ve hedef uç noktalar için <code>web/GAPS.md</code>.
        </Banner>
      </div>
    </AdminShell>
  );
}

export default function AdminPage() {
  const router = useRouter();
  const { user, loading } = useSession();

  // Role-based guard. The frontend only navigates; every admin API is enforced by
  // the SuperAdmin policy on the backend regardless of what the client renders.
  useEffect(() => {
    if (loading) {
      return;
    }
    if (!user) {
      router.replace(ROUTES.signIn);
      return;
    }
    if (user.role !== 'SuperAdmin') {
      router.replace(routeForOnboardingState(user.onboardingState));
    }
  }, [loading, user, router]);

  if (loading || !user || user.role !== 'SuperAdmin') {
    return (
      <AuthShell>
        <p className="loading-note">Yükleniyor…</p>
      </AuthShell>
    );
  }

  return <AdminPanel />;
}
