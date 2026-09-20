'use client';

import { useMemo } from 'react';
import {
  WEEK_ZOOM_LEVELS,
  dayName,
  densityFor,
  formatMinutes,
  formatTime,
  gridBounds,
  hourMarks,
  isoWeekday,
  layoutDay,
  minutesOf,
  readableTextColor,
  weekDays,
} from '@/lib/calendarWeek';
import type { WeekDensity, WeekZoom } from '@/lib/calendarWeek';
import type { CohortSimulationEvent } from '@/lib/types';

interface WeekCalendarGridProps {
  /** Monday, `YYYY-MM-DD`. */
  weekStart: string;
  events: CohortSimulationEvent[];
  /** Today in Europe/Istanbul, so the column is highlighted for every operator alike. */
  today: string;
  zoom?: WeekZoom;
  selectedId?: string | null;
  onSelect: (event: CohortSimulationEvent) => void;
}

function eventKey(event: CohortSimulationEvent): string {
  return event.raw.canonicalRecordId;
}

function timeRange(event: CohortSimulationEvent): string {
  if (event.isAllDay || !event.startLocalTime || !event.endLocalTime) return 'Tüm gün';
  return `${formatTime(event.startLocalTime)}–${formatTime(event.endLocalTime)}`;
}

/**
 * A week of lessons, laid out the way a calendar lays one out.
 *
 * Purely presentational: it fetches nothing and decides nothing about which lessons belong here.
 * Its two real jobs are placing concurrent events side by side — several sources publish into a
 * single cohort, and two of them claiming the same hour is exactly what an operator opens this
 * page to find — and fitting each lesson's text to the box its hours give it.
 */
export function WeekCalendarGrid({
  weekStart,
  events,
  today,
  zoom = 'normal',
  selectedId,
  onSelect,
}: WeekCalendarGridProps) {
  const hourHeight = WEEK_ZOOM_LEVELS[zoom];
  const days = useMemo(() => weekDays(weekStart), [weekStart]);

  const timed = useMemo(
    () => events.filter((event) => !event.isAllDay && event.startLocalTime && event.endLocalTime),
    [events],
  );
  const allDay = useMemo(() => events.filter((event) => event.isAllDay), [events]);

  const grid = useMemo(
    () => gridBounds(timed.map((event) => ({
      start: minutesOf(event.startLocalTime!),
      end: minutesOf(event.endLocalTime!),
    }))),
    [timed],
  );

  const marks = useMemo(() => hourMarks(grid), [grid]);
  const bodyHeight = marks.length * hourHeight;

  const perDay = useMemo(
    () => days.map((day) => layoutDay(
      timed.filter((event) => event.localDate === day),
      (event) => ({
        start: minutesOf(event.startLocalTime!),
        end: minutesOf(event.endLocalTime!),
      }),
      grid,
    )),
    [days, timed, grid],
  );

  return (
    <div
      className="week-grid"
      data-testid="week-grid"
      style={{ ['--week-hour-height' as string]: `${hourHeight}px` }}
    >
      <div className="week-grid__head">
        <div className="week-grid__corner" aria-hidden="true" />
        {days.map((day) => (
          <div
            key={day}
            className="week-grid__day-head"
            data-today={day === today}
            data-weekend={isoWeekday(day) >= 6}
          >
            <span className="week-grid__day-name">{dayName(day)}</span>
            <span className="week-grid__day-date">{Number(day.slice(8, 10))}</span>
          </div>
        ))}
      </div>

      {/*
        The all-day strip is always rendered, even when the week has none, so the columns below
        do not shift by a row as the operator pages from one week to the next.
      */}
      <div className="week-grid__allday">
        <div className="week-grid__gutter-label">Tüm gün</div>
        {days.map((day) => (
          <div key={day} className="week-grid__allday-cell" data-weekend={isoWeekday(day) >= 6}>
            {allDay
              .filter((event) => event.localDate === day)
              .map((event) => (
                <EventChip
                  key={eventKey(event)}
                  event={event}
                  selected={selectedId === eventKey(event)}
                  onSelect={onSelect}
                  density="compact"
                  allDay
                />
              ))}
          </div>
        ))}
      </div>

      <div className="week-grid__scroll">
        <div className="week-grid__body" style={{ height: `${bodyHeight}px` }}>
          <div className="week-grid__gutter">
            {marks.map((minute) => (
              <div key={minute} className="week-grid__hour">
                {formatMinutes(minute)}
              </div>
            ))}
          </div>
          {days.map((day, index) => (
            <div
              key={day}
              className="week-grid__day"
              data-today={day === today}
              data-weekend={isoWeekday(day) >= 6}
            >
              {perDay[index].map(({ event, topPct, heightPct, leftPct, widthPct }) => (
                <EventChip
                  key={eventKey(event)}
                  event={event}
                  selected={selectedId === eventKey(event)}
                  onSelect={onSelect}
                  // The chip's real height in pixels is what decides how much of the lesson it
                  // can show; a percentage on its own says nothing about whether text fits.
                  density={densityFor((heightPct / 100) * bodyHeight)}
                  style={{
                    top: `${topPct}%`,
                    height: `${heightPct}%`,
                    left: `${leftPct}%`,
                    width: `${widthPct}%`,
                  }}
                />
              ))}
            </div>
          ))}
        </div>
      </div>
    </div>
  );
}

function EventChip({
  event,
  selected,
  onSelect,
  density,
  style,
  allDay = false,
}: {
  event: CohortSimulationEvent;
  selected: boolean;
  onSelect: (event: CohortSimulationEvent) => void;
  density: WeekDensity;
  style?: React.CSSProperties;
  allDay?: boolean;
}) {
  const background = event.label.backgroundColor;

  // The shortest chips give their whole width to the lesson's name and show no hours at all.
  // Where the chip sits in the grid already says when it is, and the row is narrow enough that
  // a time would eat the title down to "1-…" — the one thing nothing else on screen tells you.
  // The full label stays in the tooltip and in the detail panel either way.
  const meta = density === 'tiny'
    ? null
    : `${timeRange(event)}${density === 'full' && event.location ? ` · ${event.location}` : ''}`;

  return (
    <button
      type="button"
      className={allDay ? 'week-event week-event--allday' : 'week-event'}
      data-selected={selected}
      data-density={density}
      aria-pressed={selected}
      // Short chips hide the place and the end time, so the full label stays reachable by
      // pointer and by assistive technology even when the box cannot show it.
      title={`${event.summary} · ${timeRange(event)}${event.location ? ` · ${event.location}` : ''}`}
      style={{ ...style, background, color: readableTextColor(background) }}
      onClick={() => onSelect(event)}
    >
      <span className="week-event__title">
        {event.raw.sharesStableIdentity && (
          <span
            className="week-event__warn"
            title="Bu hafta aynı kimliği taşıyan başka bir ders var; takvimde tek etkinliğe düşerler."
          >
            ⚠{' '}
          </span>
        )}
        {event.summary}
      </span>
      {meta && <span className="week-event__meta">{meta}</span>}
    </button>
  );
}
