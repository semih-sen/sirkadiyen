'use client';

import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { LoadState } from '@/components/AdminData';
import { ScheduleSimulationEventDetail } from '@/components/ScheduleSimulationEventDetail';
import {
  ScheduleSimulationProfileLoader,
  describeSelectors,
} from '@/components/ScheduleSimulationProfileLoader';
import type { LoadedCohort } from '@/components/ScheduleSimulationProfileLoader';
import { WeekCalendarGrid } from '@/components/WeekCalendarGrid';
import { Banner } from '@/components/ui';
import { ApiError, getProfileOptions, simulateCohortWeek } from '@/lib/api';
import { addDays, formatWeekRange, istanbulToday, mondayOf } from '@/lib/calendarWeek';
import { applySelector, dimensionLabel, selectorValues } from '@/lib/selectors';
import type {
  CohortSimulationEvent,
  CohortSimulationWeek,
  ProgramLanguage,
  SupportedProfileProgram,
} from '@/lib/types';

const LANGUAGES: { value: ProgramLanguage; label: string }[] = [
  { value: 'Turkish', label: 'Türkçe' },
  { value: 'English', label: 'İngilizce' },
];

/**
 * Renders one week of the live published schedule for a cohort the operator states.
 *
 * It resolves published truth rather than reading any student's calendar, so it answers a
 * question nothing else here can: what *would* a student in this cohort receive? Nothing on this
 * screen writes, queues, or touches a calendar.
 */
export function ScheduleSimulation() {
  const [programs, setPrograms] = useState<SupportedProfileProgram[] | null>(null);
  const [optionsError, setOptionsError] = useState<string | null>(null);

  const [classYear, setClassYear] = useState<number | null>(null);
  const [programLanguage, setProgramLanguage] = useState<ProgramLanguage>('Turkish');
  const [selectors, setSelectors] = useState<Record<string, string>>({});
  const [copiedFrom, setCopiedFrom] = useState<string | null>(null);

  const today = useMemo(() => istanbulToday(), []);
  const [weekStart, setWeekStart] = useState(() => mondayOf(istanbulToday()));

  const [week, setWeek] = useState<CohortSimulationWeek | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [selected, setSelected] = useState<CohortSimulationEvent | null>(null);

  const loadOptions = useCallback(async () => {
    setOptionsError(null);
    try {
      const options = await getProfileOptions();
      setPrograms(options.programs);
      const first = [...options.programs].sort((a, b) => a.classYear - b.classYear)[0];
      if (first) {
        setClassYear(first.classYear);
        setProgramLanguage(first.programLanguage);
      }
    } catch {
      setOptionsError('Program seçenekleri getirilemedi.');
    }
  }, []);

  useEffect(() => { void loadOptions(); }, [loadOptions]);

  const classYears = useMemo(
    () => [...new Set((programs ?? []).map((program) => program.classYear))].sort((a, b) => a - b),
    [programs],
  );

  // The cohort dimensions are only knowable once a class year and language are chosen: the schema
  // defines them per program, and offering `anatomyGroup` to a programme that has none would let
  // an operator state a cohort that cannot exist.
  const program = useMemo(
    () => (programs ?? []).find(
      (candidate) => candidate.classYear === classYear
        && candidate.programLanguage === programLanguage,
    ),
    [programs, classYear, programLanguage],
  );
  const dimensions = useMemo(() => program?.dimensions ?? [], [program]);

  const missing = useMemo(
    () => dimensions.filter((dimension) => dimension.required && !selectors[dimension.key]),
    [dimensions, selectors],
  );
  const ready = program !== undefined && missing.length === 0;

  // A later request must not be overtaken by an earlier one: paging weeks with the arrow keys
  // outruns the network easily, and a stale response would render a week the operator has
  // already left.
  const inFlight = useRef<AbortController | null>(null);

  const runSimulation = useCallback(async () => {
    if (classYear === null || !ready) {
      setWeek(null);
      return;
    }

    inFlight.current?.abort();
    const controller = new AbortController();
    inFlight.current = controller;

    setLoading(true);
    setError(null);
    try {
      const result = await simulateCohortWeek(
        { classYear, programLanguage, selectors, date: weekStart },
        { signal: controller.signal },
      );
      if (controller.signal.aborted) return;
      setWeek(result);
    } catch (cause) {
      if (controller.signal.aborted || (cause as Error)?.name === 'AbortError') return;
      setError(cause instanceof ApiError
        ? (cause.problem?.detail ?? 'Program getirilemedi.')
        : 'Program getirilemedi.');
    } finally {
      if (!controller.signal.aborted) setLoading(false);
    }
  }, [classYear, programLanguage, selectors, weekStart, ready]);

  useEffect(() => { void runSimulation(); }, [runSimulation]);
  useEffect(() => () => inFlight.current?.abort(), []);

  function changeClassYear(next: number) {
    setClassYear(next);
    // Every dimension belongs to a program, so none of them survives changing one.
    setSelectors({});
    setCopiedFrom(null);
    setSelected(null);
  }

  function changeLanguage(next: ProgramLanguage) {
    setProgramLanguage(next);
    setSelectors({});
    setCopiedFrom(null);
    setSelected(null);
  }

  function changeSelector(key: string, value: string) {
    setSelectors((current) => applySelector(dimensions, current, key, value));
    setCopiedFrom(null);
    setSelected(null);
  }

  function loadFromUser(cohort: LoadedCohort) {
    setClassYear(cohort.classYear);
    setProgramLanguage(cohort.programLanguage);
    setSelectors(cohort.selectors);
    setCopiedFrom(cohort.email);
    setSelected(null);
  }

  function goToWeek(next: string) {
    setWeekStart(mondayOf(next));
    setSelected(null);
  }

  if (!programs) {
    return <LoadState loading={!optionsError} error={optionsError} onRetry={() => void loadOptions()} />;
  }

  return (
    <div className="stack">
      <section className="stack" aria-label="Kitle">
        <div className="cluster" style={{ justifyContent: 'space-between', alignItems: 'flex-end' }}>
          <h3 className="eyebrow" style={{ margin: 0 }}>Kitle</h3>
          <ScheduleSimulationProfileLoader onLoad={loadFromUser} />
        </div>

        <div className="grid grid-2">
          <label className="field">
            <span>Dönem</span>
            <select
              className="select-input"
              value={classYear ?? ''}
              onChange={(changed) => changeClassYear(Number(changed.target.value))}
            >
              {classYears.map((year) => (
                <option key={year} value={year}>Dönem {year}</option>
              ))}
            </select>
          </label>

          <label className="field">
            <span>Program dili</span>
            <select
              className="select-input"
              value={programLanguage}
              onChange={(changed) => changeLanguage(changed.target.value as ProgramLanguage)}
            >
              {LANGUAGES.map((language) => (
                <option key={language.value} value={language.value}>{language.label}</option>
              ))}
            </select>
          </label>

          {dimensions.map((dimension) => {
            const values = selectorValues(dimension, selectors);
            return (
              <label className="field" key={dimension.key}>
                <span>{dimensionLabel(dimension.key)}</span>
                <select
                  className="select-input"
                  value={selectors[dimension.key] ?? ''}
                  disabled={values.length === 0}
                  onChange={(changed) => changeSelector(dimension.key, changed.target.value)}
                >
                  <option value="">Seç…</option>
                  {values.map((value) => (
                    <option key={value} value={value}>{value}</option>
                  ))}
                </select>
              </label>
            );
          })}
        </div>

        {program === undefined && (
          <Banner tone="warning">
            Dönem {classYear} için bu dilde tanımlı bir program yok.
          </Banner>
        )}

        {program !== undefined && missing.length > 0 && (
          <Banner tone="warning">
            Şu boyutlar seçilmeden simülasyon çalıştırılamaz:{' '}
            {missing.map((dimension) => dimensionLabel(dimension.key)).join(', ')}.
            Gerçek bir öğrenci de bunların hepsini bildirmek zorundadır.
          </Banner>
        )}

        {copiedFrom && (
          <Banner tone="neutral">
            Konfigürasyon <strong>{copiedFrom}</strong> profilinden kopyalandı; buradan serbestçe
            değiştirebilirsin. Simülasyon bu kullanıcıya değil, yukarıdaki kitleye sorulur.
          </Banner>
        )}
      </section>

      <section className="stack" aria-label="Hafta">
        <div className="cluster" style={{ justifyContent: 'space-between', alignItems: 'center' }}>
          <div className="cluster" style={{ gap: 8 }}>
            <button
              className="btn btn-secondary btn-sm"
              type="button"
              onClick={() => goToWeek(addDays(weekStart, -7))}
            >
              ‹ Önceki hafta
            </button>
            <button
              className="btn btn-tertiary btn-sm"
              type="button"
              onClick={() => goToWeek(today)}
            >
              Bu hafta
            </button>
            <button
              className="btn btn-secondary btn-sm"
              type="button"
              onClick={() => goToWeek(addDays(weekStart, 7))}
            >
              Sonraki hafta ›
            </button>
          </div>

          <label className="field" style={{ margin: 0 }}>
            <span className="sr-only">Tarihe git</span>
            <input
              className="text-input"
              type="date"
              value={weekStart}
              onChange={(changed) => changed.target.value && goToWeek(changed.target.value)}
            />
          </label>
        </div>

        <h3 style={{ margin: 0, fontSize: 18 }}>{formatWeekRange(weekStart)}</h3>

        {week && (
          <p className="muted" style={{ margin: 0 }}>
            Akademik yıl <strong>{week.academicYear}</strong> · Saat dilimi {week.timeZoneId}
            {week.publishedSourceIds.length > 0 && (
              <>
                {' · '}Bu kitleye yayımlayan kaynaklar:{' '}
                <code>{week.publishedSourceIds.join(', ')}</code>
              </>
            )}
          </p>
        )}

        {!ready && !error && (
          <p className="muted">Kitleyi tamamla; hafta otomatik olarak yüklenecek.</p>
        )}

        {error && (
          <div className="error" role="alert">
            {error}
            <button
              className="btn btn-secondary btn-sm"
              style={{ marginLeft: 12 }}
              type="button"
              onClick={() => void runSimulation()}
            >
              Yeniden dene
            </button>
          </div>
        )}

        {loading && <p className="loading-note">Yükleniyor…</p>}

        {/*
          The previous week stays on screen, dimmed, while the next one loads: collapsing the grid
          on every arrow press makes paging through a term unreadable.
        */}
        {week && (
          <div data-loading={loading} className="week-grid-wrap">
            <WeekCalendarGrid
              weekStart={week.weekStartLocalDate}
              events={week.events}
              today={today}
              selectedId={selected?.raw.canonicalRecordId ?? null}
              onSelect={setSelected}
            />
          </div>
        )}

        {week && week.events.length === 0 && (
          week.cohortYearEventCount > 0 ? (
            <Banner tone="neutral">
              Bu hafta bu kitle için yayımlanmış ders yok. Bu programda yıl genelinde{' '}
              <strong>{week.cohortYearEventCount}</strong> ders var, yani kitle doğru ama hafta boş.
            </Banner>
          ) : (
            <Banner tone="warning">
              Bu kitle için yayımlanmış hiçbir ders yok — yıl genelinde de. Seçtiğin boyutları
              kontrol et; kaynakların bu programda henüz yayımlanmamış olması da mümkün.
              {describeSelectors(selectors) && (
                <> Sorulan kitle: {describeSelectors(selectors)}.</>
              )}
            </Banner>
          )
        )}
      </section>

      {selected && (
        <ScheduleSimulationEventDetail event={selected} onClose={() => setSelected(null)} />
      )}
    </div>
  );
}
