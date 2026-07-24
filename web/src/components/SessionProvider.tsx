'use client';

import { createContext, useCallback, useContext, useEffect, useMemo, useState } from 'react';
import type { ReactNode } from 'react';
import { getMe } from '@/lib/api';
import type { CurrentUser } from '@/lib/types';

interface SessionContextValue {
  user: CurrentUser | null;
  loading: boolean;
  /** Re-reads /api/auth/me. Call after any action that advances onboarding. */
  refresh: () => Promise<CurrentUser | null>;
  setUser: (user: CurrentUser | null) => void;
}

const SessionContext = createContext<SessionContextValue | null>(null);

export function SessionProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<CurrentUser | null>(null);
  const [loading, setLoading] = useState(true);

  const refresh = useCallback(async () => {
    const me = await getMe();
    setUser(me);
    setLoading(false);
    return me;
  }, []);

  useEffect(() => {
    void refresh();
  }, [refresh]);

  const value = useMemo<SessionContextValue>(
    () => ({ user, loading, refresh, setUser }),
    [user, loading, refresh],
  );

  return <SessionContext.Provider value={value}>{children}</SessionContext.Provider>;
}

export function useSession(): SessionContextValue {
  const context = useContext(SessionContext);
  if (!context) {
    throw new Error('useSession must be used within a SessionProvider.');
  }
  return context;
}
