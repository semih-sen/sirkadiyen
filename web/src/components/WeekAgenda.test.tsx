import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { WeekAgenda } from './WeekAgenda';
import type { CohortSimulationEvent } from '@/lib/types';

const WEEK_START = '2026-09-21';
const TODAY = '2026-09-23';

let idCounter = 0;

function event(overrides: Partial<CohortSimulationEvent> = {}): CohortSimulationEvent {
  idCounter += 1;
  return {
    summary: 'Anatomi',
    description: null,
    location: 'SAMİ ZAN AMFİSİ',
    label: { id: 'label', name: 'Anatomi AD', backgroundColor: '#D50000' },
    localDate: WEEK_START,
    startLocalTime: '09:20:00',
    endLocalTime: '10:00:00',
    isAllDay: false,
    timeZoneId: 'Europe/Istanbul',
    ...overrides,
    raw: {
      canonicalRecordId: `record-${idCounter}`,
      scheduleRevisionId: 'revision-1',
      sourceId: 'G3-TR-B-ANNUAL',
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

function renderAgenda(events: CohortSimulationEvent[], onSelect = vi.fn()) {
  const view = render(
    <WeekAgenda weekStart={WEEK_START} events={events} today={TODAY} onSelect={onSelect} />,
  );
  return { onSelect, ...view };
}

describe('WeekAgenda', () => {
  it('lists all seven days in order', () => {
    renderAgenda([]);

    const days = screen.getAllByRole('listitem').slice(0, 7);
    expect(within(days[0]).getByText('Pazartesi')).toBeInTheDocument();
    expect(within(days[6]).getByText('Pazar')).toBeInTheDocument();
  });

  it('says a day is empty rather than skipping it', () => {
    // "Saturday has nothing" and "I scrolled past Saturday" look identical otherwise.
    renderAgenda([event()]);

    expect(screen.getAllByText('Ders yok')).toHaveLength(6);
  });

  it('shows a lesson in full, with no truncation to undo', () => {
    renderAgenda([
      event({ summary: '2-Folik asit metabolizması ve megaloblastik anemiler' }),
    ]);

    const item = screen.getByRole('button', { name: /Folik asit/ });
    expect(item).toHaveTextContent('2-Folik asit metabolizması ve megaloblastik anemiler');
    expect(item).toHaveTextContent('09:20');
    expect(item).toHaveTextContent('10:00');
    expect(item).toHaveTextContent('SAMİ ZAN AMFİSİ');
  });

  it('orders a day by when its lessons happen, all-day first', () => {
    renderAgenda([
      event({ summary: 'Öğleden sonra', startLocalTime: '14:00:00', endLocalTime: '14:40:00' }),
      event({ summary: 'Sabah', startLocalTime: '09:00:00', endLocalTime: '09:40:00' }),
      event({ summary: 'Resmî tatil', isAllDay: true, startLocalTime: null, endLocalTime: null }),
    ]);

    const titles = screen.getAllByRole('button').map((item) => item.textContent ?? '');
    expect(titles[0]).toContain('Resmî tatil');
    expect(titles[1]).toContain('Sabah');
    expect(titles[2]).toContain('Öğleden sonra');
  });

  it('labels an all-day item as such instead of inventing hours', () => {
    renderAgenda([
      event({ summary: 'Resmî tatil', isAllDay: true, startLocalTime: null, endLocalTime: null }),
    ]);

    expect(screen.getByRole('button', { name: /Resmî tatil/ })).toHaveTextContent('Tüm gün');
  });

  it('marks today', () => {
    renderAgenda([]);

    expect(screen.getByText('Bugün')).toBeInTheDocument();
  });

  it('reports the lesson that was tapped', async () => {
    const chosen = event({ summary: 'Fizyoloji' });
    const { onSelect } = renderAgenda([chosen]);

    await userEvent.click(screen.getByRole('button', { name: /Fizyoloji/ }));

    expect(onSelect).toHaveBeenCalledWith(chosen);
  });

  it('marks the selected lesson for assistive technology', () => {
    const chosen = event({ summary: 'Biyokimya' });
    render(
      <WeekAgenda
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
    renderAgenda([event({ summary: 'Çakışan', raw: { sharesStableIdentity: true } as never })]);

    expect(screen.getByRole('button', { name: /Çakışan/ })).toHaveTextContent('⚠');
  });

  it('puts each lesson under the day it falls on', () => {
    renderAgenda([
      event({ summary: 'Pazartesi dersi', localDate: '2026-09-21' }),
      event({ summary: 'Cuma dersi', localDate: '2026-09-25' }),
    ]);

    const days = screen.getAllByRole('listitem').filter(
      (item) => item.className.includes('week-agenda__day'),
    );
    expect(within(days[0]).getByRole('button', { name: /Pazartesi dersi/ })).toBeInTheDocument();
    expect(within(days[4]).getByRole('button', { name: /Cuma dersi/ })).toBeInTheDocument();
  });
});
