'use client';

import { useState } from 'react';
import { AdminSectionTitle } from '@/components/AdminShell';
import { ApiError, createLicense } from '@/lib/api';
import type { CreatedLicense } from '@/lib/types';

export function LicenseAdministration({ onCreated }: { onCreated?: () => void }) {
  const [expiresAt, setExpiresAt] = useState('');
  const [notes, setNotes] = useState('');
  const [created, setCreated] = useState<CreatedLicense | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function onCreate() {
    setBusy(true); setError(null); setCreated(null);
    try {
      const expiry = expiresAt ? new Date(expiresAt).toISOString() : null;
      setCreated(await createLicense(expiry, notes.trim() || null));
      setNotes(''); onCreated?.();
    } catch (err) { setError(err instanceof ApiError ? err.message : 'Lisans üretilemedi.'); }
    finally { setBusy(false); }
  }

  return <section className="card card-content">
    <span className="eyebrow">Tek kullanımlık</span><AdminSectionTitle>Yeni lisans üret</AdminSectionTitle>
    <p className="muted">Kod yalnız üretim yanıtında bir kez gösterilir; daha sonra listelerde veya detayda gösterilmez.</p>
    <div className="field" style={{ marginTop: 16 }}><label htmlFor="license-expiry">Son kullanım (isteğe bağlı)</label><input id="license-expiry" type="datetime-local" value={expiresAt} onChange={(event) => setExpiresAt(event.target.value)} /></div>
    <div className="field"><label htmlFor="license-notes">Not (isteğe bağlı)</label><textarea id="license-notes" value={notes} onChange={(event) => setNotes(event.target.value)} /></div>
    <button className="btn btn-primary" type="button" disabled={busy} onClick={() => void onCreate()}>{busy ? 'Üretiliyor…' : 'Güvenli lisans üret'}</button>
    {created && <div className="created-license" role="status"><span>Bu kodu şimdi güvenli biçimde paylaş</span><strong>{created.plaintextCode}</strong><small>Lisans kimliği: {created.licenseId}</small></div>}
    {error && <div className="error" role="alert">{error}</div>}
  </section>;
}
