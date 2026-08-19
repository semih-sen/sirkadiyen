import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { SourceCatalogEditor } from './SourceCatalogEditor';

const api = vi.hoisted(() => ({
  getSourceCatalog: vi.fn(),
  previewSourceCatalog: vi.fn(),
  applySourceCatalog: vi.fn(),
  listSourceCatalogRevisions: vi.fn(),
  getSourceCatalogRevision: vi.fn(),
  ApiError: class ApiError extends Error {
    status: number;

    constructor(status: number, _problem: unknown, fallback: string) {
      super(fallback);
      this.status = status;
    }
  },
}));
vi.mock('@/lib/api', () => api);

const catalog = {
  catalogVersion: '1.0',
  sources: [
    {
      sourceId: 'G1-TR-ANNUAL',
      displayName: 'Dönem 1 Türkçe yıllık program',
      transport: 'googleSheets',
      documentFormat: 'googleSheet',
      sourceUri: 'https://docs.google.com/spreadsheets/d/1abc/edit?gid=1',
      externalId: '1abc',
      sheetGid: 1,
      parserProfile: 'grade1_yearly_v1',
      parserProfileVersion: '1.5.0',
      academicYear: '2026-2027',
      classYear: 1,
      programLanguage: 'turkish',
      timeZoneId: 'Europe/Istanbul',
    },
  ],
};

const document = {
  path: '/srv/sirkadiyen/config/schedule-sources.json',
  content: `${JSON.stringify(catalog, null, 2)}\n`,
  contentHash: 'hash-on-disk',
  lastModifiedUtc: '2026-08-19T09:00:00Z',
  isWritable: true,
  isValid: true,
  validationError: null,
  catalogVersion: '1.0',
  sourceCount: 1,
};

const plan = {
  planHash: 'plan-hash-0123456789',
  baseContentHash: 'hash-on-disk',
  proposedContentHash: 'hash-proposed',
  normalizedContent: 'normalized',
  sourceCount: 1,
  added: [],
  removed: [],
  modified: [
    {
      sourceId: 'G1-TR-ANNUAL',
      displayName: 'Dönem 1 Türkçe yıllık program',
      program: 'Dönem 1 · Turkish · 2026-2027',
      kind: 'Modified' as const,
      fields: [
        { field: 'parserProfileVersion', before: '1.5.0', after: '1.6.0', risk: 'High' as const },
      ],
      isHighRisk: true,
    },
  ],
  unchangedCount: 0,
  warnings: [
    { code: 'parser-changed', message: 'Kaynak farklı bir parser profiliyle okunacak.', risk: 'High' as const },
  ],
  hasHighRiskChange: true,
  hasChanges: true,
};

describe('SourceCatalogEditor', () => {
  beforeEach(() => {
    Object.values(api).forEach((value) => {
      if (typeof value === 'function' && 'mockReset' in value) (value as ReturnType<typeof vi.fn>).mockReset();
    });
    api.getSourceCatalog.mockResolvedValue(document);
    api.previewSourceCatalog.mockResolvedValue(plan);
    api.applySourceCatalog.mockResolvedValue({
      revisionId: 'revision-1',
      contentHash: 'hash-proposed',
      appliedAtUtc: '2026-08-19T09:05:00Z',
      sourceRowsChanged: 1,
      pollingDisabledSourceIds: [],
      plan,
    });
    api.listSourceCatalogRevisions.mockResolvedValue([]);
  });

  it('shows the server file it is editing and refuses to preview an untouched document', async () => {
    render(<SourceCatalogEditor />);

    expect(await screen.findByText('/srv/sirkadiyen/config/schedule-sources.json')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Değişiklikleri incele' })).toBeDisabled();
  });

  it('previews an edit, then applies it only with a reason', async () => {
    const user = userEvent.setup();
    render(<SourceCatalogEditor />);
    await screen.findByText('/srv/sirkadiyen/config/schedule-sources.json');

    await user.click(screen.getByRole('button', { name: /Dönem 1 Türkçe yıllık program/ }));
    const version = screen.getByLabelText(/Parser sürümü/);
    await user.clear(version);
    await user.type(version, '1.6.0');

    await user.click(screen.getByRole('button', { name: 'Değişiklikleri incele' }));

    expect(await screen.findByText('parserProfileVersion')).toBeInTheDocument();
    expect(screen.getByText('Kaynak farklı bir parser profiliyle okunacak.')).toBeInTheDocument();

    // The confirmation is unavailable until a reason is written: it is what the audit trail and
    // the stored revision are answerable from.
    const apply = screen.getByRole('button', { name: 'Katalogu uygula' });
    expect(apply).toBeDisabled();

    await user.type(screen.getByLabelText('Değişiklik gerekçesi'), 'Parser sürümü yükseltildi');
    await user.click(screen.getByRole('button', { name: 'Katalogu uygula' }));

    await waitFor(() => expect(api.applySourceCatalog).toHaveBeenCalled());
    const [, baseHash, planHash, reason] = api.applySourceCatalog.mock.calls[0];
    expect(baseHash).toBe('hash-on-disk');
    expect(planHash).toBe('plan-hash-0123456789');
    expect(reason).toBe('Parser sürümü yükseltildi');
  });

  it('drops a previewed plan as soon as the document is edited again', async () => {
    const user = userEvent.setup();
    render(<SourceCatalogEditor />);
    await screen.findByText('/srv/sirkadiyen/config/schedule-sources.json');

    await user.click(screen.getByRole('button', { name: /Dönem 1 Türkçe yıllık program/ }));
    await user.type(screen.getByLabelText(/Görünen ad/), '!');
    await user.click(screen.getByRole('button', { name: 'Değişiklikleri incele' }));
    await screen.findByRole('button', { name: 'Katalogu uygula' });

    await user.type(screen.getByLabelText(/Görünen ad/), '?');

    // A plan hash that outlived the document it was computed for would authorize a change nobody
    // was shown.
    expect(screen.queryByRole('button', { name: 'Katalogu uygula' })).not.toBeInTheDocument();
  });

  it('surfaces a backend validation failure instead of writing anything', async () => {
    const user = userEvent.setup();
    api.previewSourceCatalog.mockRejectedValue(
      new api.ApiError(400, null, "Source 'G1-TR-ANNUAL' states an unsupported class year 9."),
    );
    render(<SourceCatalogEditor />);
    await screen.findByText('/srv/sirkadiyen/config/schedule-sources.json');

    await user.click(screen.getByRole('button', { name: /Dönem 1 Türkçe yıllık program/ }));
    await user.type(screen.getByLabelText(/Görünen ad/), '!');
    await user.click(screen.getByRole('button', { name: 'Değişiklikleri incele' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('unsupported class year');
    expect(api.applySourceCatalog).not.toHaveBeenCalled();
  });

  it('keeps the raw JSON editor usable when the document does not parse', async () => {
    const user = userEvent.setup();
    api.getSourceCatalog.mockResolvedValue({
      ...document,
      content: '{ broken',
      isValid: false,
      validationError: 'Beklenmeyen karakter.',
      sourceCount: null,
    });
    render(<SourceCatalogEditor />);
    await screen.findByText('/srv/sirkadiyen/config/schedule-sources.json');

    // The editor is the repair tool for a broken catalog, so it must show one rather than hide it.
    expect(screen.getByText(/Diskteki katalog geçerli değil/)).toBeInTheDocument();

    await user.click(screen.getByRole('tab', { name: 'JSON' }));
    expect(screen.getByLabelText('Katalog JSON belgesi')).toHaveValue('{ broken');
  });

  it('warns instead of offering an edit when the file is not writable', async () => {
    api.getSourceCatalog.mockResolvedValue({ ...document, isWritable: false });
    render(<SourceCatalogEditor />);

    expect(await screen.findByText(/yazılabilir değil/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Değişiklikleri incele' })).toBeDisabled();
  });

  it('loads a stored revision into the editor without applying it', async () => {
    const user = userEvent.setup();
    api.listSourceCatalogRevisions.mockResolvedValue([
      {
        id: 'revision-1',
        kind: 'Edit',
        recordedAtUtc: '2026-08-18T09:00:00Z',
        contentHash: 'older-hash',
        previousContentHash: null,
        sourceCount: 1,
        actorUserId: null,
        actorEmail: 'admin@example.com',
        reason: 'Gid düzeltildi',
        changeSummary: null,
        isCurrent: false,
      },
    ]);
    api.getSourceCatalogRevision.mockResolvedValue({
      summary: { id: 'revision-1' },
      content: '{ "catalogVersion": "1.0", "sources": [] }\n',
    });

    render(<SourceCatalogEditor />);
    await screen.findByText('/srv/sirkadiyen/config/schedule-sources.json');
    await user.click(screen.getByRole('tab', { name: 'Sürüm geçmişi' }));

    const row = (await screen.findByText('Gid düzeltildi')).closest('tr')!;
    await user.click(within(row).getByRole('button', { name: 'Editöre yükle' }));

    expect(await screen.findByLabelText('Katalog JSON belgesi'))
      .toHaveValue('{ "catalogVersion": "1.0", "sources": [] }\n');
    expect(api.applySourceCatalog).not.toHaveBeenCalled();
  });
});
