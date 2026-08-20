'use client';

import { useState } from 'react';
import { useRouter } from 'next/navigation';
import { useSession } from '@/components/SessionProvider';
import { ApiError, deleteOwnAccount } from '@/lib/api';
import { ROUTES } from '@/lib/onboarding';

/**
 * The account owner's "Hesabımı sil" danger zone (ADR-118). Deletion is permanent, so it is gated
 * behind an explicit toggle and a confirmation phrase — the account's own e-mail, retyped — before
 * the final button is even enabled. On success the session is over and the user is sent back to the
 * sign-in screen.
 */
export function DeleteAccountCard() {
  const router = useRouter();
  const { user, setUser } = useSession();
  const [open, setOpen] = useState(false);
  const [confirmEmail, setConfirmEmail] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const email = user?.email ?? '';
  const matches = confirmEmail.trim().toLowerCase() === email.toLowerCase() && email.length > 0;

  async function onDelete() {
    setBusy(true);
    setError(null);
    try {
      await deleteOwnAccount(confirmEmail.trim());
      setUser(null);
      router.replace(`${ROUTES.signIn}?deleted=1`);
    } catch (caught) {
      setError(caught instanceof ApiError ? caught.message : 'Hesap silinemedi.');
      setBusy(false);
    }
  }

  return (
    <section className="card card-content" style={{ borderColor: 'var(--danger, #c0392b)' }}>
      <h3 style={{ fontSize: 15, color: 'var(--danger, #c0392b)' }}>Hesabımı sil</h3>
      <p className="muted" style={{ marginTop: 8, fontSize: 13 }}>
        Hesabını kalıcı olarak siler. Akademik profilin, takvim bağlantın, oluşturulan Sirkadiyen
        takvimin ve tüm senkronizasyon kayıtların geri alınamaz biçimde kaldırılır. Bu işlem geri
        alınamaz.
      </p>

      {!open ? (
        <button
          className="btn btn-secondary btn-sm"
          type="button"
          style={{ marginTop: 14, color: 'var(--danger, #c0392b)', borderColor: 'var(--danger, #c0392b)' }}
          onClick={() => setOpen(true)}
        >
          Hesabımı silmek istiyorum
        </button>
      ) : (
        <div style={{ marginTop: 14 }}>
          <label htmlFor="delete-confirm-email" style={{ display: 'block', fontSize: 13 }}>
            Onaylamak için e-posta adresini yaz: <strong>{email}</strong>
          </label>
          <input
            id="delete-confirm-email"
            className="input"
            type="email"
            autoComplete="off"
            value={confirmEmail}
            onChange={(event) => setConfirmEmail(event.target.value)}
            placeholder={email}
            style={{ marginTop: 6, maxWidth: 340 }}
          />
          {error && <p className="error" style={{ marginTop: 8 }}>{error}</p>}
          <div className="cluster" style={{ marginTop: 14, gap: 10 }}>
            <button
              className="btn btn-primary btn-sm"
              type="button"
              disabled={!matches || busy}
              style={matches ? { background: 'var(--danger, #c0392b)', borderColor: 'var(--danger, #c0392b)' } : undefined}
              onClick={() => void onDelete()}
            >
              {busy ? 'Siliniyor…' : 'Hesabımı kalıcı olarak sil'}
            </button>
            <button
              className="btn btn-tertiary btn-sm"
              type="button"
              disabled={busy}
              onClick={() => { setOpen(false); setConfirmEmail(''); setError(null); }}
            >
              Vazgeç
            </button>
          </div>
        </div>
      )}
    </section>
  );
}
