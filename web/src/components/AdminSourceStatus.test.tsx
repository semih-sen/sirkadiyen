import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { AdminSourceWorkspace } from './AdminSourceStatus';

const api = vi.hoisted(() => ({ listAdminSources: vi.fn(), getAdminSource: vi.fn() }));
vi.mock('@/lib/api', () => api);
vi.mock('@/components/SourceDocumentUpload', () => ({ SourceDocumentUpload: () => <div>Yükleme</div> }));

const summary = { sourceId: 'G1-TR', displayName: 'Dönem 1 Türkçe', classYear: 1, programLanguage: 'Turkish', transport: 'GoogleSheets', isPollingEnabled: true, latestParseRunStatus: 'CompletedWithWarnings', latestParseWarningCount: 1, latestParseErrorCount: 0, latestRevisionState: 'Published' };

describe('AdminSourceWorkspace', () => {
  beforeEach(() => {
    api.listAdminSources.mockResolvedValue([summary]);
    api.getAdminSource.mockResolvedValue({
      summary,
      parserProfile: 'grade1_yearly_v1',
      parserProfileVersion: '1.4.0',
      latestParseWarnings: [{ severity: 'Warning', code: 'row.ignored', message: 'Satır güvenle çözümlenemedi.', evidence: { sheetId: '1', sheetTitle: 'Dönem 1', range: 'A12:G12', extractionRule: 'annual.row' } }],
      recentSnapshots: [],
    });
  });

  it('says a source whose document cannot be acquired is failing, and for how long', async () => {
    // Every other column on the row describes the state the source reached before it started
    // failing, so without this the screen shows a source that merely looks quiet. Three Grade 3
    // workbooks were in the Drive trash for four days and nothing on this screen said so
    // (ADR-137).
    const failedAt = new Date(Date.now() - 4 * 24 * 60 * 60 * 1000).toISOString();
    const failing = {
      ...summary,
      sourceId: 'G3-TR-A-ANNUAL',
      displayName: 'Dönem 3 Türkçe A yıllık program',
      lastPolledAtUtc: '2026-08-26T06:15:00Z',
      lastPollFailureAtUtc: failedAt,
      lastPollFailureReason:
        "Google Drive file '1DsC72z' is in the trash, so it is no longer a published source.",
    };
    api.listAdminSources.mockResolvedValue([failing]);
    api.getAdminSource.mockResolvedValue({
      summary: failing,
      parserProfile: 'grade3_yearly_v1',
      parserProfileVersion: '1.3.0',
      latestParseWarnings: [],
      recentSnapshots: [],
    });

    const user = userEvent.setup();
    render(<AdminSourceWorkspace />);

    expect(await screen.findByText(/1 kaynağın belgesi alınamıyor/)).toBeInTheDocument();
    expect(screen.getByText('4 gündür alınamıyor')).toBeInTheDocument();

    // And the reason itself, which is the part that says what to do about it.
    await user.click(screen.getByText('Dönem 3 Türkçe A yıllık program'));
    expect(await screen.findByText(/is in the trash/)).toBeInTheDocument();
  });

  it('shows why the latest parse run failed, inline and in the detail', async () => {
    // A failed run stores no parser response, so the warning list below is empty and the
    // warning/error counts are both zero. Without the reason the row is a red "Failed" badge next to
    // "0 / 0" and nothing that says what to fix.
    const reason =
      "InvalidDataException: Candidate 'S1!R4C3' contradicts its configured source context.";
    const failed = {
      ...summary,
      sourceId: 'G1-TR-PRACTICE',
      displayName: 'Dönem 1 Türkçe uygulama programı',
      latestParseRunStatus: 'Failed',
      latestParseWarningCount: 0,
      latestParseErrorCount: 0,
      latestParseRunAtUtc: '2026-09-06T12:15:00Z',
      latestParseFailureReason: reason,
    };
    api.listAdminSources.mockResolvedValue([failed]);
    api.getAdminSource.mockResolvedValue({
      summary: failed,
      parserProfile: 'grade1_practice_v1',
      parserProfileVersion: '1.2.0',
      latestParseWarnings: [],
      recentSnapshots: [],
    });

    const user = userEvent.setup();
    render(<AdminSourceWorkspace />);

    // Inline in the table (truncated preview), and in full inside the detail drawer.
    expect(await screen.findAllByText(new RegExp('contradicts its configured source context'))).not.toHaveLength(0);
    await user.click(screen.getByText('Dönem 1 Türkçe uygulama programı'));
    expect(await screen.findByText('Parse başarısız oldu.')).toBeInTheDocument();
    // The reason shows both inline in the row and in the drawer, so more than one node carries it.
    expect(
      screen.getAllByText(new RegExp(reason.replace(/[.*+?^${}()|[\]\\]/g, '\\$&'))),
    ).not.toHaveLength(0);
  });

  it('shows persisted parser warning details without exposing a parse action', async () => {
    const user = userEvent.setup();
    render(<AdminSourceWorkspace />);
    await user.click(await screen.findByText('Dönem 1 Türkçe'));
    expect(await screen.findByText('Satır güvenle çözümlenemedi.')).toBeInTheDocument();
    expect(screen.getByText(/A12:G12/)).toBeInTheDocument();
    expect(screen.getByText(/poll veya parse başlatmaz/)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /parse/i })).not.toBeInTheDocument();
  });
});
