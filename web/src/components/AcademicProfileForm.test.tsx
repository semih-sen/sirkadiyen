import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { AcademicProfileForm, ProfileSaveNotice } from './AcademicProfileForm';

const api = vi.hoisted(() => ({
  getProfileOptions: vi.fn(),
  saveProfile: vi.fn(),
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
