import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { RevisionReview } from './RevisionReview';

const api = vi.hoisted(() => ({
  listRevisions: vi.fn(),
  getRevision: vi.fn(),
  approveRevision: vi.fn(),
  rejectRevision: vi.fn(),
  acceptSourceDateCorrection: vi.fn(),
  listAllSourceDateCorrections: vi.fn(),
  retireSourceDateCorrection: vi.fn(),
  ApiError: class ApiError extends Error {},
}));
vi.mock('@/lib/api', () => api);

const summary = {
  revisionId: 'rev-1',
  sourceId: 'G2-TR-PRACTICE',
  displayName: 'Dönem 2 Türkçe pratik programı',
  classYear: 2,
  programLanguage: 'Turkish',
  academicYear: '2026-2027',
  state: 'ReviewRequired',
  createdAtUtc: '2026-08-15T08:00:00Z',
  recordCount: 164,
  publishedRecordCount: 180,
  errorFindingCount: 1,
  warningFindingCount: 0,
  stateReason: 'AudienceOverlap: 4 aynı ders çakışması.',
};

const storedCorrection = {
  id: 'corr-1',
  sourceId: 'G2-TR-PRACTICE',
  original: '2019-10-02',
  corrected: '2026-10-02',
  decidedBy: 'ops@example.com',
  decidedAtUtc: '2026-08-15T09:00:00Z',
  note: 'Satır geçen yılın dosyasından kalmış.',
};

describe('RevisionReview', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    api.listRevisions.mockResolvedValue([summary]);
    api.getRevision.mockResolvedValue({
      summary,
      findings: [{
        rule: 'AudienceOverlap', severity: 'Error', message: 'Aynı ders iki kez yazılmış.',
        affectedRecordCount: 4, createdAtUtc: '2026-08-15T08:00:00Z', detail: '[]',
      }],
    });
    api.rejectRevision.mockResolvedValue({ revisionId: 'rev-1', rejected: true });
    api.listAllSourceDateCorrections.mockResolvedValue([storedCorrection]);
    api.retireSourceDateCorrection.mockResolvedValue(undefined);
  });

  it('makes rejection a deliberate, reasoned and terminal action', async () => {
    render(<RevisionReview />);
    await userEvent.click(await screen.findByText('Dönem 2 Türkçe pratik programı'));
    expect(await screen.findByText('Aynı ders iki kez yazılmış.')).toBeInTheDocument();

    // Rejection is behind a confirmation step, and the confirmation states that the correction
    // is a newer revision rather than a rollback (ADR-033).
    expect(screen.queryByLabelText(/Reddetme gerekçesi/)).not.toBeInTheDocument();
    expect(screen.getByText(/geri alınamaz/)).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: 'Revizyonu reddet' }));
    await userEvent.click(screen.getByRole('button', { name: 'Reddetmeyi onayla' }));
    expect(api.rejectRevision).not.toHaveBeenCalled();
    expect(screen.getByRole('alert')).toHaveTextContent('gerekçe');

    await userEvent.type(screen.getByLabelText(/Reddetme gerekçesi/), 'Tarih sütunu kaymış.');
    await userEvent.click(screen.getByRole('button', { name: 'Reddetmeyi onayla' }));
    await waitFor(() => expect(api.rejectRevision).toHaveBeenCalledWith('rev-1', 'Tarih sütunu kaymış.'));
    expect(api.approveRevision).not.toHaveBeenCalled();
  });

  it('reads back who rejected a revision and why, with no action offered', async () => {
    const rejected = { ...summary, state: 'Rejected' };
    api.listRevisions.mockResolvedValue([rejected]);
    api.getRevision.mockResolvedValue({
      summary: rejected,
      findings: [],
      rejectedBy: 'ops@example.com',
      rejectionReason: 'Kaynak belgede tarih hatası var.',
      rejectedAtUtc: '2026-08-15T09:30:00Z',
    });

    render(<RevisionReview />);
    await userEvent.click(await screen.findByRole('tab', { name: 'Reddedilen' }));
    await waitFor(() => expect(api.listRevisions).toHaveBeenCalledWith('Rejected'));

    await userEvent.click(await screen.findByText('Dönem 2 Türkçe pratik programı'));
    expect(await screen.findByText('ops@example.com')).toBeInTheDocument();
    expect(screen.getByText('Kaynak belgede tarih hatası var.')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Onayla ve yayınla' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Revizyonu reddet' })).not.toBeInTheDocument();
  });

  it('says whose schedule this is and how it compares with what is published', async () => {
    // The decision is "may these lessons replace the ones students have", and it cannot be made
    // from a source ID and a record count alone (ADR-135).
    render(<RevisionReview />);

    expect(await screen.findByText('Dönem 2 Türkçe pratik programı')).toBeInTheDocument();
    expect(screen.getByText('G2-TR-PRACTICE')).toBeInTheDocument();
    expect(screen.getByText(/Dönem 2 · Turkish · 2026-2027/)).toBeInTheDocument();
    expect(screen.getByText('1 hata')).toBeInTheDocument();

    // The number that decides it: 164 against a published 180 removes 16 lessons from calendars.
    expect(screen.getByText(/16 dersi kaldırıyor/)).toBeInTheDocument();
  });

  it('renders the recorded evidence as records rather than as [object Object]', async () => {
    // Every rule that names records stores an array of objects. Rendering them with String()
    // produced a column of "[object Object]", which is the same as having stored no evidence.
    api.getRevision.mockResolvedValue({
      summary,
      findings: [{
        rule: 'RecordDateOutsideAcademicYear',
        severity: 'Error',
        message: '2 record(s) fall outside academic year.',
        affectedRecordCount: 2,
        createdAtUtc: '2026-08-15T08:00:00Z',
        detail: JSON.stringify([
          { candidateId: 'c-1', date: '2019-10-02', displayTitle: 'Anatomi' },
          { candidateId: 'c-2', date: '2019-10-03', displayTitle: 'Fizyoloji' },
        ]),
      }],
    });

    render(<RevisionReview />);
    await userEvent.click(await screen.findByText('Dönem 2 Türkçe pratik programı'));

    // The date appears twice on purpose: once as evidence, once as the correction it can be given
    // (ADR-139 amendment), so this asserts the evidence row rather than a unique occurrence.
    expect((await screen.findAllByText('2019-10-02')).length).toBeGreaterThan(0);
    expect(screen.getByText('Anatomi')).toBeInTheDocument();
    expect(screen.getByText('Fizyoloji')).toBeInTheDocument();
    expect(screen.queryByText('[object Object]')).not.toBeInTheDocument();

    // And the rule is explained, not just named: the operator is told what approving would do.
    expect(screen.getByText('Akademik yıl dışında tarih')).toBeInTheDocument();
    expect(screen.getByText(/tarihin yanlış okunduğu anlamına gelir/)).toBeInTheDocument();
  });

  it('offers the readings a refused date may have meant, and corrects the source with one', async () => {
    // The lever the screen was missing (ADR-139). `21 Mayıs 2026 Perşembe` substitutes to
    // 2027-05-21, which is a Friday, so the cell disagrees with its own repair and the parser
    // published it as written. Approving would put the lesson a year early; rejecting would hold
    // the schedule for a workbook that is not ours to edit. Accepting a reading corrects the
    // source, and the next poll reads it.
    api.getRevision.mockResolvedValue({
      summary,
      findings: [{
        rule: 'RecordDateOutOfSequence',
        severity: 'Error',
        message: '1 date(s) contradict the order of the column they sit in.',
        affectedRecordCount: 1,
        createdAtUtc: '2026-08-15T08:00:00Z',
        detail: JSON.stringify([{
          original: '2026-05-21',
          applied: null,
          lowerAnchor: null,
          upperAnchor: '2027-05-24',
          reason: 'candidateContradictsTheStatedWeekday',
          cell: 'A248',
          candidates: [
            { value: '2027-05-21', rule: 'sequenceYearSubstitution', weekdayMatches: false },
            { value: '2027-05-20', rule: 'sequenceWeekdayAlternative', weekdayMatches: true },
          ],
        }]),
      }],
    });
    api.acceptSourceDateCorrection.mockResolvedValue({ id: 'c-1' });

    render(<RevisionReview />);
    await userEvent.click(await screen.findByText('Dönem 2 Türkçe pratik programı'));

    expect(await screen.findByText('Sıra dışı tarih')).toBeInTheDocument();

    // The cell's address and the reason the parser withheld the correction, so the operator can
    // open the document at the row this is about.
    // Both the generic evidence table and the action above it name the cell, so this asserts
    // presence rather than uniqueness.
    expect(screen.getAllByText(/A248/).length).toBeGreaterThan(0);
    expect(
      screen.getAllByText(/Hücre kendi yazdığı gün adıyla çelişiyor/).length,
    ).toBeGreaterThan(0);

    // A decision this consequential is never taken without a recorded reason.
    await userEvent.click(screen.getByRole('button', { name: /2027-05-20/ }));
    expect(api.acceptSourceDateCorrection).not.toHaveBeenCalled();
    expect(screen.getByRole('alert')).toHaveTextContent('gerekçe');

    await userEvent.type(
      screen.getByLabelText(/Kabul gerekçesi/),
      'Belgeyi kontrol ettim; satır geçen yılın dosyasından kalmış.',
    );
    await userEvent.click(screen.getByRole('button', { name: /2027-05-20/ }));

    await waitFor(() => expect(api.acceptSourceDateCorrection).toHaveBeenCalledWith(
      'G2-TR-PRACTICE',
      '2026-05-21',
      '2027-05-20',
      'Belgeyi kontrol ettim; satır geçen yılın dosyasından kalmış.',
    ));

    // Accepting does not settle this revision, and the screen says so rather than leaving the
    // operator to wonder why the queue still holds it.
    expect(await screen.findByText(/^Kabul edildi:/)).toBeInTheDocument();
  });

  it('offers no decision for a date the parser already repaired', async () => {
    api.getRevision.mockResolvedValue({
      summary,
      findings: [{
        rule: 'RecordDateOutOfSequence',
        severity: 'Warning',
        message: '1 date(s) were read as a mistyped year.',
        affectedRecordCount: 1,
        createdAtUtc: '2026-08-15T08:00:00Z',
        detail: JSON.stringify([{
          original: '2020-11-20',
          applied: '2026-11-20',
          lowerAnchor: '2026-11-19',
          upperAnchor: '2026-11-20',
          reason: 'repaired',
          cell: 'A69',
          candidates: [
            { value: '2026-11-20', rule: 'sequenceYearSubstitution', weekdayMatches: null },
          ],
        }]),
      }],
    });

    render(<RevisionReview />);
    await userEvent.click(await screen.findByText('Dönem 2 Türkçe pratik programı'));

    // The repair is reported — it moved a lesson — but there is nothing left to accept.
    expect(await screen.findByText('Sıra dışı tarih')).toBeInTheDocument();
    expect(screen.getByText('2020-11-20')).toBeInTheDocument();
    expect(screen.queryByLabelText(/Kabul gerekçesi/)).not.toBeInTheDocument();
  });

  it('takes a date typed from the document when the parser proposed none', async () => {
    // The parser reads the dates around a cell; the operator reads the document. When the anchors
    // leave no candidate the parser proposes nothing, and before this the screen could only tell
    // the operator to go elsewhere — to a screen that did not exist.
    api.getRevision.mockResolvedValue({
      summary,
      findings: [{
        rule: 'RecordDateOutOfSequence',
        severity: 'Error',
        message: '1 date(s) could not be read.',
        affectedRecordCount: 1,
        createdAtUtc: '2026-08-15T08:00:00Z',
        detail: JSON.stringify([{
          original: '2019-10-02',
          applied: null,
          reason: 'noCandidateFitsTheAnchors',
          cell: 'A248',
          candidates: [],
        }]),
      }],
    });

    render(<RevisionReview />);
    await userEvent.click(await screen.findByText('Dönem 2 Türkçe pratik programı'));

    expect(await screen.findByText(/doğru tarihi belgeden okuyup yukarıya/)).toBeInTheDocument();

    await userEvent.type(screen.getByLabelText('Belgedeki doğru tarih'), '2026-10-02');
    await userEvent.click(screen.getByRole('button', { name: 'Bu tarihi kabul et' }));

    // Still no decision without a recorded reason, exactly as accepting a candidate.
    expect(api.acceptSourceDateCorrection).not.toHaveBeenCalled();
    expect(screen.getByRole('alert')).toHaveTextContent('gerekçe');

    await userEvent.type(screen.getByLabelText(/Kabul gerekçesi/), 'Belgede 2 Ekim 2026 yazıyor.');
    await userEvent.click(screen.getByRole('button', { name: 'Bu tarihi kabul et' }));

    await waitFor(() => expect(api.acceptSourceDateCorrection).toHaveBeenCalledWith(
      'G2-TR-PRACTICE',
      '2019-10-02',
      '2026-10-02',
      'Belgede 2 Ekim 2026 yazıyor.',
    ));
  });

  it('lets a date outside the academic year be corrected per distinct date, not per lesson', async () => {
    api.getRevision.mockResolvedValue({
      summary,
      findings: [{
        rule: 'RecordDateOutsideAcademicYear',
        severity: 'Error',
        message: '3 record(s) fall outside academic year.',
        affectedRecordCount: 3,
        createdAtUtc: '2026-08-15T08:00:00Z',
        detail: JSON.stringify([
          { candidateId: 'c-1', date: '2019-10-02', displayTitle: 'Anatomi' },
          { candidateId: 'c-2', date: '2019-10-02', displayTitle: 'Fizyoloji' },
          { candidateId: 'c-3', date: '2019-10-03', displayTitle: 'Histoloji' },
        ]),
      }],
    });

    render(<RevisionReview />);
    await userEvent.click(await screen.findByText('Dönem 2 Türkçe pratik programı'));

    // A correction is keyed by the wrong date, so three lessons on two dates are two decisions.
    expect(await screen.findByText(/2 ders/)).toBeInTheDocument();
    expect(screen.getAllByLabelText('Belgedeki doğru tarih')).toHaveLength(2);

    await userEvent.type(
      screen.getAllByLabelText('Belgedeki doğru tarih')[0],
      '2026-10-02',
    );
    await userEvent.type(
      screen.getAllByLabelText(/Kabul gerekçesi/)[0],
      'Belgede 2026 yazıyor; parser yılı doğru okumuş, satır eski dosyadan kalmış.',
    );
    await userEvent.click(screen.getAllByRole('button', { name: 'Bu tarihi kabul et' })[0]);

    await waitFor(() => expect(api.acceptSourceDateCorrection).toHaveBeenCalledWith(
      'G2-TR-PRACTICE',
      '2019-10-02',
      '2026-10-02',
      'Belgede 2026 yazıyor; parser yılı doğru okumuş, satır eski dosyadan kalmış.',
    ));
  });

  it('reads back the stored corrections and changes one without touching a calendar', async () => {
    render(<RevisionReview />);
    await userEvent.click(await screen.findByRole('tab', { name: 'Sıradışı tarihler' }));

    expect(await screen.findByText('G2-TR-PRACTICE')).toBeInTheDocument();
    expect(screen.getByText('2019-10-02')).toBeInTheDocument();
    expect(screen.getByText('Satır geçen yılın dosyasından kalmış.')).toBeInTheDocument();

    // Changing a correction does not repair a written calendar; it changes what the next parse
    // reads, and the screen says so (ADR-033).
    expect(screen.getByText(/kaynağı yeniden çekin/)).toBeInTheDocument();

    await userEvent.clear(screen.getByLabelText('Okunacak tarih'));
    await userEvent.type(screen.getByLabelText('Okunacak tarih'), '2026-10-09');
    await userEvent.click(screen.getByRole('button', { name: 'Tarihi değiştir' }));
    expect(api.acceptSourceDateCorrection).not.toHaveBeenCalled();
    expect(screen.getByRole('alert')).toHaveTextContent('gerekçe');

    await userEvent.type(screen.getByLabelText(/Gerekçe/), 'Ders bir hafta ileri alındı.');
    await userEvent.click(screen.getByRole('button', { name: 'Tarihi değiştir' }));

    await waitFor(() => expect(api.acceptSourceDateCorrection).toHaveBeenCalledWith(
      'G2-TR-PRACTICE',
      '2019-10-02',
      '2026-10-09',
      'Ders bir hafta ileri alındı.',
    ));
  });

  it('confirms before retiring a stored correction, saying what returns', async () => {
    render(<RevisionReview />);
    await userEvent.click(await screen.findByRole('tab', { name: 'Sıradışı tarihler' }));
    await screen.findByText('G2-TR-PRACTICE');

    await userEvent.click(screen.getByRole('button', { name: 'Düzeltmeyi kaldır' }));
    expect(api.retireSourceDateCorrection).not.toHaveBeenCalled();
    expect(screen.getByText(/dersler o tarihe/)).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: 'Kaldırmayı onayla' }));
    await waitFor(() => expect(api.retireSourceDateCorrection).toHaveBeenCalledWith(
      'G2-TR-PRACTICE',
      'corr-1',
    ));
  });
});
