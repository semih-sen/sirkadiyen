import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ScheduleSimulationProfileLoader } from './ScheduleSimulationProfileLoader';

const api = vi.hoisted(() => ({
  listAdminUsers: vi.fn(),
  getAdminUser: vi.fn(),
  ApiError: class extends Error {
    problem?: { detail?: string };
  },
}));
vi.mock('@/lib/api', () => api);

function listing(items: unknown[]) {
  return { items, page: 1, pageSize: 10, totalCount: items.length, totalPages: 1 };
}

const withProfile = {
  id: 'u1',
  email: 'ogrenci@example.com',
  role: 'User',
  licenseState: 'Active',
  hasProfile: true,
  classYear: 3,
  programLanguage: 'Turkish',
  managedEventCount: 40,
  createdAtUtc: '2026-08-01T00:00:00Z',
  lastSignedInAtUtc: '2026-09-01T00:00:00Z',
};

const withoutProfile = { ...withProfile, id: 'u2', email: 'yeni@example.com', hasProfile: false, classYear: null };

describe('ScheduleSimulationProfileLoader', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    api.listAdminUsers.mockResolvedValue(listing([withProfile]));
    api.getAdminUser.mockResolvedValue({
      user: {
        profile: {
          academicYear: '2026-2027',
          classYear: 3,
          programLanguage: 'Turkish',
          studentNumber: '0101240048',
          selectorSchemaVersion: '1.6',
          selectors: { curriculumGroup: '3-A', facultyPracticeGroup: 'A5' },
          updatedAtUtc: '2026-09-01T00:00:00Z',
        },
      },
      onboardingState: 'Completed',
    });
  });

  it('searches the directory for the typed term', async () => {
    render(<ScheduleSimulationProfileLoader onLoad={vi.fn()} />);
    await userEvent.click(screen.getByRole('button', { name: /Gerçek bir kullanıcıdan yükle/ }));

    await userEvent.type(screen.getByLabelText('Ara'), 'ogrenci');

    await waitFor(() => expect(api.listAdminUsers).toHaveBeenCalledWith(
      expect.objectContaining({ search: 'ogrenci' }),
    ));
  });

  it('copies the chosen profile into the form as a cohort', async () => {
    const onLoad = vi.fn();
    render(<ScheduleSimulationProfileLoader onLoad={onLoad} />);
    await userEvent.click(screen.getByRole('button', { name: /Gerçek bir kullanıcıdan yükle/ }));

    await userEvent.click(await screen.findByRole('button', { name: /ogrenci@example.com/ }));

    await waitFor(() => expect(onLoad).toHaveBeenCalledWith({
      classYear: 3,
      programLanguage: 'Turkish',
      selectors: { curriculumGroup: '3-A', facultyPracticeGroup: 'A5' },
      email: 'ogrenci@example.com',
    }));
  });

  it('closes itself once a cohort has been copied', async () => {
    render(<ScheduleSimulationProfileLoader onLoad={vi.fn()} />);
    await userEvent.click(screen.getByRole('button', { name: /Gerçek bir kullanıcıdan yükle/ }));
    await userEvent.click(await screen.findByRole('button', { name: /ogrenci@example.com/ }));

    await waitFor(() => expect(screen.queryByLabelText('Ara')).not.toBeInTheDocument());
  });

  it('offers an account with no profile but does not let it be chosen', async () => {
    api.listAdminUsers.mockResolvedValue(listing([withoutProfile]));
    render(<ScheduleSimulationProfileLoader onLoad={vi.fn()} />);
    await userEvent.click(screen.getByRole('button', { name: /Gerçek bir kullanıcıdan yükle/ }));

    const row = await screen.findByRole('button', { name: /yeni@example.com/ });
    expect(row).toBeDisabled();
    expect(row).toHaveTextContent('Profil yok');
  });

  it('reports a directory failure rather than showing an empty list', async () => {
    const failure = new api.ApiError('nope');
    failure.problem = { detail: 'Dizin okunamadı.' };
    api.listAdminUsers.mockRejectedValue(failure);

    render(<ScheduleSimulationProfileLoader onLoad={vi.fn()} />);
    await userEvent.click(screen.getByRole('button', { name: /Gerçek bir kullanıcıdan yükle/ }));

    expect(await screen.findByRole('alert')).toHaveTextContent('Dizin okunamadı.');
  });

  it('never sends a user id to the simulation', async () => {
    const onLoad = vi.fn();
    render(<ScheduleSimulationProfileLoader onLoad={onLoad} />);
    await userEvent.click(screen.getByRole('button', { name: /Gerçek bir kullanıcıdan yükle/ }));
    await userEvent.click(await screen.findByRole('button', { name: /ogrenci@example.com/ }));

    // The account is a source of values, not a subject of the query: the cohort handed back
    // carries no identifier the simulation could be scoped to.
    await waitFor(() => expect(onLoad).toHaveBeenCalled());
    expect(onLoad.mock.calls[0][0]).not.toHaveProperty('userId');
    expect(onLoad.mock.calls[0][0]).not.toHaveProperty('id');
  });
});
