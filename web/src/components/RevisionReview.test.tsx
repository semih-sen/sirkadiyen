import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { RevisionReview } from './RevisionReview';

const api = vi.hoisted(() => ({
  listRevisions: vi.fn(),
  getRevision: vi.fn(),
  approveRevision: vi.fn(),
  rejectRevision: vi.fn(),
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

    expect(await screen.findByText('2019-10-02')).toBeInTheDocument();
    expect(screen.getByText('Anatomi')).toBeInTheDocument();
    expect(screen.getByText('Fizyoloji')).toBeInTheDocument();
    expect(screen.queryByText('[object Object]')).not.toBeInTheDocument();

    // And the rule is explained, not just named: the operator is told what approving would do.
    expect(screen.getByText('Akademik yıl dışında tarih')).toBeInTheDocument();
    expect(screen.getByText(/tarihin yanlış okunduğu anlamına gelir/)).toBeInTheDocument();
  });
});
