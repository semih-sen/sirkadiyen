import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import CalendarPage from './page';

const api = vi.hoisted(() => ({
  assessCalendarRebuild: vi.fn(),
  authorizeCalendar: vi.fn(),
  getCalendarAuthorizationOptions: vi.fn(),
  rebuildCalendar: vi.fn(),
  ApiError: class extends Error {},
}));
vi.mock('@/lib/api', () => api);

const session = vi.hoisted(() => ({
  user: { onboardingState: 'ActionRequired' } as { onboardingState: string },
  refresh: vi.fn(),
}));
vi.mock('@/components/SessionProvider', () => ({
  useSession: () => session,
}));

const router = vi.hoisted(() => ({ replace: vi.fn() }));
vi.mock('next/navigation', () => ({ useRouter: () => router }));

// The gate is a session/route guard; these tests are about what the page offers once inside it.
vi.mock('@/components/OnboardingGate', () => ({
  OnboardingGate: ({ children }: { children: React.ReactNode }) => <>{children}</>,
}));

vi.mock('@/lib/google', () => ({ requestCalendarAuthorizationCode: vi.fn() }));

describe('onboarding calendar page', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    session.user = { onboardingState: 'ActionRequired' };
    session.refresh.mockResolvedValue({ onboardingState: 'ReadyForInitialSync' });
    api.assessCalendarRebuild.mockResolvedValue({
      outcome: 'Reset',
      unavailableSinceUtc: '2026-08-15T00:00:00Z',
    });
    api.rebuildCalendar.mockResolvedValue({ outcome: 'Reset', discardedMappings: 412 });
  });

  it('says the calendar is missing rather than blaming the Google grant', async () => {
    // `ActionRequired` has exactly one cause: the dedicated calendar is unreachable. Calling it a
    // revoked grant sent students to re-consent, which does not clear the flag — the loop ADR-116
    // exists to break.
    render(<CalendarPage />);

    expect(await screen.findByText(/Sirkadiyen takvimin bulunamıyor/)).toBeInTheDocument();
    expect(screen.queryByText(/Google erişimi iptal edilmiş/)).not.toBeInTheDocument();
  });

  it('offers a rebuild and routes to the sync step once the connection is reset', async () => {
    render(<CalendarPage />);

    await userEvent.click(
      await screen.findByRole('button', { name: 'Takvimimi yeniden oluştur' }),
    );

    // Stated before it is done: the old events are gone with the calendar they lived on.
    expect(screen.getByText(/eski etkinlikler geri gelmez/)).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: 'Evet, yeniden oluştur' }));

    await waitFor(() => expect(api.rebuildCalendar).toHaveBeenCalled());
    // The user starts the synchronization themselves (ADR-058), so they land on that step.
    await waitFor(() => expect(router.replace).toHaveBeenCalledWith('/onboarding/sync'));
  });

  it('does not offer a rebuild when the server says there is nothing to rebuild', async () => {
    // Asked rather than inferred from the onboarding state: a calendar that came back on its own
    // must not be offered a destructive repair.
    api.assessCalendarRebuild.mockResolvedValue({ outcome: 'NotEligible' });

    render(<CalendarPage />);

    await screen.findByText(/Sirkadiyen takvimin bulunamıyor/);
    expect(
      screen.queryByRole('button', { name: 'Takvimimi yeniden oluştur' }),
    ).not.toBeInTheDocument();
  });

  it('shows no rebuild banner at all during ordinary first-time authorization', async () => {
    session.user = { onboardingState: 'CalendarAuthorizationRequired' };

    render(<CalendarPage />);

    expect(await screen.findByRole('button', { name: 'Google ile izin ver' })).toBeInTheDocument();
    expect(screen.queryByText(/Sirkadiyen takvimin bulunamıyor/)).not.toBeInTheDocument();
    expect(api.assessCalendarRebuild).not.toHaveBeenCalled();
  });
});
