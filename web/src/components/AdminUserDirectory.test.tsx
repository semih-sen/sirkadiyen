import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { AdminUserDirectory } from './AdminUserDirectory';

const api = vi.hoisted(() => ({ listAdminUsers: vi.fn(), getAdminUser: vi.fn(), listAdminLicenses: vi.fn(), getAdminLicense: vi.fn(), revokeLicense: vi.fn() }));
vi.mock('@/lib/api', () => api);
vi.mock('@/components/LicenseAdministration', () => ({ LicenseAdministration: () => <div>Yeni lisans</div> }));

const userPage = { items: [{ id: 'u1', email: 'user@example.com', displayName: 'User', role: 'Student', licenseState: 'Active', hasProfile: true, createdAtUtc: '2026-08-01T00:00:00Z', lastSignedInAtUtc: '2026-08-04T00:00:00Z' }], page: 1, pageSize: 50, totalCount: 1, totalPages: 1 };
const licensePage = { items: [{ licenseId: 'l1', kind: 'Code', status: 'Redeemed', createdByEmail: 'admin@example.com', createdAtUtc: '2026-08-01T00:00:00Z' }], page: 1, pageSize: 50, totalCount: 1, totalPages: 1 };

describe('AdminUserDirectory', () => {
  beforeEach(() => {
    api.listAdminUsers.mockResolvedValue(userPage); api.listAdminLicenses.mockResolvedValue(licensePage);
    api.getAdminUser.mockResolvedValue({ user: { summary: userPage.items[0], profile: null, managedEventCount: 12, licenses: [] }, onboardingState: 'Active', recentSignIns: [] });
    api.getAdminLicense.mockResolvedValue({ summary: licensePage.items[0], audit: [] }); api.revokeLicense.mockResolvedValue({ outcome: 'Revoked' });
  });

  it('debounces email search and opens authoritative user detail', async () => {
    render(<AdminUserDirectory />); await screen.findByText('user@example.com');
    await userEvent.type(screen.getByLabelText('Kullanıcı e-postası ara'), 'student@');
    await waitFor(() => expect(api.listAdminUsers).toHaveBeenCalledWith(expect.objectContaining({ search: 'student@', pageSize: 50 })), { timeout: 1000 });
    await userEvent.click(screen.getByText('user@example.com'));
    expect(await screen.findByText('Yönetilen etkinlik')).toBeInTheDocument();
    expect(screen.getByText('12')).toBeInTheDocument();
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
