import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { BulkEventComposer } from './BulkEventComposer';
import type { AnnouncementPreview } from '@/lib/types';

const api = vi.hoisted(() => ({
  getAnnouncementOptions: vi.fn(),
  getProfileOptions: vi.fn(),
  previewAnnouncement: vi.fn(),
  createAnnouncement: vi.fn(),
  updateAnnouncement: vi.fn(),
  listAnnouncements: vi.fn(),
  listAnnouncementDeliveries: vi.fn(),
  cancelAnnouncement: vi.fn(),
  ApiError: class ApiError extends Error {},
}));
vi.mock('@/lib/api', () => api);

const options = {
  categories: [
    { key: 'announcement:notice', name: 'Sirkadiyen duyurusu', backgroundColor: '#00838F' },
  ],
  templates: [],
  timeZoneId: 'Europe/Istanbul',
  earliestLocalDate: '2026-11-10',
};

const profileOptions = {
  academicYear: '2025-2026',
  schemaVersion: '1.1',
  programs: [
    {
      academicYear: '2025-2026',
      classYear: 2,
      programLanguage: 'Turkish' as const,
      dimensions: [{ key: 'practiceGroup', required: true, values: ['A', 'B'] }],
    },
  ],
};

function preview(overrides: Partial<AnnouncementPreview> = {}): AnnouncementPreview {
  return {
    campaignKey: 'bulk:2026-11-12:abc123',
    planHash: 'hash-1',
    recipientCount: 3,
    excludedCount: 2,
    exclusions: [
      { reason: 'LicenseInactive', count: 1 },
      { reason: 'CalendarAuthorizationRevoked', count: 1 },
    ],
    recipients: [
      { userId: 'u1', email: 'a@example.test' },
      { userId: 'u2', email: 'b@example.test' },
      { userId: 'u3', email: 'c@example.test' },
    ],
    excludedRecipients: [
      { userId: 'u4', email: 'd@example.test', exclusionReason: 'LicenseInactive' },
      { userId: 'u5', email: 'e@example.test', exclusionReason: 'CalendarAuthorizationRevoked' },
    ],
    confirmationPhrase: '3',
    ...overrides,
  };
}

async function reachReview(user: ReturnType<typeof userEvent.setup>) {
  render(<BulkEventComposer />);
  await screen.findByLabelText('Akademik yıl');

  await user.click(screen.getByRole('button', { name: /Devam: etkinlik ayrıntıları/ }));
  await user.type(screen.getByLabelText('Başlık'), 'Telafi dersi');
  await user.type(screen.getByLabelText('Açıklama'), 'Telafi yapılacaktır.');
  await user.click(screen.getByRole('button', { name: /Alıcıları hesapla/ }));
  await screen.findByText('Takvim önizlemesi');
}

describe('BulkEventComposer', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    api.getAnnouncementOptions.mockResolvedValue(options);
    api.getProfileOptions.mockResolvedValue(profileOptions);
    api.previewAnnouncement.mockResolvedValue(preview());
    api.listAnnouncements.mockResolvedValue([]);
    api.createAnnouncement.mockResolvedValue({ outcome: 'Queued' });
  });

  it('shows the excluded accounts with their reasons before any confirmation is offered', async () => {
    const user = userEvent.setup();
    await reachReview(user);

    // The exclusion breakdown is the point of the review step: an operator told only "3 recipients"
    // does not know that two accounts cannot receive it at all. Each reason appears twice — once
    // in the grouped counts, once against the account it applies to.
    expect(screen.getAllByText(/Lisans etkin değil/)).toHaveLength(2);
    expect(screen.getAllByText(/Takvim yetkisi geri alınmış/)).toHaveLength(2);
    expect(screen.getByText(/d@example.test/)).toBeInTheDocument();
  });

  it('keeps the confirm button disabled until the phrase and a reason are both supplied', async () => {
    const user = userEvent.setup();
    await reachReview(user);

    const confirm = screen.getByRole('button', { name: /Onayla ve kuyruğa al/ });
    expect(confirm).toBeDisabled();

    await user.type(screen.getByLabelText(/Onaylamak için yazın/), '3');
    expect(confirm).toBeDisabled();

    await user.type(screen.getByLabelText(/Gerekçe/), 'Fakülte istedi.');
    expect(confirm).toBeEnabled();
  });

  it('refuses a mistyped confirmation phrase without calling the API', async () => {
    const user = userEvent.setup();
    await reachReview(user);

    await user.type(screen.getByLabelText(/Onaylamak için yazın/), '30');
    await user.type(screen.getByLabelText(/Gerekçe/), 'Fakülte istedi.');

    expect(screen.getByRole('button', { name: /Onayla ve kuyruğa al/ })).toBeDisabled();
    expect(api.createAnnouncement).not.toHaveBeenCalled();
  });

  it('sends back the server-computed plan hash rather than anything the browser decided', async () => {
    const user = userEvent.setup();
    await reachReview(user);

    await user.type(screen.getByLabelText(/Onaylamak için yazın/), '3');
    await user.type(screen.getByLabelText(/Gerekçe/), 'Fakülte istedi.');
    await user.click(screen.getByRole('button', { name: /Onayla ve kuyruğa al/ }));

    await waitFor(() => expect(api.createAnnouncement).toHaveBeenCalledTimes(1));
    expect(api.createAnnouncement.mock.calls[0][0]).toMatchObject({
      planHash: 'hash-1',
      confirmationPhrase: '3',
      reason: 'Fakülte istedi.',
    });
  });

  it('reports a duplicate campaign key as a replay, never as a delivery', async () => {
    api.createAnnouncement.mockResolvedValue({ outcome: 'AlreadyExists' });
    const user = userEvent.setup();
    await reachReview(user);

    await user.type(screen.getByLabelText(/Onaylamak için yazın/), '3');
    await user.type(screen.getByLabelText(/Gerekçe/), 'Fakülte istedi.');
    await user.click(screen.getByRole('button', { name: /Onayla ve kuyruğa al/ }));

    expect(await screen.findByText(/ikinci bir kopya yazılmadı/)).toBeInTheDocument();
  });

  it('never claims the announcement was delivered, only that it was queued', async () => {
    const user = userEvent.setup();
    await reachReview(user);

    await user.type(screen.getByLabelText(/Onaylamak için yazın/), '3');
    await user.type(screen.getByLabelText(/Gerekçe/), 'Fakülte istedi.');
    await user.click(screen.getByRole('button', { name: /Onayla ve kuyruğa al/ }));

    // The worker performs the writes; the confirmation only records intent (AI_GUIDELINE §16).
    const notice = await screen.findByText(/kuyruğa alındı/i);
    expect(notice.textContent).toMatch(/arka plan/i);
  });

  it('invalidates an approved plan as soon as the content is edited again', async () => {
    const user = userEvent.setup();
    await reachReview(user);

    await user.click(screen.getByRole('tab', { name: '2 · Etkinlik' }));
    await user.type(screen.getByLabelText('Başlık'), ' güncellendi');

    // The review tab is unreachable until the audience is recomputed, so a stale plan can never
    // be the one confirmed.
    expect(screen.getByRole('tab', { name: '3 · İnceleme ve onay' })).toBeDisabled();
  });
});
