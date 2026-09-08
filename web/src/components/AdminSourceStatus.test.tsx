import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { AdminSourceWorkspace } from './AdminSourceStatus';

const api = vi.hoisted(() => ({ listAdminSources: vi.fn(), getAdminSource: vi.fn() }));
vi.mock('@/lib/api', () => api);
vi.mock('@/components/SourceDocumentUpload', () => ({ SourceDocumentUpload: () => <div>Yükleme</div> }));

const summary = { sourceId: 'G1-TR', displayName: 'Dönem 1 Türkçe', classYear: 1, programLanguage: 'Turkish', transport: 'GoogleSheets', isPollingEnabled: true, publishesSchedule: true, latestParseRunStatus: 'CompletedWithWarnings', latestParseWarningCount: 1, latestParseErrorCount: 0, latestRevisionState: 'Published' };

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

  it('keeps a source the catalog no longer declares out of the operational list', async () => {
    // A retired source is never polled again, so every column on its row is frozen at the day it
    // was dropped. Left in the table it is a row that can only ever read as stale, which is what
    // G2-VERTICAL-SPRING and G2-VERTICAL-AUTUMN were doing months after their documents were
    // retired (ADR-155). Nothing is deleted, so it stays reachable underneath.
    const retired = {
      ...summary,
      sourceId: 'G2-VERTICAL-SPRING',
      displayName: 'Dönem 2 dikey koridor beceri uygulamaları bahar',
      isPollingEnabled: false,
      retiredAtUtc: '2026-09-01T09:00:00Z',
    };
    api.listAdminSources.mockResolvedValue([summary, retired]);

    const user = userEvent.setup();
    render(<AdminSourceWorkspace />);

    await screen.findByText('Dönem 1 Türkçe');
    const table = screen.getByRole('table');
    expect(within(table).queryByText('Dönem 2 dikey koridor beceri uygulamaları bahar')).toBeNull();

    // And still reachable, with its evidence, from the retired list.
    expect(screen.getByText('Katalogdan çıkarılmış 1 kaynak')).toBeInTheDocument();
    api.getAdminSource.mockResolvedValue({
      summary: retired,
      parserProfile: 'grade2_vertical_corridor_v1',
      parserProfileVersion: '1.2.0',
      latestParseWarnings: [],
      recentSnapshots: [],
    });
    await user.click(screen.getByRole('button', { name: 'Dönem 2 dikey koridor beceri uygulamaları bahar' }));
    expect(await screen.findByText('Bu kaynak katalogdan çıkarılmış.')).toBeInTheDocument();
  });

  it('does not raise the acquisition alarm for a retired source', async () => {
    // Its last failure can never be resolved, because nothing will poll it again. Counted in the
    // banner it is a permanent alarm, and a permanent alarm is one nobody reads.
    api.listAdminSources.mockResolvedValue([
      {
        ...summary,
        sourceId: 'G2-VERTICAL-AUTUMN',
        displayName: 'Dönem 2 dikey koridor beceri uygulamaları güz',
        retiredAtUtc: '2026-09-01T09:00:00Z',
        lastPollFailureAtUtc: '2026-09-01T08:00:00Z',
        lastPollFailureReason: 'The Drive file is in the trash.',
      },
    ]);

    render(<AdminSourceWorkspace />);

    expect(await screen.findByText('Katalogdan çıkarılmış 1 kaynak')).toBeInTheDocument();
    expect(screen.queryByText(/kaynağın belgesi alınamıyor/)).toBeNull();
  });

  it('says an uploaded source is failing to be processed, not to be acquired', async () => {
    // An administratively uploaded document has no location to fetch from, so "belge alınamıyor"
    // describes something that never happens for it (ADR-155).
    api.listAdminSources.mockResolvedValue([
      {
        ...summary,
        sourceId: 'G2-ANATOMY-AUTUMN',
        displayName: 'Dönem 2 anatomi salon grup saatleri güz',
        transport: 'AdministrativeUpload',
        lastPollFailureAtUtc: new Date(Date.now() - 3 * 60 * 60 * 1000).toISOString(),
        lastPollFailureReason: 'The parser service refused the request.',
      },
    ]);

    render(<AdminSourceWorkspace />);

    expect(await screen.findByText('3 saattir işlenemiyor')).toBeInTheDocument();
  });

  it('reads a companion source\'s empty revision as its design, not as a rejection', async () => {
    // The bedside lists and the weekly amphitheatre program emit no candidates at all: the annual
    // states when each session is, and these say what it is about or which room it uses. Their
    // revision is empty and refused every cycle, which is correct — and as a plain red "Rejected"
    // it is a permanent alarm sitting beside the ones that mean something (ADR-156).
    const companion = {
      ...summary,
      sourceId: 'G3-TR-A-BEDSIDE',
      displayName: 'Dönem 3 Türkçe A hasta başı uygulama',
      publishesSchedule: false,
      latestRevisionState: 'Rejected',
    };
    api.listAdminSources.mockResolvedValue([companion]);
    api.getAdminSource.mockResolvedValue({
      summary: companion,
      parserProfile: 'grade3_bedside_v1',
      parserProfileVersion: '1.1.0',
      latestParseWarnings: [],
      recentSnapshots: [],
    });

    const user = userEvent.setup();
    render(<AdminSourceWorkspace />);

    expect(await screen.findByText('Yayımlamaz')).toBeInTheDocument();
    expect(screen.queryByText('Rejected')).toBeNull();

    await user.click(screen.getByText('Dönem 3 Türkçe A hasta başı uygulama'));
    expect(await screen.findByText('Bu kaynak kendi programını yayımlamaz.')).toBeInTheDocument();
  });

  it('still shows a companion source\'s state when it is not the expected rejection', async () => {
    // The label must not become a blindfold: a source declared as publishing nothing that has
    // suddenly published something is exactly what an operator has to see.
    api.listAdminSources.mockResolvedValue([
      {
        ...summary,
        sourceId: 'SHARED-AMPHI',
        displayName: 'Haftalık amfi programı',
        publishesSchedule: false,
        latestRevisionState: 'ReviewRequired',
      },
    ]);

    render(<AdminSourceWorkspace />);

    expect(await screen.findByText('ReviewRequired')).toBeInTheDocument();
    expect(screen.queryByText('Yayımlamaz')).toBeNull();
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
