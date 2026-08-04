'use client';

import type { ReactNode } from 'react';

export function formatDateTime(value?: string | null): string {
  if (!value) return '—';
  return new Intl.DateTimeFormat('tr-TR', {
    dateStyle: 'medium', timeStyle: 'short', timeZone: 'Europe/Istanbul',
  }).format(new Date(value));
}

export function statusBadge(value: string): string {
  const normalized = value.toLowerCase();
  if (['active', 'completed', 'published', 'redeemed', 'healthy', 'authorized'].some((x) => normalized.includes(x))) return 'badge-success';
  if (['failed', 'revoked', 'suspended', 'error', 'expired', 'down'].some((x) => normalized.includes(x))) return 'badge-danger';
  if (['pending', 'review', 'progress', 'warning', 'overdue', 'frozen'].some((x) => normalized.includes(x))) return 'badge-warning';
  return 'badge-neutral';
}

export function Tabs({ value, onChange, items }: {
  value: string;
  onChange: (value: string) => void;
  items: { value: string; label: string }[];
}) {
  return <div className="cluster" role="tablist" style={{ gap: 8, marginBottom: 18 }}>
    {items.map((item) => <button key={item.value} type="button" role="tab" aria-selected={value === item.value} className={`btn btn-sm ${value === item.value ? 'btn-primary' : 'btn-secondary'}`} onClick={() => onChange(item.value)}>{item.label}</button>)}
  </div>;
}

export function Pager({ page, totalPages, totalCount, onChange }: {
  page: number; totalPages: number; totalCount: number; onChange: (page: number) => void;
}) {
  if (totalPages <= 1) return totalCount ? <p className="muted" style={{ marginTop: 12, fontSize: 12 }}>{totalCount} kayıt</p> : null;
  return <div className="cluster" style={{ justifyContent: 'space-between', marginTop: 16 }}>
    <span className="muted" style={{ fontSize: 13 }}>{totalCount} kayıt · Sayfa {page}/{totalPages}</span>
    <div className="cluster" style={{ gap: 8 }}>
      <button className="btn btn-secondary btn-sm" type="button" disabled={page <= 1} onClick={() => onChange(page - 1)}>Önceki</button>
      <button className="btn btn-secondary btn-sm" type="button" disabled={page >= totalPages} onClick={() => onChange(page + 1)}>Sonraki</button>
    </div>
  </div>;
}

export function DetailDrawer({ title, subtitle, onClose, children }: {
  title: string; subtitle?: string; onClose: () => void; children: ReactNode;
}) {
  return <>
    <button type="button" aria-label="Detayı kapat" onClick={onClose} style={{ position: 'fixed', inset: 0, border: 0, background: 'rgba(20,35,40,.35)', zIndex: 290 }} />
    <aside aria-label={`${title} detayı`} style={{ position: 'fixed', zIndex: 300, top: 0, right: 0, bottom: 0, width: 'min(520px, 100vw)', overflowY: 'auto', background: 'var(--canvas)', boxShadow: 'var(--shadow-lg)', padding: 24 }}>
      <div className="cluster" style={{ justifyContent: 'space-between' }}><div><h2 style={{ fontSize: 19 }}>{title}</h2>{subtitle && <p className="muted" style={{ marginTop: 4 }}>{subtitle}</p>}</div><button className="btn-icon" type="button" onClick={onClose} aria-label="Kapat">×</button></div>
      <div style={{ marginTop: 20 }}>{children}</div>
    </aside>
  </>;
}

export function LoadState({ loading, error, empty, onRetry }: { loading: boolean; error?: string | null; empty?: boolean; onRetry?: () => void }) {
  if (loading) return <p className="loading-note">Yükleniyor…</p>;
  if (error) return <div className="error" role="alert">{error}{onRetry && <button className="btn btn-secondary btn-sm" style={{ marginLeft: 12 }} type="button" onClick={onRetry}>Yeniden dene</button>}</div>;
  if (empty) return <p className="muted" style={{ padding: '18px 0' }}>Eşleşen kayıt bulunamadı.</p>;
  return null;
}
