'use client';

import { useEffect, useMemo, useState } from 'react';
import type { FormEvent } from 'react';
import { useRouter } from 'next/navigation';
import { OnboardingGate } from '@/components/OnboardingGate';
import { useSession } from '@/components/SessionProvider';
import { getProfileOptions, saveProfile, ApiError } from '@/lib/api';
import { routeForOnboardingState } from '@/lib/onboarding';
import { AuthShell, Brand, ImplNote, Stepper } from '@/components/ui';
import type {
  ProgramLanguage,
  SupportedProfileDimension,
  SupportedProfileOptions,
  SupportedProfileProgram,
} from '@/lib/types';

// The schema names dimensions in the contract's language; the form is Turkish.
// An unlabelled key falls back to itself rather than being hidden, so a new
// dimension is visibly unlabelled instead of silently unselectable.
const DIMENSION_LABELS: Record<string, string> = {
  practiceGroup: 'Uygulama grubu',
  practiceSubgroup: 'Uygulama alt grubu',
  anatomyGroup: 'Anatomi grubu',
  curriculumGroup: 'Müfredat grubu',
  facultyPracticeGroup: 'Öğretim üyesi uygulama grubu',
};

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

function ProfileForm() {
  const router = useRouter();
  const { refresh } = useSession();
  const [options, setOptions] = useState<SupportedProfileOptions | null>(null);
  const [loadError, setLoadError] = useState<string | null>(null);

  const [classYear, setClassYear] = useState<number | ''>('');
  const [language, setLanguage] = useState<ProgramLanguage | ''>('');
  const [studentNumber, setStudentNumber] = useState('');
  const [selectors, setSelectors] = useState<Record<string, string>>({});
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
      const me = await refresh();
      router.replace(routeForOnboardingState(me?.onboardingState ?? result.onboarding.state));
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
      <AuthShell wide>
        <Brand />
        <div className="error" role="alert">
          {loadError}
        </div>
      </AuthShell>
    );
  }

  if (!options) {
    return (
      <AuthShell wide>
        <p className="loading-note">Yükleniyor…</p>
      </AuthShell>
    );
  }

  return (
    <AuthShell wide>
      <Brand />
      <div style={{ margin: '20px 0 24px' }}>
        <Stepper activeIndex={1} />
      </div>

      <h1>Akademik profil</h1>
      {/*
        Once a program is chosen, its own academic year is shown rather than the
        schema's. They differ during a rollover — the faculty publishes the new
        year one grade at a time — and the year a student sees here is the one
        their profile is stamped with (ADR-103).
      */}
      <p className="muted" style={{ marginTop: 8 }}>
        {program?.academicYear ?? options.academicYear} akademik yılı için sınıfını ve grubunu
        seç. Yalnızca seçtiğin programa uygulanan alanlar gösterilir.
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
          {busy ? 'Kaydediliyor…' : 'Devam et'}
        </button>
      </form>

      {error && (
        <div className="error" role="alert" aria-live="polite">
          {error}
        </div>
      )}

      <ImplNote>
        Alanlar <code>GET /api/profile/options</code> şemasından dinamik üretilir;{' '}
        <code>PUT /api/profile</code> ile kaydedilir (ADR-055, ADR-056).
      </ImplNote>
    </AuthShell>
  );
}

export default function ProfilePage() {
  return (
    <OnboardingGate allow={['ProfileRequired']}>
      <ProfileForm />
    </OnboardingGate>
  );
}
