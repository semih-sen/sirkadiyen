'use client';

import { useEffect, useState } from 'react';
import { ApiError, createAnnouncement, previewAnnouncement } from '@/lib/api';
import { CalendarPreview, ConfirmPanel, EXCLUSION_LABELS } from '@/components/AnnouncementShared';
import { Banner } from '@/components/ui';
import type {
  AnnouncementComposition,
  AnnouncementCompositionOptions,
  AnnouncementPreview,
} from '@/lib/types';

/**
 * Composing, previewing and confirming a warning addressed to one already-chosen account.
 *
 * It is shared by the standalone `/admin/user-warning` workspace and the account detail page, so
 * the confirmation path — server-computed plan, binding `planHash`, hand-typed phrase, required
 * reason — exists once. Duplicating it per screen would mean two copies of the only control that
 * stops an approved preview from writing to a different set of people (ADR-107).
 *
 * The warning key is user + template + local date, so sending the same template to the same person
 * twice in one day is a replay, never a second event on their calendar.
 */
export function UserWarningForm({
  userId,
  options,
  onSent,
  headingLevel = 'h2',
}: {
  userId: string;
  options: AnnouncementCompositionOptions;
  /** Called after a successful confirmation, with the operator-facing outcome message. */
  onSent: (notice: string) => void;
  headingLevel?: 'h2' | 'h3';
}) {
  const [composition, setComposition] = useState<AnnouncementComposition>(() =>
    draftFor(userId, options));
  const [preview, setPreview] = useState<AnnouncementPreview | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // Switching to another account must not carry the previous draft's preview with it: the plan
  // hash it holds was computed for a different recipient.
  useEffect(() => {
    setComposition(draftFor(userId, options));
    setPreview(null);
    setError(null);
  }, [userId, options]);

  const Heading = headingLevel;
  const category = options.categories.find((item) => item.key === composition.categoryKey);

  function patch(changes: Partial<AnnouncementComposition>) {
    setComposition((current) => ({ ...current, ...changes }));
    setPreview(null);
  }

  function applyTemplate(templateKey: string) {
    const template = options.templates.find((item) => item.key === templateKey);
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
    if (!preview) return;
    setBusy(true);
    setError(null);
    try {
      const result = await createAnnouncement({
        announcement: composition,
        planHash: preview.planHash,
        confirmationPhrase: phrase,
        reason,
      });
      setPreview(null);
      setComposition(draftFor(userId, options));
      onSent(result.outcome === 'AlreadyExists'
        ? 'Bu uyarı bugün aynı şablonla zaten teslim edilmiş. Yeni bir etkinlik oluşturulmadı.'
        : 'Uyarı kuyruğa alındı. Takvime yazma işini arka plan görevi yapar.');
    } catch (caught) {
      setError(caught instanceof ApiError ? caught.message : 'Uyarı kuyruğa alınamadı.');
    } finally {
      setBusy(false);
    }
  }

  return (
    <>
      <section>
        <Heading style={{ fontSize: 16 }}>Şablon ve içerik</Heading>
        <label htmlFor="warning-template">Şablon</label>
        <select
          className="select-input"
          id="warning-template"
          value={composition.templateKey ?? ''}
          onChange={(event) => applyTemplate(event.target.value)}
        >
          {options.templates.map((template) => (
            <option key={template.key} value={template.key}>{template.name}</option>
          ))}
        </select>
        <small className="muted" style={{ display: 'block', marginTop: 4, fontSize: 12 }}>
          Şablon yalnızca bir taslaktır; metni düzenleyebilirsin. Şablon anahtarı uyarı kimliğine
          girer: aynı kullanıcıya aynı gün aynı şablonu ikinci kez göndermek yeni bir etkinlik
          oluşturmaz.
        </small>

        <label htmlFor="warning-title">Başlık</label>
        <input
          className="text-input"
          id="warning-title"
          value={composition.title}
          maxLength={200}
          onChange={(event) => patch({ title: event.target.value })}
        />

        <label htmlFor="warning-body">Mesaj</label>
        <textarea
          className="text-input"
          id="warning-body"
          rows={8}
          maxLength={4000}
          value={composition.body}
          onChange={(event) => patch({ body: event.target.value })}
        />

        <label htmlFor="warning-date">Tarih (Europe/Istanbul)</label>
        <input
          className="text-input"
          id="warning-date"
          type="date"
          min={options.earliestLocalDate}
          value={composition.localDate}
          onChange={(event) => patch({ localDate: event.target.value })}
        />

        <div className="cluster" style={{ gap: 12 }}>
          <div>
            <label htmlFor="warning-start">Başlangıç</label>
            <input
              className="text-input"
              id="warning-start"
              type="time"
              value={composition.startLocalTime ?? ''}
              onChange={(event) => patch({ startLocalTime: event.target.value })}
            />
          </div>
          <div>
            <label htmlFor="warning-end">Bitiş</label>
            <input
              className="text-input"
              id="warning-end"
              type="time"
              value={composition.endLocalTime ?? ''}
              onChange={(event) => patch({ endLocalTime: event.target.value })}
            />
          </div>
        </div>

        <label htmlFor="warning-reminder">Hatırlatıcı</label>
        <select
          className="select-input"
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
          <Heading style={{ fontSize: 16 }}>Önizleme ve onay</Heading>
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
            <ConfirmPanel preview={preview} kind="UserWarning" busy={busy} onConfirm={confirm} />
          )}
          {error && <div className="error" role="alert">{error}</div>}
        </section>
      )}
    </>
  );
}

function draftFor(
  userId: string,
  options: AnnouncementCompositionOptions,
): AnnouncementComposition {
  const template = options.templates[0];
  return {
    kind: 'UserWarning',
    targetUserId: userId,
    templateKey: template?.key ?? null,
    title: template?.suggestedTitle ?? '',
    body: template?.suggestedBody ?? '',
    location: null,
    isAllDay: false,
    localDate: options.earliestLocalDate,
    startLocalTime: '09:00',
    endLocalTime: '09:15',
    reminderMinutesBefore: 0,
    categoryKey: template?.categoryKey ?? 'announcement:warning',
    internalNote: null,
  };
}
