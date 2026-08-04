'use client';

import { useEffect, useState } from 'react';
import { useRouter } from 'next/navigation';
import { useSession } from '@/components/SessionProvider';
import { AdminSectionTitle } from '@/components/AdminShell';
import {
  ApiError,
  activateUser,
  getFreeze,
  listScopedFreezes,
  setFreeze,
  setScopedFreeze,
} from '@/lib/api';
import { routeForOnboardingState } from '@/lib/onboarding';
import type { OperationalFreezeScope, OperationalFreezeSnapshot, ProgramLanguage } from '@/lib/types';

export function FreezeControl() {
  const [freeze, setSnapshot] = useState<OperationalFreezeSnapshot | null>(null);
  const [scoped, setScoped] = useState<OperationalFreezeSnapshot[]>([]);
  const [classYear, setClassYear] = useState(1);
  const [programLanguage, setProgramLanguage] = useState<ProgramLanguage>('Turkish');
  const [reason, setReason] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    void Promise.all([getFreeze(), listScopedFreezes()])
      .then(([globalState, scopedStates]) => {
        setSnapshot(globalState);
        setScoped(scopedStates);
      })
      .catch((caught) => setError(caught instanceof ApiError ? caught.message : 'Durum alınamadı.'));
  }, []);

  const selectedScope: OperationalFreezeScope = { classYear, programLanguage };
  const selectedState = scoped.find((item) => item.scope?.classYear === classYear
    && item.scope.programLanguage === programLanguage);

  async function toggleGlobal() {
    if (!freeze || !reason.trim()) { setError('Denetim kaydı için bir gerekçe yazın.'); return; }
    setBusy(true); setError(null);
    try {
      const result = await setFreeze(!freeze.isFrozen, reason.trim());
      setSnapshot(result.state);
      setReason('');
    } catch (caught) {
      setError(caught instanceof ApiError ? caught.message : 'Global durum değiştirilemedi.');
    } finally { setBusy(false); }
  }

  async function toggleScope() {
    if (!reason.trim()) { setError('Denetim kaydı için bir gerekçe yazın.'); return; }
    setBusy(true); setError(null);
    try {
      const result = await setScopedFreeze(
        selectedScope,
        !(selectedState?.isFrozen ?? false),
        reason.trim(),
      );
      setScoped((current) => [
        ...current.filter((item) => item.scope?.classYear !== classYear
          || item.scope.programLanguage !== programLanguage),
        result.state,
      ]);
      setReason('');
    } catch (caught) {
      setError(caught instanceof ApiError ? caught.message : 'Hat durumu değiştirilemedi.');
    } finally { setBusy(false); }
  }

  return (
    <section className={`card operation-control-card ${freeze?.isFrozen ? 'is-frozen' : ''}`}>
      <div className="operation-control-head">
        <div><span className="eyebrow">Denetimli güvenlik anahtarı</span><AdminSectionTitle>Veri hatları</AdminSectionTitle></div>
        <span className={`operation-state ${freeze?.isFrozen ? 'danger' : 'healthy'}`}>{freeze?.isFrozen ? 'Global dondurma açık' : 'Global hat aktif'}</span>
      </div>
      <p className="muted">Global anahtar bütün dönem ve programları durdurur. Kapsamlı kontrol yalnız seçilen dönem/program hattını etkiler; diğer hatlar çalışmayı sürdürür.</p>
      {freeze?.reason && <div className="operation-last-change"><strong>Global son gerekçe</strong><span>{freeze.reason}</span></div>}

      <div className="grid grid-2" style={{ marginTop: 18 }}>
        <div className="field">
          <label htmlFor="freeze-class-year">Dönem</label>
          <select id="freeze-class-year" className="text-input" value={classYear} onChange={(event) => setClassYear(Number(event.target.value))}>
            {[1, 2, 3, 4, 5, 6].map((year) => <option key={year} value={year}>Dönem {year}</option>)}
          </select>
        </div>
        <div className="field">
          <label htmlFor="freeze-language">Program</label>
          <select id="freeze-language" className="text-input" value={programLanguage} onChange={(event) => setProgramLanguage(event.target.value as ProgramLanguage)}>
            <option value="Turkish">Türkçe</option>
            <option value="English">İngilizce</option>
          </select>
        </div>
      </div>

      <div className="operation-last-change">
        <strong>Dönem {classYear} · {programLanguage === 'Turkish' ? 'Türkçe' : 'İngilizce'}</strong>
        <span className={`badge ${selectedState?.isFrozen ? 'badge-danger' : 'badge-success'}`}>{selectedState?.isFrozen ? 'Donduruldu' : 'Aktif'}</span>
        {selectedState?.reason && <span>{selectedState.reason}</span>}
      </div>

      <div className="field" style={{ marginTop: 18 }}>
        <label htmlFor="freeze-reason">Değişiklik gerekçesi</label>
        <textarea id="freeze-reason" className="text-input" value={reason} onChange={(event) => setReason(event.target.value)} placeholder="Bu değişiklik neden gerekli?" />
      </div>
      <div className="cluster">
        <button className={`btn ${selectedState?.isFrozen ? 'btn-primary' : 'btn-danger'}`} type="button" disabled={busy || !freeze} onClick={() => void toggleScope()}>
          {busy ? 'İşleniyor…' : selectedState?.isFrozen ? 'Seçili hattı yeniden aç' : 'Seçili hattı dondur'}
        </button>
        <button className={`btn ${freeze?.isFrozen ? 'btn-primary' : 'btn-danger'}`} type="button" disabled={busy || !freeze} onClick={() => void toggleGlobal()}>
          {freeze?.isFrozen ? 'Tüm hatları yeniden aç' : 'Tüm hatları dondur'}
        </button>
      </div>
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
      const me = await refresh();
      router.push(routeForOnboardingState(me?.onboardingState ?? 'ProfileRequired'));
    } catch (caught) {
      setError(caught instanceof ApiError ? caught.message : 'Hesap etkinleştirilemedi.');
      setBusy(false);
    }
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
