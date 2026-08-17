'use client';

import { useCallback, useEffect, useState } from 'react';
import Link from 'next/link';
import {
  ApiError,
  getAdminLicense,
  getProfileOptions,
  listAdminLicenses,
  listAdminUsers,
  revokeLicense,
} from '@/lib/api';
import { DetailDrawer, LoadState, Pager, Tabs, formatDateTime, statusBadge } from '@/components/AdminData';
import { AdminUserFilterBar } from '@/components/AdminUserFilterBar';
import { LicenseAdministration } from '@/components/LicenseAdministration';
import type {
  AdminLicenseDetail,
  AdminLicenseListItem,
  AdminUserFilters,
  AdminUserListItem,
  LicenseKind,
  LicenseStatus,
  PagedResult,
  SupportedProfileOptions,
} from '@/lib/types';

export function AdminUserDirectory() {
  const [tab, setTab] = useState('users');
  return <><Tabs value={tab} onChange={setTab} items={[{ value: 'users', label: 'Kullanıcılar' }, { value: 'licenses', label: 'Lisanslar' }]} />{tab === 'users' ? <Users /> : <Licenses />}</>;
}

const PAGE_SIZE = 50;

/**
 * The account directory.
 *
 * Filtering, sorting and paging are all server-side: the row set is the page the backend selected,
 * so the record count under a filter is the real one rather than whatever part of a page happened
 * to be fetched. A row is a link to the account's own page, not a drawer — the operations an
 * operator performs on a user (activation, warnings, calendar inspection) are too many to fit in a
 * panel and each of them deserves an address that can be shared.
 */
function Users() {
  const [searchInput, setSearchInput] = useState('');
  const [filters, setFilters] = useState<AdminUserFilters>({ sort: 'CreatedAtUtc', descending: true });
  const [expanded, setExpanded] = useState(false);
  const [page, setPage] = useState(1);
  const [profileOptions, setProfileOptions] = useState<SupportedProfileOptions | null>(null);
  const [data, setData] = useState<PagedResult<AdminUserListItem> | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  // The typed term is debounced into the filter state; every other control applies at once,
  // because a dropdown is one deliberate choice rather than a stream of keystrokes.
  useEffect(() => {
    const timer = window.setTimeout(() => {
      setFilters((current) => ({ ...current, search: searchInput.trim() || undefined }));
      setPage(1);
    }, 300);
    return () => window.clearTimeout(timer);
  }, [searchInput]);

  useEffect(() => {
    // The cohort dimensions the advanced panel offers come from the supported-profile schema, so a
    // failure here narrows the panel rather than breaking the directory.
    void (async () => {
      try {
        setProfileOptions(await getProfileOptions());
      } catch {
        setProfileOptions(null);
      }
    })();
  }, []);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      setData(await listAdminUsers({ ...filters, page, pageSize: PAGE_SIZE }));
    } catch (caught) {
      setError(caught instanceof ApiError ? caught.message : 'Kullanıcılar alınamadı.');
    } finally {
      setLoading(false);
    }
  }, [filters, page]);

  useEffect(() => { void load(); }, [load]);

  function change(changes: Partial<AdminUserFilters>) {
    setFilters((current) => ({ ...current, ...changes }));
    setPage(1);
  }

  function reset() {
    setSearchInput('');
    setFilters({ sort: 'CreatedAtUtc', descending: true });
    setPage(1);
  }

  return (
    <section className="card admin-workspace-card">
      <AdminUserFilterBar
        filters={{ ...filters, search: searchInput }}
        profileOptions={profileOptions}
        onChange={({ search, ...rest }) => {
          if (search !== undefined) setSearchInput(search);
          if (Object.keys(rest).length > 0) change(rest);
        }}
        onReset={reset}
        expanded={expanded}
        onToggleExpanded={() => setExpanded((value) => !value)}
      />

      <LoadState
        loading={loading}
        error={error}
        empty={!loading && data?.items.length === 0}
        onRetry={() => void load()}
      />

      {!loading && data && data.items.length > 0 && (
        <>
          <div className="table-wrap">
            <table className="data-table data-table--stack">
              <thead>
                <tr>
                  <th>Kullanıcı</th>
                  <th>Rol</th>
                  <th>Lisans</th>
                  <th>Profil</th>
                  <th>Takvim</th>
                  <th>Son giriş</th>
                </tr>
              </thead>
              <tbody>
                {data.items.map((user) => (
                  <tr key={user.id}>
                    <td data-label="Kullanıcı">
                      <Link className="link" href={`/admin/users/${user.id}`}>
                        <strong>{user.displayName ?? user.email}</strong>
                      </Link>
                      <small className="muted" style={{ display: 'block' }}>
                        {user.email}
                        {user.studentNumber ? ` · ${user.studentNumber}` : ''}
                      </small>
                    </td>
                    <td data-label="Rol">{user.role}</td>
                    <td data-label="Lisans">
                      <span className={`badge ${statusBadge(user.licenseState)}`}>
                        {user.licenseState}
                      </span>
                    </td>
                    <td data-label="Profil">
                      {user.hasProfile
                        ? `${user.classYear}. dönem · ${user.programLanguage === 'English' ? 'İngilizce' : 'Türkçe'}`
                        : 'Eksik'}
                    </td>
                    <td data-label="Takvim">
                      {user.calendarStatus ? (
                        <>
                          <span className={`badge ${statusBadge(user.calendarStatus)}`}>
                            {user.initialSyncState}
                          </span>
                          <small className="muted" style={{ display: 'block' }}>
                            {user.managedEventCount} etkinlik
                          </small>
                        </>
                      ) : (
                        <span className="muted">Bağlı değil</span>
                      )}
                    </td>
                    <td data-label="Son giriş">{formatDateTime(user.lastSignedInAtUtc)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          <Pager
            page={data.page}
            totalPages={data.totalPages}
            totalCount={data.totalCount}
            onChange={setPage}
          />
        </>
      )}
    </section>
  );
}

function Licenses() {
  const [status, setStatus] = useState<LicenseStatus | ''>(''); const [kind, setKind] = useState<LicenseKind | ''>(''); const [page, setPage] = useState(1);
  const [data, setData] = useState<PagedResult<AdminLicenseListItem> | null>(null); const [detail, setDetail] = useState<AdminLicenseDetail | null>(null);
  const [loading, setLoading] = useState(true); const [error, setError] = useState<string | null>(null); const [reason, setReason] = useState(''); const [busy, setBusy] = useState(false);
  const load = useCallback(async () => { setLoading(true); setError(null); try { setData(await listAdminLicenses({ status: status || undefined, kind: kind || undefined, page, pageSize: 50 })); } catch (e) { setError(e instanceof ApiError ? e.message : 'Lisanslar alınamadı.'); } finally { setLoading(false); } }, [status, kind, page]);
  useEffect(() => { void load(); }, [load]);
  async function open(item: AdminLicenseListItem) { try { setDetail(await getAdminLicense(item.licenseId)); setReason(''); } catch (e) { setError(e instanceof ApiError ? e.message : 'Lisans detayı alınamadı.'); } }
  async function revoke() { if (!detail || !reason.trim()) return; setBusy(true); try { await revokeLicense(detail.summary.licenseId, reason.trim()); setDetail(await getAdminLicense(detail.summary.licenseId)); setReason(''); await load(); } catch (e) { setError(e instanceof ApiError ? e.message : 'Lisans iptal edilemedi.'); } finally { setBusy(false); } }
  return <div className="stack" style={{ gap: 18 }}><LicenseAdministration onCreated={() => void load()} /><section className="card admin-workspace-card"><div className="cluster" style={{ gap: 10, marginBottom: 16 }}><select className="select-input" value={status} onChange={(e) => { setStatus(e.target.value); setPage(1); }}><option value="">Tüm durumlar</option>{['Created','Active','Redeemed','Revoked','Expired'].map((v) => <option key={v}>{v}</option>)}</select><select className="select-input" value={kind} onChange={(e) => { setKind(e.target.value); setPage(1); }}><option value="">Tüm türler</option><option>Code</option><option>Manual</option></select></div><LoadState loading={loading} error={error} empty={!loading && data?.items.length === 0} onRetry={() => void load()} />{!loading && data && data.items.length > 0 && <><div className="table-wrap"><table className="data-table"><thead><tr><th>Kimlik</th><th>Tür</th><th>Durum</th><th>Oluşturan</th><th>Oluşturma</th></tr></thead><tbody>{data.items.map((item) => <tr key={item.licenseId} onClick={() => void open(item)} style={{ cursor: 'pointer' }}><td className="mono">{item.licenseId}</td><td>{item.kind}</td><td><span className={`badge ${statusBadge(item.status)}`}>{item.status}</span></td><td>{item.createdByEmail}</td><td>{formatDateTime(item.createdAtUtc)}</td></tr>)}</tbody></table></div><Pager page={data.page} totalPages={data.totalPages} totalCount={data.totalCount} onChange={setPage} /></>}{detail && <DetailDrawer title="Lisans detayı" subtitle={detail.summary.licenseId} onClose={() => setDetail(null)}><Detail title="Durum"><Row label="Tür" value={detail.summary.kind} /><Row label="Durum" value={detail.summary.status} /><Row label="Oluşturan" value={detail.summary.createdByEmail} /><Row label="Not" value={detail.summary.notes ?? '—'} /></Detail><Detail title="Denetim geçmişi">{detail.audit.map((entry, i) => <p key={`${entry.occurredAtUtc}-${i}`}><strong>{entry.action}</strong> · {entry.actorEmail}<small className="muted" style={{ display: 'block' }}>{entry.reason || 'Gerekçe yok'} · {formatDateTime(entry.occurredAtUtc)}</small></p>)}</Detail>{detail.summary.status !== 'Revoked' && <Detail title="Lisansı iptal et"><p className="muted">Takvim verileri korunur; gelecekteki senkronizasyon durur.</p><textarea className="text-input" value={reason} onChange={(e) => setReason(e.target.value)} placeholder="Zorunlu gerekçe" style={{ margin: '10px 0' }} /><button className="btn btn-danger" type="button" disabled={busy || !reason.trim()} onClick={() => void revoke()}>{busy ? 'İptal ediliyor…' : 'Seçili lisansı iptal et'}</button></Detail>}</DetailDrawer>}</section></div>;
}

function Detail({ title, children }: { title: string; children: React.ReactNode }) { return <section style={{ padding: '14px 0', borderBottom: '1px solid var(--border)' }}><h3 style={{ fontSize: 13, textTransform: 'uppercase', color: 'var(--ink-70)' }}>{title}</h3><div style={{ marginTop: 8 }}>{children}</div></section>; }
function Row({ label, value }: { label: string; value: string }) { return <div className="summary-row"><span className="muted">{label}</span><strong>{value}</strong></div>; }
