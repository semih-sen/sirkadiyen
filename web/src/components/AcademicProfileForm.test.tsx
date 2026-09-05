import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { AcademicProfileForm, ProfileSaveNotice, RosterLookupNotice } from './AcademicProfileForm';

const api = vi.hoisted(() => ({
  getProfileOptions: vi.fn(),
  saveProfile: vi.fn(),
  lookUpStudentRoster: vi.fn(),
  ApiError: class ApiError extends Error {},
}));
vi.mock('@/lib/api', () => api);

const options = {
  academicYear: '2025-2026',
  schemaVersion: '1.2',
  programs: [
    {
      academicYear: '2025-2026',
      classYear: 2,
      programLanguage: 'Turkish',
      dimensions: [
        { key: 'practiceGroup', required: true, values: ['A', 'C'] },
        {
          key: 'practiceSubgroup',
          required: true,
          dependsOn: 'practiceGroup',
          valuesByParent: { A: ['A1', 'A2'], C: ['C1', 'C2'] },
        },
      ],
    },
    {
      academicYear: '2026-2027',
      classYear: 3,
      programLanguage: 'Turkish',
      dimensions: [{ key: 'curriculumGroup', required: true, values: ['A', 'B'] }],
    },
    {
      academicYear: '2026-2027',
      classYear: 3,
      programLanguage: 'English',
      dimensions: [
        { key: 'microPathologyGroup', required: true, values: ['A1', 'A2', 'B1', 'B2'] },
      ],
    },
  ],
};

const storedProfile = {
  userId: 'user-1',
  academicYear: '2025-2026',
  classYear: 2,
  programLanguage: 'Turkish' as const,
  studentNumber: '0101240048',
  selectorSchemaVersion: '1.2',
  selectors: { practiceGroup: 'A', practiceSubgroup: 'A2' },
  updatedAtUtc: '2026-08-16T09:00:00Z',
};

describe('AcademicProfileForm', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    api.getProfileOptions.mockResolvedValue(options);
    api.saveProfile.mockResolvedValue({
      profile: storedProfile,
      onboarding: { state: 'Active', hasActiveLicense: true, nextAction: 'None' },
      calendarResyncRequested: true,
    });
  });

  it('prefills every field from a stored profile so an edit is a change, not a re-entry', async () => {
    render(
      <AcademicProfileForm
        initial={storedProfile}
        submitLabel="Profili güncelle"
        busyLabel="Kaydediliyor…"
        onSaved={() => {}}
      />,
    );

    // Blanking a stored value would make the student re-declare a cohort they never changed,
    // and a mistyped re-entry silently moves their calendar.
    expect(await screen.findByLabelText('Sınıf')).toHaveValue('2');
    expect(screen.getByLabelText('Program dili')).toHaveValue('Turkish');
    expect(screen.getByLabelText(/Uygulama grubu/)).toHaveValue('A');
    expect(screen.getByLabelText(/Uygulama alt grubu/)).toHaveValue('A2');
    expect(screen.getByLabelText('Öğrenci numarası')).toHaveValue('0101240048');
  });

  it("shows the chosen program's own academic year rather than the schema's", async () => {
    // They differ during a rollover, and the year shown is the one the profile is stamped
    // with (ADR-103), so showing the schema's would misstate which year the calendar covers.
    render(
      <AcademicProfileForm
        initial={{ ...storedProfile, classYear: 3, selectors: { curriculumGroup: 'A' } }}
        submitLabel="Profili güncelle"
        busyLabel="Kaydediliyor…"
        onSaved={() => {}}
      />,
    );

    expect(await screen.findByText(/2026-2027 akademik yılı/)).toBeInTheDocument();
  });

  it('renders the microbiology/pathology group for the now-onboardable Grade 3 English program', async () => {
    // Grade 3 English used to have no cohort at all (ADR-098); it now onboards on
    // its microPathologyGroup, an independent four-value selector, and the label
    // must be Turkish rather than the raw contract key (ADR-145).
    render(<AcademicProfileForm submitLabel="Kaydet" busyLabel="Kaydediliyor…" onSaved={() => {}} />);

    await userEvent.selectOptions(await screen.findByLabelText('Sınıf'), '3');
    await userEvent.selectOptions(screen.getByLabelText('Program dili'), 'English');

    const group = screen.getByLabelText(/Mikrobiyoloji-Patoloji uygulama grubu/);
    expect(group).toBeInTheDocument();
    await userEvent.selectOptions(group, 'B1');
    expect(group).toHaveValue('B1');
  });

  it('clears a dependent selector when its parent changes', async () => {
    render(
      <AcademicProfileForm
        initial={storedProfile}
        submitLabel="Profili güncelle"
        busyLabel="Kaydediliyor…"
        onSaved={() => {}}
      />,
    );

    await userEvent.selectOptions(await screen.findByLabelText(/Uygulama grubu/), 'C');

    // A subgroup left over from the previous group is not a valid cohort at all; keeping it
    // would submit a combination the student never chose.
    expect(screen.getByLabelText(/Uygulama alt grubu/)).toHaveValue('');
  });

  it('reports the backend validation problem instead of deciding validity itself', async () => {
    const failure = new api.ApiError('Geçersiz') as Error & {
      problem?: { errors: Record<string, string[]> };
    };
    failure.problem = { errors: { studentNumber: ['Öğrenci numarası programla uyuşmuyor.'] } };
    api.saveProfile.mockRejectedValue(failure);

    const onSaved = vi.fn();
    render(
      <AcademicProfileForm
        initial={storedProfile}
        submitLabel="Profili güncelle"
        busyLabel="Kaydediliyor…"
        onSaved={onSaved}
      />,
    );

    await userEvent.click(await screen.findByRole('button', { name: 'Profili güncelle' }));

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Öğrenci numarası programla uyuşmuyor.',
    );
    expect(onSaved).not.toHaveBeenCalled();
  });

  it('hands the save result to the caller so the resync outcome can be reported', async () => {
    const onSaved = vi.fn();
    render(
      <AcademicProfileForm
        initial={storedProfile}
        submitLabel="Profili güncelle"
        busyLabel="Kaydediliyor…"
        onSaved={onSaved}
      />,
    );

    await userEvent.click(await screen.findByRole('button', { name: 'Profili güncelle' }));

    await waitFor(() =>
      expect(onSaved).toHaveBeenCalledWith(
        expect.objectContaining({ calendarResyncRequested: true }),
      ),
    );
  });
});

describe('AcademicProfileForm roster lookup', () => {
  const matched = {
    outcome: 'Matched' as const,
    studentNumber: '0101250001',
    givenName: 'HAY*******',
    familyName: 'KIY***',
    academicYear: '2025-2026',
    classYear: 2,
    programLanguage: 'Turkish' as const,
    suggestedSelectors: { practiceGroup: 'A', practiceSubgroup: 'A1' },
    dimensionsRequiringInput: ['anatomyGroup'],
    notices: [],
    someListsUnreadable: false,
  };

  beforeEach(() => {
    vi.clearAllMocks();
    api.getProfileOptions.mockResolvedValue(options);
  });

  it('fills the form from the list and keeps every filled value editable', async () => {
    api.lookUpStudentRoster.mockResolvedValue(matched);
    render(
      <AcademicProfileForm submitLabel="Kaydet" busyLabel="Kaydediliyor…" onSaved={() => {}} />,
    );

    await userEvent.type(await screen.findByLabelText('Öğrenci numarası'), '0101250001');
    await userEvent.click(screen.getByRole('button', { name: 'Öğrenci listesinde ara' }));

    expect(await screen.findByLabelText('Sınıf')).toHaveValue('2');
    expect(screen.getByLabelText('Program dili')).toHaveValue('Turkish');
    expect(screen.getByLabelText(/Uygulama grubu/)).toHaveValue('A');

    // ADR-085: a roster value is a suggestion. Changing one must be possible and
    // must stop the field claiming the faculty said it.
    await userEvent.selectOptions(screen.getByLabelText(/Uygulama grubu/), 'C');
    expect(screen.getByLabelText(/Uygulama grubu/)).toHaveValue('C');
  });

  it('does not fill anything in when the number is on two lists', async () => {
    // Two rows claim the number and the backend deliberately did not choose. The
    // form must not choose either.
    api.lookUpStudentRoster.mockResolvedValue({
      ...matched,
      outcome: 'Ambiguous',
      givenName: null,
      familyName: null,
      classYear: null,
      programLanguage: null,
      suggestedSelectors: {},
      dimensionsRequiringInput: [],
    });
    render(
      <AcademicProfileForm submitLabel="Kaydet" busyLabel="Kaydediliyor…" onSaved={() => {}} />,
    );

    await userEvent.type(await screen.findByLabelText('Öğrenci numarası'), '0101240080');
    await userEvent.click(screen.getByRole('button', { name: 'Öğrenci listesinde ara' }));

    expect(await screen.findByText(/birden fazla kez geçiyor/)).toBeInTheDocument();
    expect(screen.getByLabelText('Sınıf')).toHaveValue('');
  });

  it('leaves the form usable when the lookup itself fails', async () => {
    // A lookup is a convenience. Losing it must not block onboarding.
    api.lookUpStudentRoster.mockRejectedValue(new api.ApiError('429'));
    render(
      <AcademicProfileForm submitLabel="Kaydet" busyLabel="Kaydediliyor…" onSaved={() => {}} />,
    );

    await userEvent.type(await screen.findByLabelText('Öğrenci numarası'), '0101250001');
    await userEvent.click(screen.getByRole('button', { name: 'Öğrenci listesinde ara' }));

    expect(await screen.findByRole('alert')).toBeInTheDocument();
    await userEvent.selectOptions(screen.getByLabelText('Sınıf'), '2');
    expect(screen.getByLabelText('Sınıf')).toHaveValue('2');
  });
});

describe('RosterLookupNotice', () => {
  it('names what the list filled in and, separately, what the student still owes', () => {
    // A successful lookup does not make a profile complete, and the notice may
    // never imply that it does (ADR-085).
    render(
      <RosterLookupNotice
        result={{
          outcome: 'Matched',
          studentNumber: '0101250001',
          givenName: 'HAY*******',
          familyName: 'KIY***',
          academicYear: '2026-2027',
          classYear: 2,
          programLanguage: 'Turkish',
          suggestedSelectors: { practiceGroup: 'A', practiceSubgroup: 'A1' },
          dimensionsRequiringInput: ['anatomyGroup'],
          notices: [],
          someListsUnreadable: false,
        }}
      />,
    );

    const notice = screen.getByRole('status');
    expect(notice).toHaveTextContent('HAY******* KIY***');
    expect(notice).toHaveTextContent('2 alan listeden dolduruldu');
    expect(notice).toHaveTextContent('Anatomi grubu listede yazmıyor');
  });

  it('distinguishes an unreadable list from an absent student', () => {
    // "We could not read your list" and "you are not on any list" ask the student
    // for different things.
    render(
      <RosterLookupNotice
        result={{
          outcome: 'NotFound',
          studentNumber: '0101250001',
          suggestedSelectors: {},
          dimensionsRequiringInput: [],
          notices: [],
          someListsUnreadable: true,
        }}
      />,
    );

    expect(screen.getByRole('status')).toHaveTextContent('okunamadı');
  });
});

describe('ProfileSaveNotice', () => {
  it('says the re-synchronization was requested, never that it finished', () => {
    render(<ProfileSaveNotice resyncRequested />);

    // The worker converges the calendar on its next cycle. Claiming a finished synchronization
    // here would be exactly the claim AI_GUIDELINE §16 forbids before backend confirmation.
    const notice = screen.getByRole('status');
    expect(notice).toHaveTextContent('arka planda yapılır');
    expect(notice).not.toHaveTextContent(/tamamlandı\b/);
  });

  it('claims nothing about the calendar when no re-synchronization was requested', () => {
    // A false flag has more than one cause — an unchanged audience, or a changed audience on an
    // account with no completed calendar connection — so it must not assert either.
    render(<ProfileSaveNotice resyncRequested={false} />);

    const notice = screen.getByRole('status');
    expect(notice).toHaveTextContent('Profilin kaydedildi.');
    expect(notice).not.toHaveTextContent(/takvim/i);
  });
});
