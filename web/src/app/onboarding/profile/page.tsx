'use client';

import { useEffect, useMemo, useState } from 'react';
import type { FormEvent } from 'react';
import { useRouter } from 'next/navigation';
import { OnboardingGate } from '@/components/OnboardingGate';
import { useSession } from '@/components/SessionProvider';
import { getProfileOptions, saveProfile, ApiError } from '@/lib/api';
import { routeForOnboardingState } from '@/lib/onboarding';
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
};

function allowedValues(dimension: SupportedProfileDimension, selectors: Record<string, string>): string[] {
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
    () => (options ? [...new Set(options.programs.map((program) => program.classYear))].sort() : []),
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
      <div className="card">
        <div className="error">{loadError}</div>
      </div>
    );
  }

  if (!options) {
    return (
      <div className="card">
        <p className="muted">Yükleniyor…</p>
      </div>
    );
  }

  return (
    <div className="card">
      <div className="brand">Sirkadiyen</div>
      <div className="steps">
        <div className="step done" />
        <div className="step current" />
        <div className="step" />
        <div className="step" />
      </div>
      <h1>Akademik profil</h1>
      <p className="muted">
        {options.academicYear} akademik yılı için sınıfını ve grubunu seç.
      </p>

      <form onSubmit={onSubmit}>
        <label htmlFor="classYear">Sınıf</label>
        <select
          id="classYear"
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
              {year}. sınıf
            </option>
          ))}
        </select>

        <label htmlFor="language">Program dili</label>
        <select
          id="language"
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

        {classYear !== '' && language !== '' && !program && (
          <div className="error" style={{ marginTop: 16 }}>
            Bu sınıf ve dil kombinasyonu şu an desteklenmiyor.
          </div>
        )}

        {program?.dimensions.map((dimension) => {
          const values = allowedValues(dimension, selectors);
          const disabled = dimension.dependsOn ? !selectors[dimension.dependsOn] : false;
          return (
            <div key={dimension.key}>
              <label htmlFor={dimension.key}>
                {DIMENSION_LABELS[dimension.key] ?? dimension.key}
                {dimension.required ? ' *' : ''}
              </label>
              <select
                id={dimension.key}
                value={selectors[dimension.key] ?? ''}
                disabled={disabled}
                required={dimension.required}
                onChange={(event) => setSelector(dimension.key, event.target.value, program.dimensions)}
              >
                <option value="">Seç…</option>
                {values.map((value) => (
                  <option key={value} value={value}>
                    {value}
                  </option>
                ))}
              </select>
            </div>
          );
        })}

        <label htmlFor="studentNumber">Öğrenci numarası</label>
        <input
          id="studentNumber"
          value={studentNumber}
          onChange={(event) => setStudentNumber(event.target.value)}
          inputMode="numeric"
          placeholder="10 haneli numara"
          maxLength={10}
          required
        />

        <button className="primary" type="submit" disabled={busy || !program}>
          {busy ? 'Kaydediliyor…' : 'Devam et'}
        </button>
      </form>

      {error && <div className="error">{error}</div>}
    </div>
  );
}

export default function ProfilePage() {
  return (
    <OnboardingGate allow={['ProfileRequired']}>
      <ProfileForm />
    </OnboardingGate>
  );
}
