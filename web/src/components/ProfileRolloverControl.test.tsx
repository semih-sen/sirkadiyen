import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ProfileRolloverControl } from './ProfileRolloverControl';

const api = vi.hoisted(() => ({
  previewProfileRollover: vi.fn(),
  requestProfileRollover: vi.fn(),
  ApiError: class ApiError extends Error {
    status: number;

    constructor(status: number, _problem: unknown, fallback: string) {
      super(fallback);
      this.status = status;
    }
  },
}));
vi.mock('@/lib/api', () => api);

const plan = {
  scope: { fromAcademicYear: '2025-2026', classYear: 2, programLanguage: 'Turkish' },
  toAcademicYear: '2026-2027',
  toSchemaVersion: '1.3',
  users: [
    { userId: 'user-1', gainedEventCount: 412, strandedEventCount: 30, convergenceQueueable: true },
    { userId: 'user-2', gainedEventCount: 400, strandedEventCount: 0, convergenceQueueable: false },
  ],
  totalGainedEvents: 812,
  totalStrandedEvents: 30,
  profilesWithoutSyncReadyConnection: 1,
  blockedByInvalidSelectors: [],
  planHash: 'abcdef0123456789abcdef',
};

/** How the backend states that the deployed schema does not support the move. */
const unsupportedPlan = { ...plan, toAcademicYear: '', toSchemaVersion: '', users: [] };

describe('ProfileRolloverControl', () => {
  beforeEach(() => {
    api.previewProfileRollover.mockReset();
    api.requestProfileRollover.mockReset();
    api.previewProfileRollover.mockResolvedValue(plan);
    api.requestProfileRollover.mockResolvedValue({
      outcome: 'Moved',
      profilesMoved: 2,
      convergenceRequested: 1,
      plan,
    });
  });

  it('shows both years, how many profiles move and how many lessons that puts back', async () => {
    const user = userEvent.setup();
    render(<ProfileRolloverControl />);

    await user.click(screen.getByRole('button', { name: 'Ön izleme al' }));

    expect(await screen.findByText('812')).toBeInTheDocument();
    expect(screen.getByText('ders takvimlere yazılacak')).toBeInTheDocument();
    expect(screen.getByText('profil taşınacak')).toBeInTheDocument();
    // Both years, because "moved to 2026-2027" alone does not say what was left.
    expect(screen.getByText('2025-2026')).toBeInTheDocument();
    expect(screen.getByText('2026-2027')).toBeInTheDocument();
  });

  it('states that last year\'s events stay rather than being deleted', async () => {
    // The rollover deletes nothing from the year being left: convergence measures removals
    // against the new year's published identities (ADR-089). Saying so is the difference between
    // an operator expecting a clean-up and one who is surprised by leftovers.
    const user = userEvent.setup();
    render(<ProfileRolloverControl />);

    await user.click(screen.getByRole('button', { name: 'Ön izleme al' }));

    expect(await screen.findByText(/eski yıl kaydı olduğu gibi kalacak/)).toBeInTheDocument();
  });

  it('refuses to offer a confirmation when the deployed schema states no new year', async () => {
    api.previewProfileRollover.mockResolvedValue(unsupportedPlan);
    const user = userEvent.setup();
    render(<ProfileRolloverControl />);

    await user.click(screen.getByRole('button', { name: 'Ön izleme al' }));

    expect(await screen.findByText(/şemayı yayına alın/)).toBeInTheDocument();
    expect(screen.queryByLabelText('Taşıma gerekçesi')).not.toBeInTheDocument();
  });

  it('names the profiles it will not move and why', async () => {
    api.previewProfileRollover.mockResolvedValue({
      ...plan,
      blockedByInvalidSelectors: ['user-9'],
    });
    const user = userEvent.setup();
    render(<ProfileRolloverControl />);

    await user.click(screen.getByRole('button', { name: 'Ön izleme al' }));

    expect(await screen.findByText(/1 profil taşınmayacak/)).toBeInTheDocument();
    expect(screen.getByText('user-9')).toBeInTheDocument();
  });

  it('requires a reason before the move can be confirmed', async () => {
    const user = userEvent.setup();
    render(<ProfileRolloverControl />);

    await user.click(screen.getByRole('button', { name: 'Ön izleme al' }));
    const confirm = await screen.findByRole('button', {
      name: /2 profili 2026-2027 yılına taşı/,
    });

    expect(confirm).toBeDisabled();

    await user.type(screen.getByLabelText('Taşıma gerekçesi'), 'Kaynaklar 26-27 dosyalarına taşındı.');
    expect(confirm).toBeEnabled();
  });

  it('sends the previewed plan hash with the confirmation', async () => {
    // The hash is what stops an approved preview from authorizing a move of a different set of
    // students, so it must be the one the preview returned rather than anything recomputed here.
    const user = userEvent.setup();
    render(<ProfileRolloverControl />);

    await user.click(screen.getByRole('button', { name: 'Ön izleme al' }));
    await user.type(
      await screen.findByLabelText('Taşıma gerekçesi'),
      'Kaynaklar 26-27 dosyalarına taşındı.',
    );
    await user.click(screen.getByRole('button', { name: /2 profili 2026-2027 yılına taşı/ }));

    expect(api.requestProfileRollover).toHaveBeenCalledWith(
      { fromAcademicYear: '2025-2026', classYear: 2, programLanguage: 'Turkish' },
      'abcdef0123456789abcdef',
      'Kaynaklar 26-27 dosyalarına taşındı.',
    );
  });

  it('drops a previewed plan when the scope is edited', async () => {
    // A hash belongs to the scope it was computed for. Leaving it attached to a changed form
    // would let an operator confirm a move for a program they are no longer looking at.
    const user = userEvent.setup();
    render(<ProfileRolloverControl />);

    await user.click(screen.getByRole('button', { name: 'Ön izleme al' }));
    expect(await screen.findByText('812')).toBeInTheDocument();

    await user.selectOptions(screen.getByLabelText('Dönem'), '3');

    expect(screen.queryByText('812')).not.toBeInTheDocument();
    expect(screen.queryByLabelText('Taşıma gerekçesi')).not.toBeInTheDocument();
  });
});
