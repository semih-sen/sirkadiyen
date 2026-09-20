'use client';

import { useMemo } from 'react';
import { dayName, formatTime, minutesOf, weekDays } from '@/lib/calendarWeek';
import type { CohortSimulationEvent } from '@/lib/types';

interface WeekAgendaProps {
  /** Monday, `YYYY-MM-DD`. */
  weekStart: string;
  events: CohortSimulationEvent[];
  today: string;
  selectedId?: string | null;
  onSelect: (event: CohortSimulationEvent) => void;
}

/** All-day items first, then by start time — the order a day actually happens in. */
function byStart(a: CohortSimulationEvent, b: CohortSimulationEvent): number {
  if (a.isAllDay !== b.isAllDay) return a.isAllDay ? -1 : 1;
  if (a.isAllDay) return 0;
  return minutesOf(a.startLocalTime ?? '00:00') - minutesOf(b.startLocalTime ?? '00:00');
}

/**
 * The same week as a vertical list of days.
 *
 * On a phone a seven-column grid is seven 50px slivers, and this programme's lessons are mostly
 * 40 minutes long — too short to read in a box that narrow. A list has no width to run out of,
 * so every lesson shows its hours, its title and its place in full.
 *
 * Empty days are listed rather than skipped: "Saturday has nothing" and "I scrolled past
 * Saturday" look the same otherwise.
 */
export function WeekAgenda({ weekStart, events, today, selectedId, onSelect }: WeekAgendaProps) {
  const days = useMemo(() => weekDays(weekStart), [weekStart]);

  const byDay = useMemo(
    () => days.map((day) => ({
      day,
      lessons: events.filter((event) => event.localDate === day).sort(byStart),
    })),
    [days, events],
  );

  return (
    <ol className="week-agenda" data-testid="week-agenda">
      {byDay.map(({ day, lessons }) => (
        <li key={day} className="week-agenda__day" data-today={day === today}>
          <h4 className="week-agenda__heading">
            <span className="week-agenda__date">{Number(day.slice(8, 10))}</span>
            <span>{dayName(day)}</span>
            {day === today && <span className="badge badge-success">Bugün</span>}
          </h4>

          {lessons.length === 0 ? (
            <p className="week-agenda__empty muted">Ders yok</p>
          ) : (
            <ul className="week-agenda__list">
              {lessons.map((event) => (
                <li key={event.raw.canonicalRecordId}>
                  <button
                    type="button"
                    className="week-agenda__item"
                    data-selected={selectedId === event.raw.canonicalRecordId}
                    aria-pressed={selectedId === event.raw.canonicalRecordId}
                    onClick={() => onSelect(event)}
                  >
                    <span
                      className="week-agenda__stripe"
                      aria-hidden="true"
                      style={{ background: event.label.backgroundColor }}
                    />
                    <span className="week-agenda__when">
                      {event.isAllDay || !event.startLocalTime || !event.endLocalTime ? (
                        <span className="week-agenda__allday">Tüm gün</span>
                      ) : (
                        <>
                          <strong>{formatTime(event.startLocalTime)}</strong>
                          <span className="muted">{formatTime(event.endLocalTime)}</span>
                        </>
                      )}
                    </span>
                    <span className="week-agenda__what">
                      <span className="week-agenda__title">
                        {event.raw.sharesStableIdentity && (
                          <span title="Bu hafta aynı kimliği taşıyan başka bir ders var; takvimde tek etkinliğe düşerler.">
                            ⚠{' '}
                          </span>
                        )}
                        {event.summary}
                      </span>
                      {event.location && (
                        <span className="week-agenda__where muted">{event.location}</span>
                      )}
                    </span>
                  </button>
                </li>
              ))}
            </ul>
          )}
        </li>
      ))}
    </ol>
  );
}
