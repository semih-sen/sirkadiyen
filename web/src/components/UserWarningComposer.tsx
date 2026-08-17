'use client';

import { useCallback, useEffect, useState } from 'react';
import {
  ApiError,
  createAnnouncement,
  getAdminUser,
  getAnnouncementOptions,
  listAdminUsers,
  previewAnnouncement,
} from '@/lib/api';
import { LoadState, formatDateTime } from '@/components/AdminData';
import {
  AnnouncementHistory,
  CalendarPreview,
  ConfirmPanel,
  EXCLUSION_LABELS,
} from '@/components/AnnouncementShared';
import { Banner } from '@/components/ui';
import type {
  AdminUserDetailResponse,
  AdminUserListItem,
  AnnouncementComposition,
  AnnouncementCompositionOptions,
  AnnouncementPreview,
} from '@/lib/types';

/**
 * The single-user warning workspace (ADR-107, plan §4.5, §5.12).
 *
 * It is the same domain as the bulk event with an audience of exactly one, so it inherits the
 * deterministic key, the delivery ledger and the cancel path. What differs is the identity step:
 * the operator has to see who they are writing to and what state that account is actually in
 * before choosing a template, because most warnings are *about* that state.
 *
 * The warning key is user + template + local date, so sending the same template to the same person
 * twice in one day is a replay, never a second event on their calendar.
 */
export function UserWarningComposer() {
  const [options, setOptions] = useState<AnnouncementCompositionOptions | null>(null);
  const [loadError, setLoadError] = useState<string | null>(null);

  const [search, setSearch] = useState('');
  const [results, setResults] = useState<AdminUserListItem[] | null>(null);
  const [selected, setSelected] = useState<AdminUserDetailResponse | null>(null);

  const [composition, setComposition] = useState<AnnouncementComposition | null>(null);
  const [preview, setPreview] = useState<AnnouncementPreview | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [reloadToken, setReloadToken] = useState(0);

  const load = useCallback(async () => {
    setLoadError(null);
    try {
      setOptions(await getAnnouncementOptions());
    } catch (caught) {
      setLoadError(caught instanceof ApiError ? caught.message : 'Seçenekler yüklenemedi.');
    }
  }, []);

  useEffect(() => { void load(); }, [load]);

  async function runSearch() {
    setResults(null);
    setError(null);
    try {
      const paged = await listAdminUsers({ search: search.trim(), pageSize: 10 });
      setResults(paged.items);
    } catch (caught) {
      setError(caught instanceof ApiError ? caught.message : 'Kullanıcı aranamadı.');
    }
  }

  async function select(userId: string) {
    setError(null);
    setPreview(null);
    try {
      const detail = await getAdminUser(userId);
      setSelected(detail);
      const template = options?.templates[0];
      setComposition({
        kind: 'UserWarning',
        targetUserId: userId,
        templateKey: template?.key ?? null,
        title: template?.suggestedTitle ?? '',
        body: template?.suggestedBody ?? '',
        location: null,
        isAllDay: false,
        localDate: options?.earliestLocalDate ?? '',
        startLocalTime: '09:00',
        endLocalTime: '09:15',
        reminderMinutesBefore: 0,
        categoryKey: template?.categoryKey ?? 'announcement:warning',
        internalNote: null,
      });
    } catch (caught) {
      setError(caught instanceof ApiError ? caught.message : 'Kullanıcı ayrıntısı alınamadı.');
    }
  }

  function patch(changes: Partial<AnnouncementComposition>) {
    setComposition((current) => (current ? { ...current, ...changes } : current));
    setPreview(null);
  }

  function applyTemplate(templateKey: string) {
    const template = options?.templates.find((item) => item.key === templateKey);
    if (!template) return;
    // A template is a draft, not a locked message: the title and body remain editable. Only the
    // key travels into the warning identity, which is why changing the template changes what a
    // repeat send means.
    patch({
      templateKey: template.key,
      title: template.suggestedTitle,
      body: template.suggestedBody,
      categoryKey: template.categoryKey,
    });
  }

  async function runPreview() {
    if (!composition) return;
    setBusy(true);
    setError(null);
    try {
      setPreview(await previewAnnouncement(composition));
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
        ? 'Bu uyarı bugün aynı şablonla zaten teslim edilmiş. Yeni bir etkinlik oluşturulmadı.'
        : 'Uyarı kuyruğa alındı. Takvime yazma işini arka plan görevi yapar.');
      setPreview(null);
      setSelected(null);
      setComposition(null);
      setResults(null);
      setReloadToken((token) => token + 1);
    } catch (caught) {
      setError(caught instanceof ApiError ? caught.message : 'Uyarı kuyruğa alınamadı.');
    } finally {
      setBusy(false);
    }
  }

  if (!options) {
    return <LoadState loading={!loadError} error={loadError} onRetry={() => void load()} />;
  }

  const category = options.categories.find((item) => item.key === composition?.categoryKey);

  return (
    <div>
      {notice && <Banner tone="info">{notice}</Banner>}

      <section>
        <h2 style={{ fontSize: 16 }}>1 · Kullanıcı</h2>
        <label htmlFor="warning-search">E-posta veya ad ile ara</label>
        <div className="cluster" style={{ gap: 8 }}>
          <input className="text-input"
            id="warning-search"
            value={search}
            onChange={(event) => setSearch(event.target.value)}
            onKeyDown={(event) => { if (event.key === 'Enter') void runSearch(); }}
            placeholder="ornek@ogr.iu.edu.tr"
          />
          <button className="btn btn-secondary" type="button" onClick={() => void runSearch()}>
            Ara
          </button>
        </div>

        {results?.length === 0 && (
          <p className="muted" style={{ fontSize: 13 }}>Eşleşen kullanıcı bulunamadı.</p>
        )}
        {results && results.length > 0 && (
          <ul style={{ listStyle: 'none', padding: 0, marginTop: 10 }}>
            {results.map((user) => (
              <li key={user.id} style={{ marginBottom: 6 }}>
                <button
                  className="btn btn-tertiary btn-sm"
                  type="button"
                  onClick={() => void select(user.id)}
                >
                  {user.email}
                </button>
              </li>
            ))}
          </ul>
        )}
      </section>

      {selected && composition && (
        <>
          <section style={{ marginTop: 24 }}>
            <h2 style={{ fontSize: 16 }}>2 · Hesabın durumu</h2>
            <p className="muted" style={{ fontSize: 13 }}>
              Uyarıların çoğu bu durumlar hakkındadır, bu yüzden şablon seçmeden önce burada
              gösterilir. Takvimi olmayan bir hesaba uyarı yazılamaz; önizleme bunu gerekçesiyle
              söyler.
            </p>
            <ul style={{ listStyle: 'none', padding: 0, margin: 0, fontSize: 13 }}>
              <li><strong>E-posta:</strong> {selected.user.summary.email}</li>
              <li><strong>Lisans:</strong> {selected.user.summary.licenseState}</li>
              <li><strong>Onboarding:</strong> {selected.onboardingState}</li>
              <li>
                <strong>Akademik profil:</strong>{' '}
                {selected.user.profile
                  ? `Dönem ${selected.user.profile.classYear} · ${selected.user.profile.programLanguage}`
                  : 'Yok'}
              </li>
              <li><strong>Yönetilen etkinlik:</strong> {selected.user.managedEventCount}</li>
              <li>
                <strong>Son giriş:</strong>{' '}
                {formatDateTime(selected.user.summary.lastSignedInAtUtc)}
              </li>
            </ul>
          </section>

          <section style={{ marginTop: 24 }}>
            <h2 style={{ fontSize: 16 }}>3 · Şablon ve içerik</h2>
            <label htmlFor="warning-template">Şablon</label>
            <select className="select-input"
              id="warning-template"
              value={composition.templateKey ?? ''}
              onChange={(event) => applyTemplate(event.target.value)}
            >
              {options.templates.map((template) => (
                <option key={template.key} value={template.key}>{template.name}</option>
              ))}
            </select>
            <small className="muted" style={{ display: 'block', marginTop: 4, fontSize: 12 }}>
              Şablon yalnızca bir taslaktır; metni düzenleyebilirsin. Şablon anahtarı uyarı
              kimliğine girer: aynı kullanıcıya aynı gün aynı şablonu ikinci kez göndermek yeni bir
              etkinlik oluşturmaz.
            </small>

            <label htmlFor="warning-title">Başlık</label>
            <input className="text-input"
              id="warning-title"
              value={composition.title}
              maxLength={200}
              onChange={(event) => patch({ title: event.target.value })}
            />

            <label htmlFor="warning-body">Mesaj</label>
            <textarea className="text-input"
              id="warning-body"
              rows={8}
              maxLength={4000}
              value={composition.body}
              onChange={(event) => patch({ body: event.target.value })}
            />

            <label htmlFor="warning-date">Tarih (Europe/Istanbul)</label>
            <input className="text-input"
              id="warning-date"
              type="date"
              min={options.earliestLocalDate}
              value={composition.localDate}
              onChange={(event) => patch({ localDate: event.target.value })}
            />

            <div className="cluster" style={{ gap: 12 }}>
              <div>
                <label htmlFor="warning-start">Başlangıç</label>
                <input className="text-input"
                  id="warning-start"
                  type="time"
                  value={composition.startLocalTime ?? ''}
                  onChange={(event) => patch({ startLocalTime: event.target.value })}
                />
              </div>
              <div>
                <label htmlFor="warning-end">Bitiş</label>
                <input className="text-input"
                  id="warning-end"
                  type="time"
                  value={composition.endLocalTime ?? ''}
                  onChange={(event) => patch({ endLocalTime: event.target.value })}
                />
              </div>
            </div>

            <label htmlFor="warning-reminder">Hatırlatıcı</label>
            <select className="select-input"
              id="warning-reminder"
              value={composition.reminderMinutesBefore ?? ''}
              onChange={(event) => patch({
                reminderMinutesBefore: event.target.value ? Number(event.target.value) : null,
              })}
            >
              <option value="0">Etkinlik anında</option>
              <option value="10">10 dakika önce</option>
              <option value="60">1 saat önce</option>
              <option value="">Yok</option>
            </select>

            <button
              className="btn btn-primary"
              type="button"
              onClick={() => void runPreview()}
              disabled={busy}
            >
              {busy ? 'Önizleniyor…' : 'Önizle'}
            </button>
            {error && <div className="error" role="alert">{error}</div>}
          </section>

          {preview && (
            <section style={{ marginTop: 24 }}>
              <h2 style={{ fontSize: 16 }}>4 · Önizleme ve onay</h2>
              <CalendarPreview
                composition={composition}
                categoryName={category?.name ?? composition.categoryKey}
                categoryColor={category?.backgroundColor ?? 'var(--border)'}
              />
              <small className="muted" style={{ display: 'block', marginTop: 8, fontSize: 12 }}>
                Uyarı anahtarı: <code>{preview.campaignKey}</code>
              </small>

              {preview.recipientCount === 0 && (
                <Banner tone="warning">
                  Bu kullanıcıya şu anda yazılamaz:{' '}
                  {preview.excludedRecipients[0]?.exclusionReason
                    ? EXCLUSION_LABELS[preview.excludedRecipients[0].exclusionReason]
                    : 'yönetilen takvimi yok'}
                  . Uyarıyı takvime yazmak yerine e-posta ile ulaşman gerekir.
                </Banner>
              )}

              {preview.existingAnnouncement && (
                <Banner tone="warning">
                  Bu uyarı bugün aynı şablonla zaten teslim edilmiş. Onaylamak ikinci bir etkinlik
                  oluşturmaz.
                </Banner>
              )}

              {preview.recipientCount > 0 && (
                <ConfirmPanel
                  preview={preview}
                  kind="UserWarning"
                  busy={busy}
                  onConfirm={confirm}
                />
              )}
              {error && <div className="error" role="alert">{error}</div>}
            </section>
          )}
        </>
      )}

      <hr style={{ margin: '28px 0', border: 0, borderTop: '1px solid var(--border)' }} />
      <h2 style={{ fontSize: 18 }}>Gönderilen uyarılar</h2>
      <AnnouncementHistory kind="UserWarning" reloadToken={reloadToken} />
    </div>
  );
}
