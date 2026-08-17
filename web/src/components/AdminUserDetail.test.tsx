import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { AdminUserDetail } from './AdminUserDetail';

const api = vi.hoisted(() => ({
  getAdminUser: vi.fn(),
  getAdminUserCalendarEvents: vi.fn(),
  getAdminUserCalendarChanges: vi.fn(),
  getAnnouncementOptions: vi.fn(),
  previewAnnouncement: vi.fn(),
  createAnnouncement: vi.fn(),
  listAnnouncements: vi.fn(),
  listAnnouncementDeliveries: vi.fn(),
  cancelAnnouncement: vi.fn(),
  activateUser: vi.fn(),
  revokeLicense: vi.fn(),
  ApiError: class extends Error {},
}));
vi.mock('@/lib/api', () => api);

const summary = {
  id: 'u1', email: 'user@example.com', displayName: 'Zeynep', role: 'User',
  licenseState: 'None', hasProfile: true, academicYear: '2025-2026', classYear: 2,
  programLanguage: 'Turkish', studentNumber: '0102030405', calendarStatus: 'Authorized',
  initialSyncState: 'Completed', managedEventCount: 3,
  createdAtUtc: '2026-08-01T00:00:00Z', lastSignedInAtUtc: '2026-08-04T00:00:00Z',
};

const detail = {
  user: {
    summary,
    profile: {
      academicYear: '2025-2026', classYear: 2, programLanguage: 'Turkish',
      studentNumber: '0102030405', selectorSchemaVersion: '1.1',
      selectors: { practiceGroup: 'A' }, updatedAtUtc: '2026-08-02T00:00:00Z',
    },
    managedEventCount: 3,
    licenses: [],
    calendarConnection: {
      status: 'Authorized', initialSyncState: 'Completed', hasManagedCalendar: true,
      lastCalendarInventoryAtUtc: '2026-08-16T00:00:00Z',
    },
  },
  onboardingState: 'Active',
  recentSignIns: [],
  recentActivity: [],
};

describe('AdminUserDetail', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    api.getAdminUser.mockResolvedValue(detail);
    api.getAdminUserCalendarEvents.mockResolvedValue({
      fromLocalDate: '2026-08-17', toLocalDate: '2026-09-16', timeZoneId: 'Europe/Istanbul',
      events: [{
        stableIdentity: 'lesson-1', title: 'Fizyoloji 1. Uygulaması', localDate: '2026-08-20',
        startLocalTime: '08:30:00', endLocalTime: '10:20:00', isAllDay: false,
        timeZoneId: 'Europe/Istanbul', location: 'Amfi 2', instructor: 'Dr. A',
        eventType: 'Practice', departments: ['Fizyoloji'],
      }],
    });
    api.getAdminUserCalendarChanges.mockResolvedValue([]);
    api.getAnnouncementOptions.mockResolvedValue({
      categories: [{ key: 'announcement:warning', name: 'Uyarı', backgroundColor: '#f00' }],
      templates: [{
        key: 'profile-missing', name: 'Profil eksik', suggestedTitle: 'Profilini tamamla',
        suggestedBody: 'Lütfen profilini tamamla.', categoryKey: 'announcement:warning',
      }],
      timeZoneId: 'Europe/Istanbul',
      earliestLocalDate: '2026-08-17',
    });
    api.listAnnouncements.mockResolvedValue([]);
    api.activateUser.mockResolvedValue({ outcome: 'Activated', userId: 'u1', licenseId: 'l9' });
  });

  it('shows the authoritative account state without inventing anything', async () => {
    render(<AdminUserDetail userId="u1" />);

    expect(await screen.findByRole('heading', { name: 'Zeynep' })).toBeInTheDocument();
    expect(screen.getByText('Lisans: None')).toBeInTheDocument();
    expect(screen.getByText('Takvim: Completed')).toBeInTheDocument();
    // The onboarding state is shown as a badge and repeated in the account rows.
    expect(screen.getAllByText('Active')).toHaveLength(2);
    expect(screen.getByText('0102030405')).toBeInTheDocument();
    expect(screen.getByText('Lisans kaydı yok.')).toBeInTheDocument();
  });

  it('activates the account only with a reason, then reloads authoritative state', async () => {
    render(<AdminUserDetail userId="u1" />);
    await screen.findByRole('heading', { name: 'Zeynep' });

    const activate = screen.getByRole('button', { name: 'Hesabı etkinleştir' });
    expect(activate).toBeDisabled();

    await userEvent.type(
      screen.getByLabelText('Gerekçe (denetim kaydına yazılır)'),
      'Kod ulaşmadı',
    );
    await userEvent.click(screen.getByRole('button', { name: 'Hesabı etkinleştir' }));

    await waitFor(() => expect(api.activateUser).toHaveBeenCalledWith('u1', 'Kod ulaşmadı'));
    expect(api.getAdminUser).toHaveBeenCalledTimes(2);
  });

  it('reads the managed calendar from the ledger over a chosen window', async () => {
    render(<AdminUserDetail userId="u1" />);
    await screen.findByRole('heading', { name: 'Zeynep' });

    await userEvent.click(screen.getByRole('tab', { name: 'Takvim' }));
    expect(await screen.findByText('Fizyoloji 1. Uygulaması')).toBeInTheDocument();
    expect(api.getAdminUserCalendarEvents).toHaveBeenCalledWith(
      'u1',
      expect.objectContaining({ limit: 500 }),
    );

    await userEvent.click(screen.getByRole('button', { name: '7 gün' }));
    await waitFor(() => {
      const last = api.getAdminUserCalendarEvents.mock.calls.at(-1)!;
      const { from, to } = last[1] as { from: string; to: string };
      expect(dayGap(from, to)).toBe(7);
    });
  });

  it('scopes the warning history to this account and composes for this user', async () => {
    render(<AdminUserDetail userId="u1" />);
    await screen.findByRole('heading', { name: 'Zeynep' });

    await userEvent.click(screen.getByRole('tab', { name: 'Uyarılar' }));
    await waitFor(() => expect(api.listAnnouncements).toHaveBeenCalledWith(
      expect.objectContaining({ kind: 'UserWarning', targetUserId: 'u1' }),
    ));

    api.previewAnnouncement.mockResolvedValue({
      campaignKey: 'warn-u1', planHash: 'hash', recipientCount: 1, excludedCount: 0,
      exclusions: [], recipients: [], excludedRecipients: [], confirmationPhrase: 'uyar',
    });
    await userEvent.click(await screen.findByRole('button', { name: 'Önizle' }));

    await waitFor(() => expect(api.previewAnnouncement).toHaveBeenCalledWith(
      expect.objectContaining({ kind: 'UserWarning', targetUserId: 'u1' }),
    ));
    // The confirmation is the server's plan hash, never a count the browser recomputed.
    expect(await screen.findByText('uyar')).toBeInTheDocument();
  });
});

function dayGap(from: string, to: string): number {
  return (Date.parse(`${to}T00:00:00Z`) - Date.parse(`${from}T00:00:00Z`)) / 86_400_000;
}
