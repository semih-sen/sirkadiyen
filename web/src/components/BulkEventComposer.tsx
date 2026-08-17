'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import {
  ApiError,
  createAnnouncement,
  getAnnouncementOptions,
  getProfileOptions,
  previewAnnouncement,
  updateAnnouncement,
} from '@/lib/api';
import { LoadState } from '@/components/AdminData';
import { DIMENSION_LABELS } from '@/components/AcademicProfileForm';
import {
  AnnouncementHistory,
  CalendarPreview,
  ConfirmPanel,
  EXCLUSION_LABELS,
  ExclusionList,
} from '@/components/AnnouncementShared';
import { Banner } from '@/components/ui';
import type {
  AnnouncementComposition,
  AnnouncementCompositionOptions,
  AnnouncementPreview,
  AnnouncementSummary,
  ProgramLanguage,
  SupportedProfileOptions,
  SupportedProfileProgram,
} from '@/lib/types';

type Step = 'audience' | 'content' | 'review';

const STEPS: { key: Step; label: string }[] = [
  { key: 'audience', label: '1 · Kitle' },
  { key: 'content', label: '2 · Etkinlik' },
  { key: 'review', label: '3 · İnceleme ve onay' },
];

function emptyComposition(academicYear: string, localDate: string): AnnouncementComposition {
  return {
    kind: 'Bulk',
    academicYear,
    classYear: null,
    programLanguage: null,
    selectors: {},
    title: '',
    body: '',
    location: null,
    isAllDay: false,
    localDate,
    startLocalTime: '12:00',
    endLocalTime: '12:30',
    reminderMinutesBefore: null,
    categoryKey: 'announcement:notice',
    internalNote: null,
  };
}

/**
 * The bulk calendar-event workspace (ADR-107, plan §4.4, §5.11).
 *
 * The six-step high-risk pattern with the server owning every step that decides anything: it
 * resolves the audience, lists the exclusions with their reasons, hashes the plan, and refuses a
 * confirmation whose plan has moved. The browser chooses nothing about who receives what.
 *
 * The screen's tone follows the plan: this is a distribution operation, not a send button. Nothing
 * here reports a delivery — queueing is what the confirmation achieves, and the delivery counters
 * come from the ledger the worker writes.
 */
export function BulkEventComposer() {
  const [options, setOptions] = useState<AnnouncementCompositionOptions | null>(null);
  const [profileOptions, setProfileOptions] = useState<SupportedProfileOptions | null>(null);
  const [loadError, setLoadError] = useState<string | null>(null);

  const [step, setStep] = useState<Step>('audience');
  const [composition, setComposition] = useState<AnnouncementComposition | null>(null);
  const [preview, setPreview] = useState<AnnouncementPreview | null>(null);
  const [editing, setEditing] = useState<AnnouncementSummary | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [reloadToken, setReloadToken] = useState(0);

  const load = useCallback(async () => {
    setLoadError(null);
    try {
      const [announcement, profile] = await Promise.all([
        getAnnouncementOptions(),
        getProfileOptions(),
      ]);
      setOptions(announcement);
      setProfileOptions(profile);
      setComposition((current) =>
        current ?? emptyComposition(profile.academicYear, announcement.earliestLocalDate));
    } catch (caught) {
      setLoadError(caught instanceof ApiError ? caught.message : 'Seçenekler yüklenemedi.');
    }
  }, []);

  useEffect(() => { void load(); }, [load]);

  const programs: SupportedProfileProgram[] = profileOptions?.programs ?? [];
  const classYears = useMemo(
    () => [...new Set(programs.map((program) => program.classYear))].sort((a, b) => a - b),
    [programs],
  );

  // The cohort dimensions are only knowable once a class year and language are chosen: the schema
  // defines them per program, and offering `anatomyGroup` to a programme that has none would let an
  // operator address a cohort that cannot exist.
  const dimensions = useMemo(() => {
    if (!composition?.classYear || !composition.programLanguage) return [];
    return programs.find((program) =>
      program.classYear === composition.classYear
      && program.programLanguage === composition.programLanguage)?.dimensions ?? [];
  }, [programs, composition?.classYear, composition?.programLanguage]);

  function patch(changes: Partial<AnnouncementComposition>) {
    setComposition((current) => (current ? { ...current, ...changes } : current));
    // Any edit invalidates the approved plan; it must be recomputed before it can be confirmed.
    setPreview(null);
  }

  async function runPreview() {
    if (!composition) return;
    setBusy(true);
    setError(null);
    try {
      setPreview(await previewAnnouncement(composition));
      setStep('review');
    } catch (caught) {
      setError(caught instanceof ApiError ? caught.message : 'Önizleme alınamadı.');
    } finally {
      setBusy(false);
    }
  }

  async function confirm(phrase: string, reason: string) {
    if (!composition || !preview) return;
    setBusy(true);
    setError(null);
    try {
      const result = await createAnnouncement({
        announcement: composition,
        planHash: preview.planHash,
        confirmationPhrase: phrase,
        reason,
      });
      setNotice(result.outcome === 'AlreadyExists'
        ? 'Bu kampanya anahtarı zaten var. Aynı kitleye ikinci bir kopya yazılmadı; mevcut duyuru aşağıda.'
        : 'Duyuru kuyruğa alındı. Takvimlere yazma işini arka plan görevi yapar; ilerlemeyi aşağıdaki sayaçlardan izleyebilirsin.');
      setPreview(null);
      setStep('audience');
      setComposition(emptyComposition(
        profileOptions?.academicYear ?? '',
        options?.earliestLocalDate ?? '',
      ));
      setReloadToken((token) => token + 1);
    } catch (caught) {
      setError(caught instanceof ApiError ? caught.message : 'Duyuru kuyruğa alınamadı.');
    } finally {
      setBusy(false);
    }
  }

  async function saveEdit(reason: string) {
    if (!composition || !editing) return;
    setBusy(true);
    setError(null);
    try {
      await updateAnnouncement(editing.announcementId, composition, reason);
      setNotice('İçerik güncellendi. Yazılmış her kopya yamalanacak; ikinci bir etkinlik oluşmaz.');
      setEditing(null);
      setStep('audience');
      setReloadToken((token) => token + 1);
    } catch (caught) {
      setError(caught instanceof ApiError ? caught.message : 'Duyuru güncellenemedi.');
    } finally {
      setBusy(false);
    }
  }

  if (!options || !profileOptions || !composition) {
    return <LoadState loading={!loadError} error={loadError} onRetry={() => void load()} />;
  }

  const category = options.categories.find((item) => item.key === composition.categoryKey)
    ?? options.categories[0];

  return (
    <div>
      {notice && <Banner tone="info">{notice}</Banner>}

      <div className="cluster" role="tablist" style={{ gap: 8, margin: '0 0 18px' }}>
        {STEPS.map((item) => (
          <button
            key={item.key}
            type="button"
            role="tab"
            aria-selected={step === item.key}
            className={`btn btn-sm ${step === item.key ? 'btn-primary' : 'btn-secondary'}`}
            onClick={() => setStep(item.key)}
            disabled={item.key === 'review' && !preview}
          >
            {item.label}
          </button>
        ))}
      </div>

      {step === 'audience' && (
        <section>
          <p className="muted" style={{ fontSize: 13, marginTop: 0 }}>
            Kitleyi akademik boyutlarla daraltırsın. Seçtiğin her boyut <em>ve</em> bağlacıyla
            uygulanır: “Dönem 2, uygulama grubu C” ikisini birden karşılayan öğrenciler demektir.
            Hesap durumu, lisans ve senkronizasyon uygunluğu burada birer filtre değildir — bunlar
            yazılacak takvimin olup olmadığını belirler ve hariç bırakma gerekçesi olarak görünür.
          </p>

          <label htmlFor="bulk-academic-year">Akademik yıl</label>
          <input className="text-input" id="bulk-academic-year" value={composition.academicYear ?? ''} readOnly />

          <label htmlFor="bulk-class-year">Dönem</label>
          <select className="select-input"
            id="bulk-class-year"
            value={composition.classYear ?? ''}
            onChange={(event) => patch({
              classYear: event.target.value ? Number(event.target.value) : null,
              selectors: {},
            })}
          >
            <option value="">Tüm dönemler</option>
            {classYears.map((year) => <option key={year} value={year}>Dönem {year}</option>)}
          </select>

          <label htmlFor="bulk-language">Program dili</label>
          <select className="select-input"
            id="bulk-language"
            value={composition.programLanguage ?? ''}
            onChange={(event) => patch({
              programLanguage: (event.target.value || null) as ProgramLanguage | null,
              selectors: {},
            })}
          >
            <option value="">Her iki program</option>
            <option value="Turkish">Türkçe</option>
            <option value="English">İngilizce</option>
          </select>

          {dimensions.map((dimension) => (
            <div key={dimension.key}>
              <label htmlFor={`bulk-selector-${dimension.key}`}>
                {DIMENSION_LABELS[dimension.key] ?? dimension.key}
              </label>
              <select className="select-input"
                id={`bulk-selector-${dimension.key}`}
                value={composition.selectors?.[dimension.key] ?? ''}
                onChange={(event) => {
                  const next = { ...(composition.selectors ?? {}) };
                  if (event.target.value) next[dimension.key] = event.target.value;
                  else delete next[dimension.key];
                  patch({ selectors: next });
                }}
              >
                <option value="">Tümü</option>
                {selectorValues(dimension, composition.selectors ?? {}).map((value) => (
                  <option key={value} value={value}>{value}</option>
                ))}
              </select>
            </div>
          ))}

          {!composition.classYear && (
            <p className="muted" style={{ fontSize: 13 }}>
              Dönem seçilmediği için grup boyutları gösterilemiyor: hangi boyutların var olduğunu
              programın kendisi belirler.
            </p>
          )}

          <button className="btn btn-primary" type="button" onClick={() => setStep('content')}>
            Devam: etkinlik ayrıntıları
          </button>
        </section>
      )}

      {step === 'content' && (
        <section>
          <label htmlFor="bulk-title">Başlık</label>
          <input className="text-input"
            id="bulk-title"
            value={composition.title}
            maxLength={200}
            onChange={(event) => patch({ title: event.target.value })}
          />

          <label htmlFor="bulk-body">Açıklama</label>
          <textarea className="text-input"
            id="bulk-body"
            rows={6}
            maxLength={4000}
            value={composition.body}
            onChange={(event) => patch({ body: event.target.value })}
          />

          <label htmlFor="bulk-date">Tarih (Europe/Istanbul)</label>
          <input className="text-input"
            id="bulk-date"
            type="date"
            min={options.earliestLocalDate}
            value={composition.localDate}
            onChange={(event) => patch({ localDate: event.target.value })}
          />

          <label>
            <input
              type="checkbox"
              checked={composition.isAllDay}
              onChange={(event) => patch({
                isAllDay: event.target.checked,
                startLocalTime: event.target.checked ? null : '12:00',
                endLocalTime: event.target.checked ? null : '12:30',
              })}
            />{' '}
            Tüm gün
          </label>

          {!composition.isAllDay && (
            <div className="cluster" style={{ gap: 12 }}>
              <div>
                <label htmlFor="bulk-start">Başlangıç</label>
                <input className="text-input"
                  id="bulk-start"
                  type="time"
                  value={composition.startLocalTime ?? ''}
                  onChange={(event) => patch({ startLocalTime: event.target.value })}
                />
              </div>
              <div>
                <label htmlFor="bulk-end">Bitiş</label>
                <input className="text-input"
                  id="bulk-end"
                  type="time"
                  value={composition.endLocalTime ?? ''}
                  onChange={(event) => patch({ endLocalTime: event.target.value })}
                />
              </div>
            </div>
          )}

          <label htmlFor="bulk-location">Konum (isteğe bağlı)</label>
          <input className="text-input"
            id="bulk-location"
            value={composition.location ?? ''}
            onChange={(event) => patch({ location: event.target.value || null })}
          />

          <label htmlFor="bulk-category">Kategori</label>
          <select className="select-input"
            id="bulk-category"
            value={composition.categoryKey}
            onChange={(event) => patch({ categoryKey: event.target.value })}
          >
            {options.categories.map((item) => (
              <option key={item.key} value={item.key}>{item.name}</option>
            ))}
          </select>

          <label htmlFor="bulk-reminder">Hatırlatıcı</label>
          <select className="select-input"
            id="bulk-reminder"
            value={composition.reminderMinutesBefore ?? ''}
            onChange={(event) => patch({
              reminderMinutesBefore: event.target.value ? Number(event.target.value) : null,
            })}
          >
            <option value="">Yok (öğrencinin kendi ayarları geçerli)</option>
            <option value="10">10 dakika önce</option>
            <option value="30">30 dakika önce</option>
            <option value="60">1 saat önce</option>
            <option value="1440">1 gün önce</option>
          </select>

          <label htmlFor="bulk-note">Dahili yönetici notu (takvime yazılmaz)</label>
          <input className="text-input"
            id="bulk-note"
            value={composition.internalNote ?? ''}
            onChange={(event) => patch({ internalNote: event.target.value || null })}
          />

          <div className="cluster" style={{ gap: 8 }}>
            <button className="btn btn-secondary" type="button" onClick={() => setStep('audience')}>
              Geri
            </button>
            {editing ? (
              <EditActions busy={busy} onSave={saveEdit} onCancel={() => { setEditing(null); }} />
            ) : (
              <button
                className="btn btn-primary"
                type="button"
                onClick={() => void runPreview()}
                disabled={busy}
              >
                {busy ? 'Kitle hesaplanıyor…' : 'Alıcıları hesapla ve önizle'}
              </button>
            )}
          </div>
          {error && <div className="error" role="alert">{error}</div>}
        </section>
      )}

      {step === 'review' && preview && (
        <section>
          <div className="cluster" style={{ gap: 24, alignItems: 'flex-start' }}>
            <div style={{ flex: '1 1 260px', minWidth: 240 }}>
              <h3 style={{ fontSize: 15 }}>Alıcılar</h3>
              <p style={{ fontSize: 28, margin: '4px 0 0', fontWeight: 700 }}>
                {preview.recipientCount}
              </p>
              <small className="muted">etkinliği alabilecek hesap</small>

              <h3 style={{ fontSize: 15, marginTop: 18 }}>Hariç bırakılanlar</h3>
              <p style={{ fontSize: 20, margin: '4px 0 0', fontWeight: 600 }}>
                {preview.excludedCount}
              </p>
              <ExclusionList groups={preview.exclusions} />
            </div>

            <div style={{ flex: '1 1 320px', minWidth: 280 }}>
              <h3 style={{ fontSize: 15 }}>Takvim önizlemesi</h3>
              <CalendarPreview
                composition={composition}
                categoryName={category?.name ?? composition.categoryKey}
                categoryColor={category?.backgroundColor ?? 'var(--border)'}
              />
              <small className="muted" style={{ display: 'block', marginTop: 8, fontSize: 12 }}>
                Kampanya anahtarı: <code>{preview.campaignKey}</code>
              </small>
            </div>
          </div>

          {preview.existingAnnouncement && (
            <Banner tone="warning">
              Bu kampanya anahtarı zaten kullanılmış: “{preview.existingAnnouncement.title}”
              duyurusu {preview.existingAnnouncement.counts.written} takvime yazılmış durumda.
              Onaylamak yeni bir kopya oluşturmaz; mevcut duyuru olduğu gibi kalır. Farklı bir
              duyuru göndermek istiyorsan başlığı veya tarihi değiştir.
            </Banner>
          )}

          {preview.recipients.length > 0 && (
            <details style={{ marginTop: 16 }}>
              <summary>Alıcı listesi ({preview.recipients.length} tanesi gösteriliyor)</summary>
              <ul style={{ fontSize: 13, paddingLeft: 18 }}>
                {preview.recipients.map((candidate) => (
                  <li key={candidate.userId}>{candidate.email}</li>
                ))}
              </ul>
            </details>
          )}

          {preview.excludedRecipients.length > 0 && (
            <details style={{ marginTop: 8 }}>
              <summary>
                Hariç bırakılanlar ve gerekçeleri ({preview.excludedRecipients.length} tanesi
                gösteriliyor)
              </summary>
              <ul style={{ fontSize: 13, paddingLeft: 18 }}>
                {preview.excludedRecipients.map((candidate) => (
                  <li key={candidate.userId}>
                    {candidate.email} —{' '}
                    {candidate.exclusionReason
                      ? EXCLUSION_LABELS[candidate.exclusionReason] ?? candidate.exclusionReason
                      : '—'}
                  </li>
                ))}
              </ul>
            </details>
          )}

          <ConfirmPanel preview={preview} kind="Bulk" busy={busy} onConfirm={confirm} />
          {error && <div className="error" role="alert">{error}</div>}
        </section>
      )}

      <hr style={{ margin: '28px 0', border: 0, borderTop: '1px solid var(--border)' }} />
      <h2 style={{ fontSize: 18 }}>Gönderilen toplu etkinlikler</h2>
      <AnnouncementHistory
        kind="Bulk"
        reloadToken={reloadToken}
        onEdit={(summary) => {
          // An edit corrects what an announcement says. It never re-resolves the audience: the
          // recipients were frozen at confirmation, so the audience step stays out of this flow.
          setEditing(summary);
          setNotice(null);
          setStep('content');
        }}
      />
      {editing && (
        <Banner tone="warning">
          “{editing.title}” duyurusunun içeriğini düzenliyorsun. Kaydettiğinde yazılmış her kopya
          yamalanır; alıcı listesi değişmez.
        </Banner>
      )}
    </div>
  );
}

function EditActions({
  busy,
  onSave,
  onCancel,
}: {
  busy: boolean;
  onSave: (reason: string) => void;
  onCancel: () => void;
}) {
  const [reason, setReason] = useState('');
  return (
    <div style={{ flex: '1 1 240px' }}>
      <label htmlFor="bulk-edit-reason">Düzenleme gerekçesi (denetim kaydına yazılır)</label>
      <input className="text-input"
        id="bulk-edit-reason"
        value={reason}
        onChange={(event) => setReason(event.target.value)}
      />
      <div className="cluster" style={{ gap: 8 }}>
        <button
          className="btn btn-primary"
          type="button"
          disabled={busy || reason.trim().length === 0}
          onClick={() => onSave(reason.trim())}
        >
          {busy ? 'Kaydediliyor…' : 'İçeriği güncelle'}
        </button>
        <button className="btn btn-tertiary" type="button" onClick={onCancel}>
          Vazgeç
        </button>
      </div>
    </div>
  );
}

function selectorValues(
  dimension: { values?: string[] | null; dependsOn?: string | null; valuesByParent?: Record<string, string[]> | null },
  selectors: Record<string, string>,
): string[] {
  if (!dimension.dependsOn) return dimension.values ?? [];
  const parent = selectors[dimension.dependsOn];
  if (!parent || !dimension.valuesByParent) return [];
  return dimension.valuesByParent[parent] ?? [];
}
