'use client';

import { useEffect, useMemo, useState } from 'react';
import type { FormEvent } from 'react';
import { getProfileOptions, saveProfile, ApiError } from '@/lib/api';
import { Banner } from '@/components/ui';
import type {
  ProgramLanguage,
  SaveStudentProfileResponse,
  StudentProfileView,
  SupportedProfileDimension,
  SupportedProfileOptions,
  SupportedProfileProgram,
} from '@/lib/types';

// The schema names dimensions in the contract's language; the form is Turkish.
// An unlabelled key falls back to itself rather than being hidden, so a new
// dimension is visibly unlabelled instead of silently unselectable.
export const DIMENSION_LABELS: Record<string, string> = {
  practiceGroup: 'Uygulama grubu',
  practiceSubgroup: 'Uygulama alt grubu',
  anatomyGroup: 'Anatomi grubu',
  curriculumGroup: 'Müfredat grubu',
  facultyPracticeGroup: 'Öğretim üyesi uygulama grubu',
};

/**
 * What a completed save is allowed to claim.
 *
 * `calendarResyncRequested` says the work was *requested*, not that it happened — the worker
 * converges the calendar on its next cycle — so the message must not read as a finished
 * synchronization (AI_GUIDELINE §16). A false flag has more than one cause (the audience did not
 * change, or it changed on an account with no completed calendar connection), so it claims nothing
 * about the calendar at all rather than guessing which.
 */
export function ProfileSaveNotice({ resyncRequested }: { resyncRequested: boolean }) {
  return resyncRequested ? (
    <Banner tone="info">
      Profilin kaydedildi. Yeni grubuna uymayan etkinlikler takviminden kaldırılacak, uyanlar
      eklenecek. Bu işlem arka planda yapılır; tamamlandığında takviminde görünür.
    </Banner>
  ) : (
    <Banner tone="neutral">Profilin kaydedildi.</Banner>
  );
}

function allowedValues(
  dimension: SupportedProfileDimension,
  selectors: Record<string, string>,
): string[] {
  if (!dimension.dependsOn) {
    return dimension.values ?? [];
  }
  const parentValue = selectors[dimension.dependsOn];
  if (!parentValue || !dimension.valuesByParent) {
    return [];
  }
  return dimension.valuesByParent[parentValue] ?? [];
}

/**
 * The academic profile form, shared by the onboarding step and the later edit
 * surface. It renders `GET /api/profile/options` and submits `PUT /api/profile`;
 * it never decides what a valid combination is — the backend validates and this
 * form renders the problem it returns (AI_GUIDELINE §16).
 *
 * `initial` prefills it from a stored profile. A stored program the schema no
 * longer defines is deliberately not hidden: the form shows the unsupported
 * combination, because silently blanking it would look like the student never
 * chose one.
 */
export function AcademicProfileForm({
  initial,
  submitLabel,
  busyLabel,
  onSaved,
}: {
  initial?: StudentProfileView | null;
  submitLabel: string;
  busyLabel: string;
  onSaved: (result: SaveStudentProfileResponse) => void | Promise<void>;
}) {
  const [options, setOptions] = useState<SupportedProfileOptions | null>(null);
  const [loadError, setLoadError] = useState<string | null>(null);

  const [classYear, setClassYear] = useState<number | ''>(initial?.classYear ?? '');
  const [language, setLanguage] = useState<ProgramLanguage | ''>(initial?.programLanguage ?? '');
  const [studentNumber, setStudentNumber] = useState(initial?.studentNumber ?? '');
  const [selectors, setSelectors] = useState<Record<string, string>>(
    initial ? { ...initial.selectors } : {},
  );
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    getProfileOptions()
      .then(setOptions)
      .catch(() => setLoadError('Profil seçenekleri yüklenemedi.'));
  }, []);

  const program: SupportedProfileProgram | undefined = useMemo(() => {
    if (!options || classYear === '' || language === '') {
      return undefined;
    }
    return options.programs.find(
      (candidate) => candidate.classYear === classYear && candidate.programLanguage === language,
    );
  }, [options, classYear, language]);

  const classYears = useMemo(
    () => (options ? [...new Set(options.programs.map((p) => p.classYear))].sort((a, b) => a - b) : []),
    [options],
  );

  function setSelector(key: string, value: string, dimensions: SupportedProfileDimension[]) {
    setSelectors((previous) => {
      const next = { ...previous, [key]: value };
      // Clear children whose parent just changed, so a stale subgroup can't persist.
      for (const dimension of dimensions) {
        if (dimension.dependsOn === key) {
          delete next[dimension.key];
        }
      }
      return next;
    });
  }

  async function onSubmit(event: FormEvent) {
    event.preventDefault();
    if (classYear === '' || language === '' || !program) {
      return;
    }
    setBusy(true);
    setError(null);
    try {
      const result = await saveProfile({
        classYear,
        programLanguage: language,
        studentNumber: studentNumber.trim(),
        selectors,
      });
      await onSaved(result);
      setBusy(false);
    } catch (err) {
      setBusy(false);
      if (err instanceof ApiError && err.problem?.errors) {
        setError(Object.values(err.problem.errors).flat().join(' '));
      } else {
        setError(err instanceof ApiError ? err.message : 'Profil kaydedilemedi.');
      }
    }
  }

  if (loadError) {
    return (
      <div className="error" role="alert">
        {loadError}
      </div>
    );
  }

  if (!options) {
    return <p className="loading-note">Yükleniyor…</p>;
  }

  return (
    <>
      {/*
        Once a program is chosen, its own academic year is shown rather than the
        schema's. They differ during a rollover — the faculty publishes the new
        year one grade at a time — and the year a student sees here is the one
        their profile is stamped with (ADR-103).
      */}
      <p className="muted" style={{ marginTop: 8 }}>
        {program?.academicYear ?? options.academicYear} akademik yılı için sınıfını ve grubunu seç.
        Yalnızca seçtiğin programa uygulanan alanlar gösterilir.
      </p>

      <form onSubmit={onSubmit} style={{ marginTop: 24 }}>
        <div className="field">
          <label htmlFor="classYear">Sınıf</label>
          <select
            id="classYear"
            className="select-input"
            value={classYear}
            onChange={(event) => {
              setClassYear(event.target.value === '' ? '' : Number(event.target.value));
              setSelectors({});
            }}
            required
          >
            <option value="">Seç…</option>
            {classYears.map((year) => (
              <option key={year} value={year}>
                {year}. sınıf (Dönem {year})
              </option>
            ))}
          </select>
        </div>

        <div className="field">
          <label htmlFor="language">Program dili</label>
          <select
            id="language"
            className="select-input"
            value={language}
            onChange={(event) => {
              setLanguage(event.target.value as ProgramLanguage | '');
              setSelectors({});
            }}
            required
          >
            <option value="">Seç…</option>
            <option value="Turkish">Türkçe</option>
            <option value="English">İngilizce</option>
          </select>
        </div>

        {classYear !== '' && language !== '' && !program && (
          <div className="error" role="alert">
            Bu sınıf ve dil kombinasyonu şu an desteklenmiyor.
          </div>
        )}

        {program?.dimensions.map((dimension) => {
          const values = allowedValues(dimension, selectors);
          const disabled = dimension.dependsOn ? !selectors[dimension.dependsOn] : false;
          return (
            <div className="field" key={dimension.key}>
              <label htmlFor={dimension.key}>
                {DIMENSION_LABELS[dimension.key] ?? dimension.key}
                {dimension.required ? ' *' : ''}
              </label>
              <select
                id={dimension.key}
                className="select-input"
                value={selectors[dimension.key] ?? ''}
                disabled={disabled}
                required={dimension.required}
                onChange={(event) => setSelector(dimension.key, event.target.value, program.dimensions)}
              >
                <option value="">{disabled ? 'Önce üst grubu seç…' : 'Seç…'}</option>
                {values.map((value) => (
                  <option key={value} value={value}>
                    {value}
                  </option>
                ))}
              </select>
            </div>
          );
        })}

        <div className="field">
          <label htmlFor="studentNumber">Öğrenci numarası</label>
          <input
            id="studentNumber"
            className="text-input"
            value={studentNumber}
            onChange={(event) => setStudentNumber(event.target.value.replace(/\D/g, '').slice(0, 10))}
            inputMode="numeric"
            placeholder="10 haneli numara"
            maxLength={10}
            required
          />
          <p className="field-hint">Baştaki sıfırlar korunur; fakülte ve program hanesi seçilen programla tutarlı olmalı.</p>
        </div>

        <button className="btn btn-primary btn-block" type="submit" disabled={busy || !program}>
          {busy ? busyLabel : submitLabel}
        </button>
      </form>

      {error && (
        <div className="error" role="alert" aria-live="polite">
          {error}
        </div>
      )}
    </>
  );
}
