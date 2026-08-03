'use client';

import { useEffect, useState } from 'react';
import { useRouter } from 'next/navigation';
import { useSession } from '@/components/SessionProvider';
import { AdminSectionTitle } from '@/components/AdminShell';
import { ApiError, activateUser, getFreeze, setFreeze } from '@/lib/api';
import { routeForOnboardingState } from '@/lib/onboarding';
import type { OperationalFreezeSnapshot } from '@/lib/types';

export function FreezeControl() {
  const [freeze, setSnapshot] = useState<OperationalFreezeSnapshot | null>(null);
  const [reason, setReason] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => { void getFreeze().then(setSnapshot).catch((err) => setError(err instanceof ApiError ? err.message : 'Durum alınamadı.')); }, []);

  async function toggle() {
    if (!freeze || !reason.trim()) { setError('Denetim kaydı için bir gerekçe yazın.'); return; }
    setBusy(true); setError(null);
    try {
      const result = await setFreeze(!freeze.isFrozen, reason.trim());
      setSnapshot(result.state); setReason('');
    } catch (err) { setError(err instanceof ApiError ? err.message : 'Durum değiştirilemedi.'); }
    finally { setBusy(false); }
  }

  return (
    <section className={`card operation-control-card ${freeze?.isFrozen ? 'is-frozen' : ''}`}>
      <div className="operation-control-head">
        <div><span className="eyebrow">Global güvenlik anahtarı</span><AdminSectionTitle>Veri hattı</AdminSectionTitle></div>
        <span className={`operation-state ${freeze?.isFrozen ? 'danger' : 'healthy'}`}>{freeze?.isFrozen ? 'Donduruldu' : 'Aktif'}</span>
      </div>
      <p className="muted">Acquisition, parsing, publication ve takvim işlerini tek denetimli kararla durdurur. Mevcut takvim verilerini silmez.</p>
      {freeze?.reason && <div className="operation-last-change"><strong>Son gerekçe</strong><span>{freeze.reason}</span></div>}
      <div className="field" style={{ marginTop: 18 }}>
        <label htmlFor="freeze-reason">Değişiklik gerekçesi</label>
        <textarea id="freeze-reason" className="text-input" value={reason} onChange={(event) => setReason(event.target.value)} placeholder={freeze?.isFrozen ? 'Neden yeniden açılıyor?' : 'Neden acil durdurma gerekiyor?'} />
      </div>
      <button className={`btn ${freeze?.isFrozen ? 'btn-primary' : 'btn-danger'}`} type="button" disabled={busy || !freeze} onClick={() => void toggle()}>
        {busy ? 'İşleniyor…' : freeze?.isFrozen ? 'Veri hattını yeniden aç' : 'Veri hattını dondur'}
      </button>
      {error && <div className="error" role="alert">{error}</div>}
    </section>
  );
}

export function SelfActivationCard() {
  const router = useRouter();
  const { user, refresh } = useSession();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  async function activate() {
    if (!user) return;
    setBusy(true); setError(null);
    try {
      await activateUser(user.userId, 'SuperAdmin self-activation for student testing');
      const me = await refresh(); router.push(routeForOnboardingState(me?.onboardingState ?? 'ProfileRequired'));
    } catch (err) { setError(err instanceof ApiError ? err.message : 'Hesap etkinleştirilemedi.'); setBusy(false); }
  }
  return (
    <section className="card">
      <AdminSectionTitle>Kendi öğrenci akışını test et</AdminSectionTitle>
      <p className="muted">SuperAdmin hesabını denetimli manuel aktivasyonla öğrenci onboarding akışına hazırlar.</p>
      <button className="btn btn-secondary" style={{ marginTop: 16 }} type="button" disabled={busy} onClick={() => void activate()}>{busy ? 'Etkinleştiriliyor…' : 'Hesabımı etkinleştir →'}</button>
      {error && <div className="error" role="alert">{error}</div>}
    </section>
  );
}
