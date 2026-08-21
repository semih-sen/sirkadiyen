'use client';

import { useCallback, useEffect, useState } from 'react';
import Link from 'next/link';
import { useRouter } from 'next/navigation';
import { OnboardingGate } from '@/components/OnboardingGate';
import { useSession } from '@/components/SessionProvider';
import { ApiError, getLicenseStatus, getProfile, getScheduleChanges, getSyncProgress, getUpcomingSchedule, logout, requestReconciliation } from '@/lib/api';
import { ROUTES } from '@/lib/onboarding';
import { Banner, StudentTopbar } from '@/components/ui';
import { DepartmentColorEditor } from '@/components/DepartmentColorEditor';
import { DeleteAccountCard } from '@/components/DeleteAccountCard';
import { formatDateTime, statusBadge } from '@/components/AdminData';
import type { CalendarSyncProgressResponse, LicenseStatusResponse, StudentProfileView, UserScheduleChangeView, UserScheduleEventView } from '@/lib/types';

const PROGRAM_LABELS: Record<string, string> = { Turkish: 'Türkçe', English: 'İngilizce' };
const SELECTOR_LABELS: Record<string, string> = { practiceGroup: 'Uygulama grubu', practiceSubgroup: 'Uygulama alt grubu', anatomyGroup: 'Anatomi grubu', curriculumGroup: 'Müfredat grubu' };

function localDate(value: string) { return new Intl.DateTimeFormat('tr-TR', { dateStyle: 'full', timeZone: 'Europe/Istanbul' }).format(new Date(`${value}T12:00:00+03:00`)); }
function localTime(value?: string | null) { return value ? value.slice(0, 5) : null; }

function Dashboard() {
  const router = useRouter();
  const { user, setUser } = useSession();
  const [status, setStatus] = useState<CalendarSyncProgressResponse | null>(null);
  const [profile, setProfile] = useState<StudentProfileView | null>(null);
  const [upcoming, setUpcoming] = useState<UserScheduleEventView[] | null>(null);
  const [changes, setChanges] = useState<UserScheduleChangeView[] | null>(null);
  const [license, setLicense] = useState<LicenseStatusResponse | null>(null);
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [reconcileBusy, setReconcileBusy] = useState(false);
  const [reconcileNotice, setReconcileNotice] = useState<string | null>(null);

  const load = useCallback(<T,>(key: string, action: () => Promise<T>, apply: (value: T) => void) => {
    action().then((value) => { apply(value); setErrors((old) => { const next = { ...old }; delete next[key]; return next; }); })
      .catch((error) => setErrors((old) => ({ ...old, [key]: error instanceof ApiError ? error.message : 'Veri alınamadı.' })));
  }, []);

  useEffect(() => {
    load('status', getSyncProgress, setStatus);
    load('profile', getProfile, setProfile);
    load('upcoming', () => getUpcomingSchedule(14), setUpcoming);
    load('changes', () => getScheduleChanges(20), setChanges);
    load('license', getLicenseStatus, setLicense);
  }, [load]);

  async function onSignOut() { await logout(); setUser(null); router.replace(ROUTES.signIn); }
  async function reconcile() {
    setReconcileBusy(true); setReconcileNotice(null);
    try { await requestReconciliation(); setReconcileNotice('Onarım talebi kuyruğa alındı. Takvim denetimi arka planda güvenli biçimde yapılacak.'); }
    catch (error) {
      if (error instanceof ApiError && error.status === 429) setReconcileNotice('Saatlik onarım talebi sınırına ulaşıldı. Lütfen daha sonra yeniden deneyin.');
      else if (error instanceof ApiError && error.status === 409) setReconcileNotice('Onarım henüz kullanılamıyor. İlk senkronizasyonu tamamlayın veya Calendar yetkisini yenileyin.');
      else setReconcileNotice(error instanceof ApiError ? error.message : 'Onarım talebi gönderilemedi.');
    } finally { setReconcileBusy(false); }
  }

  const synced = status?.initialSyncState === 'Completed';
  const reconcileEligible = synced && status?.hasManagedCalendar;
  const subtitle = profile ? `${user?.displayName ?? user?.email} · Dönem ${profile.classYear}` : (user?.displayName ?? user?.email ?? undefined);

  return <div className="student-shell"><StudentTopbar subtitle={subtitle} onSignOut={onSignOut} /><main id="main" style={{ padding: '32px 0 80px' }}><div className="container">
    <Banner tone={synced ? 'info' : 'warning'}><strong>{synced ? 'Takvimin güncel.' : 'Senkronizasyon tamamlanmadı.'}</strong><div className="muted" style={{ marginTop: 2, fontSize: 13.5 }}>{errors.status ?? (synced ? `Takvimindeki yönetilen etkinlik sayısı: ${status?.mappedEventCount ?? 0}.` : `Durum: ${status?.initialSyncState ?? '—'}.`)}</div></Banner>
    <div className="dashboard-grid">
      <div className="stack" style={{ gap: 20 }}>
        <section className="card card-content"><h3 style={{ fontSize: 16 }}>Senkronizasyon görünümü</h3>{errors.status ? <p className="error">{errors.status}</p> : !status ? <p className="loading-note">Yükleniyor…</p> : <div style={{ marginTop: 10 }}>
          <div className="summary-row"><span className="muted">Takvimdeki etkinlik</span><strong>{status.mappedEventCount}</strong></div>
          <div className="summary-row"><span className="muted">İlk yazımdaki içerikte</span><strong>{status.createdEventCount}</strong></div>
          <div className="summary-row"><span className="muted">Sonradan güncellenmiş</span><strong>{status.updatedEventCount}</strong></div>
          <div className="summary-row"><span className="muted">Son takvim yazımı</span><strong>{formatDateTime(status.lastWrittenAtUtc)}</strong></div>
          <p className="muted" style={{ fontSize: 12, marginTop: 10 }}>Bu sayılar kalıcı takvim envanterini anlatır; tek bir senkronizasyon çalışmasının sonuç sayaçları değildir.</p>
        </div>}</section>
        <section className="card card-content"><h3 style={{ fontSize: 16 }}>Sıradaki dersler</h3>{errors.upcoming ? <p className="error">{errors.upcoming}</p> : upcoming === null ? <p className="loading-note">Yükleniyor…</p> : upcoming.length === 0 ? <p className="muted" style={{ marginTop: 10 }}>Önümüzdeki 14 günde yönetilen etkinlik bulunmuyor.</p> : <div style={{ marginTop: 10 }}>{upcoming.map((event) => <article className="detail-section" key={event.stableIdentity} style={{ padding: '12px 0', borderBottom: '1px solid var(--border)' }}><div className="cluster" style={{ justifyContent: 'space-between' }}><strong>{event.title}</strong><span className="badge badge-neutral">{event.eventType}</span></div><p style={{ marginTop: 5 }}>{localDate(event.localDate)} · {event.isAllDay ? 'Tüm gün' : `${localTime(event.startLocalTime)}–${localTime(event.endLocalTime)}`}</p>{(event.location || event.instructor) && <p className="muted" style={{ fontSize: 13 }}>{[event.location, event.instructor].filter(Boolean).join(' · ')}</p>}{event.departments.length > 0 && <p className="muted" style={{ fontSize: 12 }}>{event.departments.join(', ')}</p>}</article>)}</div>}</section>
        <section className="card card-content"><h3 style={{ fontSize: 16 }}>Son program değişiklikleri</h3><p className="muted" style={{ fontSize: 12, marginTop: 4 }}>Mevcut takvim kayıtlarındaki oluşturma ve güncellemeler gösterilir; silmeler bu akışta yer almaz.</p>{errors.changes ? <p className="error">{errors.changes}</p> : changes === null ? <p className="loading-note">Yükleniyor…</p> : changes.length === 0 ? <p className="muted" style={{ marginTop: 10 }}>Gösterilecek yakın tarihli değişiklik yok.</p> : <div style={{ marginTop: 10 }}>{changes.map((change) => <div className="summary-row" key={`${change.stableIdentity}-${change.changedAtUtc}`}><span><strong>{change.title}</strong><small className="muted" style={{ display: 'block' }}>{localDate(change.localDate)} · {formatDateTime(change.changedAtUtc)}</small></span><span className={`badge ${statusBadge(change.kind)}`}>{change.kind === 'Created' ? 'Oluşturuldu' : 'Güncellendi'}</span></div>)}</div>}</section>
        <section className="card card-content"><h3 style={{ fontSize: 16 }}>Bir sorun mu var?</h3><p className="muted" style={{ marginTop: 8, fontSize: 14 }}>Onarım talebi, yönetilen takvimi silmeden envanter denetimini öne alır.</p><button className="btn btn-secondary btn-sm" type="button" disabled={!reconcileEligible || reconcileBusy} onClick={() => void reconcile()} style={{ marginTop: 14 }}>{reconcileBusy ? <><span className="spinner" aria-hidden="true" />Gönderiliyor…</> : <>Onarım talebi oluştur<span aria-hidden="true">→</span></>}</button>{!reconcileEligible && <p className="muted" style={{ fontSize: 12, marginTop: 8 }}>İlk senkronizasyon ve yönetilen takvim tamamlandığında kullanılabilir.</p>}{reconcileNotice && <p role="status" style={{ marginTop: 10 }}>{reconcileNotice}</p>}</section>
        <DepartmentColorEditor mode="user" collapsible />
      </div>
      <div className="stack" style={{ gap: 20 }}>
        <section className="card card-content"><h3 style={{ fontSize: 15 }}>Akademik profil</h3>{errors.profile ? <p className="error">{errors.profile}</p> : profile ? <div style={{ marginTop: 10 }}><div className="summary-row"><span className="muted">Dönem</span><strong>{profile.classYear}</strong></div><div className="summary-row"><span className="muted">Program dili</span><strong>{PROGRAM_LABELS[profile.programLanguage] ?? profile.programLanguage}</strong></div>{Object.entries(profile.selectors).map(([key, value]) => <div className="summary-row" key={key}><span className="muted">{SELECTOR_LABELS[key] ?? key}</span><strong>{value}</strong></div>)}<p style={{ marginTop: 14 }}><Link className="btn btn-secondary btn-sm" href="/profile">Profili düzenle<span aria-hidden="true">→</span></Link></p></div> : <p className="loading-note">Yükleniyor…</p>}</section>
        <section className="card card-content"><h3 style={{ fontSize: 15 }}>Lisans erişimi</h3>{errors.license ? <p className="error">{errors.license}</p> : license ? <div style={{ marginTop: 10 }}><span className={`badge ${statusBadge(license.state)}`}>{license.state === 'Active' ? 'Aktif' : license.state === 'Suspended' ? 'Askıda' : 'Etkin değil'}</span><div className="summary-row"><span className="muted">Etkinleştirme</span><strong>{formatDateTime(license.activatedAtUtc)}</strong></div>{license.revokedAtUtc && <div className="summary-row"><span className="muted">İptal</span><strong>{formatDateTime(license.revokedAtUtc)}</strong></div>}<p className="muted" style={{ fontSize: 12, marginTop: 8 }}>Lisans tek kullanımlık hesap aktivasyonudur; abonelik süresi değildir.</p></div> : <p className="loading-note">Yükleniyor…</p>}</section>
        <section className="card card-content"><h3 style={{ fontSize: 15 }}>Google Calendar bağlantısı</h3><span className={`badge ${status?.hasManagedCalendar ? 'badge-success' : 'badge-neutral'}`} style={{ marginTop: 12 }}>{status?.hasManagedCalendar ? 'Yönetilen takvim hazır' : 'Takvim bekleniyor'}</span></section>
        <section className="card card-content" style={{ opacity: .75 }}><h3 style={{ fontSize: 15 }}>Senkron geçmişi · Bildirimler · Makaleler</h3><p className="muted" style={{ marginTop: 8, fontSize: 13 }}>Bu alanların yetkili backend sözleşmeleri henüz bulunmuyor.</p><span className="badge badge-neutral" style={{ marginTop: 10 }}>Yakında</span></section>
        <DeleteAccountCard />
      </div>
    </div>
  </div></main></div>;
}

export default function DashboardPage() { return <OnboardingGate allow={['Active']}><Dashboard /></OnboardingGate>; }
