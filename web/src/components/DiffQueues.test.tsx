import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { DiffQueues } from './DiffQueues';
import type { ScheduleDiffSummary } from '@/lib/types';

const api = vi.hoisted(() => ({
  listDiffs: vi.fn(),
  listDiffsByDispatchState: vi.fn(),
  getDiff: vi.fn(),
  releaseDiff: vi.fn(),
  retryDiff: vi.fn(),
  ApiError: class ApiError extends Error {},
}));
vi.mock('@/lib/api', () => api);

function summary(overrides: Partial<ScheduleDiffSummary> = {}): ScheduleDiffSummary {
  return {
    scheduleDiffId: 'd1',
    sourceId: 'G1-TR-ANNUAL',
    state: 'Held',
    currentRevisionId: 'r2',
    previousRevisionId: 'r1',
    createdCount: 2,
    updatedCount: 1,
    deletedCount: 40,
    unchangedCount: 800,
    ambiguousCount: 0,
    previousRecordCount: 843,
    currentRecordCount: 803,
    createdAtUtc: '2026-08-15T09:00:00Z',
    holdReason: 'Kütlesel silme eşiği aşıldı: 40 ders kaldırılıyor.',
    isReleasable: true,
    calendarDispatchState: 'Pending',
    dispatchAttempts: 0,
    isDispatchRetriable: false,
    dispatchRetryCount: 0,
    ...overrides,
  };
}

const detail = {
  summary: summary(),
  entries: [
    {
      change: 'Deleted',
      match: 'None',
      previous: {
        recordId: 'c1',
        displayTitle: 'FİZYOLOJİ 3. DERS',
        localDate: '2026-03-04',
        startLocalTime: '10:30',
        endLocalTime: '11:20',
        isAllDay: false,
        audienceSelectors: 'practiceGroup=A',
        instructor: 'Dr. X',
        location: 'Amfi 2',
      },
    },
  ],
  actionableEntryCount: 43,
};

describe('DiffQueues', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    api.listDiffs.mockResolvedValue([summary()]);
    api.listDiffsByDispatchState.mockResolvedValue([]);
    api.getDiff.mockResolvedValue(detail);
    api.releaseDiff.mockResolvedValue({ scheduleDiffId: 'd1', released: true });
    api.retryDiff.mockResolvedValue({ scheduleDiffId: 'd1', retried: true, dispatchRetryCount: 1 });
  });

  it('releases a held diff only with a reason, after showing the lessons it deletes', async () => {
    render(<DiffQueues />);
    await userEvent.click(await screen.findByText('G1-TR-ANNUAL'));

    // The deletion evidence has to be visible before the action is offered: releasing without
    // seeing which lessons disappear is rubber-stamping.
    expect(await screen.findByText('FİZYOLOJİ 3. DERS')).toBeInTheDocument();
    expect(screen.getByText(/43 girişin ilk 1 tanesi/)).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: 'Serbest bırak ve dağıt' }));
    expect(api.releaseDiff).not.toHaveBeenCalled();
    expect(screen.getByRole('alert')).toHaveTextContent('gerekçe');

    await userEvent.type(screen.getByLabelText(/Serbest bırakma gerekçesi/), 'Fakülte teyit etti.');
    await userEvent.click(screen.getByRole('button', { name: 'Serbest bırak ve dağıt' }));
    await waitFor(() => expect(api.releaseDiff).toHaveBeenCalledWith('d1', 'Fakülte teyit etti.'));
  });

  it('refuses an ambiguity hold in words instead of offering a release', async () => {
    api.listDiffs.mockResolvedValue([
      summary({ ambiguousCount: 3, isReleasable: false, holdReason: 'Belirsiz eşleşme.' }),
    ]);
    api.getDiff.mockResolvedValue({ ...detail, summary: summary({ isReleasable: false }) });

    render(<DiffQueues />);
    await userEvent.click(await screen.findByText('G1-TR-ANNUAL'));

    expect(await screen.findByText(/serbest bırakılamaz/)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Serbest bırak ve dağıt' })).not.toBeInTheDocument();
    expect(screen.queryByLabelText(/Serbest bırakma gerekçesi/)).not.toBeInTheDocument();
  });

  it('lists the failed queue by dispatch state and surfaces the retry count', async () => {
    api.listDiffsByDispatchState.mockResolvedValue([
      summary({
        state: 'Released',
        calendarDispatchState: 'Failed',
        dispatchAttempts: 5,
        dispatchFailureReason: 'Google API kotası aşıldı.',
        isReleasable: false,
        isDispatchRetriable: true,
        dispatchRetryCount: 2,
        lastDispatchRetriedBy: 'ops@example.com',
        lastDispatchRetriedAtUtc: '2026-08-15T12:00:00Z',
      }),
    ]);

    render(<DiffQueues />);
    await userEvent.click(await screen.findByRole('tab', { name: 'Başarısız dağıtım' }));

    // The failed queue must not be fetched by review state: a terminally failed diff is still
    // Ready/Released, so the state filter would never return it.
    await waitFor(() => expect(api.listDiffsByDispatchState).toHaveBeenCalledWith('Failed'));
    expect(await screen.findByText('Google API kotası aşıldı.')).toBeInTheDocument();
    expect(screen.getByText(/operatör yeniden denemesi: 2/)).toBeInTheDocument();

    await userEvent.click(screen.getByText('G1-TR-ANNUAL'));
    await userEvent.type(await screen.findByLabelText(/Yeniden deneme gerekçesi/), 'Kota yenilendi.');
    await userEvent.click(screen.getByRole('button', { name: 'Dağıtımı yeniden dene' }));
    await waitFor(() => expect(api.retryDiff).toHaveBeenCalledWith('d1', 'Kota yenilendi.'));
  });
});
