'use client';

import { useCallback, useEffect, useState, type FormEvent } from 'react';
import { ApiError, createVaultNote, listVaultJobs } from '@/lib/api';
import { DetailDrawer, LoadState, Tabs, formatDateTime } from '@/components/AdminData';
import { AdminVaultNotes } from '@/components/AdminVaultNotes';
import type { VaultAdminJob, VaultAdminJobList, VaultBacklinkStatus, VaultFlashcardSummary, VaultJobStatus } from '@/lib/types';

/** How often the list is refreshed while a job is still running; a note takes about a minute. */
const POLL_INTERVAL_MS = 5000;

const STATUS_LABELS: Record<VaultJobStatus, string> = {
  Queued: 'Sırada',
  Cataloging: 'Vault taranıyor',
  Generating: 'Not yazılıyor',
  Uploading: 'Yükleniyor',
  Backlinking: 'Backlink ekleniyor',
  Succeeded: 'Tamamlandı',
  Failed: 'Başarısız',
};

const BACKLINK_LABELS: Record<VaultBacklinkStatus, string> = {
  Updated: 'Eklendi',
  SkippedChanged: 'Atlandı · not değişmiş',
  SkippedInvalid: 'Atlandı · geçersiz',
  SkippedLimit: 'Atlandı · limit',
  Failed: 'Başarısız',
};

/** A flashcard job reads an existing note rather than cataloging and writes cards rather than a note. */
function statusLabel(job: VaultAdminJob): string {
  if (job.kind === 'Flashcards' && job.status === 'Cataloging') return 'Not okunuyor';
  if (job.kind === 'Flashcards' && job.status === 'Generating') return 'Flashcard yazılıyor';
  return STATUS_LABELS[job.status];
}

function jobSummary(job: VaultAdminJob): string {
  return job.kind === 'Flashcards' ? `Flashcard ekle: ${job.targetPath ?? '—'}` : job.prompt ?? '—';
}

function cardCount(summary?: VaultFlashcardSummary | null): number | null {
  return summary ? summary.clozeCount + summary.questionCount : null;
}

function isFinished(status: VaultJobStatus): boolean {
  return status === 'Succeeded' || status === 'Failed';
}

function statusClass(status: VaultJobStatus): string {
  if (status === 'Succeeded') return 'badge-success';
  if (status === 'Failed') return 'badge-danger';
  if (status === 'Queued') return 'badge-neutral';
  return 'badge-info';
}

export function AdminVault() {
  const [data, setData] = useState<VaultAdminJobList | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [tab, setTab] = useState<'jobs' | 'notes'>('jobs');

  const load = useCallback(async (signal?: AbortSignal) => {
    try {
      const result = await listVaultJobs(50, signal);
      setData(result);
      setError(null);
    } catch (caught) {
      if (signal?.aborted) return;
      setError(caught instanceof ApiError ? caught.message : 'Vault işleri alınamadı.');
    } finally {
      if (!signal?.aborted) setLoading(false);
    }
  }, []);

  useEffect(() => {
    const controller = new AbortController();
    void load(controller.signal);
    return () => controller.abort();
  }, [load]);

  // Polls only while something is still running, so an idle page makes no requests.
  const running = data?.jobs.some((job) => !isFinished(job.status)) ?? false;
  useEffect(() => {
    if (!running) return;
    const timer = window.setInterval(() => void load(), POLL_INTERVAL_MS);
    return () => window.clearInterval(timer);
  }, [running, load]);

  function onQueued(job: VaultAdminJob) {
    setData((current) => current && { ...current, jobs: [job, ...current.jobs.filter((item) => item.id !== job.id)] });
  }

  const selected = data?.jobs.find((job) => job.id === selectedId) ?? null;

  return (
    <div className="stack" style={{ gap: 18 }}>
      {data && !data.enabled && (
        <div className="banner banner-warning" role="status">
          Vault özelliği bu sunucuda kapalı (<span className="mono">SIRKADIYEN_VAULT__API_KEY</span> tanımlı değil).
          Geçmiş işler görüntülenebilir; yeni not isteği gönderilemez.
        </div>
      )}

      <Tabs
        value={tab}
        onChange={(value) => setTab(value as 'jobs' | 'notes')}
        items={[{ value: 'jobs', label: 'Not işleri' }, { value: 'notes', label: 'Vault notları' }]}
      />

      {tab === 'notes' && <AdminVaultNotes jobs={data?.jobs ?? []} onQueued={onQueued} />}

      {tab === 'jobs' && data?.enabled && <VaultNoteForm maxPromptLength={data.maxPromptLength} onQueued={onQueued} />}

      {tab === 'jobs' && <section className="card admin-workspace-card">
        <div className="cluster" style={{ justifyContent: 'space-between', marginBottom: 12 }}>
          <div>
            <h2 style={{ fontSize: 17 }}>Not işleri</h2>
            <p className="muted" style={{ fontSize: 13, marginTop: 4 }}>
              iPad kısayolundan ve bu panelden gönderilen tüm istekler (yeni not ve flashcard ekleme); en yeni üstte.
              {running && ' Çalışan iş varken liste kendiliğinden yenilenir.'}
            </p>
          </div>
          <button className="btn btn-secondary btn-sm" type="button" disabled={loading} onClick={() => void load()}>Yenile</button>
        </div>

        <LoadState loading={loading && !data} error={error} empty={!loading && data?.jobs.length === 0} onRetry={() => void load()} />

        {data && data.jobs.length > 0 && (
          <div className="table-wrap">
            <table className="data-table data-table--stack">
              <thead>
                <tr><th>Zaman</th><th>Kaynak</th><th>İstek</th><th>Durum</th><th>Not</th><th>Kart</th><th>Backlink</th></tr>
              </thead>
              <tbody>
                {data.jobs.map((job) => (
                  <tr key={job.id}>
                    <td>{formatDateTime(job.createdAtUtc)}</td>
                    <td>{job.source === 'Admin' ? <>Panel<small className="muted" style={{ display: 'block' }}>{job.requestedBy}</small></> : 'Kısayol'}</td>
                    <td>
                      <button className="btn btn-tertiary btn-sm" type="button" style={{ textAlign: 'left', whiteSpace: 'normal' }} onClick={() => setSelectedId(job.id)}>
                        {truncate(jobSummary(job), 90)}
                      </button>
                    </td>
                    <td><span className={`badge ${statusClass(job.status)}`}>{statusLabel(job)}</span></td>
                    <td className="mono">{job.notePath ?? '—'}</td>
                    <td>{cardCount(job.flashcards) ?? '—'}</td>
                    <td>{job.backlinks.length > 0 ? `${job.backlinks.filter((link) => link.status === 'Updated').length}/${job.backlinks.length}` : '—'}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>}

      {selected && <VaultJobDetail job={selected} onClose={() => setSelectedId(null)} />}
    </div>
  );
}

function VaultNoteForm({ maxPromptLength, onQueued }: { maxPromptLength: number; onQueued: (job: VaultAdminJob) => void }) {
  const [prompt, setPrompt] = useState('');
  const [folder, setFolder] = useState('');
  const [title, setTitle] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  async function submit(event: FormEvent) {
    event.preventDefault();
    if (!prompt.trim()) return;
    setBusy(true);
    setError(null);
    setNotice(null);
    try {
      const job = await createVaultNote({
        prompt: prompt.trim(),
        folder: folder.trim() || null,
        title: title.trim() || null,
      });
      onQueued(job);
      setPrompt('');
      setFolder('');
      setTitle('');
      setNotice('İstek kuyruğa alındı; durum aşağıdaki listede güncellenecek.');
    } catch (caught) {
      setError(caught instanceof ApiError ? problemText(caught) : 'İstek gönderilemedi.');
    } finally {
      setBusy(false);
    }
  }

  return (
    <form className="card admin-workspace-card" onSubmit={(event) => void submit(event)}>
      <h2 style={{ fontSize: 17 }}>Yeni not iste</h2>
      <p className="muted" style={{ fontSize: 13, marginTop: 4 }}>
        Claude Code notu flashcard&apos;larıyla (deste etiketi, ==cloze== vurguları, soru kartları) yazar, vault&apos;a kaydeder
        ve ilgili en fazla birkaç nota geri bağlantı ekler.
      </p>

      <label htmlFor="vault-prompt">İstek</label>
      <textarea
        className="text-input"
        id="vault-prompt"
        rows={6}
        maxLength={maxPromptLength}
        value={prompt}
        onChange={(event) => setPrompt(event.target.value)}
        placeholder="Örn. Beta blokerlerin etki mekanizması, endikasyonları ve yan etkileri hakkında bir not"
      />
      <small className="muted" style={{ display: 'block', marginTop: 4, fontSize: 12 }}>{prompt.length}/{maxPromptLength}</small>

      <div className="grid grid-2" style={{ marginTop: 8 }}>
        <div>
          <label htmlFor="vault-folder">Klasör (isteğe bağlı)</label>
          <input className="text-input" id="vault-folder" value={folder} onChange={(event) => setFolder(event.target.value)} placeholder="Boş bırakılırsa Claude seçer" />
        </div>
        <div>
          <label htmlFor="vault-title">Başlık (isteğe bağlı)</label>
          <input className="text-input" id="vault-title" value={title} onChange={(event) => setTitle(event.target.value)} placeholder="Boş bırakılırsa Claude önerir" />
        </div>
      </div>

      {error && <div className="error" role="alert" style={{ marginTop: 12 }}>{error}</div>}
      {notice && <div className="banner banner-info" role="status" style={{ marginTop: 12 }}>{notice}</div>}

      <div className="cluster" style={{ justifyContent: 'flex-end', marginTop: 14 }}>
        <button className="btn btn-primary" type="submit" disabled={busy || !prompt.trim()}>{busy ? 'Gönderiliyor…' : 'Gönder'}</button>
      </div>
    </form>
  );
}

function VaultJobDetail({ job, onClose }: { job: VaultAdminJob; onClose: () => void }) {
  return (
    <DetailDrawer title={job.noteLink ?? (job.kind === 'Flashcards' ? 'Flashcard işi' : 'Not işi')} subtitle={`${statusLabel(job)} · ${formatDateTime(job.createdAtUtc)}`} onClose={onClose}>
      <div className="stack" style={{ gap: 16 }}>
        <section>
          <h3 style={{ fontSize: 14 }}>İstek</h3>
          {job.kind === 'Flashcards' ? (
            <p style={{ marginTop: 6 }}>Mevcut nota flashcard ekle: <span className="mono">{job.targetPath}</span></p>
          ) : (
            <>
              <p style={{ whiteSpace: 'pre-wrap', marginTop: 6 }}>{job.prompt}</p>
              <p className="muted" style={{ fontSize: 13, marginTop: 6 }}>
                Klasör: {job.folder === '' ? 'kök' : job.folder ?? 'Claude seçti'} · Başlık: {job.title ?? 'Claude önerdi'}
              </p>
            </>
          )}
          <p className="muted" style={{ fontSize: 13 }}>
            Kaynak: {job.source === 'Admin' ? `Panel (${job.requestedBy ?? '—'})` : 'iPad kısayolu'} · Bitiş: {formatDateTime(job.completedAtUtc)}
          </p>
        </section>

        {job.notePath && (
          <section>
            <h3 style={{ fontSize: 14 }}>Yazılan not</h3>
            <p className="mono" style={{ marginTop: 6 }}>{job.notePath}</p>
          </section>
        )}

        {job.flashcards && (
          <section>
            <h3 style={{ fontSize: 14 }}>Flashcard</h3>
            <p style={{ marginTop: 6 }}>
              {job.flashcards.deck ? <>Deste <span className="mono">{job.flashcards.deck}</span> · </> : 'Deste etiketi yok · '}
              {job.flashcards.clozeCount} cloze · {job.flashcards.questionCount} soru
            </p>
          </section>
        )}

        {job.error && <div className="error" role="alert">{job.error}</div>}

        {job.warnings.length > 0 && (
          <section>
            <h3 style={{ fontSize: 14 }}>Uyarılar</h3>
            <ul style={{ marginTop: 6, paddingLeft: 18 }}>
              {job.warnings.map((warning, index) => <li key={index}>{warning}</li>)}
            </ul>
          </section>
        )}

        {job.backlinks.length > 0 && (
          <section>
            <h3 style={{ fontSize: 14 }}>Backlinkler</h3>
            <ul style={{ marginTop: 6, paddingLeft: 18 }}>
              {job.backlinks.map((link, index) => (
                <li key={index}>
                  <span className="mono">{link.path ?? link.target}</span>{' '}
                  <span className={`badge badge-xs ${link.status === 'Updated' ? 'badge-success' : link.status === 'Failed' ? 'badge-danger' : 'badge-neutral'}`}>{BACKLINK_LABELS[link.status]}</span>
                  {link.detail && <small className="muted" style={{ display: 'block' }}>{link.detail}</small>}
                </li>
              ))}
            </ul>
          </section>
        )}
      </div>
    </DetailDrawer>
  );
}

function truncate(value: string, length: number): string {
  return value.length > length ? `${value.slice(0, length - 1)}…` : value;
}

/** A validation problem carries the reason in `errors`, not in `detail`. */
function problemText(error: ApiError): string {
  const errors = error.problem?.errors;
  const first = errors ? Object.values(errors).flat()[0] : undefined;
  return first ?? error.message;
}
