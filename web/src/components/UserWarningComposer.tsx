'use client';

import { useCallback, useEffect, useState } from 'react';
import Link from 'next/link';
import { ApiError, getAdminUser, getAnnouncementOptions, listAdminUsers } from '@/lib/api';
import { LoadState, formatDateTime } from '@/components/AdminData';
import { AnnouncementHistory } from '@/components/AnnouncementShared';
import { UserWarningForm } from '@/components/UserWarningForm';
import { Banner } from '@/components/ui';
import type {
  AdminUserDetailResponse,
  AdminUserListItem,
  AnnouncementCompositionOptions,
} from '@/lib/types';

/**
 * The single-user warning workspace (ADR-107, plan §4.5, §5.12).
 *
 * It is the same domain as the bulk event with an audience of exactly one, so it inherits the
 * deterministic key, the delivery ledger and the cancel path. What differs is the identity step:
 * the operator has to see who they are writing to and what state that account is actually in
 * before choosing a template, because most warnings are *about* that state.
 *
 * Composition itself lives in {@link UserWarningForm}, which the account detail page reuses — so an
 * operator already looking at a student can warn them without coming here first.
 */
export function UserWarningComposer() {
  const [options, setOptions] = useState<AnnouncementCompositionOptions | null>(null);
  const [loadError, setLoadError] = useState<string | null>(null);

  const [search, setSearch] = useState('');
  const [results, setResults] = useState<AdminUserListItem[] | null>(null);
  const [selected, setSelected] = useState<AdminUserDetailResponse | null>(null);

  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [reloadToken, setReloadToken] = useState(0);

  const load = useCallback(async () => {
    setLoadError(null);
    try {
      setOptions(await getAnnouncementOptions());
    } catch (caught) {
      setLoadError(caught instanceof ApiError ? caught.message : 'Seçenekler yüklenemedi.');
    }
  }, []);

  useEffect(() => { void load(); }, [load]);

  async function runSearch() {
    setResults(null);
    setError(null);
    try {
      const paged = await listAdminUsers({ search: search.trim(), pageSize: 10 });
      setResults(paged.items);
    } catch (caught) {
      setError(caught instanceof ApiError ? caught.message : 'Kullanıcı aranamadı.');
    }
  }

  async function select(userId: string) {
    setError(null);
    try {
      setSelected(await getAdminUser(userId));
    } catch (caught) {
      setError(caught instanceof ApiError ? caught.message : 'Kullanıcı ayrıntısı alınamadı.');
    }
  }

  function onSent(message: string) {
    setNotice(message);
    setSelected(null);
    setResults(null);
    setReloadToken((token) => token + 1);
  }

  if (!options) {
    return <LoadState loading={!loadError} error={loadError} onRetry={() => void load()} />;
  }

  return (
    <div>
      {notice && <Banner tone="info">{notice}</Banner>}

      <section>
        <h2 style={{ fontSize: 16 }}>1 · Kullanıcı</h2>
        <label htmlFor="warning-search">E-posta, ad veya öğrenci numarası ile ara</label>
        <div className="cluster" style={{ gap: 8 }}>
          <input
            className="text-input"
            id="warning-search"
            value={search}
            onChange={(event) => setSearch(event.target.value)}
            onKeyDown={(event) => { if (event.key === 'Enter') void runSearch(); }}
            placeholder="ornek@ogr.iu.edu.tr"
          />
          <button className="btn btn-secondary" type="button" onClick={() => void runSearch()}>
            Ara
          </button>
        </div>

        {results?.length === 0 && (
          <p className="muted" style={{ fontSize: 13 }}>Eşleşen kullanıcı bulunamadı.</p>
        )}
        {results && results.length > 0 && (
          <ul style={{ listStyle: 'none', padding: 0, marginTop: 10 }}>
            {results.map((user) => (
              <li key={user.id} style={{ marginBottom: 6 }}>
                <button
                  className="btn btn-tertiary btn-sm"
                  type="button"
                  onClick={() => void select(user.id)}
                >
                  {user.email}
                </button>
              </li>
            ))}
          </ul>
        )}
        {error && <div className="error" role="alert">{error}</div>}
      </section>

      {selected && (
        <>
          <section style={{ marginTop: 24 }}>
            <h2 style={{ fontSize: 16 }}>2 · Hesabın durumu</h2>
            <p className="muted" style={{ fontSize: 13 }}>
              Uyarıların çoğu bu durumlar hakkındadır, bu yüzden şablon seçmeden önce burada
              gösterilir. Takvimi olmayan bir hesaba uyarı yazılamaz; önizleme bunu gerekçesiyle
              söyler.
            </p>
            <ul style={{ listStyle: 'none', padding: 0, margin: 0, fontSize: 13 }}>
              <li><strong>E-posta:</strong> {selected.user.summary.email}</li>
              <li><strong>Lisans:</strong> {selected.user.summary.licenseState}</li>
              <li><strong>Onboarding:</strong> {selected.onboardingState}</li>
              <li>
                <strong>Akademik profil:</strong>{' '}
                {selected.user.profile
                  ? `Dönem ${selected.user.profile.classYear} · ${selected.user.profile.programLanguage}`
                  : 'Yok'}
              </li>
              <li><strong>Yönetilen etkinlik:</strong> {selected.user.managedEventCount}</li>
              <li>
                <strong>Son giriş:</strong>{' '}
                {formatDateTime(selected.user.summary.lastSignedInAtUtc)}
              </li>
            </ul>
            <Link
              className="btn btn-tertiary btn-sm"
              style={{ marginTop: 10 }}
              href={`/admin/users/${selected.user.summary.id}`}
            >
              Hesabın tüm ayrıntıları
            </Link>
          </section>

          <div style={{ marginTop: 24 }}>
            <UserWarningForm
              userId={selected.user.summary.id}
              options={options}
              onSent={onSent}
            />
          </div>
        </>
      )}

      <hr style={{ margin: '28px 0', border: 0, borderTop: '1px solid var(--border)' }} />
      <h2 style={{ fontSize: 18 }}>Gönderilen uyarılar</h2>
      <AnnouncementHistory kind="UserWarning" reloadToken={reloadToken} />
    </div>
  );
}
