import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ScheduleSimulation } from './ScheduleSimulation';

const api = vi.hoisted(() => ({
  getProfileOptions: vi.fn(),
  simulateCohortWeek: vi.fn(),
  listAdminUsers: vi.fn(),
  getAdminUser: vi.fn(),
  ApiError: class extends Error {
    problem?: { detail?: string };
  },
}));
vi.mock('@/lib/api', () => api);

const profileOptions = {
  academicYear: '2026-2027',
  schemaVersion: '1.6',
  programs: [
    {
      academicYear: '2026-2027',
      classYear: 2,
      programLanguage: 'Turkish',
      dimensions: [{ key: 'anatomyGroup', required: true, values: ['1', '2', '3'] }],
    },
    {
      academicYear: '2026-2027',
      classYear: 3,
      programLanguage: 'Turkish',
      dimensions: [
        { key: 'curriculumGroup', required: true, values: ['3-A', '3-B'] },
        {
          key: 'facultyPracticeGroup',
          required: true,
          dependsOn: 'curriculumGroup',
          valuesByParent: { '3-A': ['A1', 'A2'], '3-B': ['B1', 'B2'] },
        },
      ],
    },
  ],
};

function week(overrides: Record<string, unknown> = {}) {
  return {
    academicYear: '2026-2027',
    classYear: 2,
    programLanguage: 'Turkish',
    selectors: { anatomyGroup: '1' },
    weekStartLocalDate: '2026-09-21',
    weekEndLocalDate: '2026-09-27',
    timeZoneId: 'Europe/Istanbul',
    cohortYearEventCount: 0,
    publishedSourceIds: [],
    events: [],
    ...overrides,
  };
}

const lesson = {
  summary: 'Anatomi',
  description: 'Öğretim üyesi: Dr. X',
  location: 'Amfi 1',
  label: { id: 'label', name: 'Anatomi AD', backgroundColor: '#D50000' },
  localDate: '2026-09-21',
  startLocalTime: '09:00:00',
  endLocalTime: '10:00:00',
  isAllDay: false,
  timeZoneId: 'Europe/Istanbul',
  raw: {
    canonicalRecordId: 'record-1',
    scheduleRevisionId: 'revision-1',
    sourceId: 'G2-TR-ANNUAL',
    candidateId: 'cand-1',
    stableIdentity: 'identity-1',
    contentHash: 'sha256:content',
    recordStatus: 'Scheduled',
    eventType: 'Theory',
    audienceScope: 'AllStudentsInProgram',
    audienceSelectors: [],
    displayTitle: 'Anatomi',
    departments: ['anatomi'],
    confidence: 0.99,
    evidence: '[]',
    sharesStableIdentity: false,
  },
};

/** The cohort of the most recent simulation request. */
function lastQuery() {
  return api.simulateCohortWeek.mock.calls.at(-1)?.[0];
}

describe('ScheduleSimulation', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    api.getProfileOptions.mockResolvedValue(profileOptions);
    api.simulateCohortWeek.mockResolvedValue(week());
  });

  it('does not ask for a week until every required dimension is stated', async () => {
    render(<ScheduleSimulation />);

    await screen.findByLabelText('Anatomi grubu');
    expect(api.simulateCohortWeek).not.toHaveBeenCalled();
    expect(screen.getByText(/Şu boyutlar seçilmeden simülasyon çalıştırılamaz/))
      .toHaveTextContent('Anatomi grubu');
    expect(screen.getByLabelText('Anatomi grubu')).toHaveValue('');
  });

  it('asks for the stated cohort once it is complete', async () => {
    render(<ScheduleSimulation />);
    await screen.findByLabelText('Anatomi grubu');

    await userEvent.selectOptions(screen.getByLabelText('Anatomi grubu'), '2');

    await waitFor(() => expect(api.simulateCohortWeek).toHaveBeenCalled());
    expect(lastQuery()).toMatchObject({
      classYear: 2,
      programLanguage: 'Turkish',
      selectors: { anatomyGroup: '2' },
    });
  });

  it('clears a dependent dimension when its parent changes', async () => {
    render(<ScheduleSimulation />);
    await screen.findByLabelText('Anatomi grubu');

    await userEvent.selectOptions(screen.getByLabelText('Dönem'), '3');
    await userEvent.selectOptions(screen.getByLabelText('Müfredat grubu'), '3-A');
    await userEvent.selectOptions(screen.getByLabelText('Öğretim üyesi uygulama grubu'), 'A2');
    await waitFor(() => expect(lastQuery()?.selectors).toEqual({
      curriculumGroup: '3-A',
      facultyPracticeGroup: 'A2',
    }));

    const beforeSwitch = api.simulateCohortWeek.mock.calls.length;
    await userEvent.selectOptions(screen.getByLabelText('Müfredat grubu'), '3-B');

    // A2 belongs to the A rotation; carrying it into 3-B would state a cohort nobody is in.
    await waitFor(() =>
      expect(screen.getByLabelText('Öğretim üyesi uygulama grubu')).toHaveValue(''));
    // The cohort is incomplete again, so nothing is asked of the server in the meantime.
    expect(api.simulateCohortWeek.mock.calls.length).toBe(beforeSwitch);

    await userEvent.selectOptions(screen.getByLabelText('Öğretim üyesi uygulama grubu'), 'B1');

    await waitFor(() => expect(lastQuery()?.selectors).toEqual({
      curriculumGroup: '3-B',
      facultyPracticeGroup: 'B1',
    }));
  });

  it('clears every selector when the class year changes', async () => {
    render(<ScheduleSimulation />);
    await screen.findByLabelText('Anatomi grubu');
    await userEvent.selectOptions(screen.getByLabelText('Anatomi grubu'), '1');
    await waitFor(() => expect(api.simulateCohortWeek).toHaveBeenCalled());

    await userEvent.selectOptions(screen.getByLabelText('Dönem'), '3');

    expect(screen.getByLabelText('Müfredat grubu')).toHaveValue('');
    expect(screen.queryByLabelText('Anatomi grubu')).not.toBeInTheDocument();
  });

  it('moves a week at a time and returns to today', async () => {
    render(<ScheduleSimulation />);
    await screen.findByLabelText('Anatomi grubu');
    await userEvent.selectOptions(screen.getByLabelText('Anatomi grubu'), '1');
    await waitFor(() => expect(api.simulateCohortWeek).toHaveBeenCalled());

    const started = lastQuery()?.date as string;

    await userEvent.click(screen.getByRole('button', { name: /Sonraki hafta/ }));
    await waitFor(() => expect(lastQuery()?.date).not.toBe(started));
    const forward = lastQuery()?.date as string;
    expect(Date.parse(forward) - Date.parse(started)).toBe(7 * 86_400_000);

    await userEvent.click(screen.getByRole('button', { name: /Önceki hafta/ }));
    await waitFor(() => expect(lastQuery()?.date).toBe(started));

    await userEvent.click(screen.getByRole('button', { name: /Sonraki hafta/ }));
    await userEvent.click(screen.getByRole('button', { name: 'Bu hafta' }));
    await waitFor(() => expect(lastQuery()?.date).toBe(started));
  });

  it('distinguishes a quiet week from a cohort that receives nothing', async () => {
    api.simulateCohortWeek.mockResolvedValue(week({ cohortYearEventCount: 40 }));
    render(<ScheduleSimulation />);
    await screen.findByLabelText('Anatomi grubu');
    await userEvent.selectOptions(screen.getByLabelText('Anatomi grubu'), '1');

    await screen.findByText(/Bu hafta bu kitle için yayımlanmış ders yok/);
    expect(screen.getByText(/yıl genelinde/)).toHaveTextContent('40');
    expect(screen.queryByText(/hiçbir ders yok/)).not.toBeInTheDocument();
  });

  it('says so plainly when the cohort receives nothing all year', async () => {
    api.simulateCohortWeek.mockResolvedValue(week({ cohortYearEventCount: 0 }));
    render(<ScheduleSimulation />);
    await screen.findByLabelText('Anatomi grubu');
    await userEvent.selectOptions(screen.getByLabelText('Anatomi grubu'), '1');

    await screen.findByText(/hiçbir ders yok/);
    expect(screen.queryByText(/Bu hafta bu kitle için yayımlanmış ders yok/))
      .not.toBeInTheDocument();
  });

  it('names the sources that published to this cohort', async () => {
    // A program whose sources are catalogued but not yet captured otherwise looks broken.
    api.simulateCohortWeek.mockResolvedValue(
      week({ cohortYearEventCount: 5, publishedSourceIds: ['G2-TR-ANNUAL', 'G2-TR-PRACTICE'] }),
    );
    render(<ScheduleSimulation />);
    await screen.findByLabelText('Anatomi grubu');
    await userEvent.selectOptions(screen.getByLabelText('Anatomi grubu'), '1');

    await screen.findByText('G2-TR-ANNUAL, G2-TR-PRACTICE');
  });

  it('shows the academic year it resolved rather than asking for one', async () => {
    api.simulateCohortWeek.mockResolvedValue(week({ cohortYearEventCount: 1 }));
    render(<ScheduleSimulation />);
    await screen.findByLabelText('Anatomi grubu');
    await userEvent.selectOptions(screen.getByLabelText('Anatomi grubu'), '1');

    await screen.findByText(/Akademik yıl/);
    expect(screen.getByText('2026-2027')).toBeInTheDocument();
    expect(screen.queryByLabelText(/Akademik yıl/)).not.toBeInTheDocument();
    expect(lastQuery()).not.toHaveProperty('academicYear');
  });

  it('reports a failure and retries on demand', async () => {
    const failure = new api.ApiError('nope');
    failure.problem = { detail: 'Kitle geçersiz.' };
    api.simulateCohortWeek.mockRejectedValueOnce(failure);

    render(<ScheduleSimulation />);
    await screen.findByLabelText('Anatomi grubu');
    await userEvent.selectOptions(screen.getByLabelText('Anatomi grubu'), '1');

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent('Kitle geçersiz.');

    api.simulateCohortWeek.mockResolvedValue(week({ cohortYearEventCount: 3 }));
    await userEvent.click(screen.getByRole('button', { name: 'Yeniden dene' }));

    await waitFor(() => expect(screen.queryByRole('alert')).not.toBeInTheDocument());
  });

  it('opens the raw record for the lesson that was clicked', async () => {
    api.simulateCohortWeek.mockResolvedValue(
      week({ cohortYearEventCount: 1, events: [lesson] }),
    );
    render(<ScheduleSimulation />);
    await screen.findByLabelText('Anatomi grubu');
    await userEvent.selectOptions(screen.getByLabelText('Anatomi grubu'), '1');

    await userEvent.click(await screen.findByRole('button', { name: /Anatomi/ }));

    expect(await screen.findByText('identity-1')).toBeInTheDocument();
    expect(screen.getByText('G2-TR-ANNUAL')).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: 'Kapat' }));
    await waitFor(() => expect(screen.queryByText('identity-1')).not.toBeInTheDocument());
  });

  it('reports an unusable program rather than asking for a week', async () => {
    render(<ScheduleSimulation />);
    await screen.findByLabelText('Anatomi grubu');

    await userEvent.selectOptions(screen.getByLabelText('Program dili'), 'English');

    expect(await screen.findByText(/tanımlı bir program yok/)).toBeInTheDocument();
    expect(api.simulateCohortWeek).not.toHaveBeenCalled();
  });
});
