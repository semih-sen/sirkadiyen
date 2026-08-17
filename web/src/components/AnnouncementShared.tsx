'use client';

import { useCallback, useEffect, useState } from 'react';
import {
  ApiError,
  cancelAnnouncement,
  listAnnouncementDeliveries,
  listAnnouncements,
} from '@/lib/api';
import { LoadState, Pager, formatDateTime } from '@/components/AdminData';
import type {
  AnnouncementComposition,
  AnnouncementDeliveryView,
  AnnouncementExclusionGroup,
  AnnouncementPreview,
  AnnouncementSummary,
  AnnouncementExclusionReason,
  CalendarAnnouncementKind,
  CalendarAnnouncementStatus,
} from '@/lib/types';

/**
 * Why a candidate cannot receive an announcement, in the operator's language.
 *
 * Each of these is a fact about the account, not a filter someone chose to apply. There is no
 * "include them anyway" control, because there would be no calendar to write to — and offering one
 * would be a promise the API cannot keep (ADR-107).
 */
export const EXCLUSION_LABELS: Record<AnnouncementExclusionReason, string> = {
  NoStudentProfile: 'Akademik profil yok',
  LicenseInactive: 'Lisans etkin değil',
  NoCalendarConnection: 'Takvim bağlı değil',
  CalendarAuthorizationRevoked: 'Takvim yetkisi geri alınmış',
  InitialSyncIncomplete: 'İlk senkronizasyon tamamlanmamış',
  ManagedCalendarUnavailable: 'Yönetilen takvim erişilemez',
};

const STATUS_LABELS: Record<CalendarAnnouncementStatus, string> = {
  Queued: 'Kuyrukta',
  Delivering: 'Dağıtılıyor',
  Delivered: 'Dağıtıldı',
  Cancelling: 'İptal ediliyor',
  Cancelled: 'İptal edildi',
  Failed: 'Başarısız',
};

export function statusTone(status: CalendarAnnouncementStatus): string {
  if (status === 'Delivered') return 'badge-success';
  if (status === 'Failed') return 'badge-danger';
  if (status === 'Cancelled' || status === 'Cancelling') return 'badge-neutral';
  return 'badge-warning';
}

export function StatusBadge({ status }: { status: CalendarAnnouncementStatus }) {
  return <span className={`badge ${statusTone(status)}`}>{STATUS_LABELS[status] ?? status}</span>;
}

export function formatWhen(summary: {
  localDate: string;
  isAllDay: boolean;
  startLocalTime?: string | null;
  endLocalTime?: string | null;
}): string {
  if (summary.isAllDay) return `${summary.localDate} · tüm gün`;
  const start = summary.startLocalTime?.slice(0, 5) ?? '';
  const end = summary.endLocalTime?.slice(0, 5) ?? '';
  return `${summary.localDate} · ${start}–${end}`;
}

export function ExclusionList({ groups }: { groups: AnnouncementExclusionGroup[] }) {
  if (groups.length === 0) {
    return <p className="muted" style={{ fontSize: 13 }}>Hariç bırakılan hesap yok.</p>;
  }
  return (
    <ul style={{ margin: '8px 0 0', paddingLeft: 18, fontSize: 13 }}>
      {groups.map((group) => (
        <li key={group.reason}>
          {EXCLUSION_LABELS[group.reason] ?? group.reason}: <strong>{group.count}</strong>
        </li>
      ))}
    </ul>
  );
}

/**
 * The delivery counters. They are read from the per-recipient ledger, never stored on the
 * announcement, so what is shown here cannot disagree with the rows it summarizes.
 */
export function DeliveryCounters({ summary }: { summary: AnnouncementSummary }) {
  const { counts } = summary;
  return (
    <small className="muted" style={{ display: 'block', marginTop: 4, fontSize: 12 }}>
      Yazıldı {counts.written} · bekliyor {counts.pending} · atlandı {counts.skipped}
      {counts.removed > 0 ? ` · kaldırıldı ${counts.removed}` : ''}
      {counts.failed > 0 ? ` · başarısız ${counts.failed}` : ''}
      {` · onaylanan alıcı ${summary.recipientCount}`}
    </small>
  );
}

/**
 * The confirmation step (plan §4.3 step 5).
 *
 * The recipient count is the largest thing on the panel, and the operator has to type the phrase
 * the server chose before the button becomes usable. The `planHash` travelling back with the
 * confirmation is what makes it binding: the server recomputes the audience and refuses if it
 * moved, so an approved preview can never queue a different set of people.
 */
export function ConfirmPanel({
  preview,
  kind,
  busy,
  onConfirm,
}: {
  preview: AnnouncementPreview;
  kind: CalendarAnnouncementKind;
  busy: boolean;
  onConfirm: (phrase: string, reason: string) => void;
}) {
  const [phrase, setPhrase] = useState('');
  const [reason, setReason] = useState('');
  const matches = phrase.trim().toLowerCase() === preview.confirmationPhrase.toLowerCase();
  const ready = matches && reason.trim().length > 0 && !busy;

  return (
    <div style={{ borderTop: '1px solid var(--border)', paddingTop: 16, marginTop: 16 }}>
      <p style={{ fontSize: 40, lineHeight: 1.1, margin: 0, fontWeight: 700 }}>
        {preview.recipientCount}
      </p>
      <p className="muted" style={{ margin: '2px 0 12px', fontSize: 13 }}>
        {kind === 'UserWarning'
          ? 'kullanıcının takvimine yazılacak.'
          : 'öğrencinin takvimine yazılacak. Bu bir “gönder” düğmesi değil, bir dağıtım işlemidir.'}
      </p>

      <p className="muted" style={{ fontSize: 13 }}>
        Kuyruğa alındıktan sonra alıcı listesi dondurulur: bu andan sonra grubunu değiştiren bir
        öğrenci ne bu duyuruyu kazanır ne de kaybeder. İçerik daha sonra düzeltilebilir — yazılmış
        etkinlikler yamalanır, ikinci bir kopya oluşmaz — ve iptal, yazılmış etkinlikleri kaldırır.
      </p>

      <label htmlFor="announcement-phrase">
        Onaylamak için yazın: <code>{preview.confirmationPhrase}</code>
      </label>
      <input className="text-input"
        id="announcement-phrase"
        value={phrase}
        onChange={(event) => setPhrase(event.target.value)}
        autoComplete="off"
      />

      <label htmlFor="announcement-reason">Gerekçe (denetim kaydına yazılır)</label>
      <input className="text-input"
        id="announcement-reason"
        value={reason}
        onChange={(event) => setReason(event.target.value)}
        placeholder="Fakülte 12 Kasım telafi duyurusunun takvimlere yazılmasını istedi."
      />

      <button
        className="btn btn-primary"
        type="button"
        disabled={!ready}
        onClick={() => onConfirm(phrase.trim(), reason.trim())}
      >
        {busy ? 'Kuyruğa alınıyor…' : 'Onayla ve kuyruğa al'}
      </button>
    </div>
  );
}

/** The per-recipient ledger, which is also the answer to "did it reach them". */
export function DeliveryLedger({ announcementId }: { announcementId: string }) {
  const [page, setPage] = useState(1);
  const [result, setResult] = useState<{
    items: AnnouncementDeliveryView[]; totalCount: number; totalPages: number;
  } | null>(null);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    setResult(null);
    setError(null);
    try {
      const paged = await listAnnouncementDeliveries(announcementId, { page, pageSize: 25 });
      setResult({
        items: paged.items,
        totalCount: paged.totalCount,
        totalPages: paged.totalPages,
      });
    } catch (caught) {
      setError(caught instanceof ApiError ? caught.message : 'Teslim kayıtları yüklenemedi.');
    }
  }, [announcementId, page]);

  useEffect(() => { void load(); }, [load]);

  return (
    <div style={{ marginTop: 12 }}>
      <LoadState
        loading={result === null && !error}
        error={error}
        empty={result?.items.length === 0}
        onRetry={() => void load()}
      />
      {result && result.items.length > 0 && (
        <div style={{ overflowX: 'auto' }}>
          <table className="data-table">
            <thead>
              <tr><th>Kullanıcı</th><th>Durum</th><th>Sürüm</th><th>Güncelleme</th></tr>
            </thead>
            <tbody>
              {result.items.map((delivery) => (
                <tr key={delivery.userId}>
                  <td>
                    {delivery.email}
                    {delivery.displayName && (
                      <small className="muted" style={{ display: 'block' }}>{delivery.displayName}</small>
                    )}
                  </td>
                  <td>
                    {delivery.state}
                    {delivery.skipReason && (
                      <small className="muted" style={{ display: 'block' }}>
                        {EXCLUSION_LABELS[delivery.skipReason] ?? delivery.skipReason}
                      </small>
                    )}
                    {delivery.failureReason && (
                      <small className="muted" style={{ display: 'block' }}>{delivery.failureReason}</small>
                    )}
                  </td>
                  <td>{delivery.appliedContentVersion ?? '—'}</td>
                  <td>{formatDateTime(delivery.updatedAtUtc)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
      {result && (
        <Pager
          page={page}
          totalPages={result.totalPages}
          totalCount={result.totalCount}
          onChange={setPage}
        />
      )}
    </div>
  );
}

/**
 * Existing announcements with their delivery state and the two write actions.
 *
 * Cancellation is deliberately not a "delete" of a record: it removes the events already written
 * to calendars and keeps every delivery row, so the trail still says who received what and when.
 */
export function AnnouncementHistory({
  kind,
  reloadToken,
  onEdit,
}: {
  kind: CalendarAnnouncementKind;
  reloadToken: number;
  onEdit?: (summary: AnnouncementSummary) => void;
}) {
  const [items, setItems] = useState<AnnouncementSummary[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [openId, setOpenId] = useState<string | null>(null);

  const load = useCallback(async () => {
    setItems(null);
    setError(null);
    try {
      setItems(await listAnnouncements({ kind, limit: 50 }));
    } catch (caught) {
      setError(caught instanceof ApiError ? caught.message : 'Duyurular yüklenemedi.');
    }
  }, [kind]);

  useEffect(() => { void load(); }, [load, reloadToken]);

  return (
    <div>
      <LoadState
        loading={items === null && !error}
        error={error}
        empty={items?.length === 0}
        onRetry={() => void load()}
      />
      {items?.map((summary) => (
        <AnnouncementRow
          key={summary.announcementId}
          summary={summary}
          open={openId === summary.announcementId}
          onToggle={() => setOpenId(openId === summary.announcementId ? null : summary.announcementId)}
          onChanged={() => void load()}
          onEdit={onEdit}
        />
      ))}
    </div>
  );
}

function AnnouncementRow({
  summary,
  open,
  onToggle,
  onChanged,
  onEdit,
}: {
  summary: AnnouncementSummary;
  open: boolean;
  onToggle: () => void;
  onChanged: () => void;
  onEdit?: (summary: AnnouncementSummary) => void;
}) {
  const [reason, setReason] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const cancellable = summary.status !== 'Cancelled' && summary.status !== 'Cancelling';

  async function cancel() {
    const trimmed = reason.trim();
    if (trimmed.length === 0) {
      setError('Bir gerekçe girin; iptal, takvimlerden etkinlik siler.');
      return;
    }
    setBusy(true);
    setError(null);
    try {
      await cancelAnnouncement(summary.announcementId, trimmed);
      onChanged();
    } catch (caught) {
      setError(caught instanceof ApiError ? caught.message : 'Duyuru iptal edilemedi.');
    } finally {
      setBusy(false);
    }
  }

  return (
    <div style={{ border: '1px solid var(--border)', borderRadius: 8, padding: 12, marginBottom: 10 }}>
      <button
        type="button"
        onClick={onToggle}
        className="link"
        style={{ display: 'block', width: '100%', textAlign: 'left', color: 'var(--text)' }}
      >
        <span className="cluster" style={{ justifyContent: 'space-between' }}>
          <strong>{summary.title}</strong>
          <span className="cluster" style={{ gap: 8 }}>
            <StatusBadge status={summary.status} />
            <span aria-hidden="true">{open ? '▲' : '▼'}</span>
          </span>
        </span>
        <small className="muted" style={{ display: 'block', marginTop: 4 }}>
          {formatWhen(summary)} · v{summary.contentVersion} · {summary.createdBy} ·{' '}
          {formatDateTime(summary.createdAtUtc)}
        </small>
        <DeliveryCounters summary={summary} />
      </button>

      <small className="muted" style={{ display: 'block', marginTop: 6, fontSize: 12 }}>
        Kampanya anahtarı: <code>{summary.campaignKey}</code>
      </small>
      {summary.lastFailureReason && (
        <p style={{ margin: '8px 0 0', fontSize: 13 }}>{summary.lastFailureReason}</p>
      )}
      {summary.cancellationReason && (
        <p className="muted" style={{ margin: '8px 0 0', fontSize: 13 }}>
          İptal ({summary.cancelledBy}): {summary.cancellationReason}
        </p>
      )}

      {open && (
        <>
          <DeliveryLedger announcementId={summary.announcementId} />
          <div style={{ marginTop: 14, borderTop: '1px solid var(--border)', paddingTop: 14 }}>
            {onEdit && cancellable && (
              <button
                className="btn btn-secondary btn-sm"
                type="button"
                style={{ marginBottom: 12 }}
                onClick={() => onEdit(summary)}
              >
                İçeriği düzenle
              </button>
            )}
            {cancellable ? (
              <>
                <p className="muted" style={{ fontSize: 13, margin: '0 0 10px' }}>
                  İptal, bu duyurunun yazıldığı her takvimden etkinliği kaldırır. Teslim kayıtları
                  silinmez: kimin ne aldığı kayıtlı kalır.
                </p>
                <label htmlFor={`cancel-reason-${summary.announcementId}`}>
                  İptal gerekçesi (denetim kaydına yazılır)
                </label>
                <input className="text-input"
                  id={`cancel-reason-${summary.announcementId}`}
                  value={reason}
                  onChange={(event) => setReason(event.target.value)}
                  placeholder="Etkinlik ertelendi; yeni tarih ayrı duyuruyla yazılacak."
                />
                <button
                  className="btn btn-danger"
                  type="button"
                  onClick={() => void cancel()}
                  disabled={busy}
                >
                  {busy ? 'İptal ediliyor…' : 'Takvimlerden kaldır'}
                </button>
              </>
            ) : (
              <p className="muted" style={{ fontSize: 13 }}>
                Bu duyuru iptal edildi; takvimlerden kaldırılması tamamlandığında sayaçlar
                “kaldırıldı” sütununda görünür.
              </p>
            )}
            {error && <div className="error" role="alert">{error}</div>}
          </div>
        </>
      )}
    </div>
  );
}

/** A read-only rendering of exactly the calendar event that will be written (plan §4.4 step 5). */
export function CalendarPreview({
  composition,
  categoryName,
  categoryColor,
}: {
  composition: AnnouncementComposition;
  categoryName: string;
  categoryColor: string;
}) {
  return (
    <div
      style={{
        border: '1px solid var(--border)',
        borderLeft: `6px solid ${categoryColor}`,
        borderRadius: 8,
        padding: 14,
      }}
    >
      <div className="cluster" style={{ justifyContent: 'space-between' }}>
        <strong>{composition.title || '—'}</strong>
        <span className="badge badge-neutral">{categoryName}</span>
      </div>
      <small className="muted" style={{ display: 'block', marginTop: 4 }}>
        {formatWhen({
          localDate: composition.localDate,
          isAllDay: composition.isAllDay,
          startLocalTime: composition.startLocalTime,
          endLocalTime: composition.endLocalTime,
        })}
        {composition.location ? ` · ${composition.location}` : ''}
        {composition.reminderMinutesBefore != null
          ? ` · ${composition.reminderMinutesBefore} dk önce hatırlatma`
          : ' · hatırlatma yok'}
      </small>
      <p style={{ whiteSpace: 'pre-wrap', marginTop: 10, fontSize: 13 }}>{composition.body}</p>
      {composition.internalNote && (
        <p className="muted" style={{ fontSize: 12, marginTop: 8 }}>
          Yönetici notu (takvime yazılmaz): {composition.internalNote}
        </p>
      )}
    </div>
  );
}
