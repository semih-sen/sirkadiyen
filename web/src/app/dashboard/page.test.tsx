import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ApiError } from '@/lib/api';
import DashboardPage from './page';

const api = vi.hoisted(() => ({
  getSyncProgress: vi.fn(), getProfile: vi.fn(), getUpcomingSchedule: vi.fn(), getScheduleChanges: vi.fn(), getLicenseStatus: vi.fn(), requestReconciliation: vi.fn(), logout: vi.fn(),
}));
vi.mock('@/lib/api', async (original) => ({ ...(await original<typeof import('@/lib/api')>()), ...api }));
vi.mock('next/navigation', () => ({ useRouter: () => ({ replace: vi.fn() }) }));
vi.mock('@/components/SessionProvider', () => ({ useSession: () => ({ user: { email: 'student@example.com', displayName: 'Student' }, setUser: vi.fn() }) }));
vi.mock('@/components/DepartmentColorEditor', () => ({ DepartmentColorEditor: () => <div>Renkler</div> }));
vi.mock('@/components/OnboardingGate', () => ({ OnboardingGate: ({ children }: { children: React.ReactNode }) => children }));

describe('Dashboard', () => {
  beforeEach(() => {
    api.getSyncProgress.mockResolvedValue({ initialSyncState: 'Completed', hasManagedCalendar: true, mappedEventCount: 2, createdEventCount: 1, updatedEventCount: 1, lastWrittenAtUtc: '2026-08-04T08:00:00Z', onboarding: {} });
    api.getProfile.mockResolvedValue({ classYear: 2, programLanguage: 'Turkish', selectors: {}, studentNumber: '123', academicYear: '2025-2026', userId: 'u', selectorSchemaVersion: '1', updatedAtUtc: '2026-08-04T00:00:00Z' });
    api.getUpcomingSchedule.mockResolvedValue([{ stableIdentity: 'e1', title: 'Resmî tatil', localDate: '2026-08-05', isAllDay: true, timeZoneId: 'Europe/Istanbul', eventType: 'Holiday', departments: [] }]);
    api.getScheduleChanges.mockResolvedValue([]); api.getLicenseStatus.mockResolvedValue({ state: 'Active', activatedAtUtc: '2026-08-01T10:00:00Z' }); api.requestReconciliation.mockResolvedValue({ requested: true });
  });

  it('renders authoritative counts, empty changes and all-day events', async () => {
    render(<DashboardPage />);
    expect((await screen.findAllByText('Resmî tatil')).length).toBeGreaterThan(0);
    expect(screen.getAllByText(/Tüm gün/).length).toBeGreaterThan(0);
    expect(screen.getByText('Gösterilecek yakın tarihli değişiklik yok.')).toBeInTheDocument();
    expect(screen.getByText('Takvimdeki toplam ders')).toBeInTheDocument();
  });

  it('marks every card with a mobile-order cell hook and offers the Google Takvim app links', async () => {
    const { container } = render(<DashboardPage />);
    await screen.findByText('Akademik profil');

    // Mobile order is CSS-driven (`.dashboard-grid > .stack { display: contents }`
    // plus `order`), so the contract the markup owes is the cell hook on every card.
    const cells = Array.from(container.querySelectorAll('.dash-cell'));
    const ordered = cells.map((cell) => Array.from(cell.classList).find((name) => name.startsWith('dash-cell--')));
    expect(ordered).toContain('dash-cell--profile');
    expect(ordered).toContain('dash-cell--app');
    expect(new Set(ordered).size).toBe(ordered.length);

    expect(screen.getByRole('link', { name: /Google Play/ })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /App Store/ })).toBeInTheDocument();
  });

  it('reports rate limiting for reconciliation', async () => {
    api.requestReconciliation.mockRejectedValue(new ApiError(429, null, 'rate limited'));
    render(<DashboardPage />);
    const button = await screen.findByRole('button', { name: 'Onarım talebi oluştur' });
    await userEvent.click(button);
    await waitFor(() => expect(screen.getByText(/Saatlik onarım talebi sınırına ulaşıldı/)).toBeInTheDocument());
  });
});
