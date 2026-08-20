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
  previewUserCalendarRecheck: vi.fn(),
  requestUserCalendarRecheck: vi.fn(),
  rebuildUserCalendar: vi.fn(),
  deleteUser: vi.fn(),
  ApiError: class extends Error {},
}));
vi.mock('@/lib/api', () => api);
vi.mock('next/navigation', () => ({ useRouter: () => ({ replace: vi.fn() }) }));

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

  it('re-checks one calendar and confirms with the plan hash the server returned', async () => {
    // The per-user path exists so an operator can fix one student without authorizing a cohort
    // (ADR-115). It must send the server's hash, not a count the browser recomputed.
    api.previewUserCalendarRecheck.mockResolvedValue({
      scope: { academicYear: '2026-2027', classYear: 2, programLanguage: 'Turkish' },
      users: [{
        userId: 'u1', surplusEventCount: 0, missingEventCount: 412, untouchableRetiredCount: 30,
      }],
      cohortUserCount: 1,
      totalSurplusEvents: 0,
      totalMissingEvents: 412,
      totalUntouchableRetired: 30,
      planHash: 'plan-hash-from-the-server',
    });
    api.requestUserCalendarRecheck.mockResolvedValue({ outcome: 'Requested', usersRequested: 1 });

    render(<AdminUserDetail userId="u1" />);
    await screen.findByRole('heading', { name: 'Zeynep' });
    await userEvent.click(screen.getByRole('tab', { name: 'Takvim' }));

    await userEvent.click(await screen.findByRole('button', { name: 'Farkı hesapla' }));

    // The 412 lessons this student is missing are exactly what an academic-year divergence looks
    // like from one calendar's side.
    expect(await screen.findByText('412')).toBeInTheDocument();

    await userEvent.type(screen.getByLabelText('Gerekçe'), 'Dönem 2 yıl taşıması sonrası.');
    await userEvent.click(screen.getByRole('button', { name: 'Takvimi yeniden eşitle' }));

    await waitFor(() => expect(api.requestUserCalendarRecheck).toHaveBeenCalledWith(
      'u1',
      'plan-hash-from-the-server',
      'Dönem 2 yıl taşıması sonrası.',
    ));
  });

  it('says a calendar is already correct rather than offering a pointless confirmation', async () => {
    api.previewUserCalendarRecheck.mockResolvedValue({
      scope: { academicYear: '2025-2026', classYear: 2, programLanguage: 'Turkish' },
      users: [],
      cohortUserCount: 1,
      totalSurplusEvents: 0,
      totalMissingEvents: 0,
      totalUntouchableRetired: 0,
      planHash: 'hash',
    });

    render(<AdminUserDetail userId="u1" />);
    await screen.findByRole('heading', { name: 'Zeynep' });
    await userEvent.click(screen.getByRole('tab', { name: 'Takvim' }));
    await userEvent.click(await screen.findByRole('button', { name: 'Farkı hesapla' }));

    expect(await screen.findByText(/Yakınsanacak bir şey yok/)).toBeInTheDocument();
    expect(screen.queryByLabelText('Gerekçe')).not.toBeInTheDocument();
  });

  it('offers a rebuild when the managed calendar is unreachable, and states what it discards', async () => {
    // Before ADR-116 the panel could only describe the dead end. The destructive part must be
    // stated before the action is offered, not after it is taken.
    api.getAdminUser.mockResolvedValue({
      ...detail,
      user: {
        ...detail.user,
        calendarConnection: {
          ...detail.user.calendarConnection,
          managedCalendarUnavailableAtUtc: '2026-08-15T00:00:00Z',
        },
      },
    });
    api.rebuildUserCalendar.mockResolvedValue({ outcome: 'Reset', discardedMappings: 412 });

    render(<AdminUserDetail userId="u1" />);
    await screen.findByRole('heading', { name: 'Zeynep' });

    await userEvent.click(await screen.findByRole('button', { name: 'Takvimi yeniden kur' }));
    expect(await screen.findByText(/eşleşme defteri tamamen silinir/)).toBeInTheDocument();

    // The reason is required, because the person deciding is not the account owner.
    const confirm = screen.getByRole('button', { name: 'Takvimi yeniden kur' });
    expect(confirm).toBeDisabled();

    await userEvent.type(screen.getByLabelText('Gerekçe'), 'Öğrenci takvimi silmiş.');
    await userEvent.click(confirm);

    await waitFor(() => expect(api.rebuildUserCalendar).toHaveBeenCalledWith(
      'u1',
      'Öğrenci takvimi silmiş.',
    ));
    // The count is how the operator learns the size of what the student will see rewritten.
    expect(await screen.findByText(/412 eşleşme kaydı/)).toBeInTheDocument();
  });

  it('does not offer a rebuild for a healthy calendar', async () => {
    render(<AdminUserDetail userId="u1" />);
    await screen.findByRole('heading', { name: 'Zeynep' });

    expect(screen.queryByRole('button', { name: 'Takvimi yeniden kur' })).not.toBeInTheDocument();
  });

  it('deletes an account only with a reason and the matching confirmation e-mail', async () => {
    api.deleteUser.mockResolvedValue({
      outcome: 'Deleted', hadManagedCalendar: true, googleCalendarDeleted: true,
      googleTokenRevoked: true, anonymizedAuditEvents: 4,
    });
    render(<AdminUserDetail userId="u1" />);
    await screen.findByRole('heading', { name: 'Zeynep' });

    await userEvent.click(screen.getByRole('button', { name: 'Hesabı silmek istiyorum' }));
    const confirm = screen.getByRole('button', { name: 'Hesabı kalıcı olarak sil' });
    expect(confirm).toBeDisabled();

    await userEvent.type(screen.getByLabelText('Silme gerekçesi (denetim kaydına yazılır)'), 'KVKK talebi.');
    // A wrong e-mail keeps the button disabled.
    await userEvent.type(screen.getByLabelText(/hesabın e-postasını yaz/), 'wrong@example.com');
    expect(confirm).toBeDisabled();

    await userEvent.clear(screen.getByLabelText(/hesabın e-postasını yaz/));
    await userEvent.type(screen.getByLabelText(/hesabın e-postasını yaz/), 'user@example.com');
    expect(confirm).toBeEnabled();

    await userEvent.click(confirm);
    await waitFor(() => expect(api.deleteUser).toHaveBeenCalledWith(
      'u1', 'KVKK talebi.', 'user@example.com',
    ));
  });

  it('refuses to delete a SuperAdmin account', async () => {
    api.getAdminUser.mockResolvedValue({
      ...detail,
      user: { ...detail.user, summary: { ...summary, role: 'SuperAdmin' } },
    });
    render(<AdminUserDetail userId="u1" />);
    await screen.findByRole('heading', { name: 'Zeynep' });

    expect(screen.queryByRole('button', { name: 'Hesabı silmek istiyorum' })).not.toBeInTheDocument();
    expect(screen.getByText(/Yönetici hesabı bu akıştan silinemez/)).toBeInTheDocument();
  });
});

function dayGap(from: string, to: string): number {
  return (Date.parse(`${to}T00:00:00Z`) - Date.parse(`${from}T00:00:00Z`)) / 86_400_000;
}
