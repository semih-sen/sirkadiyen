'use client';

import { useMemo } from 'react';
import {
  dayName,
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
import type { CohortSimulationEvent } from '@/lib/types';

interface WeekCalendarGridProps {
  /** Monday, `YYYY-MM-DD`. */
  weekStart: string;
  events: CohortSimulationEvent[];
  /** Today in Europe/Istanbul, so the column is highlighted for every operator alike. */
  today: string;
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
 * Its one real job is placing concurrent events side by side, which is not decoration — several
 * sources publish into a single cohort, and two of them claiming the same hour is exactly what
 * an operator opens this page to find.
 */
export function WeekCalendarGrid({
  weekStart,
  events,
  today,
  selectedId,
  onSelect,
}: WeekCalendarGridProps) {
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

  // The body's height follows the number of hours on show, so a short week is a short grid
  // rather than a tall one padded with nothing.
  const bodyHeight = `calc(${marks.length} * var(--week-hour-height))`;

  return (
    <div className="week-grid" data-testid="week-grid">
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
                  allDay
                />
              ))}
          </div>
        ))}
      </div>

      <div className="week-grid__scroll">
        <div className="week-grid__body" style={{ height: bodyHeight }}>
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
  style,
  allDay = false,
}: {
  event: CohortSimulationEvent;
  selected: boolean;
  onSelect: (event: CohortSimulationEvent) => void;
  style?: React.CSSProperties;
  allDay?: boolean;
}) {
  const background = event.label.backgroundColor;

  return (
    <button
      type="button"
      className={allDay ? 'week-event week-event--allday' : 'week-event'}
      data-selected={selected}
      aria-pressed={selected}
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
      <span className="week-event__meta">
        {timeRange(event)}
        {event.location ? ` · ${event.location}` : ''}
      </span>
    </button>
  );
}
