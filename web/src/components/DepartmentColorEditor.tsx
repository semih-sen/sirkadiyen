'use client';

import { useCallback, useEffect, useMemo, useState, type CSSProperties } from 'react';
import { ApiError, getAdminDepartmentColors, getDepartmentColors, resetAdminDepartmentColor, resetDepartmentColor, setAdminDepartmentColor, setDepartmentColor } from '@/lib/api';
import type { DepartmentColorView, DepartmentDivision } from '@/lib/types';

const DIVISIONS: { key: DepartmentDivision | 'All'; label: string; short: string }[] = [
  { key: 'All', label: 'Tüm anabilim dalları', short: 'Tümü' },
  { key: 'Basic', label: 'Temel Tıp Bilimleri', short: 'Temel' },
  { key: 'Internal', label: 'Dahili Tıp Bilimleri', short: 'Dahili' },
  { key: 'Surgical', label: 'Cerrahi Tıp Bilimleri', short: 'Cerrahi' },
];

function contrastColor(hex: string): string {
  const rgb = hex.replace('#', '').match(/.{2}/g)?.map((part) => Number.parseInt(part, 16)) ?? [255, 255, 255];
  return (rgb[0] * 299 + rgb[1] * 587 + rgb[2] * 114) / 1000 > 150 ? '#173332' : '#ffffff';
}

export function DepartmentColorEditor({ mode }: { mode: 'admin' | 'user' }) {
  const [items, setItems] = useState<DepartmentColorView[]>([]);
  const [drafts, setDrafts] = useState<Record<string, string>>({});
  const [reason, setReason] = useState('');
  const [query, setQuery] = useState('');
  const [division, setDivision] = useState<DepartmentDivision | 'All'>('All');
  const [onlyCustomized, setOnlyCustomized] = useState(false);
  const [busyKey, setBusyKey] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  const load = useCallback(async () => {
    try {
      const next = mode === 'admin' ? await getAdminDepartmentColors() : await getDepartmentColors();
      setItems(next);
      setDrafts(Object.fromEntries(next.map((item) => [item.key, item.effectiveColor])));
    } catch (err) { setError(err instanceof ApiError ? err.message : 'Renk paleti yüklenemedi.'); }
  }, [mode]);

  useEffect(() => { void load(); }, [load]);

  const filtered = useMemo(() => {
    const normalized = query.trim().toLocaleLowerCase('tr-TR');
    return items.filter((item) => {
      const customized = mode === 'admin' ? Boolean(item.adminDefaultColor) : Boolean(item.userColor);
      return (division === 'All' || item.division === division)
        && (!onlyCustomized || customized)
        && (!normalized || item.name.toLocaleLowerCase('tr-TR').includes(normalized));
    });
  }, [items, query, division, onlyCustomized, mode]);

  const customizedCount = items.filter((item) => mode === 'admin' ? item.adminDefaultColor : item.userColor).length;

  async function save(item: DepartmentColorView) {
    const color = drafts[item.key] ?? item.effectiveColor;
    if (mode === 'admin' && !reason.trim()) { setError('Fakülte varsayılanını değiştirmek için bir gerekçe yazın.'); return; }
    setBusyKey(item.key); setError(null); setNotice(null);
    try {
      if (mode === 'admin') await setAdminDepartmentColor(item.key, color.toUpperCase(), reason.trim());
      else await setDepartmentColor(item.key, color.toUpperCase());
      await load();
      setNotice(`${item.name} rengi kaydedildi. Takvim görünümü sıradaki yenilemede güncellenecek.`);
    } catch (err) { setError(err instanceof ApiError ? err.message : 'Renk kaydedilemedi.'); }
    finally { setBusyKey(null); }
  }

  async function reset(item: DepartmentColorView) {
    if (mode === 'admin' && !reason.trim()) { setError('Varsayılanı sıfırlamak için bir gerekçe yazın.'); return; }
    setBusyKey(item.key); setError(null); setNotice(null);
    try {
      if (mode === 'admin') await resetAdminDepartmentColor(item.key, reason.trim());
      else await resetDepartmentColor(item.key);
      await load();
      setNotice(`${item.name} ${mode === 'admin' ? 'sistem' : 'fakülte'} varsayılanına döndü.`);
    } catch (err) { setError(err instanceof ApiError ? err.message : 'Renk sıfırlanamadı.'); }
    finally { setBusyKey(null); }
  }

  return (
    <div className={`color-studio ${mode === 'user' ? 'color-studio--user' : ''}`}>
      <section className="color-studio-hero">
        <div className="color-studio-copy">
          <span className="eyebrow">Renk sistemi</span>
          <h2>{mode === 'admin' ? 'Fakülte paleti' : 'Kişisel paletim'}</h2>
          <p>{mode === 'admin' ? 'Her renk, kişisel seçim yapmamış öğrencilerin yönetilen Google Takvim etkinliklerinde kullanılır.' : 'Seçtiğin renk yalnızca senin takviminde fakülte varsayılanının önüne geçer.'}</p>
        </div>
        <div className="color-studio-metrics">
          <div><strong>{items.length || '—'}</strong><span>Anabilim dalı</span></div>
          <div><strong>{customizedCount}</strong><span>Özelleştirilmiş</span></div>
          <div><strong>{items.length - customizedCount || '—'}</strong><span>Varsayılan</span></div>
        </div>
      </section>

      {mode === 'admin' && (
        <section className="color-audit-bar">
          <div className="color-audit-icon" aria-hidden="true">✎</div>
          <div className="field">
            <label htmlFor="department-color-reason">Bu oturumdaki değişikliklerin gerekçesi</label>
            <input id="department-color-reason" className="text-input" value={reason} onChange={(event) => setReason(event.target.value)} placeholder="Örn. fakültenin 2026–2027 görsel standardı" maxLength={1000} />
            <small>Her kaydetme ve sıfırlama işlemine ayrı denetim kaydı olarak eklenir.</small>
          </div>
        </section>
      )}

      <div className="color-toolbar">
        <label className="color-search">
          <span aria-hidden="true">⌕</span><span className="sr-only">Anabilim dalı ara</span>
          <input value={query} onChange={(event) => setQuery(event.target.value)} placeholder="Anabilim dalı ara…" />
        </label>
        <div className="color-division-tabs" role="group" aria-label="Bilim dalı grubu">
          {DIVISIONS.map((item) => <button type="button" className={division === item.key ? 'active' : ''} aria-pressed={division === item.key} onClick={() => setDivision(item.key)} key={item.key}>{item.short}<span>{item.key === 'All' ? items.length : items.filter((entry) => entry.division === item.key).length}</span></button>)}
        </div>
        <label className="color-customized-toggle"><input type="checkbox" checked={onlyCustomized} onChange={(event) => setOnlyCustomized(event.target.checked)} /> Yalnızca özelleştirilenler</label>
      </div>

      {error && <div className="error" role="alert">{error}</div>}
      {notice && <div className="success" role="status">{notice}</div>}
      {!items.length && !error ? <div className="color-loading"><span className="spinner" /> Palet yükleniyor…</div> : (
        <>
          <div className="color-results-head"><span>{filtered.length} sonuç</span><span>Renk seç → önizle → kaydet</span></div>
          <div className="department-color-grid">
            {filtered.map((item) => {
              const color = drafts[item.key] ?? item.effectiveColor;
              const customized = mode === 'admin' ? Boolean(item.adminDefaultColor) : Boolean(item.userColor);
              const dirty = color.toUpperCase() !== item.effectiveColor.toUpperCase();
              return (
                <article className="department-color-card" key={item.key}>
                  <div className="department-color-preview" style={{ background: color, color: contrastColor(color) }}>
                    <span className="department-calendar-date">18</span>
                    <span><strong>{item.name}</strong><small>10.30 · Ders</small></span>
                  </div>
                  <div className="department-color-body">
                    <div className="department-color-name"><div><h3>{item.name}</h3><span>{DIVISIONS.find((entry) => entry.key === item.division)?.label}</span></div><span className={`color-origin ${customized ? 'custom' : ''}`}>{customized ? 'Özel' : 'Varsayılan'}</span></div>
                    <div className="department-color-controls">
                      <label className="color-picker-control" style={{ '--swatch': color } as CSSProperties}><input type="color" aria-label={`${item.name} rengi`} value={color} disabled={busyKey === item.key} onChange={(event) => setDrafts((current) => ({ ...current, [item.key]: event.target.value.toUpperCase() }))} /><span>{color.toUpperCase()}</span></label>
                      <button type="button" className="btn btn-primary btn-sm" disabled={!dirty || busyKey === item.key} onClick={() => void save(item)}>{busyKey === item.key ? '…' : 'Kaydet'}</button>
                    </div>
                    <div className="department-color-footer"><span>Sistem <i style={{ background: item.systemDefaultColor }} /> {item.systemDefaultColor}</span><button type="button" disabled={!customized || busyKey === item.key} onClick={() => void reset(item)}>Varsayılana dön</button></div>
                  </div>
                </article>
              );
            })}
          </div>
          {!filtered.length && <div className="admin-empty-state compact"><div className="admin-empty-mark">⌕</div><div><h2>Eşleşen anabilim dalı yok</h2><p>Arama metnini veya filtreleri değiştir.</p></div></div>}
        </>
      )}
    </div>
  );
}
