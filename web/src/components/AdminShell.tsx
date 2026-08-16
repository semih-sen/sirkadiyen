'use client';

import Link from 'next/link';
import type { ReactNode } from 'react';

export type AdminNavKey =
  | 'dashboard'
  | 'finance'
  | 'users'
  | 'bulk-event'
  | 'user-warning'
  | 'sources'
  | 'revisions'
  | 'diffs'
  | 'colors'
  | 'operations'
  | 'server'
  | 'access-logs';

interface NavItem {
  key: AdminNavKey;
  label: string;
  icon: string;
  href: string;
}

const NAV_GROUPS: { label: string; items: NavItem[] }[] = [
  {
    label: 'Operasyon',
    items: [
      { key: 'dashboard', label: 'Genel bakış', icon: '⌂', href: '/admin' },
      { key: 'finance', label: 'Finans', icon: '₺', href: '/admin/finance' },
    ],
  },
  {
    label: 'Kullanıcı işlemleri',
    items: [
      { key: 'users', label: 'Kullanıcılar & lisans', icon: '◉', href: '/admin/users' },
      { key: 'bulk-event', label: 'Toplu etkinlik', icon: '▤', href: '/admin/bulk-event' },
      { key: 'user-warning', label: 'Kullanıcı uyarısı', icon: '◇', href: '/admin/user-warning' },
    ],
  },
  {
    label: 'Akademik veri',
    items: [
      { key: 'sources', label: 'Kaynaklar', icon: '⇄', href: '/admin/sources' },
      { key: 'revisions', label: 'Revizyonlar', icon: '✓', href: '/admin/revisions' },
      { key: 'diffs', label: 'Diff kuyrukları', icon: '⇅', href: '/admin/diffs' },
      { key: 'colors', label: 'Anabilim dalı renkleri', icon: '◐', href: '/admin/colors' },
    ],
  },
  {
    label: 'Sistem',
    items: [
      { key: 'operations', label: 'Operasyon kontrolü', icon: '⚙', href: '/admin/operations' },
      { key: 'server', label: 'Sunucu', icon: '▣', href: '/admin/server' },
      { key: 'access-logs', label: 'Erişim kayıtları', icon: '☰', href: '/admin/access-logs' },
    ],
  },
];

export function AdminShell({
  active,
  operator,
  isFrozen = false,
  onSignOut,
  children,
}: {
  active: AdminNavKey;
  operator?: string;
  isFrozen?: boolean;
  onSignOut?: () => void;
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
                <li key={item.key}>
                  <Link href={item.href} aria-current={item.key === active ? 'page' : undefined}>
                    <span className="nav-icon" aria-hidden="true">{item.icon}</span>
                    {item.label}
                  </Link>
                </li>
              ))}
            </ul>
          </div>
        ))}
      </aside>

      <div className="admin-main">
        <div className="admin-topbar">
          <div className="cluster" style={{ gap: 10 }}>
            <span className="env-chip">⚙ Yönetim paneli</span>
            <span className={`badge ${isFrozen ? 'badge-warning' : 'badge-success'}`}>
              {isFrozen ? 'Dondurulmuş' : 'Çalışıyor'}
            </span>
          </div>
          <div className="cluster" style={{ gap: 10 }}>
            {operator && <span className="admin-operator">{operator}</span>}
            <Link className="btn btn-tertiary btn-sm" href="/dashboard">Öğrenci paneli</Link>
            {onSignOut && <button className="btn btn-tertiary btn-sm" type="button" onClick={onSignOut}>Çıkış</button>}
          </div>
        </div>
        {isFrozen && <div className="banner-freeze" role="alert">⚠ Pipeline donduruldu — arka plan işleri beklemede.</div>}
        <main className="admin-content" id="admin-content">{children}</main>
      </div>
    </div>
  );
}

export function AdminPageHeader({
  eyebrow,
  title,
  description,
  actions,
}: {
  eyebrow?: string;
  title: string;
  description: string;
  actions?: ReactNode;
}) {
  return (
    <header className="admin-page-header">
      <div>
        {eyebrow && <span className="eyebrow">{eyebrow}</span>}
        <h1>{title}</h1>
        <p>{description}</p>
      </div>
      {actions && <div className="admin-page-actions">{actions}</div>}
    </header>
  );
}

export function AdminSectionTitle({ children }: { children: ReactNode }) {
  return <h2 style={{ fontSize: 18, margin: '0 0 12px' }}>{children}</h2>;
}
