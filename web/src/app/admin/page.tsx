'use client';

import { useCallback, useEffect, useState } from 'react';
import { useRouter } from 'next/navigation';
import { useSession } from '@/components/SessionProvider';
import { activateUser, getFreeze, logout, setFreeze, ApiError } from '@/lib/api';
import { ROUTES, routeForOnboardingState } from '@/lib/onboarding';
import { RevisionReview } from '@/components/RevisionReview';
import type { OperationalFreezeSnapshot } from '@/lib/types';

function AdminPanel() {
  const router = useRouter();
  const { user, refresh, setUser } = useSession();

  const [freeze, setFreezeState] = useState<OperationalFreezeSnapshot | null>(null);
  const [reason, setReason] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
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

  async function onToggleFreeze() {
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
      setFreezeState(result.state);
      setReason('');
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Freeze durumu değiştirilemedi.');
    } finally {
      setBusy(false);
    }
  }

  async function onActivateSelf() {
    if (!user) {
      return;
    }
    setActivating(true);
    setError(null);
    try {
      await activateUser(user.userId, 'SuperAdmin self-activation for student testing');
      const me = await refresh();
      // Now activated: continue through the student flow to test sync.
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

  const pending = [
    'Kaynak (source) durum paneli',
    'Diff serbest bırakma kuyruğu',
    'Lisans yönetimi ve denetim kaydı',
    'Kullanıcı senkronizasyon durumu',
  ];

  return (
    <div className="card" style={{ maxWidth: 560 }}>
      <div className="brand">Sirkadiyen · Yönetim</div>
      <h1>Yönetim paneli</h1>
      <p className="muted">
        SuperAdmin olarak giriş yaptın ({user?.email}). Öğrenci lisansı gerekmez.
      </p>

      <h2 style={{ fontSize: 16, margin: '18px 0 8px' }}>Operasyonel dondurma</h2>
      {freeze ? (
        <>
          <div className="status-row">
            <span>Durum</span>
            <span className="value" style={{ color: freeze.isFrozen ? 'var(--danger)' : 'var(--success)' }}>
              {freeze.isFrozen ? 'DONDURULDU' : 'Aktif'}
            </span>
          </div>
          {freeze.changedBy && (
            <div className="status-row">
              <span>Son değişiklik</span>
              <span className="value">
                {freeze.changedBy}
                {freeze.changedAtUtc ? ` · ${new Date(freeze.changedAtUtc).toLocaleString('tr-TR')}` : ''}
              </span>
            </div>
          )}
          {freeze.reason && (
            <div className="status-row">
              <span>Gerekçe</span>
              <span className="value">{freeze.reason}</span>
            </div>
          )}

          <label htmlFor="reason">Gerekçe (denetim kaydına yazılır)</label>
          <input
            id="reason"
            value={reason}
            onChange={(event) => setReason(event.target.value)}
            placeholder={freeze.isFrozen ? 'Neden çözülüyor?' : 'Neden donduruluyor?'}
          />
          <button className="primary" type="button" onClick={onToggleFreeze} disabled={busy}>
            {busy
              ? 'İşleniyor…'
              : freeze.isFrozen
                ? 'Dondurmayı kaldır'
                : 'Pipeline’ı dondur'}
          </button>
        </>
      ) : (
        <p className="muted">Yükleniyor…</p>
      )}

      <RevisionReview />

      <h2 style={{ fontSize: 16, margin: '26px 0 8px' }}>Yakında</h2>
      <ul className="muted" style={{ margin: 0, paddingLeft: 18, fontSize: 14 }}>
        {pending.map((item) => (
          <li key={item}>{item}</li>
        ))}
      </ul>

      <h2 style={{ fontSize: 16, margin: '26px 0 8px' }}>Kendi takvimin</h2>
      <p className="muted">
        Öğrenci akışını (profil → takvim izni → senkronizasyon) kendi hesabınla test etmek için
        kendini etkinleştirebilirsin. Bu, denetlenen bir manuel etkinleştirme kaydı oluşturur.
      </p>
      <button className="link" type="button" onClick={onActivateSelf} disabled={activating}>
        {activating ? 'Etkinleştiriliyor…' : 'Kendi öğrenci hesabımı etkinleştir →'}
      </button>

      {error && <div className="error">{error}</div>}

      <p style={{ marginTop: 22 }}>
        <button className="link" type="button" onClick={onSignOut}>
          Çıkış yap
        </button>
      </p>
    </div>
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
      <div className="card">
        <p className="muted">Yükleniyor…</p>
      </div>
    );
  }

  return <AdminPanel />;
}
