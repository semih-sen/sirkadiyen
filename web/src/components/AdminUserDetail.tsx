'use client';

import { useCallback, useEffect, useState, type ReactNode } from 'react';
import Link from 'next/link';
import { useRouter } from 'next/navigation';
import {
  ApiError,
  activateUser,
  changeUserRole,
  deleteUser,
  getAdminUser,
  getAdminUserCalendarChanges,
  getAdminUserCalendarEvents,
  getAnnouncementOptions,
  previewUserCalendarRecheck,
  requestUserCalendarRecheck,
  rebuildUserCalendar,
  revokeLicense,
} from '@/lib/api';
import { LoadState, Tabs, formatDateTime, statusBadge } from '@/components/AdminData';
import { AdminPageHeader } from '@/components/AdminShell';
import { AnnouncementHistory } from '@/components/AnnouncementShared';
import { UserWarningForm } from '@/components/UserWarningForm';
import { Banner } from '@/components/ui';
import type {
  AdminUserCalendarEventsResponse,
  AdminUserDetailResponse,
  AnnouncementCompositionOptions,
  AuditEventView,
  CohortRepairPlan,
  UserScheduleChangeView,
} from '@/lib/types';

/**
 * One account, and every operation an operator may perform on it.
 *
 * The page is a read of authoritative backend state plus the four writes the backend actually
 * supports for a single user: manual activation (ADR-053), license revocation (ADR-022), a
 * calendar warning (ADR-107) and a calendar re-check (ADR-115). It deliberately offers nothing
 * else — an operator still cannot edit a student's academic profile, and pretending otherwise with
 * a disabled control would suggest the capability exists somewhere. The re-check is not an
 * exception: it queues convergence onto the profile as the student wrote it, and changes no field
 * of it.
 *
 * The calendar tab reads the mapping ledger, so it shows what is genuinely on the calendar
 * Sirkadiyen created for this student, not what the published schedule says should be there.
 */
export function AdminUserDetail({ userId }: { userId: string }) {
  const [detail, setDetail] = useState<AdminUserDetailResponse | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [tab, setTab] = useState('overview');

  const load = useCallback(async () => {
    setError(null);
    try {
      setDetail(await getAdminUser(userId));
    } catch (caught) {
      setError(caught instanceof ApiError ? caught.message : 'Kullanıcı alınamadı.');
    }
  }, [userId]);

  useEffect(() => { void load(); }, [load]);

  if (!detail) {
    return (
      <>
        <AdminPageHeader
          eyebrow="Kimlik & erişim"
          title="Kullanıcı"
          description="Hesabın backend kayıtlarındaki durumu."
        />
        <LoadState loading={!error} error={error} onRetry={() => void load()} />
      </>
    );
  }

  const { summary, profile, calendarConnection } = detail.user;

  return (
    <>
      <AdminPageHeader
        eyebrow="Kimlik & erişim"
        title={summary.displayName ?? summary.email}
        description={summary.email}
        actions={<Link className="btn btn-tertiary btn-sm" href="/admin/users">Listeye dön</Link>}
      />

      <div className="cluster" style={{ gap: 8, marginBottom: 16, flexWrap: 'wrap' }}>
        <span className="badge badge-neutral">{summary.role}</span>
        <span className={`badge ${statusBadge(summary.licenseState)}`}>
          Lisans: {summary.licenseState}
        </span>
        <span className={`badge ${statusBadge(detail.onboardingState)}`}>
          {detail.onboardingState}
        </span>
        {calendarConnection ? (
          <span className={`badge ${statusBadge(calendarConnection.status)}`}>
            Takvim: {calendarConnection.initialSyncState}
          </span>
        ) : (
          <span className="badge badge-neutral">Takvim bağlı değil</span>
        )}
        <span className="badge badge-neutral">{detail.user.managedEventCount} etkinlik</span>
      </div>

      {notice && <Banner tone="info">{notice}</Banner>}
      {error && <div className="error" role="alert">{error}</div>}

      <Tabs
        value={tab}
        onChange={setTab}
        items={[
          { value: 'overview', label: 'Genel' },
          { value: 'calendar', label: 'Takvim' },
          { value: 'warnings', label: 'Uyarılar' },
          { value: 'audit', label: 'Denetim' },
        ]}
      />

      {tab === 'overview' && (
        <div className="stack" style={{ gap: 18 }}>
          <Card title="Hesap">
            <Row label="Kullanıcı kimliği" value={summary.id} mono />
            <Row label="E-posta" value={summary.email} />
            <Row label="Ad" value={summary.displayName ?? '—'} />
            <Row label="Rol" value={summary.role} />
            <Row label="Onboarding" value={detail.onboardingState} />
            <Row label="Kayıt" value={formatDateTime(summary.createdAtUtc)} />
            <Row label="Son giriş" value={formatDateTime(summary.lastSignedInAtUtc)} />
          </Card>

          <Card title="Akademik profil">
            {profile ? (
              <>
                <Row label="Öğrenci numarası" value={profile.studentNumber} mono />
                <Row label="Akademik yıl" value={profile.academicYear} />
                <Row label="Dönem" value={`${profile.classYear}. dönem`} />
                <Row
                  label="Program dili"
                  value={profile.programLanguage === 'English' ? 'İngilizce' : 'Türkçe'}
                />
                <Row label="Şema sürümü" value={profile.selectorSchemaVersion} />
                {Object.entries(profile.selectors).map(([key, value]) => (
                  <Row key={key} label={key} value={value} />
                ))}
                <Row label="Son güncelleme" value={formatDateTime(profile.updatedAtUtc)} />
                <p className="muted" style={{ fontSize: 13, marginTop: 10 }}>
                  Profili yalnızca öğrencinin kendisi değiştirebilir; yönetici adına düzenleme yolu
                  henüz yok.
                </p>
              </>
            ) : (
              <p className="muted">Akademik profil henüz tamamlanmadı.</p>
            )}
          </Card>

          <Card title="Takvim bağlantısı">
            {calendarConnection ? (
              <>
                <Row label="Yetki durumu" value={calendarConnection.status} />
                <Row label="İlk senkronizasyon" value={calendarConnection.initialSyncState} />
                <Row
                  label="Yönetilen takvim"
                  value={calendarConnection.hasManagedCalendar ? 'Oluşturuldu' : 'Henüz yok'}
                />
                <Row
                  label="Yönetilen etkinlik"
                  value={String(detail.user.managedEventCount)}
                />
                <Row
                  label="Son envanter"
                  value={formatDateTime(calendarConnection.lastCalendarInventoryAtUtc)}
                />
                {calendarConnection.managedCalendarUnavailableAtUtc && (
                  <CalendarRebuild
                    userId={userId}
                    unavailableSinceUtc={calendarConnection.managedCalendarUnavailableAtUtc}
                    onRebuilt={() => void load()}
                  />
                )}
                {calendarConnection.profileResyncRequiredSinceUtc && (
                  <Banner tone="info">
                    Profil değişikliği nedeniyle yeniden senkronizasyon bekliyor
                    ({formatDateTime(calendarConnection.profileResyncRequiredSinceUtc)}).
                  </Banner>
                )}
                {calendarConnection.reconciliationRequiredSinceUtc && (
                  <Banner tone="info">
                    Kesilen yetki nedeniyle kaçırılan diff&apos;lerin tekrar oynatılması bekliyor
                    ({formatDateTime(calendarConnection.reconciliationRequiredSinceUtc)}).
                  </Banner>
                )}
              </>
            ) : (
              <p className="muted">
                Bu hesap Google Takvim yetkisi vermemiş. Yetkilendirmeyi yalnızca öğrenci yapabilir.
              </p>
            )}
          </Card>

          <Licenses detail={detail} onChanged={load} onNotice={setNotice} />
          <Activation detail={detail} onChanged={load} onNotice={setNotice} />
          <RoleCard detail={detail} onChanged={load} onNotice={setNotice} />
          <DeleteAccount detail={detail} />
        </div>
      )}

      {tab === 'calendar' && <CalendarTab userId={userId} />}

      {tab === 'warnings' && (
        <WarningsTab userId={userId} onNotice={setNotice} />
      )}

      {tab === 'audit' && (
        <div className="stack" style={{ gap: 18 }}>
          <Card title="Son girişler">
            <AuditTable events={detail.recentSignIns} empty="Giriş kaydı yok." />
          </Card>
          <Card title="Son etkinlik (tüm kategoriler)">
            <AuditTable events={detail.recentActivity} empty="Denetim kaydı yok." />
            <p className="muted" style={{ fontSize: 13, marginTop: 10 }}>
              Bu liste yalnızca kullanıcının kendi yaptığı işlemleri gösterir. Yöneticinin bu hesap
              üzerinde yaptığı işlemler{' '}
              <Link className="link" href="/admin/access-logs">erişim kayıtlarında</Link> aranır.
            </p>
          </Card>
        </div>
      )}
    </>
  );
}

/** The license history, and the one write it supports: revoking an active license. */
/**
 * The operator's door to the calendar rebuild the student also has (ADR-116), for the student who
 * does not find theirs or writes in instead.
 *
 * Until this existed the panel could only describe the dead end — the banner said the calendar was
 * unreachable and would never be recreated, and that was the whole of it. Everything destructive
 * about the action is stated before it is offered: the ledger goes, the deleted calendar's old
 * events do not come back, and nothing is written until the student starts their synchronization.
 */
function CalendarRebuild({
  userId,
  unavailableSinceUtc,
  onRebuilt,
}: {
  userId: string;
  unavailableSinceUtc: string;
  onRebuilt: () => void;
}) {
  const [confirming, setConfirming] = useState(false);
  const [reason, setReason] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  async function onRebuild() {
    if (!reason.trim()) { setError('Denetim kaydı için bir gerekçe yazın.'); return; }
    setBusy(true); setError(null);
    try {
      const result = await rebuildUserCalendar(userId, reason.trim());
      setNotice(
        `Bağlantı ilk senkronizasyon durumuna alındı; ${result.discardedMappings} eşleşme kaydı `
        + 'silindi. Takvim, kullanıcı senkronizasyonu başlattığında yeniden kurulur.',
      );
      setConfirming(false);
      setReason('');
      onRebuilt();
    } catch (caught) {
      setError(caught instanceof ApiError ? caught.message : 'Takvim yeniden kurulamadı.');
    } finally { setBusy(false); }
  }

  return (
    <>
      <Banner tone="warning">
        Yönetilen takvim {formatDateTime(unavailableSinceUtc)} tarihinden beri erişilemiyor —
        kullanıcı büyük ihtimalle silmiş. Bu hâlde her yazardan düşer ve onboarding&apos;de
        &ldquo;işlem gerekli&rdquo; görür. Takvim asla kendiliğinden yeniden oluşturulmaz.
      </Banner>

      {!confirming && (
        <button
          className="btn btn-secondary btn-sm"
          type="button"
          style={{ marginTop: 10 }}
          onClick={() => setConfirming(true)}
        >
          Takvimi yeniden kur
        </button>
      )}

      {confirming && (
        <div style={{ marginTop: 12 }}>
          <Banner tone="danger">
            <strong>Bu kullanıcının eşleşme defteri tamamen silinir.</strong> Defter artık var
            olmayan bir takvimi tarif ediyor, o yüzden hiçbir takvim etkinliği silinmiş olmuyor —
            ama silinen takvimdeki eski etkinlikler de geri gelmez. Bağlantı ilk senkronizasyon
            durumuna döner; <strong>yazmayı kullanıcı başlatır</strong>, bu düğme değil.
          </Banner>
          <div className="field" style={{ marginTop: 12 }}>
            <label htmlFor="rebuild-reason">Gerekçe</label>
            <textarea
              id="rebuild-reason"
              className="text-input"
              value={reason}
              onChange={(event) => setReason(event.target.value)}
              placeholder="Bu yeniden kurma neden gerekli? (denetim kaydına yazılır)"
            />
          </div>
          <div className="cluster">
            <button
              className="btn btn-danger btn-sm"
              type="button"
              disabled={busy || !reason.trim()}
              onClick={() => void onRebuild()}
            >
              {busy ? 'İşleniyor…' : 'Takvimi yeniden kur'}
            </button>
            <button
              className="btn btn-tertiary btn-sm"
              type="button"
              disabled={busy}
              onClick={() => { setConfirming(false); setReason(''); }}
            >
              Vazgeç
            </button>
          </div>
        </div>
      )}

      {notice && <Banner tone="info">{notice}</Banner>}
      {error && <div className="error" role="alert">{error}</div>}
    </>
  );
}

function Licenses({
  detail,
  onChanged,
  onNotice,
}: {
  detail: AdminUserDetailResponse;
  onChanged: () => Promise<void>;
  onNotice: (message: string) => void;
}) {
  const [reason, setReason] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const active = detail.user.licenses.find((license) => license.status === 'Redeemed');

  async function revoke() {
    if (!active || !reason.trim()) return;
    setBusy(true);
    setError(null);
    try {
      await revokeLicense(active.licenseId, reason.trim());
      setReason('');
      onNotice('Lisans iptal edildi. Yazılmış takvim etkinlikleri korunur; yeni senkronizasyon durur.');
      await onChanged();
    } catch (caught) {
      setError(caught instanceof ApiError ? caught.message : 'Lisans iptal edilemedi.');
    } finally {
      setBusy(false);
    }
  }

  return (
    <Card title="Lisans geçmişi">
      {detail.user.licenses.length === 0 ? (
        <p className="muted">Lisans kaydı yok.</p>
      ) : (
        <div className="table-wrap">
          <table className="data-table data-table--stack">
            <thead>
              <tr><th>Kimlik</th><th>Tür</th><th>Durum</th><th>Kullanım</th><th>İptal</th></tr>
            </thead>
            <tbody>
              {detail.user.licenses.map((license) => (
                <tr key={license.licenseId}>
                  <td className="mono" data-label="Kimlik">{license.licenseId}</td>
                  <td data-label="Tür">{license.kind}</td>
                  <td data-label="Durum">
                    <span className={`badge ${statusBadge(license.status)}`}>{license.status}</span>
                  </td>
                  <td data-label="Kullanım">{formatDateTime(license.redeemedAtUtc)}</td>
                  <td data-label="İptal">{formatDateTime(license.revokedAtUtc)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {active && (
        <div style={{ marginTop: 14, borderTop: '1px solid var(--border)', paddingTop: 14 }}>
          <p className="muted" style={{ fontSize: 13 }}>
            İptal, hesabın senkronizasyonunu durdurur. Takvime yazılmış etkinlikler silinmez
            (ADR-022) ve öğrenciye bu durum ayrıca bildirilmez.
          </p>
          <label htmlFor="revoke-reason">İptal gerekçesi (denetim kaydına yazılır)</label>
          <input
            className="text-input"
            id="revoke-reason"
            value={reason}
            onChange={(event) => setReason(event.target.value)}
            placeholder="Öğrenci kaydını dondurdu."
          />
          <button
            className="btn btn-danger"
            type="button"
            disabled={busy || !reason.trim()}
            onClick={() => void revoke()}
          >
            {busy ? 'İptal ediliyor…' : 'Lisansı iptal et'}
          </button>
          {error && <div className="error" role="alert">{error}</div>}
        </div>
      )}
    </Card>
  );
}

/**
 * Manual activation (ADR-053): activating an account without a license code, on a named
 * operator's authority and with a required reason.
 */
function Activation({
  detail,
  onChanged,
  onNotice,
}: {
  detail: AdminUserDetailResponse;
  onChanged: () => Promise<void>;
  onNotice: (message: string) => void;
}) {
  const [reason, setReason] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const alreadyActive = detail.user.summary.licenseState === 'Active';

  async function activate() {
    if (!reason.trim()) return;
    setBusy(true);
    setError(null);
    try {
      const result = await activateUser(detail.user.summary.id, reason.trim());
      setReason('');
      onNotice(result.outcome === 'AlreadyActivated'
        ? 'Hesap zaten etkindi; yeni bir lisans oluşturulmadı.'
        : 'Hesap etkinleştirildi. Öğrenci akademik profilinden devam edebilir.');
      await onChanged();
    } catch (caught) {
      setError(caught instanceof ApiError ? caught.message : 'Hesap etkinleştirilemedi.');
    } finally {
      setBusy(false);
    }
  }

  return (
    <Card title="Hesabı elle etkinleştir">
      {alreadyActive ? (
        <p className="muted">
          Hesap etkin bir lisansa sahip. Elle etkinleştirme yalnızca lisansı olmayan veya iptal
          edilmiş bir hesap için anlamlıdır.
        </p>
      ) : (
        <>
          <p className="muted" style={{ fontSize: 13 }}>
            Lisans kodu olmadan, kodlu bir lisansla aynı sonucu veren denetimli bir etkinleştirme
            oluşturur. Gerekçe zorunludur ve kaydedilir.
          </p>
          <label htmlFor="activate-reason">Gerekçe (denetim kaydına yazılır)</label>
          <input
            className="text-input"
            id="activate-reason"
            value={reason}
            onChange={(event) => setReason(event.target.value)}
            placeholder="Öğrenci işleri kodun ulaşmadığını bildirdi."
          />
          <button
            className="btn btn-primary"
            type="button"
            disabled={busy || !reason.trim()}
            onClick={() => void activate()}
          >
            {busy ? 'Etkinleştiriliyor…' : 'Hesabı etkinleştir'}
          </button>
          {error && <div className="error" role="alert">{error}</div>}
        </>
      )}
    </Card>
  );
}

const WINDOW_PRESETS: { label: string; days: number }[] = [
  { label: '7 gün', days: 7 },
  { label: '30 gün', days: 30 },
  { label: '90 gün', days: 90 },
];

/** What the mapping ledger says is on this user's managed calendar. */
/**
 * The per-user calendar re-check (ADR-115): the cohort repair narrowed to one student.
 *
 * It answers the question an operator actually has while looking at one person — "is this
 * calendar right, and if not, fix it" — without authorizing a whole cohort. Everything that makes
 * a cohort repair safe still applies: the preview is the backend's plan, the `planHash` binds the
 * confirmation to it, a reason is recorded before anything is queued, and the freeze fails closed.
 * No calendar is written here; the worker's convergence pass does every mutation.
 */
function CalendarRecheck({ userId, onConverged }: { userId: string; onConverged: () => void }) {
  const [plan, setPlan] = useState<CohortRepairPlan | null>(null);
  const [reason, setReason] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  async function preview() {
    setBusy(true); setError(null); setNotice(null);
    try {
      setPlan(await previewUserCalendarRecheck(userId));
    } catch (caught) {
      setPlan(null);
      setError(caught instanceof ApiError ? caught.message : 'Ön izleme alınamadı.');
    } finally { setBusy(false); }
  }

  async function confirm() {
    if (!plan || !reason.trim()) { setError('Denetim kaydı için bir gerekçe yazın.'); return; }
    setBusy(true); setError(null);
    try {
      const result = await requestUserCalendarRecheck(userId, plan.planHash, reason.trim());
      setNotice(
        result.outcome === 'NothingToRepair'
          ? 'Bu takvimde düzeltilecek bir şey kalmamış.'
          : 'Takvim yakınsama için işaretlendi. Silme ve yazma işlemlerini worker sırayla yapar; '
            + 'bu ekran onları beklemez.',
      );
      setPlan(null);
      setReason('');
      onConverged();
    } catch (caught) {
      setPlan(null);
      setError(caught instanceof ApiError ? caught.message : 'Yeniden eşitleme talebi oluşturulamadı.');
    } finally { setBusy(false); }
  }

  const user = plan?.users[0];
  const nothingToDo = plan !== null && user === undefined;

  return (
    <Card title="Takvimi yeniden kontrol et">
      <p className="muted" style={{ fontSize: 13 }}>
        Bu öğrencinin takvimini yayımlanmış programla karşılaştırır: kendisine ait olup takviminde
        olmayan dersleri ve hâlâ yayında olup artık kendisine ait olmayan etkinlikleri sayar. Hiçbir
        şeyi bu ekran yazmaz; onaylarsanız takvim yakınsama sırasına alınır.
      </p>

      <div className="cluster" style={{ marginTop: 12 }}>
        <button
          className="btn btn-secondary btn-sm"
          type="button"
          disabled={busy}
          onClick={() => void preview()}
        >
          {busy && !plan ? 'Karşılaştırılıyor…' : 'Farkı hesapla'}
        </button>
      </div>

      {plan && (
        <div style={{ marginTop: 14 }}>
          <p className="muted" style={{ fontSize: 12 }}>
            Karşılaştırma kapsamı: <strong>{plan.scope.academicYear}</strong>, Dönem
            {' '}{plan.scope.classYear}, {plan.scope.programLanguage === 'Turkish' ? 'Türkçe' : 'İngilizce'}
            {' '}— öğrencinin kendi profilinden okundu.
          </p>
          {nothingToDo ? (
            <Banner tone="info">
              Takvim yayımlanmış programla uyumlu. Yakınsanacak bir şey yok.
            </Banner>
          ) : (
            <div className="table-wrap" style={{ marginTop: 10 }}>
              <table className="data-table data-table--stack">
                <thead>
                  <tr>
                    <th>Silinecek</th>
                    <th>Yazılacak</th>
                    <th>Dokunulmayan</th>
                  </tr>
                </thead>
                <tbody>
                  <tr>
                    <td>{user!.surplusEventCount}</td>
                    <td>{user!.missingEventCount}</td>
                    <td>{user!.untouchableRetiredCount}</td>
                  </tr>
                </tbody>
              </table>
              <p className="muted" style={{ fontSize: 12, marginTop: 8 }}>
                &ldquo;Dokunulmayan&rdquo;, dersi artık yayında olmayan kayıtlardır. Yokluktan
                silmek yasaktır (ADR-089); yalnızca raporlanır.
              </p>
            </div>
          )}
        </div>
      )}

      {plan && !nothingToDo && (
        <div style={{ borderTop: '1px solid var(--border)', paddingTop: 14, marginTop: 14 }}>
          <div className="field">
            <label htmlFor="recheck-reason">Gerekçe</label>
            <textarea
              id="recheck-reason"
              className="text-input"
              value={reason}
              onChange={(event) => setReason(event.target.value)}
              placeholder="Bu yeniden eşitleme neden gerekli? (denetim kaydına yazılır)"
            />
          </div>
          <div className="cluster">
            <button
              className="btn btn-danger btn-sm"
              type="button"
              disabled={busy || !reason.trim()}
              onClick={() => void confirm()}
            >
              {busy ? 'İşleniyor…' : 'Takvimi yeniden eşitle'}
            </button>
            <button
              className="btn btn-tertiary btn-sm"
              type="button"
              disabled={busy}
              onClick={() => { setPlan(null); setReason(''); }}
            >
              Vazgeç
            </button>
          </div>
        </div>
      )}

      {notice && <Banner tone="info">{notice}</Banner>}
      {error && <div className="error" role="alert">{error}</div>}
    </Card>
  );
}

function CalendarTab({ userId }: { userId: string }) {
  const [from, setFrom] = useState(() => todayInIstanbul());
  const [to, setTo] = useState(() => addDays(todayInIstanbul(), 30));
  const [events, setEvents] = useState<AdminUserCalendarEventsResponse | null>(null);
  const [changes, setChanges] = useState<UserScheduleChangeView[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const [loadedEvents, loadedChanges] = await Promise.all([
        getAdminUserCalendarEvents(userId, { from, to, limit: 500 }),
        getAdminUserCalendarChanges(userId, 20),
      ]);
      setEvents(loadedEvents);
      setChanges(loadedChanges);
    } catch (caught) {
      setError(caught instanceof ApiError ? caught.message : 'Takvim okunamadı.');
    } finally {
      setLoading(false);
    }
  }, [userId, from, to]);

  useEffect(() => { void load(); }, [load]);

  return (
    <div className="stack" style={{ gap: 18 }}>
      <CalendarRecheck userId={userId} onConverged={() => void load()} />
      <Card title="Takvimdeki dersler">
        <p className="muted" style={{ fontSize: 13 }}>
          Bu liste, öğrencinin takvimine gerçekten yazılmış etkinliklerin kaydından okunur —
          yayımlanmış programın ne söylediğinden değil. Silinen bir etkinlik burada görünmez, çünkü
          kayıt yalnızca takvimde duran etkinlikleri tutar.
        </p>
        <div className="cluster" style={{ gap: 10, alignItems: 'flex-end', margin: '12px 0' }}>
          <div>
            <label htmlFor="calendar-from" style={{ fontSize: 12 }}>Başlangıç</label>
            <input
              className="text-input"
              id="calendar-from"
              type="date"
              value={from}
              onChange={(event) => setFrom(event.target.value)}
            />
          </div>
          <div>
            <label htmlFor="calendar-to" style={{ fontSize: 12 }}>Bitiş</label>
            <input
              className="text-input"
              id="calendar-to"
              type="date"
              value={to}
              onChange={(event) => setTo(event.target.value)}
            />
          </div>
          {WINDOW_PRESETS.map((preset) => (
            <button
              key={preset.days}
              className="btn btn-secondary btn-sm"
              type="button"
              onClick={() => {
                const today = todayInIstanbul();
                setFrom(today);
                setTo(addDays(today, preset.days));
              }}
            >
              {preset.label}
            </button>
          ))}
        </div>

        <LoadState
          loading={loading}
          error={error}
          empty={!loading && events?.events.length === 0}
          onRetry={() => void load()}
        />

        {!loading && events && events.events.length > 0 && (
          <>
            <div className="table-wrap">
              <table className="data-table data-table--stack">
                <thead>
                  <tr>
                    <th>Tarih</th><th>Saat</th><th>Ders</th><th>Tür</th><th>Yer</th><th>Öğretim üyesi</th>
                  </tr>
                </thead>
                <tbody>
                  {events.events.map((event) => (
                    <tr key={event.stableIdentity}>
                      <td data-label="Tarih">{event.localDate}</td>
                      <td data-label="Saat">
                        {event.isAllDay
                          ? 'Tüm gün'
                          : `${event.startLocalTime?.slice(0, 5) ?? ''}–${event.endLocalTime?.slice(0, 5) ?? ''}`}
                      </td>
                      <td data-label="Ders">
                        {event.title}
                        {event.departments.length > 0 && (
                          <small className="muted" style={{ display: 'block' }}>
                            {event.departments.join(' · ')}
                          </small>
                        )}
                      </td>
                      <td data-label="Tür">{event.eventType}</td>
                      <td data-label="Yer">{event.location ?? '—'}</td>
                      <td data-label="Öğretim üyesi">{event.instructor ?? '—'}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
            <p className="muted" style={{ marginTop: 12, fontSize: 12 }}>
              {events.events.length} etkinlik · {events.fromLocalDate} – {events.toLocalDate} ·{' '}
              {events.timeZoneId}
            </p>
          </>
        )}
      </Card>

      <Card title="Son değişiklikler">
        {changes && changes.length > 0 ? (
          <div className="table-wrap">
            <table className="data-table data-table--stack">
              <thead><tr><th>Ders</th><th>Tarih</th><th>Değişiklik</th><th>Zaman</th></tr></thead>
              <tbody>
                {changes.map((change) => (
                  <tr key={change.stableIdentity}>
                    <td data-label="Ders">{change.title}</td>
                    <td data-label="Tarih">{change.localDate}</td>
                    <td data-label="Değişiklik">{change.kind === 'Created' ? 'Oluşturuldu' : 'Güncellendi'}</td>
                    <td data-label="Zaman">{formatDateTime(change.changedAtUtc)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        ) : (
          <p className="muted">Kayıtlı değişiklik yok.</p>
        )}
        <p className="muted" style={{ fontSize: 12, marginTop: 10 }}>
          Silmeler burada görünmez: kayıt defteri yalnızca takvimde duran etkinlikleri tutar.
        </p>
      </Card>
    </div>
  );
}

/** Composing a warning for this account, and every warning already addressed to it. */
function WarningsTab({
  userId,
  onNotice,
}: {
  userId: string;
  onNotice: (message: string) => void;
}) {
  const [options, setOptions] = useState<AnnouncementCompositionOptions | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [reloadToken, setReloadToken] = useState(0);

  const load = useCallback(async () => {
    setError(null);
    try {
      setOptions(await getAnnouncementOptions());
    } catch (caught) {
      setError(caught instanceof ApiError ? caught.message : 'Seçenekler yüklenemedi.');
    }
  }, []);

  useEffect(() => { void load(); }, [load]);

  return (
    <div className="stack" style={{ gap: 18 }}>
      <Card title="Yeni uyarı">
        {options ? (
          <UserWarningForm
            userId={userId}
            options={options}
            headingLevel="h3"
            onSent={(message) => {
              onNotice(message);
              setReloadToken((token) => token + 1);
            }}
          />
        ) : (
          <LoadState loading={!error} error={error} onRetry={() => void load()} />
        )}
      </Card>

      <Card title="Bu hesaba gönderilen uyarılar">
        <AnnouncementHistory
          kind="UserWarning"
          targetUserId={userId}
          reloadToken={reloadToken}
        />
      </Card>
    </div>
  );
}

function AuditTable({ events, empty }: { events: AuditEventView[]; empty: string }) {
  if (events.length === 0) return <p className="muted">{empty}</p>;
  return (
    <div className="table-wrap">
      <table className="data-table data-table--stack">
        <thead><tr><th>Kategori</th><th>Zaman</th><th>IP</th><th>Gerekçe</th></tr></thead>
        <tbody>
          {events.map((event) => (
            <tr key={event.id}>
              <td data-label="Kategori">{event.category}</td>
              <td data-label="Zaman">{formatDateTime(event.occurredAtUtc)}</td>
              <td className="mono" data-label="IP">{event.maskedIp ?? '—'}</td>
              <td data-label="Gerekçe">{event.reason ?? '—'}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

/**
 * Changes the account's authorization role (ADR-119): promote a user to operator, or remove operator
 * rights. A reason is required and audited. The backend refuses changing your own role and demoting
 * the bootstrap operator; those come back as a 409 shown here.
 */
function RoleCard({
  detail,
  onChanged,
  onNotice,
}: {
  detail: AdminUserDetailResponse;
  onChanged: () => Promise<void>;
  onNotice: (message: string) => void;
}) {
  const { summary } = detail.user;
  const isOperator = summary.role === 'SuperAdmin';
  const targetRole: 'User' | 'SuperAdmin' = isOperator ? 'User' : 'SuperAdmin';
  const [open, setOpen] = useState(false);
  const [reason, setReason] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function apply() {
    if (!reason.trim()) return;
    setBusy(true);
    setError(null);
    try {
      await changeUserRole(summary.id, targetRole, reason.trim());
      onNotice(isOperator ? 'Yöneticilik kaldırıldı.' : 'Kullanıcı yönetici (SuperAdmin) yapıldı.');
      setOpen(false);
      setReason('');
      await onChanged();
    } catch (caught) {
      setError(caught instanceof ApiError ? caught.message : 'Rol değiştirilemedi.');
    } finally {
      setBusy(false);
    }
  }

  return (
    <Card title="Yetki (rol)">
      <Row label="Geçerli rol" value={isOperator ? 'Yönetici (SuperAdmin)' : 'Kullanıcı'} />
      <p className="muted" style={{ fontSize: 13, marginTop: 8 }}>
        {isOperator
          ? 'Bu hesap yönetici. Yöneticilik, panelin tüm işlemlerine erişim verir.'
          : 'Bu hesabı yönetici yapmak, panelin tüm işlemlerine (kullanıcı silme, lisans, freeze, finans) erişim verir.'}
      </p>

      {!open ? (
        <button
          className="btn btn-secondary btn-sm"
          type="button"
          style={{ marginTop: 12 }}
          onClick={() => setOpen(true)}
        >
          {isOperator ? 'Yöneticiliği kaldır' : 'Yönetici (SuperAdmin) yap'}
        </button>
      ) : (
        <div style={{ marginTop: 12, borderTop: '1px solid var(--border)', paddingTop: 14 }}>
          <label htmlFor="role-reason">Rol değişikliği gerekçesi (denetim kaydına yazılır)</label>
          <textarea
            id="role-reason"
            className="input"
            rows={2}
            value={reason}
            onChange={(event) => setReason(event.target.value)}
            style={{ marginTop: 6 }}
          />
          {error && <p className="error" style={{ marginTop: 8 }}>{error}</p>}
          <div className="cluster" style={{ marginTop: 14, gap: 10 }}>
            <button
              className="btn btn-primary btn-sm"
              type="button"
              disabled={busy || !reason.trim()}
              onClick={() => void apply()}
            >
              {busy ? 'Uygulanıyor…' : isOperator ? 'Yöneticiliği kaldır' : 'Yönetici yap'}
            </button>
            <button
              className="btn btn-tertiary btn-sm"
              type="button"
              disabled={busy}
              onClick={() => { setOpen(false); setReason(''); setError(null); }}
            >
              Vazgeç
            </button>
          </div>
        </div>
      )}
    </Card>
  );
}

/**
 * The operator's permanent-deletion danger zone (ADR-118). Refused for a SuperAdmin; otherwise it
 * requires a reason (audited) and the target account's own e-mail as a confirmation phrase before
 * the delete button is enabled. On success the operator is returned to the directory.
 */
function DeleteAccount({ detail }: { detail: AdminUserDetailResponse }) {
  const router = useRouter();
  const { summary } = detail.user;
  const isSuperAdmin = summary.role === 'SuperAdmin';
  const [open, setOpen] = useState(false);
  const [reason, setReason] = useState('');
  const [confirmEmail, setConfirmEmail] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const matches = confirmEmail.trim().toLowerCase() === summary.email.toLowerCase();
  const ready = matches && reason.trim().length > 0;

  async function onDelete() {
    if (!ready) return;
    setBusy(true);
    setError(null);
    try {
      await deleteUser(summary.id, reason.trim(), confirmEmail.trim());
      router.replace('/admin/users?deleted=1');
    } catch (caught) {
      setError(caught instanceof ApiError ? caught.message : 'Hesap silinemedi.');
      setBusy(false);
    }
  }

  return (
    <Card title="Hesabı sil">
      <p className="muted" style={{ fontSize: 13 }}>
        Hesabı ve öğrenciye ait kişisel verileri (profil, takvim bağlantısı, etkinlik defteri, renk
        tercihleri) kalıcı olarak siler. Google&apos;daki yönetilen takvim ve yetki mümkünse
        kaldırılır; denetim kaydı öğrenciyi anonimleştirerek korunur. İşlem geri alınamaz.
      </p>

      {isSuperAdmin ? (
        <Banner tone="warning">
          Yönetici hesabı bu akıştan silinemez. Gerekiyorsa önce rol değiştirilmelidir.
        </Banner>
      ) : !open ? (
        <button
          className="btn btn-secondary btn-sm"
          type="button"
          style={{ marginTop: 12, color: 'var(--danger, #c0392b)', borderColor: 'var(--danger, #c0392b)' }}
          onClick={() => setOpen(true)}
        >
          Hesabı silmek istiyorum
        </button>
      ) : (
        <div style={{ marginTop: 12, borderTop: '1px solid var(--border)', paddingTop: 14 }}>
          <label htmlFor="delete-reason">Silme gerekçesi (denetim kaydına yazılır)</label>
          <textarea
            id="delete-reason"
            className="input"
            rows={2}
            value={reason}
            onChange={(event) => setReason(event.target.value)}
            style={{ marginTop: 6 }}
          />
          <label htmlFor="delete-confirm-email" style={{ display: 'block', marginTop: 12 }}>
            Onaylamak için hesabın e-postasını yaz: <strong>{summary.email}</strong>
          </label>
          <input
            id="delete-confirm-email"
            className="input"
            type="email"
            autoComplete="off"
            value={confirmEmail}
            onChange={(event) => setConfirmEmail(event.target.value)}
            placeholder={summary.email}
            style={{ marginTop: 6, maxWidth: 340 }}
          />
          {error && <p className="error" style={{ marginTop: 8 }}>{error}</p>}
          <div className="cluster" style={{ marginTop: 14, gap: 10 }}>
            <button
              className="btn btn-primary btn-sm"
              type="button"
              disabled={!ready || busy}
              style={ready ? { background: 'var(--danger, #c0392b)', borderColor: 'var(--danger, #c0392b)' } : undefined}
              onClick={() => void onDelete()}
            >
              {busy ? 'Siliniyor…' : 'Hesabı kalıcı olarak sil'}
            </button>
            <button
              className="btn btn-tertiary btn-sm"
              type="button"
              disabled={busy}
              onClick={() => { setOpen(false); setReason(''); setConfirmEmail(''); setError(null); }}
            >
              Vazgeç
            </button>
          </div>
        </div>
      )}
    </Card>
  );
}

function Card({ title, children }: { title: string; children: ReactNode }) {
  return (
    <section className="card admin-workspace-card">
      <h2 style={{ fontSize: 16, margin: '0 0 12px' }}>{title}</h2>
      {children}
    </section>
  );
}

function Row({ label, value, mono = false }: { label: string; value: string; mono?: boolean }) {
  return (
    <div className="summary-row">
      <span className="muted">{label}</span>
      <strong className={mono ? 'mono' : undefined}>{value}</strong>
    </div>
  );
}

/** Today as an Istanbul local date — the zone the whole schedule is interpreted in. */
function todayInIstanbul(): string {
  return new Intl.DateTimeFormat('en-CA', { timeZone: 'Europe/Istanbul' }).format(new Date());
}

function addDays(localDate: string, days: number): string {
  const date = new Date(`${localDate}T00:00:00Z`);
  date.setUTCDate(date.getUTCDate() + days);
  return date.toISOString().slice(0, 10);
}
