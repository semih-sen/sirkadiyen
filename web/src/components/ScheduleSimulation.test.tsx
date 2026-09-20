import { render, screen, waitFor, within } from '@testing-library/react';
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
    missingRequiredSelectors: [],
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
    // The zoom is remembered per viewer now, and jsdom keeps storage between tests.
    localStorage.clear();
    api.getProfileOptions.mockResolvedValue(profileOptions);
    api.simulateCohortWeek.mockResolvedValue(week());
  });

  it('asks for the week before any dimension is chosen', async () => {
    render(<ScheduleSimulation />);

    await screen.findByLabelText('Anatomi grubu');

    // Nothing is stated yet, so the answer is the programme-wide lessons and nothing else.
    await waitFor(() => expect(api.simulateCohortWeek).toHaveBeenCalled());
    expect(lastQuery()).toMatchObject({ classYear: 2, selectors: {} });
    expect(screen.getByLabelText('Anatomi grubu')).toHaveValue('');
  });

  it('explains which lessons a dimension left unchosen is hiding', async () => {
    api.simulateCohortWeek.mockResolvedValue(
      week({ cohortYearEventCount: 8, events: [lesson], missingRequiredSelectors: ['anatomyGroup'] }),
    );
    render(<ScheduleSimulation />);
    await screen.findByLabelText('Anatomi grubu');

    const notice = await screen.findByText(/Henüz seçilmeyen boyutlar/);
    expect(notice).toHaveTextContent('Anatomi grubu');
    expect(notice).toHaveTextContent('gösterilmiyor');
  });

  it('says nothing about missing dimensions once the cohort is complete', async () => {
    api.simulateCohortWeek.mockResolvedValue(
      week({ cohortYearEventCount: 8, events: [lesson], missingRequiredSelectors: [] }),
    );
    render(<ScheduleSimulation />);
    await screen.findByLabelText('Anatomi grubu');
    await userEvent.selectOptions(screen.getByLabelText('Anatomi grubu'), '1');

    await screen.findByTestId('week-grid');
    expect(screen.queryByText(/Henüz seçilmeyen boyutlar/)).not.toBeInTheDocument();
  });

  it('re-asks with the narrowed cohort as each dimension is chosen', async () => {
    render(<ScheduleSimulation />);
    await screen.findByLabelText('Anatomi grubu');
    await waitFor(() => expect(lastQuery()?.selectors).toEqual({}));

    await userEvent.selectOptions(screen.getByLabelText('Anatomi grubu'), '2');

    await waitFor(() => expect(lastQuery()?.selectors).toEqual({ anatomyGroup: '2' }));
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

    await userEvent.selectOptions(screen.getByLabelText('Müfredat grubu'), '3-B');

    // A2 belongs to the A rotation; carrying it into 3-B would state a cohort nobody is in.
    await waitFor(() =>
      expect(screen.getByLabelText('Öğretim üyesi uygulama grubu')).toHaveValue(''));
    // The partial cohort is still asked about — just without the value that no longer applies.
    await waitFor(() => expect(lastQuery()?.selectors).toEqual({ curriculumGroup: '3-B' }));

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
    api.simulateCohortWeek.mockRejectedValue(failure);

    render(<ScheduleSimulation />);
    await screen.findByLabelText('Anatomi grubu');

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

    // The lesson is rendered twice — once in the grid, once in the phone's agenda list — and
    // the stylesheet shows one of them. Scope the click so the test says which it means.
    const grid = await screen.findByTestId('week-grid');
    await userEvent.click(within(grid).getByRole('button', { name: /Anatomi/ }));

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
    expect(lastQuery()).not.toMatchObject({ programLanguage: 'English' });
  });

  it('offers the same week as a grid and as a list, for the desktop and the phone', async () => {
    api.simulateCohortWeek.mockResolvedValue(
      week({ cohortYearEventCount: 1, events: [lesson] }),
    );
    render(<ScheduleSimulation />);
    await screen.findByLabelText('Anatomi grubu');
    await userEvent.selectOptions(screen.getByLabelText('Anatomi grubu'), '1');

    const grid = await screen.findByTestId('week-grid');
    const agenda = screen.getByTestId('week-agenda');
    expect(within(grid).getByRole('button', { name: /Anatomi/ })).toBeInTheDocument();
    expect(within(agenda).getByRole('button', { name: /Anatomi/ })).toBeInTheDocument();
  });

  it('draws neither view for a week with nothing in it', async () => {
    // The sentence below already says the week is empty; an empty grid and seven "Ders yok"
    // rows would say it twice more.
    api.simulateCohortWeek.mockResolvedValue(week({ cohortYearEventCount: 40 }));
    render(<ScheduleSimulation />);
    await screen.findByLabelText('Anatomi grubu');
    await userEvent.selectOptions(screen.getByLabelText('Anatomi grubu'), '1');

    await screen.findByText(/Bu hafta bu kitle için yayımlanmış ders yok/);
    expect(screen.queryByTestId('week-grid')).not.toBeInTheDocument();
    expect(screen.queryByTestId('week-agenda')).not.toBeInTheDocument();
  });

  it('changes the row height when a zoom step is chosen', async () => {
    api.simulateCohortWeek.mockResolvedValue(
      week({ cohortYearEventCount: 1, events: [lesson] }),
    );
    render(<ScheduleSimulation />);
    await screen.findByLabelText('Anatomi grubu');
    await userEvent.selectOptions(screen.getByLabelText('Anatomi grubu'), '1');

    const grid = await screen.findByTestId('week-grid');
    const heightAt = () => grid.style.getPropertyValue('--week-hour-height');
    const normal = heightAt();

    await userEvent.click(screen.getByRole('button', { name: 'Geniş' }));
    expect(Number.parseInt(heightAt(), 10)).toBeGreaterThan(Number.parseInt(normal, 10));

    await userEvent.click(screen.getByRole('button', { name: 'Sık' }));
    expect(Number.parseInt(heightAt(), 10)).toBeLessThan(Number.parseInt(normal, 10));
  });

  it('marks the chosen zoom step as pressed', async () => {
    api.simulateCohortWeek.mockResolvedValue(
      week({ cohortYearEventCount: 1, events: [lesson] }),
    );
    render(<ScheduleSimulation />);
    await screen.findByLabelText('Anatomi grubu');
    await userEvent.selectOptions(screen.getByLabelText('Anatomi grubu'), '1');

    expect(screen.getByRole('button', { name: 'Normal' })).toHaveAttribute('aria-pressed', 'true');

    await userEvent.click(screen.getByRole('button', { name: 'Sık' }));

    expect(screen.getByRole('button', { name: 'Sık' })).toHaveAttribute('aria-pressed', 'true');
    expect(screen.getByRole('button', { name: 'Normal' })).toHaveAttribute('aria-pressed', 'false');
  });

  it('remembers the zoom for the next visit', async () => {
    api.simulateCohortWeek.mockResolvedValue(
      week({ cohortYearEventCount: 1, events: [lesson] }),
    );
    const first = render(<ScheduleSimulation />);
    await screen.findByLabelText('Anatomi grubu');
    await screen.findByTestId('week-grid');

    await userEvent.click(screen.getByRole('button', { name: 'Geniş' }));
    first.unmount();

    render(<ScheduleSimulation />);
    await screen.findByLabelText('Anatomi grubu');

    await waitFor(() =>
      expect(screen.getByRole('button', { name: 'Geniş' })).toHaveAttribute('aria-pressed', 'true'));
  });

  it('falls back to the default when nothing was remembered', async () => {
    api.simulateCohortWeek.mockResolvedValue(
      week({ cohortYearEventCount: 1, events: [lesson] }),
    );
    render(<ScheduleSimulation />);
    await screen.findByLabelText('Anatomi grubu');

    expect(screen.getByRole('button', { name: 'Normal' })).toHaveAttribute('aria-pressed', 'true');
  });

  it('ignores a stored value that is not a zoom step', async () => {
    // Storage is shared with whatever else the browser holds and can be edited by hand.
    localStorage.setItem('sirkadiyen.scheduleSimulation.zoom', 'enormous');
    api.simulateCohortWeek.mockResolvedValue(
      week({ cohortYearEventCount: 1, events: [lesson] }),
    );
    render(<ScheduleSimulation />);
    await screen.findByLabelText('Anatomi grubu');

    expect(screen.getByRole('button', { name: 'Normal' })).toHaveAttribute('aria-pressed', 'true');
  });

  it('keeps the zoom when the operator pages to another week', async () => {
    api.simulateCohortWeek.mockResolvedValue(
      week({ cohortYearEventCount: 1, events: [lesson] }),
    );
    render(<ScheduleSimulation />);
    await screen.findByLabelText('Anatomi grubu');
    await userEvent.selectOptions(screen.getByLabelText('Anatomi grubu'), '1');
    await screen.findByTestId('week-grid');

    await userEvent.click(screen.getByRole('button', { name: 'Geniş' }));
    await userEvent.click(screen.getByRole('button', { name: /Sonraki hafta/ }));

    await waitFor(() =>
      expect(screen.getByRole('button', { name: 'Geniş' })).toHaveAttribute('aria-pressed', 'true'));
  });
});
