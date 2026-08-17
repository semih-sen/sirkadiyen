import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { AdminUserDirectory } from './AdminUserDirectory';

const api = vi.hoisted(() => ({
  listAdminUsers: vi.fn(),
  getProfileOptions: vi.fn(),
  listAdminLicenses: vi.fn(),
  getAdminLicense: vi.fn(),
  revokeLicense: vi.fn(),
  ApiError: class extends Error {},
}));
vi.mock('@/lib/api', () => api);
vi.mock('@/components/LicenseAdministration', () => ({ LicenseAdministration: () => <div>Yeni lisans</div> }));

const userPage = {
  items: [{
    id: 'u1', email: 'user@example.com', displayName: 'User', role: 'User', licenseState: 'Active',
    hasProfile: true, academicYear: '2025-2026', classYear: 2, programLanguage: 'Turkish',
    studentNumber: '0102030405', calendarStatus: 'Authorized', initialSyncState: 'Completed',
    managedEventCount: 12, createdAtUtc: '2026-08-01T00:00:00Z', lastSignedInAtUtc: '2026-08-04T00:00:00Z',
  }],
  page: 1, pageSize: 50, totalCount: 1, totalPages: 1,
};

const profileOptions = {
  academicYear: '2025-2026',
  schemaVersion: '1.1',
  programs: [{
    academicYear: '2025-2026',
    classYear: 2,
    programLanguage: 'Turkish',
    dimensions: [{ key: 'practiceGroup', required: true, values: ['A', 'B'] }],
  }],
};

const licensePage = { items: [{ licenseId: 'l1', kind: 'Code', status: 'Redeemed', createdByEmail: 'admin@example.com', createdAtUtc: '2026-08-01T00:00:00Z' }], page: 1, pageSize: 50, totalCount: 1, totalPages: 1 };

describe('AdminUserDirectory', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    api.listAdminUsers.mockResolvedValue(userPage);
    api.getProfileOptions.mockResolvedValue(profileOptions);
    api.listAdminLicenses.mockResolvedValue(licensePage);
    api.getAdminLicense.mockResolvedValue({ summary: licensePage.items[0], audit: [] });
    api.revokeLicense.mockResolvedValue({ outcome: 'Revoked' });
  });

  it('debounces the free-text search and links each row to its own page', async () => {
    render(<AdminUserDirectory />);
    await screen.findByText('user@example.com', { exact: false });

    await userEvent.type(screen.getByLabelText('Kullanıcı ara'), 'student@');
    await waitFor(
      () => expect(api.listAdminUsers).toHaveBeenCalledWith(
        expect.objectContaining({ search: 'student@', pageSize: 50 }),
      ),
      { timeout: 1000 },
    );

    expect(screen.getByRole('link', { name: 'User' })).toHaveAttribute('href', '/admin/users/u1');
    expect(screen.getByText('12 etkinlik')).toBeInTheDocument();
  });

  it('sends every advanced filter to the backend rather than narrowing in the browser', async () => {
    render(<AdminUserDirectory />);
    await screen.findByText('user@example.com', { exact: false });

    await userEvent.click(screen.getByRole('button', { name: /Gelişmiş filtreler/ }));
    await userEvent.selectOptions(screen.getByLabelText('Lisans durumu'), 'Suspended');
    await waitFor(() => expect(api.listAdminUsers).toHaveBeenCalledWith(
      expect.objectContaining({ licenseState: 'Suspended', page: 1 }),
    ));

    await userEvent.selectOptions(screen.getByLabelText('İlk senkronizasyon'), 'Pending');
    await waitFor(() => expect(api.listAdminUsers).toHaveBeenCalledWith(
      expect.objectContaining({ licenseState: 'Suspended', initialSyncState: 'Pending' }),
    ));

    // The chip counts the filters that are not the free-text box.
    expect(screen.getByRole('button', { name: /Gelişmiş filtreler \(2\)/ })).toBeInTheDocument();
  });

  it('offers the selector dimensions of the chosen cohort and forgets them when it changes', async () => {
    render(<AdminUserDirectory />);
    await screen.findByText('user@example.com', { exact: false });

    await userEvent.click(screen.getByRole('button', { name: /Gelişmiş filtreler/ }));
    expect(screen.queryByLabelText('Uygulama grubu')).not.toBeInTheDocument();

    await userEvent.selectOptions(screen.getByLabelText('Dönem'), '2');
    await userEvent.selectOptions(await screen.findByLabelText('Uygulama grubu'), 'A');
    await waitFor(() => expect(api.listAdminUsers).toHaveBeenCalledWith(
      expect.objectContaining({ classYear: 2, selectors: { practiceGroup: 'A' } }),
    ));

    // A selector belongs to the cohort it was chosen in.
    await userEvent.selectOptions(screen.getByLabelText('Dönem'), '');
    await waitFor(() => expect(api.listAdminUsers).toHaveBeenCalledWith(
      expect.objectContaining({ classYear: undefined, selectors: undefined }),
    ));
  });

  it('revokes the selected license only after a reason', async () => {
    render(<AdminUserDirectory />); await userEvent.click(screen.getByRole('tab', { name: 'Lisanslar' }));
    await screen.findByText('l1'); await userEvent.click(screen.getByText('l1'));
    const revoke = await screen.findByRole('button', { name: 'Seçili lisansı iptal et' });
    expect(revoke).toBeDisabled();
    await userEvent.type(screen.getByPlaceholderText('Zorunlu gerekçe'), 'Yanlış tahsis');
    await userEvent.click(revoke);
    await waitFor(() => expect(api.revokeLicense).toHaveBeenCalledWith('l1', 'Yanlış tahsis'));
  });
});
