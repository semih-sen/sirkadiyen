import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { WeekCalendarGrid } from './WeekCalendarGrid';
import type { CohortSimulationEvent } from '@/lib/types';

const WEEK_START = '2026-09-21';
const TODAY = '2026-09-23';

let idCounter = 0;

function event(overrides: Partial<CohortSimulationEvent> = {}): CohortSimulationEvent {
  idCounter += 1;
  return {
    summary: 'Anatomi',
    description: null,
    location: 'Amfi 1',
    label: { id: 'label', name: 'Anatomi AD', backgroundColor: '#D50000' },
    localDate: WEEK_START,
    startLocalTime: '09:00:00',
    endLocalTime: '10:00:00',
    isAllDay: false,
    timeZoneId: 'Europe/Istanbul',
    ...overrides,
    raw: {
      canonicalRecordId: `record-${idCounter}`,
      scheduleRevisionId: 'revision-1',
      sourceId: 'G2-TR-ANNUAL',
      candidateId: 'cand-1',
      stableIdentity: `identity-${idCounter}`,
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
      ...overrides.raw,
    },
  };
}

function renderGrid(events: CohortSimulationEvent[], onSelect = vi.fn()) {
  render(
    <WeekCalendarGrid
      weekStart={WEEK_START}
      events={events}
      today={TODAY}
      onSelect={onSelect}
    />,
  );
  return onSelect;
}

describe('WeekCalendarGrid', () => {
  it('lays out the seven days of the week', () => {
    renderGrid([]);

    for (const day of ['Pazartesi', 'Salı', 'Çarşamba', 'Perşembe', 'Cuma', 'Cumartesi', 'Pazar']) {
      expect(screen.getByText(day)).toBeInTheDocument();
    }
    expect(screen.getByText('21')).toBeInTheDocument();
    expect(screen.getByText('27')).toBeInTheDocument();
  });

  it('renders a timed lesson as a button carrying its hours and place', () => {
    renderGrid([event({ summary: 'Histoloji' })]);

    const chip = screen.getByRole('button', { name: /Histoloji/ });
    expect(chip).toHaveTextContent('09:00–10:00');
    expect(chip).toHaveTextContent('Amfi 1');
  });

  it('positions a lesson by its hours rather than its order', () => {
    renderGrid([event({ startLocalTime: '10:00:00', endLocalTime: '11:00:00' })]);

    const chip = screen.getByRole('button', { name: /Anatomi/ });
    // The grid opens at 10:00 for a week holding only this lesson, so it fills the single row.
    expect(chip.style.top).toBe('0%');
    expect(chip.style.height).toBe('100%');
    expect(chip.style.width).toBe('100%');
  });

  it('halves two overlapping lessons instead of stacking them', () => {
    renderGrid([
      event({ summary: 'İlk', startLocalTime: '09:00:00', endLocalTime: '11:00:00' }),
      event({ summary: 'İkinci', startLocalTime: '10:00:00', endLocalTime: '12:00:00' }),
    ]);

    expect(screen.getByRole('button', { name: /İlk/ }).style.width).toBe('50%');
    const second = screen.getByRole('button', { name: /İkinci/ });
    expect(second.style.width).toBe('50%');
    expect(second.style.left).toBe('50%');
  });

  it('puts an all-day item in the all-day strip and not in the timed body', () => {
    renderGrid([
      event({ summary: 'Resmî tatil', isAllDay: true, startLocalTime: null, endLocalTime: null }),
    ]);

    const chip = screen.getByRole('button', { name: /Resmî tatil/ });
    expect(chip).toHaveTextContent('Tüm gün');
    expect(chip.className).toContain('week-event--allday');
    // Laid out by the strip's flow, not absolutely positioned into an hour.
    expect(chip.style.top).toBe('');
  });

  it('keeps the all-day strip in place for a week that has none', () => {
    renderGrid([event()]);

    // Otherwise the columns jump by a row as the operator pages between weeks.
    expect(screen.getByText('Tüm gün')).toBeInTheDocument();
  });

  it('marks today so an operator can find the current week at a glance', () => {
    const { container } = render(
      <WeekCalendarGrid weekStart={WEEK_START} events={[]} today={TODAY} onSelect={vi.fn()} />,
    );

    expect(container.querySelectorAll('[data-today="true"]').length).toBeGreaterThan(0);
  });

  it('reports the lesson that was clicked', async () => {
    const chosen = event({ summary: 'Fizyoloji' });
    const onSelect = renderGrid([chosen]);

    await userEvent.click(screen.getByRole('button', { name: /Fizyoloji/ }));

    expect(onSelect).toHaveBeenCalledWith(chosen);
  });

  it('marks the selected lesson for assistive technology too', () => {
    const chosen = event({ summary: 'Biyokimya' });
    render(
      <WeekCalendarGrid
        weekStart={WEEK_START}
        events={[chosen]}
        today={TODAY}
        selectedId={chosen.raw.canonicalRecordId}
        onSelect={vi.fn()}
      />,
    );

    expect(screen.getByRole('button', { name: /Biyokimya/ })).toHaveAttribute('aria-pressed', 'true');
  });

  it('flags a lesson that shares a stable identity with another', () => {
    renderGrid([
      event({ summary: 'Çakışan', raw: { sharesStableIdentity: true } as never }),
    ]);

    expect(screen.getByRole('button', { name: /Çakışan/ })).toHaveTextContent('⚠');
  });

  it('places each lesson under the day it falls on', () => {
    const { container } = render(
      <WeekCalendarGrid
        weekStart={WEEK_START}
        events={[
          event({ summary: 'Pazartesi dersi', localDate: '2026-09-21' }),
          event({ summary: 'Cuma dersi', localDate: '2026-09-25' }),
        ]}
        today={TODAY}
        onSelect={vi.fn()}
      />,
    );

    const columns = container.querySelectorAll('.week-grid__day');
    expect(within(columns[0] as HTMLElement).getByRole('button', { name: /Pazartesi dersi/ }))
      .toBeInTheDocument();
    expect(within(columns[4] as HTMLElement).getByRole('button', { name: /Cuma dersi/ }))
      .toBeInTheDocument();
  });

  describe('fitting text to the box', () => {
    // A week whose lessons span 08:00-18:00 gives a stable grid to measure against: at the
    // default zoom each hour is 84px, so a 40-minute lesson is 56px and a 20-minute one is 28px.
    const spanner = () => event({
      summary: 'Gün boyu', startLocalTime: '08:00:00', endLocalTime: '18:00:00',
      localDate: '2026-09-26',
    });

    it('shows title, hours and place for a 40-minute lesson at the default zoom', () => {
      renderGrid([
        spanner(),
        event({ summary: 'Kırk dakika', startLocalTime: '09:00:00', endLocalTime: '09:40:00' }),
      ]);

      const chip = screen.getByRole('button', { name: /Kırk dakika/ });
      expect(chip).toHaveAttribute('data-density', 'full');
      expect(chip).toHaveTextContent('09:00–09:40');
      expect(chip).toHaveTextContent('Amfi 1');
    });

    it('drops the place but keeps the hours in a medium box', () => {
      renderGrid([
        spanner(),
        event({ summary: 'Otuz dakika', startLocalTime: '09:00:00', endLocalTime: '09:30:00' }),
      ]);

      const chip = screen.getByRole('button', { name: /Otuz dakika/ });
      expect(chip).toHaveAttribute('data-density', 'compact');
      expect(chip).toHaveTextContent('09:00–09:30');
      expect(chip).not.toHaveTextContent('Amfi 1');
    });

    it('gives the shortest box entirely to the lesson name', () => {
      // The hours are readable from where the chip sits; the name is not readable anywhere else.
      renderGrid([
        spanner(),
        event({ summary: 'Yirmi dakika', startLocalTime: '09:00:00', endLocalTime: '09:20:00' }),
      ]);

      const chip = screen.getByRole('button', { name: /Yirmi dakika/ });
      expect(chip).toHaveAttribute('data-density', 'tiny');
      expect(chip).not.toHaveTextContent('09:00');
      expect(chip).toHaveTextContent('Yirmi dakika');
    });

    it('keeps the whole label reachable even when the box hides part of it', () => {
      renderGrid([
        spanner(),
        event({ summary: 'Yirmi dakika', startLocalTime: '09:00:00', endLocalTime: '09:20:00' }),
      ]);

      expect(screen.getByRole('button', { name: /Yirmi dakika/ }))
        .toHaveAttribute('title', 'Yirmi dakika · 09:00–09:20 · Amfi 1');
    });

    it('gives a lesson more room at a wider zoom and less at a denser one', () => {
      const lessons = [
        spanner(),
        event({ summary: 'Yirmi dakika', startLocalTime: '09:00:00', endLocalTime: '09:20:00' }),
      ];

      const { unmount } = render(
        <WeekCalendarGrid
          weekStart={WEEK_START} events={lessons} today={TODAY} zoom="wide" onSelect={vi.fn()}
        />,
      );
      expect(screen.getByRole('button', { name: /Yirmi dakika/ }))
        .toHaveAttribute('data-density', 'compact');
      unmount();

      render(
        <WeekCalendarGrid
          weekStart={WEEK_START} events={lessons} today={TODAY} zoom="compact" onSelect={vi.fn()}
        />,
      );
      expect(screen.getByRole('button', { name: /Yirmi dakika/ }))
        .toHaveAttribute('data-density', 'tiny');
    });

    it('makes the body taller at a wider zoom', () => {
      const { container, unmount } = render(
        <WeekCalendarGrid
          weekStart={WEEK_START} events={[spanner()]} today={TODAY} zoom="compact" onSelect={vi.fn()}
        />,
      );
      const dense = (container.querySelector('.week-grid__body') as HTMLElement).style.height;
      unmount();

      const { container: wideContainer } = render(
        <WeekCalendarGrid
          weekStart={WEEK_START} events={[spanner()]} today={TODAY} zoom="wide" onSelect={vi.fn()}
        />,
      );
      const airy = (wideContainer.querySelector('.week-grid__body') as HTMLElement).style.height;

      expect(Number.parseInt(airy, 10)).toBeGreaterThan(Number.parseInt(dense, 10));
    });
  });
});
