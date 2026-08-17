'use client';

import { useMemo } from 'react';
import type {
  AdminUserFilters,
  AdminUserSort,
  GoogleCalendarInitialSyncState,
  ProgramLanguage,
  SupportedProfileDimension,
  SupportedProfileOptions,
  SupportedProfileProgram,
} from '@/lib/types';

/** Turkish labels for the selector keys, which the backend names in English (ADR-079). */
const SELECTOR_LABELS: Record<string, string> = {
  practiceGroup: 'Uygulama grubu',
  practiceSubgroup: 'Uygulama alt grubu',
  anatomyGroup: 'Anatomi grubu',
  curriculumGroup: 'Müfredat grubu',
};

export const SORT_LABELS: Record<AdminUserSort, string> = {
  CreatedAtUtc: 'Kayıt tarihi',
  LastSignedInAtUtc: 'Son giriş',
  Email: 'E-posta',
};

/** The filters that are not the free-text box — what the "N filtre etkin" chip counts. */
export function activeFilterCount(filters: AdminUserFilters): number {
  const scalars: (keyof AdminUserFilters)[] = [
    'role', 'licenseState', 'hasProfile', 'academicYear', 'classYear', 'programLanguage',
    'hasCalendarConnection', 'calendarStatus', 'initialSyncState',
    'createdFromUtc', 'createdToUtc', 'lastSignedInFromUtc', 'lastSignedInToUtc',
  ];
  return scalars.filter((key) => filters[key] !== undefined).length
    + Object.keys(filters.selectors ?? {}).length;
}

/**
 * The complex filter panel over the account directory.
 *
 * Every control here maps to one backend filter and nothing is computed in the browser: the list
 * the operator sees is the page the server selected, so a count under a filter is the real count
 * and not the part of a page that happened to be fetched (AI_GUIDELINE §16 — backend state is
 * authoritative).
 *
 * The academic dimensions come from `GET /api/profile/options`, so the panel offers the cohorts
 * that actually exist in the supported-profile schema rather than a hand-written list that would
 * drift from it.
 */
export function AdminUserFilterBar({
  filters,
  profileOptions,
  onChange,
  onReset,
  expanded,
  onToggleExpanded,
}: {
  filters: AdminUserFilters;
  profileOptions: SupportedProfileOptions | null;
  onChange: (changes: Partial<AdminUserFilters>) => void;
  onReset: () => void;
  expanded: boolean;
  onToggleExpanded: () => void;
}) {
  const programs = profileOptions?.programs ?? [];
  const classYears = useMemo(
    () => [...new Set(programs.map((program) => program.classYear))].sort((a, b) => a - b),
    [programs],
  );

  // Selector dimensions are only meaningful once the cohort they belong to is chosen: the same
  // key can carry different values in different class years.
  const program: SupportedProfileProgram | undefined = programs.find((candidate) =>
    candidate.classYear === filters.classYear
    && (filters.programLanguage === undefined
      || candidate.programLanguage === filters.programLanguage));

  const count = activeFilterCount(filters);

  return (
    <div className="stack" style={{ gap: 12, marginBottom: 16 }}>
      <div className="cluster" style={{ gap: 10 }}>
        <input
          className="text-input"
          value={filters.search ?? ''}
          onChange={(event) => onChange({ search: event.target.value })}
          placeholder="E-posta, ad veya öğrenci numarası ara…"
          aria-label="Kullanıcı ara"
          style={{ minWidth: 260, flex: 1 }}
        />
        <select
          className="select-input"
          value={filters.sort ?? 'CreatedAtUtc'}
          onChange={(event) => onChange({ sort: event.target.value as AdminUserSort })}
          aria-label="Sıralama ölçütü"
        >
          {(Object.keys(SORT_LABELS) as AdminUserSort[]).map((value) => (
            <option key={value} value={value}>{SORT_LABELS[value]}</option>
          ))}
        </select>
        <select
          className="select-input"
          value={filters.descending === false ? 'asc' : 'desc'}
          onChange={(event) => onChange({ descending: event.target.value === 'desc' })}
          aria-label="Sıralama yönü"
        >
          <option value="desc">Azalan</option>
          <option value="asc">Artan</option>
        </select>
        <button
          className="btn btn-secondary btn-sm"
          type="button"
          aria-expanded={expanded}
          onClick={onToggleExpanded}
        >
          Gelişmiş filtreler{count > 0 ? ` (${count})` : ''}
        </button>
        {(count > 0 || filters.search) && (
          <button className="btn btn-tertiary btn-sm" type="button" onClick={onReset}>
            Temizle
          </button>
        )}
      </div>

      {expanded && (
        <div
          className="stack"
          style={{
            gap: 14,
            border: '1px solid var(--border)',
            borderRadius: 8,
            padding: 14,
          }}
        >
          <FilterGroup title="Hesap">
            <Choice
              label="Rol"
              value={filters.role ?? ''}
              onChange={(value) => onChange({ role: value || undefined })}
              options={[['User', 'Öğrenci'], ['SuperAdmin', 'SuperAdmin']]}
            />
            <Choice
              label="Lisans durumu"
              value={filters.licenseState ?? ''}
              onChange={(value) => onChange({ licenseState: value || undefined })}
              options={[
                ['Active', 'Etkin'],
                ['Suspended', 'İptal edilmiş'],
                ['None', 'Lisanssız'],
              ]}
            />
            <Choice
              label="Akademik profil"
              value={boolValue(filters.hasProfile)}
              onChange={(value) => onChange({ hasProfile: boolFilter(value) })}
              options={[['true', 'Var'], ['false', 'Yok']]}
            />
          </FilterGroup>

          <FilterGroup title="Akademik">
            <Choice
              label="Dönem"
              value={filters.classYear?.toString() ?? ''}
              onChange={(value) => onChange({
                classYear: value ? Number(value) : undefined,
                // A selector belongs to the cohort it was chosen in; keeping it across a class-year
                // change would filter on a dimension the new cohort may not even have.
                selectors: undefined,
              })}
              options={classYears.map((year) => [year.toString(), `${year}. dönem`])}
            />
            <Choice
              label="Program dili"
              value={filters.programLanguage ?? ''}
              onChange={(value) => onChange({
                programLanguage: (value || undefined) as ProgramLanguage | undefined,
                selectors: undefined,
              })}
              options={[['Turkish', 'Türkçe'], ['English', 'İngilizce']]}
            />
            {profileOptions && (
              <Choice
                label="Akademik yıl"
                value={filters.academicYear ?? ''}
                onChange={(value) => onChange({ academicYear: value || undefined })}
                options={[...new Set([
                  profileOptions.academicYear,
                  ...programs.map((item) => item.academicYear),
                ])].map((year) => [year, year])}
              />
            )}
          </FilterGroup>

          {program && program.dimensions.length > 0 && (
            <FilterGroup title="Gruplar">
              {program.dimensions.map((dimension) => (
                <Choice
                  key={dimension.key}
                  label={SELECTOR_LABELS[dimension.key] ?? dimension.key}
                  value={filters.selectors?.[dimension.key] ?? ''}
                  onChange={(value) => onChange({
                    selectors: withSelector(filters.selectors, dimension.key, value),
                  })}
                  options={valuesFor(dimension, filters.selectors)
                    .map((item) => [item, item] as [string, string])}
                />
              ))}
            </FilterGroup>
          )}

          <FilterGroup title="Takvim">
            <Choice
              label="Takvim bağlantısı"
              value={boolValue(filters.hasCalendarConnection)}
              onChange={(value) => onChange({ hasCalendarConnection: boolFilter(value) })}
              options={[['true', 'Var'], ['false', 'Yok']]}
            />
            <Choice
              label="Yetki durumu"
              value={filters.calendarStatus ?? ''}
              onChange={(value) => onChange({ calendarStatus: value || undefined })}
              options={[
                ['Authorized', 'Yetkili'],
                ['NeedsReauthorization', 'Yeniden yetki gerekiyor'],
              ]}
            />
            <Choice
              label="İlk senkronizasyon"
              value={filters.initialSyncState ?? ''}
              onChange={(value) => onChange({
                initialSyncState: (value || undefined) as GoogleCalendarInitialSyncState | undefined,
              })}
              options={[
                ['Pending', 'Başlamadı'],
                ['InProgress', 'Sürüyor'],
                ['Completed', 'Tamamlandı'],
              ]}
            />
          </FilterGroup>

          <FilterGroup title="Tarih aralığı (Europe/Istanbul)">
            <DayRange
              label="Kayıt"
              from={filters.createdFromUtc}
              to={filters.createdToUtc}
              onChange={(from, to) => onChange({ createdFromUtc: from, createdToUtc: to })}
            />
            <DayRange
              label="Son giriş"
              from={filters.lastSignedInFromUtc}
              to={filters.lastSignedInToUtc}
              onChange={(from, to) =>
                onChange({ lastSignedInFromUtc: from, lastSignedInToUtc: to })}
            />
          </FilterGroup>
        </div>
      )}
    </div>
  );
}

function FilterGroup({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <div>
      <div className="admin-nav-group-label">{title}</div>
      <div className="cluster" style={{ gap: 10, alignItems: 'flex-end', flexWrap: 'wrap' }}>
        {children}
      </div>
    </div>
  );
}

function Choice({ label, value, onChange, options }: {
  label: string;
  value: string;
  onChange: (value: string) => void;
  options: [string, string][];
}) {
  return (
    <div>
      <label htmlFor={`filter-${label}`} style={{ fontSize: 12 }}>{label}</label>
      <select
        className="select-input"
        id={`filter-${label}`}
        aria-label={label}
        value={value}
        onChange={(event) => onChange(event.target.value)}
      >
        <option value="">Hepsi</option>
        {options.map(([optionValue, optionLabel]) => (
          <option key={optionValue} value={optionValue}>{optionLabel}</option>
        ))}
      </select>
    </div>
  );
}

/**
 * A local-date range sent as an Istanbul-offset instant, because the whole panel reads dates in
 * that zone and a bare date would otherwise be interpreted as UTC and shift the boundary.
 */
function DayRange({ label, from, to, onChange }: {
  label: string;
  from?: string;
  to?: string;
  onChange: (from?: string, to?: string) => void;
}) {
  return (
    <div className="cluster" style={{ gap: 8, alignItems: 'flex-end' }}>
      <div>
        <label htmlFor={`from-${label}`} style={{ fontSize: 12 }}>{label} başlangıç</label>
        <input
          className="text-input"
          id={`from-${label}`}
          type="date"
          value={toLocalDate(from)}
          onChange={(event) => onChange(startOfDay(event.target.value), to)}
        />
      </div>
      <div>
        <label htmlFor={`to-${label}`} style={{ fontSize: 12 }}>{label} bitiş</label>
        <input
          className="text-input"
          id={`to-${label}`}
          type="date"
          value={toLocalDate(to)}
          onChange={(event) => onChange(from, endOfDay(event.target.value))}
        />
      </div>
    </div>
  );
}

const ISTANBUL_OFFSET = '+03:00';

function startOfDay(value: string): string | undefined {
  return value ? `${value}T00:00:00${ISTANBUL_OFFSET}` : undefined;
}

function endOfDay(value: string): string | undefined {
  return value ? `${value}T23:59:59${ISTANBUL_OFFSET}` : undefined;
}

function toLocalDate(value?: string): string {
  return value ? value.slice(0, 10) : '';
}

function boolValue(value?: boolean): string {
  return value === undefined ? '' : String(value);
}

function boolFilter(value: string): boolean | undefined {
  return value === '' ? undefined : value === 'true';
}

function withSelector(
  current: Record<string, string> | undefined,
  key: string,
  value: string,
): Record<string, string> | undefined {
  const next = { ...(current ?? {}) };
  if (value) next[key] = value;
  else delete next[key];
  return Object.keys(next).length > 0 ? next : undefined;
}

/**
 * A dependent dimension offers its parent's values when the parent is chosen, and the union
 * otherwise — an operator filtering on a subgroup alone is a legitimate query, and refusing it
 * until a parent is set would hide accounts rather than explain anything.
 */
function valuesFor(
  dimension: SupportedProfileDimension,
  selectors: Record<string, string> | undefined,
): string[] {
  if (dimension.values?.length) return dimension.values;
  const byParent = dimension.valuesByParent ?? {};
  const parentValue = dimension.dependsOn ? selectors?.[dimension.dependsOn] : undefined;
  if (parentValue && byParent[parentValue]) return byParent[parentValue];
  return [...new Set(Object.values(byParent).flat())];
}
