import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { AdminVault } from './AdminVault';
import type { VaultAdminJob } from '@/lib/types';

const api = vi.hoisted(() => {
  class ApiError extends Error {
    readonly status: number;
    readonly problem: unknown;
    constructor(status: number, problem: unknown, message: string) {
      super(message);
      this.status = status;
      this.problem = problem;
    }
  }
  return { ApiError, listVaultJobs: vi.fn(), createVaultNote: vi.fn() };
});
vi.mock('@/lib/api', () => api);

const finished: VaultAdminJob = {
  id: 'job-1',
  source: 'Shortcut',
  prompt: 'Beta blokerler hakkında özet',
  folder: null,
  title: null,
  status: 'Succeeded',
  createdAtUtc: '2026-09-23T10:00:00Z',
  completedAtUtc: '2026-09-23T10:01:00Z',
  notePath: 'Farmakoloji/Beta Blokerler.md',
  noteLink: 'Beta Blokerler',
  backlinks: [
    { target: 'Hipertansiyon', path: 'Kardiyoloji/Hipertansiyon.md', status: 'Updated' },
    { target: 'Yok', status: 'SkippedInvalid', detail: "Vault'ta böyle bir not yok." },
  ],
  warnings: ['Bir uyarı'],
};

describe('AdminVault', () => {
  beforeEach(() => {
    api.listVaultJobs.mockReset();
    api.createVaultNote.mockReset();
  });

  it('lists stored jobs and opens one', async () => {
    api.listVaultJobs.mockResolvedValue({ enabled: true, maxPromptLength: 8000, jobs: [finished] });

    render(<AdminVault />);

    expect(await screen.findByText('Farmakoloji/Beta Blokerler.md')).toBeInTheDocument();
    expect(screen.getByText('Tamamlandı')).toBeInTheDocument();
    expect(screen.getByText('1/2')).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Beta blokerler hakkında özet' }));
    expect(await screen.findByText('Bir uyarı')).toBeInTheDocument();
    expect(screen.getByText("Vault'ta böyle bir not yok.")).toBeInTheDocument();
  });

  it('queues a prompt and shows it at the top', async () => {
    api.listVaultJobs.mockResolvedValue({ enabled: true, maxPromptLength: 8000, jobs: [finished] });
    api.createVaultNote.mockResolvedValue({
      ...finished,
      id: 'job-2',
      source: 'Admin',
      requestedBy: 'admin@example.com',
      prompt: 'Aritmiler',
      status: 'Queued',
      notePath: null,
      backlinks: [],
      warnings: [],
    });

    render(<AdminVault />);
    fireEvent.change(await screen.findByLabelText('İstek'), { target: { value: '  Aritmiler ' } });
    fireEvent.change(screen.getByLabelText('Klasör (isteğe bağlı)'), { target: { value: 'Kardiyoloji' } });
    fireEvent.click(screen.getByRole('button', { name: 'Gönder' }));

    await waitFor(() => expect(api.createVaultNote).toHaveBeenCalledWith({ prompt: 'Aritmiler', folder: 'Kardiyoloji', title: null }));
    expect(await screen.findByText('Sırada')).toBeInTheDocument();
    expect(screen.getByText('admin@example.com')).toBeInTheDocument();
    expect(screen.getByLabelText('İstek')).toHaveValue('');
  });

  it('shows history but no form when the vault is off', async () => {
    api.listVaultJobs.mockResolvedValue({ enabled: false, maxPromptLength: 8000, jobs: [finished] });

    render(<AdminVault />);

    expect(await screen.findByText(/Vault özelliği bu sunucuda kapalı/)).toBeInTheDocument();
    expect(screen.queryByLabelText('İstek')).not.toBeInTheDocument();
    expect(screen.getByText('Farmakoloji/Beta Blokerler.md')).toBeInTheDocument();
  });
});
