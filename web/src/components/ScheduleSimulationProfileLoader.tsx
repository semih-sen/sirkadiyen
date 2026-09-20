'use client';

import { useCallback, useEffect, useState } from 'react';
import { DetailDrawer, LoadState } from '@/components/AdminData';
import { ApiError, getAdminUser, listAdminUsers } from '@/lib/api';
import { dimensionLabel } from '@/lib/selectors';
import type { AdminUserListItem, ProgramLanguage } from '@/lib/types';

export interface LoadedCohort {
  classYear: number;
  programLanguage: ProgramLanguage;
  selectors: Record<string, string>;
  /** Only for the notice that says where the values came from; never sent anywhere. */
  email: string;
}

function languageLabel(language: ProgramLanguage): string {
  return language === 'English' ? 'İngilizce' : 'Türkçe';
}

/**
 * Fills the cohort form from a real account's stored profile.
 *
 * It is a shortcut for typing six dropdowns, not a mode: the values are copied into the form and
 * the account is forgotten immediately. Nothing about the simulation is tied to that user, and no
 * user id is ever sent to the simulation endpoint — which is what keeps this a way of asking
 * "what does this cohort receive" rather than an unlogged window onto one student.
 */
export function ScheduleSimulationProfileLoader({ onLoad }: { onLoad: (cohort: LoadedCohort) => void }) {
  const [open, setOpen] = useState(false);
  const [search, setSearch] = useState('');
  const [users, setUsers] = useState<AdminUserListItem[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [picking, setPicking] = useState<string | null>(null);

  const load = useCallback(async (term: string) => {
    setLoading(true);
    setError(null);
    try {
      const page = await listAdminUsers({ search: term || undefined, pageSize: 10 });
      setUsers(page.items);
    } catch (cause) {
      setError(cause instanceof ApiError
        ? (cause.problem?.detail ?? 'Kullanıcılar getirilemedi.')
        : 'Kullanıcılar getirilemedi.');
    } finally {
      setLoading(false);
    }
  }, []);

  // Debounced, so typing a name does not fire a request per keystroke.
  useEffect(() => {
    if (!open) return undefined;
    const timer = setTimeout(() => { void load(search); }, 250);
    return () => clearTimeout(timer);
  }, [open, search, load]);

  async function pick(user: AdminUserListItem) {
    setPicking(user.id);
    setError(null);
    try {
      const detail = await getAdminUser(user.id);
      const profile = detail.user.profile;
      if (!profile) {
        setError('Bu kullanıcının akademik profili yok.');
        return;
      }
      onLoad({
        classYear: profile.classYear,
        programLanguage: profile.programLanguage,
        selectors: profile.selectors,
        email: user.email,
      });
      setOpen(false);
    } catch (cause) {
      setError(cause instanceof ApiError
        ? (cause.problem?.detail ?? 'Profil getirilemedi.')
        : 'Profil getirilemedi.');
    } finally {
      setPicking(null);
    }
  }

  return (
    <>
      <button
        className="btn btn-secondary btn-sm"
        type="button"
        onClick={() => setOpen(true)}
      >
        Gerçek bir kullanıcıdan yükle
      </button>

      {open && (
        <DetailDrawer
          title="Kullanıcıdan konfigürasyon yükle"
          subtitle="Seçtiğin kullanıcının profili forma kopyalanır; simülasyon yine kitleye sorulur."
          onClose={() => setOpen(false)}
        >
          <label className="field">
            <span>Ara</span>
            <input
              className="text-input"
              type="search"
              value={search}
              placeholder="E-posta, ad veya öğrenci numarası"
              onChange={(changed) => setSearch(changed.target.value)}
            />
          </label>

          <LoadState
            loading={loading}
            error={error}
            empty={!loading && !error && users.length === 0}
            onRetry={() => void load(search)}
          />

          {!loading && users.length > 0 && (
            <ul className="stack" style={{ listStyle: 'none', margin: '16px 0 0', padding: 0 }}>
              {users.map((user) => (
                <li key={user.id}>
                  <button
                    className="btn btn-tertiary"
                    type="button"
                    style={{ width: '100%', justifyContent: 'flex-start', textAlign: 'left' }}
                    disabled={!user.hasProfile || picking === user.id}
                    onClick={() => void pick(user)}
                  >
                    <span className="stack" style={{ gap: 2 }}>
                      <strong>{user.email}</strong>
                      <span className="muted">
                        {user.hasProfile && user.classYear && user.programLanguage
                          ? `Dönem ${user.classYear} · ${languageLabel(user.programLanguage)}`
                          : 'Profil yok'}
                      </span>
                    </span>
                  </button>
                </li>
              ))}
            </ul>
          )}
        </DetailDrawer>
      )}
    </>
  );
}

/** Names the dimensions a loaded profile carried, for the notice under the form. */
export function describeSelectors(selectors: Record<string, string>): string {
  return Object.entries(selectors)
    .map(([key, value]) => `${dimensionLabel(key)}: ${value}`)
    .join(' · ');
}
