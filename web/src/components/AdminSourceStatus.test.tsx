import { render, screen } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { AdminSourceWorkspace } from './AdminSourceStatus';

const api = vi.hoisted(() => ({ listAdminSources: vi.fn(), getAdminSource: vi.fn() }));
vi.mock('@/lib/api', () => api);
vi.mock('@/components/SourceDocumentUpload', () => ({ SourceDocumentUpload: () => <div>Yükleme</div> }));

describe('AdminSourceWorkspace', () => {
  beforeEach(() => api.listAdminSources.mockResolvedValue([{ sourceId: 'G1-TR', displayName: 'Dönem 1 Türkçe', classYear: 1, programLanguage: 'Turkish', transport: 'GoogleSheets', isPollingEnabled: true, latestParseRunStatus: 'Completed', latestParseWarningCount: 2, latestParseErrorCount: 0, latestRevisionState: 'Published' }]));
  it('labels the status view as read-only evidence', async () => {
    render(<AdminSourceWorkspace />);
    expect(await screen.findByText('Dönem 1 Türkçe')).toBeInTheDocument();
    expect(screen.getByText(/poll veya parse başlatmaz/)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /parse/i })).not.toBeInTheDocument();
  });
});
