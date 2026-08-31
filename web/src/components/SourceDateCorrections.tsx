'use client';

import { useCallback, useEffect, useState } from 'react';
import {
  acceptSourceDateCorrection,
  listAllSourceDateCorrections,
  retireSourceDateCorrection,
  ApiError,
} from '@/lib/api';
import { formatDateTime } from '@/components/AdminData';
import type { SourceDateCorrectionView } from '@/lib/types';

/**
 * Every date this system publishes that no document states (ADR-139).
 *
 * A correction is accepted from one revision's findings and then keeps applying, silently, to every
 * later parse of that source. That is the point of it — the document is the faculty's and stays
 * wrong — but it means the set has no other home: the revision it was decided from is superseded
 * within days, and after that nothing on any screen could say which dates we are overriding, who
 * decided so, or whether the source has since been fixed. This is that screen.
 *
 * Changing one is an accept with the same original date, which is exactly how the backend models a
 * changed mind (ADR-139): the old row gives way and the new decider and timestamp are recorded.
 */
export function SourceDateCorrections() {
  const [corrections, setCorrections] = useState<SourceDateCorrectionView[] | null>(null);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    setCorrections(null);
    setError(null);
    try {
      setCorrections(await listAllSourceDateCorrections());
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Tarih düzeltmeleri yüklenemedi.');
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  const bySource = new Map<string, SourceDateCorrectionView[]>();
  for (const correction of corrections ?? []) {
    bySource.set(correction.sourceId, [...(bySource.get(correction.sourceId) ?? []), correction]);
  }

  return (
    <div>
      <p className="muted revision-queue-note">
        Kaynağın yazdığı tarihin yanlış olduğuna karar verdiğimiz ve kendi tarihimizi koyduğumuz
        yerler. Her düzeltme kaynak bazındadır: o kaynak soldaki tarihi nerede yazarsa yazsın, her
        ayrıştırmada sağdaki tarih okunur. Fakülte belgeyi düzelttiğinde düzeltme kendiliğinden
        kalkmaz — artık hiçbir satıra denk gelmez ama kayıtta durur, o yüzden kaldırmak gerekir.
        Buradaki bir değişiklik yayımlanmış takvimleri anında düzeltmez: kaynağı yeniden çekin, yeni
        revizyon doğru tarihle gelir.
      </p>

      {corrections === null && !error && <p className="loading-note">Yükleniyor…</p>}
      {corrections !== null && corrections.length === 0 && (
        <p className="muted">Kaydedilmiş tarih düzeltmesi yok.</p>
      )}

      {[...bySource.entries()].map(([sourceId, group]) => (
        <section key={sourceId} className="revision-row">
          <p className="revision-row-identity" style={{ marginTop: 0 }}>
            <strong className="mono">{sourceId}</strong>{' '}
            <span className="muted">· {group.length} düzeltme</span>
          </p>
          {group.map((correction) => (
            <CorrectionRow
              key={correction.id}
              correction={correction}
              onChanged={() => void load()}
            />
          ))}
        </section>
      ))}

      {error && <div className="error" role="alert">{error}</div>}
    </div>
  );
}

function CorrectionRow({
  correction,
  onChanged,
}: {
  correction: SourceDateCorrectionView;
  onChanged: () => void;
}) {
  const [corrected, setCorrected] = useState(correction.corrected);
  const [reason, setReason] = useState('');
  const [confirmingRetire, setConfirmingRetire] = useState(false);
  const [busy, setBusy] = useState<'accept' | 'retire' | null>(null);
  const [error, setError] = useState<string | null>(null);
  const field = `stored-correction-${correction.id}`;

  async function accept() {
    const trimmed = reason.trim();
    if (corrected === correction.original) {
      setError('Düzeltme kaynağın yazdığı tarihle aynı olamaz; kaldırmak istiyorsanız kaldırın.');
      return;
    }
    if (trimmed.length === 0) {
      setError('Değişiklik için bir gerekçe girin; bu karar denetim kaydına yazılır.');
      return;
    }
    setBusy('accept');
    setError(null);
    try {
      await acceptSourceDateCorrection(correction.sourceId, correction.original, corrected, trimmed);
      onChanged();
    } catch (err) {
      setBusy(null);
      setError(err instanceof ApiError ? err.message : 'Düzeltme değiştirilemedi.');
    }
  }

  async function retire() {
    setBusy('retire');
    setError(null);
    try {
      await retireSourceDateCorrection(correction.sourceId, correction.id);
      onChanged();
    } catch (err) {
      setBusy(null);
      setError(err instanceof ApiError ? err.message : 'Düzeltme kaldırılamadı.');
    }
  }

  return (
    <div className="revision-date-correction">
      <p className="revision-date-correction-head">
        <strong className="mono">{correction.original}</strong>
        <span className="muted"> → </span>
        <strong className="mono">{correction.corrected}</strong>
        <span className="muted">
          {' '}· {correction.decidedBy} · {formatDateTime(correction.decidedAtUtc)}
        </span>
      </p>
      {correction.note && <p className="muted revision-finding-message">{correction.note}</p>}

      <div className="cluster" style={{ gap: 8, alignItems: 'flex-end', flexWrap: 'wrap' }}>
        <div>
          <label htmlFor={field}>Okunacak tarih</label>
          <input
            id={field}
            type="date"
            value={corrected}
            onChange={(event) => setCorrected(event.target.value)}
          />
        </div>
        <div style={{ flex: '1 1 260px' }}>
          <label htmlFor={`${field}-reason`}>Gerekçe (denetim kaydına yazılır)</label>
          <input
            id={`${field}-reason`}
            value={reason}
            onChange={(event) => setReason(event.target.value)}
            placeholder="Belgeyi yeniden okudum; ders bir hafta ileride."
          />
        </div>
        <button
          className="btn btn-secondary btn-sm"
          type="button"
          onClick={() => void accept()}
          disabled={busy !== null}
        >
          {busy === 'accept' ? 'Kaydediliyor…' : 'Tarihi değiştir'}
        </button>
        {!confirmingRetire ? (
          <button
            className="btn btn-tertiary btn-sm"
            type="button"
            onClick={() => setConfirmingRetire(true)}
            disabled={busy !== null}
          >
            Düzeltmeyi kaldır
          </button>
        ) : (
          <button
            className="btn btn-danger btn-sm"
            type="button"
            onClick={() => void retire()}
            disabled={busy !== null}
          >
            {busy === 'retire' ? 'Kaldırılıyor…' : 'Kaldırmayı onayla'}
          </button>
        )}
      </div>

      {confirmingRetire && (
        <p className="muted" style={{ fontSize: 13 }}>
          Kaldırılırsa bu kaynak {correction.original} yazdığı yerde bir daha
          {' '}{correction.corrected} okunmaz; belge hâlâ eski tarihi yazıyorsa dersler o tarihe
          geri döner.
        </p>
      )}

      {error && <div className="error" role="alert">{error}</div>}
    </div>
  );
}
