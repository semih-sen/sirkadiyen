import { beforeEach, describe, expect, it, vi } from 'vitest';
import {
  WEEK_ZOOM_LEVELS,
  WEEK_ZOOM_STORAGE_KEY,
  addDays,
  dayName,
  densityFor,
  formatWeekRange,
  gridBounds,
  hourMarks,
  isoWeekday,
  layoutDay,
  minutesOf,
  mondayOf,
  readStoredZoom,
  readableTextColor,
  storeZoom,
  weekDays,
} from '@/lib/calendarWeek';

describe('mondayOf', () => {
  // 2026-09-21 is a Monday.
  it.each([
    ['2026-09-21', 'Pazartesi'],
    ['2026-09-22', 'Salı'],
    ['2026-09-23', 'Çarşamba'],
    ['2026-09-24', 'Perşembe'],
    ['2026-09-25', 'Cuma'],
    ['2026-09-26', 'Cumartesi'],
    ['2026-09-27', 'Pazar'],
  ])('snaps %s back to its own Monday', (date) => {
    expect(mondayOf(date)).toBe('2026-09-21');
  });

  it('keeps Sunday in the week that has already started', () => {
    // The trap: Sunday is day 0 in most weekday numberings, which sends it forward a week.
    expect(isoWeekday('2026-09-27')).toBe(7);
    expect(mondayOf('2026-09-27')).toBe('2026-09-21');
  });

  it('crosses a month boundary', () => {
    expect(mondayOf('2026-10-01')).toBe('2026-09-28');
  });

  it('crosses a year boundary', () => {
    // 2026-01-01 is a Thursday.
    expect(mondayOf('2026-01-01')).toBe('2025-12-29');
  });

  it('handles a leap day', () => {
    expect(mondayOf('2028-02-29')).toBe('2028-02-28');
  });
});

describe('addDays and weekDays', () => {
  it('steps across a month end', () => {
    expect(addDays('2026-09-30', 1)).toBe('2026-10-01');
    expect(addDays('2026-10-01', -1)).toBe('2026-09-30');
  });

  it('lists the seven days of a week in order', () => {
    expect(weekDays('2026-09-21')).toEqual([
      '2026-09-21', '2026-09-22', '2026-09-23', '2026-09-24',
      '2026-09-25', '2026-09-26', '2026-09-27',
    ]);
  });

  it('names days in Turkish', () => {
    expect(dayName('2026-09-21')).toBe('Pazartesi');
    expect(dayName('2026-09-27')).toBe('Pazar');
  });
});

describe('minutesOf', () => {
  it('reads a local time as minutes past midnight', () => {
    expect(minutesOf('09:30')).toBe(570);
    expect(minutesOf('09:30:00')).toBe(570);
    expect(minutesOf('00:00')).toBe(0);
  });
});

describe('gridBounds', () => {
  it('falls back to a working day when the week is empty', () => {
    expect(gridBounds([])).toEqual({ start: 8 * 60, end: 18 * 60 });
  });

  it('rounds out to whole hours around what the week actually holds', () => {
    expect(gridBounds([{ start: minutesOf('09:30'), end: minutesOf('10:45') }]))
      .toEqual({ start: 9 * 60, end: 11 * 60 });
  });

  it('grows to fit an early session rather than clipping it', () => {
    expect(gridBounds([{ start: minutesOf('07:30'), end: minutesOf('17:00') }]))
      .toEqual({ start: 7 * 60, end: 17 * 60 });
  });

  it('never opens earlier than 07:00 or later than 23:00', () => {
    expect(gridBounds([{ start: 0, end: 24 * 60 }]))
      .toEqual({ start: 7 * 60, end: 23 * 60 });
  });

  it('keeps at least one hour of height', () => {
    const bounds = gridBounds([{ start: minutesOf('09:00'), end: minutesOf('09:30') }]);
    expect(bounds.end - bounds.start).toBeGreaterThanOrEqual(60);
  });
});

describe('hourMarks', () => {
  it('marks every hour the grid opens on', () => {
    expect(hourMarks({ start: 9 * 60, end: 12 * 60 })).toEqual([540, 600, 660]);
  });
});

interface Span { start: number; end: number }
const read = (span: Span) => span;
const at = (start: string, end: string): Span => ({ start: minutesOf(start), end: minutesOf(end) });
const GRID = { start: 8 * 60, end: 18 * 60 };

describe('layoutDay', () => {
  it('gives a lone event the full width', () => {
    const [positioned] = layoutDay([at('09:00', '10:00')], read, GRID);

    expect(positioned.widthPct).toBe(100);
    expect(positioned.leftPct).toBe(0);
    expect(positioned.topPct).toBeCloseTo(10);
    expect(positioned.heightPct).toBeCloseTo(10);
  });

  it('gives sequential events the full width each', () => {
    const laid = layoutDay([at('09:00', '10:00'), at('10:00', '11:00')], read, GRID);

    expect(laid.map((item) => item.widthPct)).toEqual([100, 100]);
    expect(laid.map((item) => item.leftPct)).toEqual([0, 0]);
  });

  it('splits two overlapping events into halves', () => {
    const laid = layoutDay([at('09:00', '11:00'), at('10:00', '12:00')], read, GRID);

    expect(laid.map((item) => item.widthPct)).toEqual([50, 50]);
    expect(laid.map((item) => item.leftPct)).toEqual([0, 50]);
  });

  it('splits three mutually overlapping events into thirds', () => {
    const laid = layoutDay(
      [at('09:00', '12:00'), at('09:30', '12:00'), at('10:00', '12:00')],
      read,
      GRID,
    );

    expect(laid.map((item) => Math.round(item.widthPct))).toEqual([33, 33, 33]);
    expect(laid.map((item) => Math.round(item.leftPct))).toEqual([0, 33, 67]);
  });

  it('reuses a freed column inside one transitive cluster', () => {
    // A 09-11, B 10-12, C 11-13. A and C never overlap, so the cluster needs two columns, not
    // three: C takes the column A has vacated. A naive "one column per event" split renders
    // three slivers and is the classic way to get this wrong.
    const laid = layoutDay(
      [at('09:00', '11:00'), at('10:00', '12:00'), at('11:00', '13:00')],
      read,
      GRID,
    );

    expect(laid.map((item) => item.widthPct)).toEqual([50, 50, 50]);
    expect(laid.map((item) => item.leftPct)).toEqual([0, 50, 0]);
  });

  it('separates clusters that do not touch', () => {
    // Two overlapping in the morning, one alone in the afternoon: the afternoon event is not
    // narrowed by a collision it has nothing to do with.
    const laid = layoutDay(
      [at('09:00', '11:00'), at('10:00', '12:00'), at('14:00', '15:00')],
      read,
      GRID,
    );

    expect(laid.map((item) => item.widthPct)).toEqual([50, 50, 100]);
  });

  it('places a contained event beside its container', () => {
    const laid = layoutDay([at('09:00', '13:00'), at('10:00', '11:00')], read, GRID);

    expect(laid.map((item) => item.widthPct)).toEqual([50, 50]);
    expect(laid[0].heightPct).toBeGreaterThan(laid[1].heightPct);
  });

  it('never produces NaN or a zero height for a degenerate span', () => {
    const [positioned] = layoutDay([{ start: minutesOf('09:00'), end: minutesOf('09:00') }], read, GRID);

    expect(Number.isFinite(positioned.topPct)).toBe(true);
    expect(Number.isFinite(positioned.heightPct)).toBe(true);
    expect(positioned.heightPct).toBeGreaterThan(0);
  });

  it('returns nothing for a day with no timed events', () => {
    expect(layoutDay([], read, GRID)).toEqual([]);
  });
});

describe('formatWeekRange', () => {
  it('collapses a month both ends share', () => {
    expect(formatWeekRange('2026-09-21')).toBe('21 – 27 Eylül 2026');
  });

  it('names both months when the week straddles them', () => {
    expect(formatWeekRange('2026-09-28')).toBe('28 Eylül – 4 Ekim 2026');
  });

  it('names both years when the week straddles them', () => {
    expect(formatWeekRange('2025-12-29')).toBe('29 Aralık 2025 – 4 Ocak 2026');
  });
});

describe('readableTextColor', () => {
  it('puts dark text on a light background and light text on a dark one', () => {
    expect(readableTextColor('#FFEB3B')).toBe('#1a1a1a');
    expect(readableTextColor('#D50000')).toBe('#ffffff');
    expect(readableTextColor('#5E35B1')).toBe('#ffffff');
  });

  it('falls back to white for an unusable value', () => {
    expect(readableTextColor('nonsense')).toBe('#ffffff');
  });
});

describe('densityFor', () => {
  it('drops to a single line when two will not fit', () => {
    expect(densityFor(20)).toBe('tiny');
    expect(densityFor(33)).toBe('tiny');
  });

  it('shows a one-line title and the hours in a medium box', () => {
    expect(densityFor(34)).toBe('compact');
    expect(densityFor(51)).toBe('compact');
  });

  it('shows everything once there is room for it', () => {
    expect(densityFor(52)).toBe('full');
    expect(densityFor(120)).toBe('full');
  });

  it('gives the 40-minute lesson a full chip at the default zoom', () => {
    // 40 minutes is the dominant teaching unit in these programmes, and the default zoom exists
    // precisely so it reads without truncation. If this flips to 'compact', the default is wrong.
    const fortyMinutes = (40 / 60) * WEEK_ZOOM_LEVELS.normal;
    expect(densityFor(fortyMinutes)).toBe('full');
  });

  it('still fits a 40-minute lesson onto two lines at the densest zoom', () => {
    const fortyMinutes = (40 / 60) * WEEK_ZOOM_LEVELS.compact;
    expect(densityFor(fortyMinutes)).toBe('compact');
  });

  it('orders the zoom steps from dense to airy', () => {
    expect(WEEK_ZOOM_LEVELS.compact).toBeLessThan(WEEK_ZOOM_LEVELS.normal);
    expect(WEEK_ZOOM_LEVELS.normal).toBeLessThan(WEEK_ZOOM_LEVELS.wide);
  });
});

describe('remembering the zoom', () => {
  beforeEach(() => localStorage.clear());

  it('reads back what was written', () => {
    storeZoom('wide');

    expect(readStoredZoom()).toBe('wide');
  });

  it('reports nothing when the viewer has no preference yet', () => {
    expect(readStoredZoom()).toBeNull();
  });

  it('ignores a value that is not a zoom step', () => {
    localStorage.setItem(WEEK_ZOOM_STORAGE_KEY, 'enormous');

    expect(readStoredZoom()).toBeNull();
  });

  it('survives storage that throws', () => {
    // A private window, blocked site data or a server render: the page must still work.
    const getItem = vi.spyOn(Storage.prototype, 'getItem')
      .mockImplementation(() => { throw new Error('denied'); });
    const setItem = vi.spyOn(Storage.prototype, 'setItem')
      .mockImplementation(() => { throw new Error('denied'); });

    expect(readStoredZoom()).toBeNull();
    expect(() => storeZoom('compact')).not.toThrow();

    getItem.mockRestore();
    setItem.mockRestore();
  });
});
