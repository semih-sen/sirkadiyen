'use client';

import { useEffect, useId, useState } from 'react';
import { ApiError, getMealSubscription, setMealSubscription } from '@/lib/api';

/**
 * The cafeteria lunch-menu preference (ADR-150). A reversible opt-in: turning it on
 * backfills the currently-known days, turning it off removes the written events. The
 * worker converges the calendar within a couple of minutes; this card only records
 * the choice and reflects it back.
 */
export function MealMenuCard() {
  const checkboxId = useId();
  const [enabled, setEnabled] = useState<boolean | null>(null);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  useEffect(() => {
    getMealSubscription()
      .then((view) => setEnabled(view.enabled))
      .catch((cause) =>
        setError(cause instanceof ApiError ? cause.message : 'Tercih alınamadı.'));
  }, []);

  async function onToggle(next: boolean) {
    if (enabled === null || saving) return;
    setSaving(true);
    setError(null);
    setNotice(null);
    const previous = enabled;
    setEnabled(next); // optimistic
    try {
      const view = await setMealSubscription(next);
      setEnabled(view.enabled);
      setNotice(
        view.enabled
          ? 'Öğle yemeği menüsü takvimine eklenecek. Yayınlanan günler birkaç dakika içinde görünür.'
          : 'Öğle yemeği menüsü takvimimden kaldırılacak.',
      );
    } catch (cause) {
      setEnabled(previous); // revert
      setError(cause instanceof ApiError ? cause.message : 'Tercih kaydedilemedi.');
    } finally {
      setSaving(false);
    }
  }

  return (
    <section className="card card-content">
      <h3 style={{ fontSize: 15 }}>Yemekhane menüsü</h3>
      <p className="muted" style={{ marginTop: 8, fontSize: 14 }}>
        Fakülte yemekhanesinin öğle yemeği menüsünü takvimine (12.30–13.00) ekleyebiliriz. Menü aylık
        yayımlanır; yayımlandıkça günler otomatik eklenir. İstediğin zaman kapatabilirsin.
      </p>
      {enabled === null && !error ? (
        <p className="loading-note" style={{ marginTop: 12 }}>Yükleniyor…</p>
      ) : (
        <label
          className="color-customized-toggle"
          htmlFor={checkboxId}
          style={{ marginTop: 14, fontSize: 14 }}
        >
          <input
            id={checkboxId}
            type="checkbox"
            checked={enabled ?? false}
            disabled={enabled === null || saving}
            onChange={(event) => void onToggle(event.target.checked)}
          />
          Öğle yemeği menüsünü takvimime ekle
        </label>
      )}
      {saving && (
        <p className="muted" style={{ fontSize: 12, marginTop: 8 }}>
          <span className="spinner" aria-hidden="true" />Kaydediliyor…
        </p>
      )}
      {notice && (
        <p role="status" style={{ marginTop: 10, fontSize: 13 }}>{notice}</p>
      )}
      {error && (
        <p className="error" style={{ marginTop: 10 }}>{error}</p>
      )}
    </section>
  );
}
