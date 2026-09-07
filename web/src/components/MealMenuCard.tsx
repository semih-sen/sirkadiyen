'use client';

import { useEffect, useId, useState } from 'react';
import { ApiError, getMealSubscription, setMealSubscription } from '@/lib/api';

/**
 * The cafeteria lunch-menu preference (ADR-150). A reversible opt-in: turning it on
 * backfills the currently-known days, turning it off removes the written events. The
 * worker converges the calendar within a couple of minutes; this component only
 * records the choice and reflects it back.
 *
 * `variant="card"` (default) is the standalone look used on the dashboard and the
 * sync step. `variant="field"` drops the surrounding card and heading so it can sit
 * inline among a form's other `.field` entries (e.g. the onboarding profile step),
 * matching how the class/language/group selectors present themselves there.
 */
export function MealMenuCard({ variant = 'card' }: { variant?: 'card' | 'field' } = {}) {
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

  const description =
    'Fakülte yemekhanesinin öğle yemeği menüsünü takvimine (12.30–13.00) ekleyebiliriz. Menü aylık ' +
    'yayımlanır; yayımlandıkça günler otomatik eklenir. İstediğin zaman kapatabilirsin.';

  const control =
    enabled === null && !error ? (
      <p className="loading-note" style={{ marginTop: variant === 'field' ? 0 : 12 }}>Yükleniyor…</p>
    ) : (
      <label
        className="color-customized-toggle"
        htmlFor={checkboxId}
        style={{ marginTop: variant === 'field' ? 0 : 14, fontSize: 14 }}
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
    );

  const status = (
    <>
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
    </>
  );

  if (variant === 'field') {
    // Mirrors the profile form's `.field` layout (label, hint, control) so the
    // preference reads as one more field rather than an unrelated card.
    return (
      <div className="field">
        <span className="field-label">Yemekhane menüsü</span>
        <p className="field-hint" style={{ marginTop: 0, marginBottom: 10 }}>{description}</p>
        {control}
        {status}
      </div>
    );
  }

  return (
    <section className="card card-content">
      <h3 style={{ fontSize: 15 }}>Yemekhane menüsü</h3>
      <p className="muted" style={{ marginTop: 8, fontSize: 14 }}>{description}</p>
      {control}
      {status}
    </section>
  );
}
