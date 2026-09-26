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
  return {
    ApiError,
    listVaultJobs: vi.fn(),
    createVaultNote: vi.fn(),
    listVaultNotes: vi.fn(),
    getVaultNoteContent: vi.fn(),
    createVaultFlashcards: vi.fn(),
  };
});
vi.mock('@/lib/api', () => api);

const finished: VaultAdminJob = {
  id: 'job-1',
  kind: 'Note',
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
    api.listVaultNotes.mockReset();
    api.getVaultNoteContent.mockReset();
    api.createVaultFlashcards.mockReset();
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

  it('lists the vault notes and queues flashcards for one without them', async () => {
    api.listVaultJobs.mockResolvedValue({ enabled: true, maxPromptLength: 8000, jobs: [finished] });
    api.listVaultNotes.mockResolvedValue({
      enabled: true,
      notes: [
        { path: 'Farmakoloji/Beta Blokerler.md', title: 'Beta Blokerler', folder: 'Farmakoloji', sizeBytes: 300, flashcards: { deck: '#flashcards/farmakoloji', clozeCount: 6, questionCount: 5 } },
        { path: 'Kardiyoloji/Aritmi.md', title: 'Aritmi', folder: 'Kardiyoloji', sizeBytes: 120 },
      ],
    });
    api.createVaultFlashcards.mockResolvedValue({
      ...finished,
      id: 'job-3',
      kind: 'Flashcards',
      source: 'Admin',
      requestedBy: 'admin@example.com',
      prompt: null,
      targetPath: 'Kardiyoloji/Aritmi.md',
      status: 'Queued',
      notePath: null,
      noteLink: null,
      backlinks: [],
      warnings: [],
    });

    render(<AdminVault />);
    fireEvent.click(await screen.findByRole('tab', { name: 'Vault notları' }));

    expect(await screen.findByText('11 kart')).toBeInTheDocument();
    const buttons = screen.getAllByRole('button', { name: 'Flashcard ekle' });
    expect(buttons[0]).toBeDisabled();
    fireEvent.click(buttons[1]);

    await waitFor(() => expect(api.createVaultFlashcards).toHaveBeenCalledWith('Kardiyoloji/Aritmi.md'));
    expect(await screen.findByText('Ekleniyor')).toBeInTheDocument();

    fireEvent.click(screen.getByRole('tab', { name: 'Not işleri' }));
    expect(await screen.findByText('Flashcard ekle: Kardiyoloji/Aritmi.md')).toBeInTheDocument();
  });

  it('shows a note with its clozes marked', async () => {
    api.listVaultJobs.mockResolvedValue({ enabled: true, maxPromptLength: 8000, jobs: [] });
    api.listVaultNotes.mockResolvedValue({
      enabled: true,
      notes: [{ path: 'Aritmi.md', title: 'Aritmi', folder: '', sizeBytes: 50 }],
    });
    api.getVaultNoteContent.mockResolvedValue({
      path: 'Aritmi.md',
      title: 'Aritmi',
      content: '#flashcards/kardiyoloji\n\nİlk tercih ==adenozin==dir.\n',
      flashcards: { deck: '#flashcards/kardiyoloji', clozeCount: 1, questionCount: 0 },
      flashcardProblems: [],
    });

    render(<AdminVault />);
    fireEvent.click(await screen.findByRole('tab', { name: 'Vault notları' }));
    fireEvent.click(await screen.findByRole('button', { name: 'Aritmi' }));

    expect(await screen.findByText('adenozin', { selector: 'mark' })).toBeInTheDocument();
    expect(screen.getByText(/1 cloze · 0 soru/)).toBeInTheDocument();
    expect(api.getVaultNoteContent).toHaveBeenCalledWith('Aritmi.md', expect.anything());
  });
});
