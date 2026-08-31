import { render, screen } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import SyncPage from './page';

const api = vi.hoisted(() => ({ getSyncStatus: vi.fn(), startSync: vi.fn() }));
vi.mock('@/lib/api', async (original) => ({ ...(await original<typeof import('@/lib/api')>()), ...api }));
vi.mock('next/navigation', () => ({ useRouter: () => ({ replace: vi.fn() }) }));
vi.mock('@/components/SessionProvider', () => ({
  useSession: () => ({ user: { email: 'student@example.com', onboardingState: 'InitialSyncInProgress' }, refresh: vi.fn() }),
}));
vi.mock('@/components/OnboardingGate', () => ({ OnboardingGate: ({ children }: { children: React.ReactNode }) => children }));

describe('InitialSync', () => {
  beforeEach(() => {
    api.getSyncStatus.mockResolvedValue({
      initialSyncState: 'InProgress',
      hasManagedCalendar: true,
      mappedEventCount: 12,
      onboarding: { state: 'InitialSyncInProgress' },
    });
  });

  it('recommends the Google Takvim app and links both stores while the sync runs', async () => {
    render(<SyncPage />);
    expect(await screen.findByText(/Google Takvim uygulamasını kur/)).toBeInTheDocument();
    expect(screen.getByText(/Google Takvim uygulaması üzerinden takip etmeni öneririz/)).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /Google Play/ })).toHaveAttribute(
      'href',
      'https://play.google.com/store/apps/details?id=com.google.android.calendar',
    );
    expect(screen.getByRole('link', { name: /App Store/ })).toHaveAttribute(
      'href',
      'https://apps.apple.com/app/google-calendar/id909319292',
    );
  });
});
