import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { RosterCatalogEditor } from './RosterCatalogEditor';

const api = vi.hoisted(() => ({
  getRosterCatalog: vi.fn(),
  previewRosterCatalog: vi.fn(),
  applyRosterCatalog: vi.fn(),
  listRosterCatalogRevisions: vi.fn(),
  getRosterCatalogRevision: vi.fn(),
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
  rosters: [
    {
      rosterId: 'G2-TR-ROSTER',
      displayName: 'Dönem 2 Türkçe öğrenci listesi',
      transport: 'googleSheets',
      documentFormat: 'googleSheet',
      sourceUri: 'https://docs.google.com/spreadsheets/d/1abc/edit?gid=1',
      externalId: '1abc',
      sheetGid: 1,
      academicYear: '2026-2027',
      classYear: 2,
      programLanguage: 'turkish',
      layout: {
        worksheetTitle: 'Sayfa1',
        headerRow: 1,
        studentNumberHeader: 'Öğrenci No',
        givenNameHeader: 'Ad',
        familyNameHeader: 'Soyad',
        dimensionColumns: [
          {
            header: 'GRUP',
            dimension: 'practiceGroup',
            statedOncePerMergedRun: true,
            valueMap: { A: 'A', B: 'B' },
          },
        ],
      },
    },
  ],
};

const document = {
  path: '/srv/sirkadiyen/shared/config/student-rosters.json',
  content: `${JSON.stringify(catalog, null, 2)}\n`,
  contentHash: 'hash-on-disk',
  lastModifiedUtc: '2026-08-30T09:00:00Z',
  isWritable: true,
  isValid: true,
  validationError: null,
  catalogVersion: '1.0',
  rosterCount: 1,
};

const plan = {
  planHash: 'plan-hash-0123456789',
  baseContentHash: 'hash-on-disk',
  proposedContentHash: 'hash-proposed',
  normalizedContent: 'normalized',
  rosterCount: 1,
  added: [],
  removed: [],
  modified: [
    {
      rosterId: 'G2-TR-ROSTER',
      displayName: 'Dönem 2 Türkçe öğrenci listesi',
      cohort: 'Dönem 2 · Turkish · 2026-2027',
      kind: 'Modified' as const,
      fields: [
        {
          field: 'layout.dimensionColumns[practiceGroup]',
          before: 'sütun "GRUP" (birleştirilmiş): A→A, B→B',
          after: 'sütun "GRUP" (birleştirilmiş): A→B, B→A',
          risk: 'High' as const,
        },
      ],
      isHighRisk: true,
    },
  ],
  unchangedCount: 0,
  warnings: [
    {
      code: 'layout-changed',
      message: 'Liste bundan sonra farklı bir yerleşimle okunacak.',
      risk: 'High' as const,
    },
  ],
  hasHighRiskChange: true,
  hasChanges: true,
};

describe('RosterCatalogEditor', () => {
  beforeEach(() => {
    Object.values(api).forEach((value) => {
      if (typeof value === 'function' && 'mockReset' in value) (value as ReturnType<typeof vi.fn>).mockReset();
    });
    api.getRosterCatalog.mockResolvedValue(document);
    api.previewRosterCatalog.mockResolvedValue(plan);
    api.applyRosterCatalog.mockResolvedValue({
      revisionId: 'revision-1',
      contentHash: 'hash-proposed',
      appliedAtUtc: '2026-08-30T09:05:00Z',
      readingInvalidated: true,
      plan,
    });
    api.listRosterCatalogRevisions.mockResolvedValue([]);
  });

  it('shows the server file it is editing and refuses to preview an untouched document', async () => {
    render(<RosterCatalogEditor />);

    expect(await screen.findByText('/srv/sirkadiyen/shared/config/student-rosters.json')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Değişiklikleri incele' })).toBeDisabled();
  });

  it('previews a value-map change showing both maps in full, then applies it only with a reason', async () => {
    const user = userEvent.setup();
    render(<RosterCatalogEditor />);
    await screen.findByText('/srv/sirkadiyen/shared/config/student-rosters.json');

    await user.click(screen.getByRole('button', { name: /Dönem 2 Türkçe öğrenci listesi/ }));
    const values = screen.getByLabelText(/Değer eşlemesi/);
    await user.clear(values);
    await user.type(values, '{{ "A": "B", "B": "A" }');

    await user.click(screen.getByRole('button', { name: 'Değişiklikleri incele' }));

    // The map is the field that can be wrong without anything failing, so the review has to show
    // which stated value now means which profile value — not that "the map changed".
    expect(await screen.findByText('layout.dimensionColumns[practiceGroup]')).toBeInTheDocument();
    expect(screen.getByText(/A→A, B→B/)).toBeInTheDocument();
    expect(screen.getByText(/A→B, B→A/)).toBeInTheDocument();

    const apply = screen.getByRole('button', { name: 'Katalogu uygula' });
    expect(apply).toBeDisabled();

    await user.type(screen.getByLabelText('Değişiklik gerekçesi'), 'Grup eşlemesi düzeltildi');
    await user.click(screen.getByRole('button', { name: 'Katalogu uygula' }));

    await waitFor(() => expect(api.applyRosterCatalog).toHaveBeenCalled());
    const [, baseHash, planHash, reason] = api.applyRosterCatalog.mock.calls[0];
    expect(baseHash).toBe('hash-on-disk');
    expect(planHash).toBe('plan-hash-0123456789');
    expect(reason).toBe('Grup eşlemesi düzeltildi');
  });

  it('drops a previewed plan as soon as the document is edited again', async () => {
    const user = userEvent.setup();
    render(<RosterCatalogEditor />);
    await screen.findByText('/srv/sirkadiyen/shared/config/student-rosters.json');

    await user.click(screen.getByRole('button', { name: /Dönem 2 Türkçe öğrenci listesi/ }));
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
    api.previewRosterCatalog.mockRejectedValue(
      new api.ApiError(400, null, "Roster 'G2-TR-ROSTER' maps no values for dimension 'practiceGroup'."),
    );
    render(<RosterCatalogEditor />);
    await screen.findByText('/srv/sirkadiyen/shared/config/student-rosters.json');

    await user.click(screen.getByRole('button', { name: /Dönem 2 Türkçe öğrenci listesi/ }));
    await user.type(screen.getByLabelText(/Görünen ad/), '!');
    await user.click(screen.getByRole('button', { name: 'Değişiklikleri incele' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('maps no values');
    expect(api.applyRosterCatalog).not.toHaveBeenCalled();
  });

  it('keeps the raw JSON editor usable when the document does not parse', async () => {
    const user = userEvent.setup();
    api.getRosterCatalog.mockResolvedValue({
      ...document,
      content: '{ broken',
      isValid: false,
      validationError: 'Beklenmeyen karakter.',
      rosterCount: null,
    });
    render(<RosterCatalogEditor />);
    await screen.findByText('/srv/sirkadiyen/shared/config/student-rosters.json');

    // The editor is the repair tool for a broken catalog, so it must show one rather than hide it.
    expect(screen.getByText(/Diskteki katalog geçerli değil/)).toBeInTheDocument();

    await user.click(screen.getByRole('tab', { name: 'JSON' }));
    expect(screen.getByLabelText('Öğrenci listesi kataloğu JSON belgesi')).toHaveValue('{ broken');
  });

  it('warns instead of offering an edit when the file is not writable', async () => {
    api.getRosterCatalog.mockResolvedValue({ ...document, isWritable: false });
    render(<RosterCatalogEditor />);

    expect(await screen.findByText(/yazılabilir değil/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Değişiklikleri incele' })).toBeDisabled();
  });

  it('loads a stored revision into the editor without applying it', async () => {
    const user = userEvent.setup();
    api.listRosterCatalogRevisions.mockResolvedValue([
      {
        id: 'revision-1',
        kind: 'Edit',
        recordedAtUtc: '2026-08-29T09:00:00Z',
        contentHash: 'older-hash',
        previousContentHash: null,
        rosterCount: 1,
        actorUserId: null,
        actorEmail: 'admin@example.com',
        reason: 'Başlık düzeltildi',
        changeSummary: null,
        isCurrent: false,
      },
    ]);
    api.getRosterCatalogRevision.mockResolvedValue({
      summary: { id: 'revision-1' },
      content: '{ "catalogVersion": "1.0", "rosters": [] }\n',
    });

    render(<RosterCatalogEditor />);
    await screen.findByText('/srv/sirkadiyen/shared/config/student-rosters.json');
    await user.click(screen.getByRole('tab', { name: 'Sürüm geçmişi' }));

    const row = (await screen.findByText('Başlık düzeltildi')).closest('tr')!;
    await user.click(within(row).getByRole('button', { name: 'Editöre yükle' }));

    expect(await screen.findByLabelText('Öğrenci listesi kataloğu JSON belgesi'))
      .toHaveValue('{ "catalogVersion": "1.0", "rosters": [] }\n');
    expect(api.applyRosterCatalog).not.toHaveBeenCalled();
  });
});
