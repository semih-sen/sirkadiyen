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
