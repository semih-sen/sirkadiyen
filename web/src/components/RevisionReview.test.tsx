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
  state: 'ReviewRequired',
  createdAtUtc: '2026-08-15T08:00:00Z',
  recordCount: 164,
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
    await userEvent.click(await screen.findByText('G2-TR-PRACTICE'));
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

    await userEvent.click(await screen.findByText('G2-TR-PRACTICE'));
    expect(await screen.findByText('ops@example.com')).toBeInTheDocument();
    expect(screen.getByText('Kaynak belgede tarih hatası var.')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Onayla ve yayınla' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Revizyonu reddet' })).not.toBeInTheDocument();
  });
});
