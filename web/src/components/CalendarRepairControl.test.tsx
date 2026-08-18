import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { CalendarRepairControl } from './CalendarRepairControl';

const api = vi.hoisted(() => ({
  previewCalendarRepair: vi.fn(),
  requestCalendarRepair: vi.fn(),
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
  scope: { academicYear: '2026-2027', classYear: 3, programLanguage: 'Turkish' },
  users: [
    { userId: 'user-1', surplusEventCount: 7, missingEventCount: 0, untouchableRetiredCount: 0 },
    { userId: 'user-2', surplusEventCount: 7, missingEventCount: 1, untouchableRetiredCount: 2 },
  ],
  cohortUserCount: 40,
  totalSurplusEvents: 14,
  totalMissingEvents: 1,
  totalUntouchableRetired: 3,
  planHash: 'abcdef0123456789abcdef',
};

const emptyPlan = { ...plan, users: [], totalSurplusEvents: 0, totalMissingEvents: 0 };

describe('CalendarRepairControl', () => {
  beforeEach(() => {
    api.previewCalendarRepair.mockReset();
    api.requestCalendarRepair.mockReset();
    api.previewCalendarRepair.mockResolvedValue(plan);
    api.requestCalendarRepair.mockResolvedValue({
      outcome: 'Requested',
      usersRequested: 2,
      plan,
    });
  });

  it('shows what the repair would delete, write and deliberately leave alone', async () => {
    const user = userEvent.setup();
    render(<CalendarRepairControl />);

    await user.click(screen.getByRole('button', { name: 'Ön izleme al' }));

    expect(await screen.findByText('14')).toBeInTheDocument();
    expect(screen.getByText('etkinlik silinecek')).toBeInTheDocument();
    expect(screen.getByText('etkinlik yazılacak')).toBeInTheDocument();
    // The untouchable count is the ADR-089 boundary made visible, not a footnote.
    expect(screen.getByText('3')).toBeInTheDocument();
    expect(screen.getByText(/olduğu gibi bırakılacak/)).toBeInTheDocument();
  });

  it('requires a reason before the destructive confirmation is enabled', async () => {
    const user = userEvent.setup();
    render(<CalendarRepairControl />);
    await user.click(screen.getByRole('button', { name: 'Ön izleme al' }));

    const confirm = await screen.findByRole('button', { name: '2 takvimi düzelt' });
    expect(confirm).toBeDisabled();

    await user.type(screen.getByLabelText('Düzeltme gerekçesi'), 'ADR-109 artıkları');
    expect(confirm).toBeEnabled();
  });

  it('sends the previewed plan hash with the confirmation', async () => {
    const user = userEvent.setup();
    render(<CalendarRepairControl />);
    await user.click(screen.getByRole('button', { name: 'Ön izleme al' }));
    await user.type(
      await screen.findByLabelText('Düzeltme gerekçesi'),
      'ADR-109 artıkları temizleniyor',
    );
    await user.click(screen.getByRole('button', { name: '2 takvimi düzelt' }));

    expect(api.requestCalendarRepair).toHaveBeenCalledWith(
      { academicYear: '2026-2027', classYear: 3, programLanguage: 'Turkish' },
      'abcdef0123456789abcdef',
      'ADR-109 artıkları temizleniyor',
    );
    expect(await screen.findByText(/2 öğrencinin takvimi/)).toBeInTheDocument();
  });

  it('drops a previewed plan when the scope is edited', async () => {
    // The hash was computed for the old cohort, so leaving it attached to a changed form would
    // let an operator confirm a plan they are no longer looking at.
    const user = userEvent.setup();
    render(<CalendarRepairControl />);
    await user.click(screen.getByRole('button', { name: 'Ön izleme al' }));
    expect(await screen.findByRole('button', { name: '2 takvimi düzelt' })).toBeInTheDocument();

    await user.selectOptions(screen.getByLabelText('Dönem'), '2');

    expect(screen.queryByRole('button', { name: '2 takvimi düzelt' })).not.toBeInTheDocument();
    expect(screen.queryByLabelText('Düzeltme gerekçesi')).not.toBeInTheDocument();
  });

  it('offers no confirmation when the cohort is already correct', async () => {
    api.previewCalendarRepair.mockResolvedValue(emptyPlan);
    const user = userEvent.setup();
    render(<CalendarRepairControl />);

    await user.click(screen.getByRole('button', { name: 'Ön izleme al' }));

    expect(await screen.findByText(/Yakınsanacak bir şey yok/)).toBeInTheDocument();
    expect(screen.queryByLabelText('Düzeltme gerekçesi')).not.toBeInTheDocument();
  });

  it('surfaces a stale-plan rejection and clears the plan so a fresh preview is required', async () => {
    api.requestCalendarRepair.mockRejectedValue(
      new api.ApiError(409, null, 'Onayladığınız plan artık geçerli değil.'),
    );
    const user = userEvent.setup();
    render(<CalendarRepairControl />);
    await user.click(screen.getByRole('button', { name: 'Ön izleme al' }));
    await user.type(await screen.findByLabelText('Düzeltme gerekçesi'), 'gerekçe');
    await user.click(screen.getByRole('button', { name: '2 takvimi düzelt' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('artık geçerli değil');
    expect(screen.queryByRole('button', { name: '2 takvimi düzelt' })).not.toBeInTheDocument();
  });
});
