'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import {
  ApiError,
  getAdminDepartmentColors,
  getDepartmentColors,
  resetAdminDepartmentColor,
  resetDepartmentColor,
  setAdminDepartmentColor,
  setDepartmentColor,
} from '@/lib/api';
import type { DepartmentColorView, DepartmentDivision } from '@/lib/types';

const DIVISION_LABELS: Record<DepartmentDivision, string> = {
  Basic: 'Temel Tıp Bilimleri',
  Internal: 'Dahili Tıp Bilimleri',
  Surgical: 'Cerrahi Tıp Bilimleri',
};

export function DepartmentColorEditor({ mode }: { mode: 'admin' | 'user' }) {
  const [items, setItems] = useState<DepartmentColorView[]>([]);
  const [reason, setReason] = useState('');
  const [busyKey, setBusyKey] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  const load = useCallback(async () => {
    try {
      setItems(mode === 'admin' ? await getAdminDepartmentColors() : await getDepartmentColors());
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Renkler yüklenemedi.');
    }
  }, [mode]);

  useEffect(() => {
    void load();
  }, [load]);

  const grouped = useMemo(
    () =>
      (['Basic', 'Internal', 'Surgical'] as DepartmentDivision[]).map((division) => ({
        division,
        items: items.filter((item) => item.division === division),
      })),
    [items],
  );

  async function save(item: DepartmentColorView, color: string) {
    if (mode === 'admin' && reason.trim().length === 0) {
      setError('Admin varsayılanını değiştirmek için denetim gerekçesi girin.');
      return;
    }
    setBusyKey(item.key);
    setError(null);
    setNotice(null);
    try {
      if (mode === 'admin') {
        await setAdminDepartmentColor(item.key, color, reason.trim());
      } else {
        await setDepartmentColor(item.key, color);
      }
      await load();
      setNotice('Renk kaydedildi. Mevcut etkinlikler sıradaki takvim yenilemesinde güncellenecek.');
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Renk kaydedilemedi.');
    } finally {
      setBusyKey(null);
    }
  }

  async function reset(item: DepartmentColorView) {
    if (mode === 'admin' && reason.trim().length === 0) {
      setError('Admin varsayılanını sıfırlamak için denetim gerekçesi girin.');
      return;
    }
    setBusyKey(item.key);
    setError(null);
    setNotice(null);
    try {
      if (mode === 'admin') {
        await resetAdminDepartmentColor(item.key, reason.trim());
      } else {
        await resetDepartmentColor(item.key);
      }
      await load();
      setNotice(mode === 'admin' ? 'Sistem varsayılanına dönüldü.' : 'Fakülte varsayılanına dönüldü.');
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Renk sıfırlanamadı.');
    } finally {
      setBusyKey(null);
    }
  }

  return (
    <div className="stack" style={{ gap: 16 }}>
      <div>
        <h3 style={{ fontSize: 16 }}>
          {mode === 'admin' ? 'Anabilim dalı renk varsayılanları' : 'Takvim renklerim'}
        </h3>
        <p className="muted" style={{ marginTop: 6, fontSize: 13.5 }}>
          {mode === 'admin'
            ? 'Bu renkler kişisel seçim yapmamış tüm kullanıcılar için geçerlidir.'
            : 'Kişisel rengin fakülte varsayılanının önüne geçer; istediğin zaman sıfırlayabilirsin.'}
        </p>
      </div>

      {mode === 'admin' && (
        <div className="field">
          <label htmlFor="department-color-reason">Değişiklik gerekçesi</label>
          <input
            id="department-color-reason"
            className="text-input"
            value={reason}
            onChange={(event) => setReason(event.target.value)}
            placeholder="Denetim kaydına yazılır"
            maxLength={1000}
          />
        </div>
      )}

      {error && <div className="error" role="alert">{error}</div>}
      {notice && <div className="success" role="status">{notice}</div>}

      {items.length === 0 && !error ? (
        <p className="muted">Yükleniyor…</p>
      ) : (
        grouped.map(({ division, items: divisionItems }) => (
          <section key={division}>
            <h4 style={{ fontSize: 13, marginBottom: 8 }}>{DIVISION_LABELS[division]}</h4>
            <div style={{ display: 'grid', gap: 8 }}>
              {divisionItems.map((item) => {
                const hasOverride = mode === 'admin' ? Boolean(item.adminDefaultColor) : Boolean(item.userColor);
                return (
                  <div
                    key={item.key}
                    className="summary-row"
                    style={{ gap: 12, alignItems: 'center' }}
                  >
                    <span style={{ flex: 1, minWidth: 180 }}>{item.name}</span>
                    <input
                      type="color"
                      aria-label={`${item.name} rengi`}
                      value={item.effectiveColor}
                      disabled={busyKey === item.key}
                      onChange={(event) => void save(item, event.target.value.toUpperCase())}
                      style={{ width: 42, height: 32, padding: 2, cursor: 'pointer' }}
                    />
                    <code style={{ width: 70 }}>{item.effectiveColor}</code>
                    <button
                      type="button"
                      className="btn btn-tertiary btn-sm"
                      disabled={!hasOverride || busyKey === item.key}
                      onClick={() => void reset(item)}
                    >
                      Sıfırla
                    </button>
                  </div>
                );
              })}
            </div>
          </section>
        ))
      )}
    </div>
  );
}
