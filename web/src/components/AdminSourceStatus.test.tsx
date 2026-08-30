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
