'use client';

import { useState } from 'react';
import { AdminSectionTitle } from '@/components/AdminShell';
import { ApiError, createLicense, revokeLicense } from '@/lib/api';
import type { CreatedLicense } from '@/lib/types';

export function LicenseAdministration() {
  const [expiresAt, setExpiresAt] = useState('');
  const [notes, setNotes] = useState('');
  const [created, setCreated] = useState<CreatedLicense | null>(null);
  const [revokeId, setRevokeId] = useState('');
  const [revokeReason, setRevokeReason] = useState('');
  const [busy, setBusy] = useState<'create' | 'revoke' | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  async function onCreate() {
    setBusy('create'); setError(null); setNotice(null); setCreated(null);
    try {
      const expiry = expiresAt ? new Date(expiresAt).toISOString() : null;
      setCreated(await createLicense(expiry, notes.trim() || null));
      setNotes('');
    } catch (err) { setError(err instanceof ApiError ? err.message : 'Lisans üretilemedi.'); }
    finally { setBusy(null); }
  }

  async function onRevoke() {
    if (!revokeId.trim() || !revokeReason.trim()) { setError('Lisans kimliği ve iptal gerekçesi zorunludur.'); return; }
    setBusy('revoke'); setError(null); setNotice(null);
    try {
      const result = await revokeLicense(revokeId.trim(), revokeReason.trim());
      setNotice(result.outcome === 'AlreadyRevoked' ? 'Bu lisans zaten iptal edilmiş.' : 'Lisans iptal edildi; takvim verileri korunur.');
      setRevokeId(''); setRevokeReason('');
    } catch (err) { setError(err instanceof ApiError ? err.message : 'Lisans iptal edilemedi.'); }
    finally { setBusy(null); }
  }

  return (
    <div className="license-admin-grid">
      <section className="card">
        <span className="eyebrow">Tek kullanımlık</span>
        <AdminSectionTitle>Yeni lisans üret</AdminSectionTitle>
        <p className="muted">Kod yalnızca üretim yanıtında bir kez gösterilir; veritabanında düz metin olarak tutulmaz.</p>
        <div className="field" style={{ marginTop: 16 }}><label htmlFor="license-expiry">Son kullanım (isteğe bağlı)</label><input id="license-expiry" type="datetime-local" value={expiresAt} onChange={(event) => setExpiresAt(event.target.value)} /></div>
        <div className="field"><label htmlFor="license-notes">Not (isteğe bağlı)</label><textarea id="license-notes" value={notes} onChange={(event) => setNotes(event.target.value)} placeholder="Kime / hangi dönem için üretildi?" /></div>
        <button className="btn btn-primary" type="button" disabled={busy !== null} onClick={() => void onCreate()}>{busy === 'create' ? 'Üretiliyor…' : 'Güvenli lisans üret'}</button>
        {created && <div className="created-license" role="status"><span>Bu kodu şimdi güvenli biçimde paylaş</span><strong>{created.plaintextCode}</strong><small>Lisans kimliği: {created.licenseId}</small></div>}
      </section>
      <section className="card">
        <span className="eyebrow">Denetimli işlem</span>
        <AdminSectionTitle>Lisans iptal et</AdminSectionTitle>
        <p className="muted">İptal gelecekteki senkronizasyonu durdurur; kullanıcının takvimini veya mevcut etkinliklerini silmez.</p>
        <div className="field" style={{ marginTop: 16 }}><label htmlFor="revoke-license-id">Lisans kimliği</label><input id="revoke-license-id" value={revokeId} onChange={(event) => setRevokeId(event.target.value)} placeholder="UUID" /></div>
        <div className="field"><label htmlFor="revoke-license-reason">İptal gerekçesi</label><textarea id="revoke-license-reason" value={revokeReason} onChange={(event) => setRevokeReason(event.target.value)} /></div>
        <button className="btn btn-danger" type="button" disabled={busy !== null} onClick={() => void onRevoke()}>{busy === 'revoke' ? 'İptal ediliyor…' : 'Lisansı iptal et'}</button>
      </section>
      {error && <div className="error license-admin-message" role="alert">{error}</div>}
      {notice && <div className="success license-admin-message" role="status">{notice}</div>}
    </div>
  );
}
