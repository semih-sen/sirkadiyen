'use client';

import Link from 'next/link';
import type { ReactNode } from 'react';
import { Banner } from '@/components/ui';

// Persistent admin chrome (plan §3.2, §5.9): a fixed sidebar, a top context bar,
// and a freeze banner that spans every operational screen while the pipeline is
// frozen — because a frozen system changes the meaning of every action below it.
//
// Only "Genel bakış" is a live route today; every other item in the plan's admin
// IA has no backend yet (see GAPS.md), so it is rendered as a disabled entry with
// a "Yakında" marker rather than a link that 404s.

export type AdminNavKey =
  | 'dashboard'
  | 'finance'
  | 'users'
  | 'bulk-event'
  | 'user-warning'
  | 'sources'
  | 'server'
  | 'access-logs';

interface NavItem {
  key: AdminNavKey;
  label: string;
  icon: string;
  href?: string;
}

const NAV_GROUPS: { label: string; items: NavItem[] }[] = [
  {
    label: 'Operasyon',
    items: [
      { key: 'dashboard', label: 'Genel bakış', icon: '▦', href: '/admin' },
      { key: 'finance', label: 'Finans', icon: '₺' },
    ],
  },
  {
    label: 'Kullanıcı işlemleri',
    items: [
      { key: 'users', label: 'Kullanıcılar', icon: '◍' },
      { key: 'bulk-event', label: 'Toplu etkinlik', icon: '▤' },
      { key: 'user-warning', label: 'Kullanıcı uyarısı', icon: '◭' },
    ],
  },
  {
    label: 'Altyapı',
    items: [
      { key: 'sources', label: 'Kaynaklar & senkron', icon: '⇄' },
      { key: 'server', label: 'Sunucu', icon: '▣' },
      { key: 'access-logs', label: 'Erişim kayıtları', icon: '☰' },
    ],
  },
];

function NavEntry({ item, active }: { item: NavItem; active: boolean }) {
  if (!item.href) {
    return (
      <li>
        <span
          className="admin-nav-disabled"
          aria-disabled="true"
          style={{
            display: 'flex',
            alignItems: 'center',
            gap: 10,
            padding: '10px 12px',
            borderRadius: 10,
            color: 'color-mix(in oklab, #fff 40%, transparent)',
            fontSize: 13.5,
            fontWeight: 600,
            cursor: 'not-allowed',
          }}
          title="Bu ekran için henüz arka uç yok (GAPS.md)"
        >
          <span className="nav-icon" aria-hidden="true">
            {item.icon}
          </span>
          {item.label}
          <span
            style={{
              marginLeft: 'auto',
              fontSize: 10,
              fontWeight: 700,
              textTransform: 'uppercase',
              letterSpacing: '0.04em',
              opacity: 0.7,
            }}
          >
            Yakında
          </span>
        </span>
      </li>
    );
  }
  return (
    <li>
      <Link href={item.href} aria-current={active ? 'page' : undefined}>
        <span className="nav-icon" aria-hidden="true">
          {item.icon}
        </span>
        {item.label}
      </Link>
    </li>
  );
}

export function AdminShell({
  active,
  operator,
  isFrozen = false,
  children,
}: {
  active: AdminNavKey;
  operator?: string;
  isFrozen?: boolean;
  children: ReactNode;
}) {
  return (
    <div className="admin-shell">
      <aside className="admin-sidebar">
        <Link className="brand" href="/admin">
          <span className="brand__mark">S</span> Sirkadiyen Yönetim
        </Link>
        {NAV_GROUPS.map((group) => (
          <div key={group.label}>
            <div className="admin-nav-group-label">{group.label}</div>
            <ul className="admin-nav">
              {group.items.map((item) => (
                <NavEntry key={item.key} item={item} active={item.key === active} />
              ))}
            </ul>
          </div>
        ))}
      </aside>

      <div className="admin-main">
        <div className="admin-topbar">
          <div className="cluster" style={{ gap: 10 }}>
            <span className="env-chip">⚙ Yönetim paneli</span>
            {isFrozen ? (
              <span className="badge badge-warning">Dondurulmuş</span>
            ) : (
              <span className="badge badge-success">Çalışıyor</span>
            )}
          </div>
          <div className="cluster" style={{ gap: 14 }}>
            {operator && (
              <span className="muted" style={{ fontSize: 13 }}>
                Operatör: {operator}
              </span>
            )}
            <Link className="btn btn-tertiary btn-sm" href="/">
              Öğrenci tarafına dön
            </Link>
          </div>
        </div>

        {isFrozen && (
          <div className="banner-freeze" role="alert">
            ⚠ Pipeline donduruldu — acquisition, parsing, publication ve calendar işleri beklemede.
          </div>
        )}

        <div className="admin-content">{children}</div>
      </div>
    </div>
  );
}

/** A section heading used across admin cards. */
export function AdminSectionTitle({ children }: { children: ReactNode }) {
  return <h2 style={{ fontSize: 18, margin: '0 0 12px' }}>{children}</h2>;
}
