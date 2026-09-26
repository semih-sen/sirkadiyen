'use client';

import { Fragment, useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { ApiError, createVaultFlashcards, getVaultNoteContent, listVaultNotes } from '@/lib/api';
import { DetailDrawer, LoadState, formatDateTime } from '@/components/AdminData';
import type { VaultAdminJob, VaultAdminNote, VaultAdminNoteList, VaultFlashcardSummary, VaultNoteContent } from '@/lib/types';

type FlashcardState =
  | { kind: 'pending' }
  | { kind: 'present'; summary: VaultFlashcardSummary }
  | { kind: 'failed'; error?: string | null }
  | { kind: 'none' };

/**
 * The vault's notes (ADR-170): what is there, what the job history knows about each, and a button
 * that has Claude Code add Spaced Repetition flashcards to a note written before they existed.
 * The running jobs come from the parent, which already polls them; the list reloads when one of
 * this page's flashcard jobs finishes so its new state shows without a manual refresh.
 */
export function AdminVaultNotes({ jobs, onQueued }: { jobs: VaultAdminJob[]; onQueued: (job: VaultAdminJob) => void }) {
  const [data, setData] = useState<VaultAdminNoteList | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [query, setQuery] = useState('');
  const [onlyWithout, setOnlyWithout] = useState(false);
  const [selectedPath, setSelectedPath] = useState<string | null>(null);
  const [busyPath, setBusyPath] = useState<string | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);

  const load = useCallback(async (signal?: AbortSignal) => {
    try {
      const result = await listVaultNotes(signal);
      setData(result);
      setError(null);
    } catch (caught) {
      if (signal?.aborted) return;
      setError(caught instanceof ApiError ? caught.message : 'Vault notları alınamadı.');
    } finally {
      if (!signal?.aborted) setLoading(false);
    }
  }, []);

  useEffect(() => {
    const controller = new AbortController();
    void load(controller.signal);
    return () => controller.abort();
  }, [load]);

  const runningPaths = useMemo(
    () => new Set(jobs.filter((job) => job.kind === 'Flashcards' && !isFinished(job.status) && job.targetPath).map((job) => job.targetPath as string)),
    [jobs],
  );

  // Reload once the set of running conversions shrinks: one of them has just finished.
  const runningKey = [...runningPaths].sort().join('\n');
  const previousRunning = useRef(runningKey);
  useEffect(() => {
    const before = previousRunning.current.split('\n').filter(Boolean);
    previousRunning.current = runningKey;
    if (before.some((path) => !runningPaths.has(path))) void load();
  }, [runningKey, runningPaths, load]);

  async function addFlashcards(path: string) {
    setBusyPath(path);
    setActionError(null);
    try {
      onQueued(await createVaultFlashcards(path));
    } catch (caught) {
      setActionError(caught instanceof ApiError ? problemText(caught) : 'Flashcard isteği gönderilemedi.');
    } finally {
      setBusyPath(null);
    }
  }

  const notes = useMemo(() => {
    const needle = query.trim().toLocaleLowerCase('tr-TR');
    return (data?.notes ?? []).filter((note) => {
      if (needle && !note.path.toLocaleLowerCase('tr-TR').includes(needle)) return false;
      return !onlyWithout || flashcardState(note, runningPaths).kind === 'none' || flashcardState(note, runningPaths).kind === 'failed';
    });
  }, [data, query, onlyWithout, runningPaths]);

  const selected = data?.notes.find((note) => note.path === selectedPath) ?? null;

  if (data && !data.enabled) {
    return (
      <div className="banner banner-warning" role="status">
        Vault bu sunucuda yapılandırılmamış; notlar listelenemez.
      </div>
    );
  }

  return (
    <section className="card admin-workspace-card">
      <div className="cluster" style={{ justifyContent: 'space-between', marginBottom: 12 }}>
        <div>
          <h2 style={{ fontSize: 17 }}>Vault notları</h2>
          <p className="muted" style={{ fontSize: 13, marginTop: 4 }}>
            &quot;Flashcard ekle&quot; notun tamamını Claude Code&apos;a okutur; başına deste etiketini, metne ==cloze== vurgularını,
            sonuna soru kartlarını ekler. Notun metni değiştirilmez.
          </p>
        </div>
        <button className="btn btn-secondary btn-sm" type="button" disabled={loading} onClick={() => void load()}>Yenile</button>
      </div>

      <div className="cluster" style={{ gap: 12, marginBottom: 12 }}>
        <input
          className="text-input"
          type="search"
          aria-label="Not ara"
          placeholder="Not ya da klasör ara"
          value={query}
          onChange={(event) => setQuery(event.target.value)}
          style={{ maxWidth: 320 }}
        />
        <label className="cluster" style={{ gap: 6, fontSize: 13, margin: 0 }}>
          <input type="checkbox" checked={onlyWithout} onChange={(event) => setOnlyWithout(event.target.checked)} />
          Yalnızca flashcard&apos;ı olmayanlar
        </label>
        {data && <span className="muted" style={{ fontSize: 13 }}>{notes.length}/{data.notes.length} not</span>}
      </div>

      {actionError && <div className="error" role="alert" style={{ marginBottom: 12 }}>{actionError}</div>}

      <LoadState loading={loading && !data} error={error} empty={!loading && !!data && notes.length === 0} onRetry={() => void load()} />

      {notes.length > 0 && (
        <div className="table-wrap">
          <table className="data-table data-table--stack">
            <thead>
              <tr><th>Not</th><th>Son değişiklik</th><th>Flashcard</th><th>İşlem</th></tr>
            </thead>
            <tbody>
              {notes.map((note) => {
                const state = flashcardState(note, runningPaths);
                return (
                  <tr key={note.path}>
                    <td>
                      <button className="btn btn-tertiary btn-sm" type="button" style={{ textAlign: 'left', whiteSpace: 'normal' }} onClick={() => setSelectedPath(note.path)}>
                        {note.title}
                      </button>
                      <small className="muted mono" style={{ display: 'block' }}>{note.folder || 'kök'}</small>
                    </td>
                    <td>{formatDateTime(note.lastModifiedUtc)}</td>
                    <td><FlashcardBadge state={state} /></td>
                    <td>
                      <button
                        className="btn btn-secondary btn-sm"
                        type="button"
                        disabled={state.kind === 'pending' || state.kind === 'present' || busyPath === note.path}
                        onClick={() => void addFlashcards(note.path)}
                      >
                        {busyPath === note.path ? 'Gönderiliyor…' : 'Flashcard ekle'}
                      </button>
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      )}

      {selected && (
        <VaultNoteDrawer
          note={selected}
          state={flashcardState(selected, runningPaths)}
          busy={busyPath === selected.path}
          onAddFlashcards={() => void addFlashcards(selected.path)}
          onClose={() => setSelectedPath(null)}
        />
      )}
    </section>
  );
}

function VaultNoteDrawer({ note, state, busy, onAddFlashcards, onClose }: {
  note: VaultAdminNote;
  state: FlashcardState;
  busy: boolean;
  onAddFlashcards: () => void;
  onClose: () => void;
}) {
  const [content, setContent] = useState<VaultNoteContent | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    const controller = new AbortController();
    setContent(null);
    setError(null);
    getVaultNoteContent(note.path, controller.signal)
      .then(setContent)
      .catch((caught: unknown) => {
        if (controller.signal.aborted) return;
        setError(caught instanceof ApiError ? caught.message : 'Not okunamadı.');
      });
    return () => controller.abort();
    // A finished conversion changes the note's state; reading it again shows the new cards.
  }, [note.path, state.kind]);

  const hasDeck = !!content?.flashcards.deck;

  return (
    <DetailDrawer title={note.title} subtitle={`${note.path} · ${formatDateTime(note.lastModifiedUtc)}`} onClose={onClose}>
      <div className="stack" style={{ gap: 16 }}>
        <section>
          <h3 style={{ fontSize: 14 }}>Flashcard</h3>
          {content ? (
            <p style={{ marginTop: 6 }}>
              {hasDeck
                ? <>Deste <span className="mono">{content.flashcards.deck}</span> · {content.flashcards.clozeCount} cloze · {content.flashcards.questionCount} soru</>
                : 'Bu notta deste etiketi yok; eklenti kartlarını görmez.'}
            </p>
          ) : !error && <p className="loading-note">Yükleniyor…</p>}
          {content && content.flashcardProblems.length > 0 && (
            <ul style={{ marginTop: 6, paddingLeft: 18 }}>
              {content.flashcardProblems.map((problem, index) => <li key={index}>{problem}</li>)}
            </ul>
          )}
          <div className="cluster" style={{ marginTop: 10 }}>
            <button
              className="btn btn-primary btn-sm"
              type="button"
              disabled={!content || hasDeck || state.kind === 'pending' || busy}
              onClick={onAddFlashcards}
            >
              {state.kind === 'pending' ? 'Flashcard ekleniyor…' : busy ? 'Gönderiliyor…' : 'Flashcard ekle'}
            </button>
          </div>
          {state.kind === 'failed' && state.error && <div className="error" role="alert" style={{ marginTop: 10 }}>Son deneme başarısız: {state.error}</div>}
        </section>

        {error && <div className="error" role="alert">{error}</div>}

        {content && (
          <section>
            <h3 style={{ fontSize: 14 }}>İçerik</h3>
            <pre className="mono" style={{ whiteSpace: 'pre-wrap', wordBreak: 'break-word', fontSize: 13, marginTop: 6 }}>
              {highlightClozes(content.content)}
            </pre>
          </section>
        )}
      </div>
    </DetailDrawer>
  );
}

function FlashcardBadge({ state }: { state: FlashcardState }) {
  switch (state.kind) {
    case 'pending':
      return <span className="badge badge-info">Ekleniyor</span>;
    case 'present':
      return (
        <span className="badge badge-success" title={state.summary.deck ?? undefined}>
          {state.summary.clozeCount + state.summary.questionCount} kart
        </span>
      );
    case 'failed':
      return <span className="badge badge-danger" title={state.error ?? undefined}>Başarısız</span>;
    default:
      return <span className="badge badge-neutral">Yok</span>;
  }
}

function flashcardState(note: VaultAdminNote, runningPaths: Set<string>): FlashcardState {
  const latest = note.latestJob;
  if (runningPaths.has(note.path) || (latest?.kind === 'Flashcards' && !isFinished(latest.status))) return { kind: 'pending' };
  if (note.flashcards?.deck) return { kind: 'present', summary: note.flashcards };
  if (latest?.kind === 'Flashcards' && latest.status === 'Failed') return { kind: 'failed', error: latest.error };
  return { kind: 'none' };
}

/** Shows each ==highlight== the way the plugin reads it: as a cloze deletion. */
function highlightClozes(text: string): ReactNode {
  const parts = text.split(/(==(?!\s)[^=\n]+?(?<!\s)==)/g);
  return parts.map((part, index) =>
    index % 2 === 1 ? <mark key={index}>{part.slice(2, -2)}</mark> : <Fragment key={index}>{part}</Fragment>,
  );
}

function isFinished(status: VaultAdminJob['status']): boolean {
  return status === 'Succeeded' || status === 'Failed';
}

/** A validation problem carries the reason in `errors`, not in `detail`. */
function problemText(error: ApiError): string {
  const errors = error.problem?.errors;
  const first = errors ? Object.values(errors).flat()[0] : undefined;
  return first ?? error.message;
}
