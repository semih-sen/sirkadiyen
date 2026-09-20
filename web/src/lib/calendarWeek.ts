/**
 * Week-grid geometry for the schedule simulation.
 *
 * Every date here is an ISO `YYYY-MM-DD` string and every arithmetic step works on those
 * strings, never on a `Date`. The schedule is stated in Europe/Istanbul, and a `Date` would
 * interpret it in whatever zone the operator's browser is in — an operator in Berlin would
 * otherwise page into a different week than the one an operator in Istanbul sees.
 */

/** Minutes past midnight, for a `HH:mm` or `HH:mm:ss` local time. */
export function minutesOf(time: string): number {
  const [hours, minutes] = time.split(':');
  return Number(hours) * 60 + Number(minutes);
}

export function formatTime(time: string): string {
  const [hours, minutes] = time.split(':');
  return `${hours}:${minutes}`;
}

function toParts(isoDate: string): [number, number, number] {
  const [year, month, day] = isoDate.split('-').map(Number);
  return [year, month, day];
}

function toIso(year: number, month: number, day: number): string {
  return `${String(year).padStart(4, '0')}-${String(month).padStart(2, '0')}-${String(day).padStart(2, '0')}`;
}

/** Days since 1970-01-01 for a proleptic Gregorian date — the calendar the ISO strings use. */
function toDayNumber(isoDate: string): number {
  const [year, month, day] = toParts(isoDate);
  return Math.floor(Date.UTC(year, month - 1, day) / 86_400_000);
}

function fromDayNumber(days: number): string {
  const at = new Date(days * 86_400_000);
  return toIso(at.getUTCFullYear(), at.getUTCMonth() + 1, at.getUTCDate());
}

export function addDays(isoDate: string, days: number): string {
  return fromDayNumber(toDayNumber(isoDate) + days);
}

/** 1 = Monday … 7 = Sunday. 1970-01-01 was a Thursday. */
export function isoWeekday(isoDate: string): number {
  return ((toDayNumber(isoDate) + 3) % 7 + 7) % 7 + 1;
}

/**
 * The Monday of the week a date falls in.
 *
 * Sunday belongs to the week that has already started, not the one about to. The server snaps
 * the anchor date again, so this only keeps the toolbar honest about which week it is asking for.
 */
export function mondayOf(isoDate: string): string {
  return addDays(isoDate, -(isoWeekday(isoDate) - 1));
}

/** The seven ISO dates of the week beginning at `weekStart`. */
export function weekDays(weekStart: string): string[] {
  return Array.from({ length: 7 }, (_, index) => addDays(weekStart, index));
}

export interface TimeSpanMinutes {
  start: number;
  end: number;
}

export interface PositionedEvent<T> {
  event: T;
  /** Percentages of the grid body, so the row height stays the stylesheet's to decide. */
  topPct: number;
  heightPct: number;
  leftPct: number;
  widthPct: number;
}

const DEFAULT_GRID_START = 8 * 60;
const DEFAULT_GRID_END = 18 * 60;
const EARLIEST_GRID_START = 7 * 60;
const LATEST_GRID_END = 23 * 60;

/**
 * The hour range the grid should cover: wide enough for everything in the week, and no wider.
 *
 * A fixed range either clips a 07:30 anatomy session or draws a dozen empty rows under a week
 * that ends at four. The bounds are rounded out to whole hours so the hour gutter stays legible.
 */
export function gridBounds(spans: readonly TimeSpanMinutes[]): TimeSpanMinutes {
  if (spans.length === 0) {
    return { start: DEFAULT_GRID_START, end: DEFAULT_GRID_END };
  }

  const earliest = Math.min(...spans.map((span) => span.start));
  const latest = Math.max(...spans.map((span) => span.end));

  const start = Math.max(EARLIEST_GRID_START, Math.floor(earliest / 60) * 60);
  const end = Math.min(LATEST_GRID_END, Math.ceil(latest / 60) * 60);

  // A week whose only lesson is a single hour would otherwise render as one enormous row.
  return end - start < 60 ? { start, end: start + 60 } : { start, end };
}

/**
 * Lays one day's timed events into columns, the way a calendar does.
 *
 * Overlaps are expected rather than exceptional here: the annual, practice and faculty sources
 * all publish into one cohort, and two of them stating the same hour is exactly the kind of
 * thing an operator opens this page to find.
 *
 * Events are swept into *clusters* — maximal runs that transitively overlap — and each cluster
 * is divided into as many columns as it needs. Within a cluster an event takes the first column
 * whose previous event has already ended, so `09-11`, `10-12`, `11-13` uses two columns with the
 * third event reusing the first one's, instead of three ever-narrower slivers.
 *
 * No event can cross midnight: a canonical record states one local date and a check constraint
 * requires its end to be after its start on that date. Nothing here handles a multi-day span,
 * and nothing should be added for one without that constraint changing first.
 */
export function layoutDay<T>(
  events: readonly T[],
  read: (event: T) => TimeSpanMinutes,
  grid: TimeSpanMinutes,
): PositionedEvent<T>[] {
  const span = Math.max(1, grid.end - grid.start);

  const sorted = [...events]
    .map((event) => ({ event, ...read(event) }))
    .sort((a, b) => a.start - b.start || b.end - a.end);

  const positioned: PositionedEvent<T>[] = [];
  let cluster: typeof sorted = [];
  let clusterEnd = -Infinity;

  const flush = () => {
    if (cluster.length === 0) return;

    // Column ends, so an event can reuse a column whose previous occupant has finished.
    const columnEnds: number[] = [];
    const columnOf = new Map<(typeof cluster)[number], number>();

    for (const item of cluster) {
      let column = columnEnds.findIndex((end) => end <= item.start);
      if (column === -1) {
        column = columnEnds.length;
        columnEnds.push(item.end);
      } else {
        columnEnds[column] = item.end;
      }
      columnOf.set(item, column);
    }

    const width = 100 / columnEnds.length;
    for (const item of cluster) {
      const top = ((item.start - grid.start) / span) * 100;
      // A zero- or negative-length span cannot reach here from the database, but a guard costs
      // one line and keeps a bad row from rendering as an invisible, unclickable chip.
      const height = (Math.max(item.end - item.start, 1) / span) * 100;
      positioned.push({
        event: item.event,
        topPct: top,
        heightPct: height,
        leftPct: (columnOf.get(item) ?? 0) * width,
        widthPct: width,
      });
    }

    cluster = [];
    clusterEnd = -Infinity;
  };

  for (const item of sorted) {
    if (cluster.length > 0 && item.start >= clusterEnd) {
      flush();
    }
    cluster.push(item);
    clusterEnd = Math.max(clusterEnd, item.end);
  }
  flush();

  return positioned;
}

/**
 * How much of a lesson a chip has room to show.
 *
 * The dominant teaching unit in these programmes is 40 minutes, which is a short chip at any
 * sensible zoom, so the content has to adapt to the box rather than the box being sized for the
 * longest possible content. The thresholds are the height at which each extra line of 11.5px
 * text at line-height 1.25 (≈14.4px) still fits inside 6px of vertical padding.
 */
export type WeekDensity = 'tiny' | 'compact' | 'full';

export function densityFor(heightPx: number): WeekDensity {
  if (heightPx < 34) return 'tiny';      // One line: title and start time side by side.
  if (heightPx < 52) return 'compact';   // Title on one line, then the time range.
  return 'full';                          // Title over two lines, then time and place.
}

/**
 * Pixels per hour at each zoom step.
 *
 * `normal` is the default and is chosen so a 40-minute lesson gets 56px — enough for a
 * two-line title and the time beneath it. `compact` fits a longer day on screen at the cost of
 * shorter titles; `wide` is for reading a dense day closely.
 */
export const WEEK_ZOOM_LEVELS = {
  compact: 56,
  normal: 84,
  wide: 112,
} as const;

export type WeekZoom = keyof typeof WEEK_ZOOM_LEVELS;

export const WEEK_ZOOM_LABELS: Record<WeekZoom, string> = {
  compact: 'Sık',
  normal: 'Normal',
  wide: 'Geniş',
};

export const WEEK_ZOOM_STORAGE_KEY = 'sirkadiyen.scheduleSimulation.zoom';

export function isWeekZoom(value: unknown): value is WeekZoom {
  return typeof value === 'string' && value in WEEK_ZOOM_LEVELS;
}

/**
 * The zoom the operator last chose, if the browser remembers one.
 *
 * Storage can be absent, empty or throw outright — a private window, blocked site data, a
 * server render — so every access is guarded and the default stands in whenever it fails. It is
 * a convenience for one viewer on one device and nothing depends on it.
 */
export function readStoredZoom(): WeekZoom | null {
  try {
    const stored = globalThis.localStorage?.getItem(WEEK_ZOOM_STORAGE_KEY);
    return isWeekZoom(stored) ? stored : null;
  } catch {
    return null;
  }
}

export function storeZoom(zoom: WeekZoom): void {
  try {
    globalThis.localStorage?.setItem(WEEK_ZOOM_STORAGE_KEY, zoom);
  } catch {
    // A viewer who cannot persist a preference still gets a working page.
  }
}

/** The hour marks a grid covers, as minutes past midnight. */
export function hourMarks(grid: TimeSpanMinutes): number[] {
  const marks: number[] = [];
  for (let minute = grid.start; minute < grid.end; minute += 60) {
    marks.push(minute);
  }
  return marks;
}

export function formatMinutes(minutes: number): string {
  return `${String(Math.floor(minutes / 60)).padStart(2, '0')}:${String(minutes % 60).padStart(2, '0')}`;
}

const DAY_NAMES = ['Pazartesi', 'Salı', 'Çarşamba', 'Perşembe', 'Cuma', 'Cumartesi', 'Pazar'];

export function dayName(isoDate: string): string {
  return DAY_NAMES[isoWeekday(isoDate) - 1];
}

const MONTH_NAMES = [
  'Ocak', 'Şubat', 'Mart', 'Nisan', 'Mayıs', 'Haziran',
  'Temmuz', 'Ağustos', 'Eylül', 'Ekim', 'Kasım', 'Aralık',
];

/** "22 – 28 Eylül 2026", collapsing the month and year when both ends share them. */
export function formatWeekRange(weekStart: string): string {
  const weekEnd = addDays(weekStart, 6);
  const [startYear, startMonth, startDay] = toParts(weekStart);
  const [endYear, endMonth, endDay] = toParts(weekEnd);

  const end = `${endDay} ${MONTH_NAMES[endMonth - 1]} ${endYear}`;
  if (startYear !== endYear) {
    return `${startDay} ${MONTH_NAMES[startMonth - 1]} ${startYear} – ${end}`;
  }
  if (startMonth !== endMonth) {
    return `${startDay} ${MONTH_NAMES[startMonth - 1]} – ${end}`;
  }
  return `${startDay} – ${end}`;
}

/**
 * Black or white text for a background, by perceived luminance.
 *
 * The department palette is arbitrary hex spanning very light and very dark, so a fixed white
 * would make several categories unreadable.
 */
export function readableTextColor(backgroundColor: string): string {
  const hex = backgroundColor.replace('#', '');
  if (hex.length !== 6) return '#ffffff';

  const channels = [0, 2, 4].map((offset) => {
    const value = Number.parseInt(hex.slice(offset, offset + 2), 16) / 255;
    return value <= 0.04045 ? value / 12.92 : ((value + 0.055) / 1.055) ** 2.4;
  });
  const luminance = 0.2126 * channels[0] + 0.7152 * channels[1] + 0.0722 * channels[2];

  return luminance > 0.45 ? '#1a1a1a' : '#ffffff';
}

/** Today's date in the zone the schedule is stated in, not the browser's. */
export function istanbulToday(now: Date = new Date()): string {
  const parts = new Intl.DateTimeFormat('en-CA', {
    timeZone: 'Europe/Istanbul',
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
  }).format(now);
  // en-CA already formats as YYYY-MM-DD.
  return parts;
}
