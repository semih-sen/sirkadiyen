'use client';

import { useState } from 'react';
import { AdminSectionTitle } from '@/components/AdminShell';
import { Banner } from '@/components/ui';
import { ApiError, previewCalendarRepair, requestCalendarRepair } from '@/lib/api';
import type { CohortRepairPlan, CohortRepairScope, ProgramLanguage } from '@/lib/types';

/**
 * The audited repair of one program's calendars (ADR-111).
 *
 * The screen exists because the correction of an audience rule does not undo what the old rule
 * wrote: those events are still published and still mapped, so no diff mentions them and the
 * periodic inventory pass deliberately never deletes from absence. This is the only deliberate
 * way to remove them.
 *
 * It is a two-step control on purpose. The preview is the plan the backend computed, and the
 * `planHash` travelling back with the confirmation is what stops an approved preview from
 * authorizing a repair of a different set of students. Any edit to the scope drops the preview
 * rather than leaving a stale hash attached to a changed form (the ADR-107 pattern).
 */
export function CalendarRepairControl() {
  const [academicYear, setAcademicYear] = useState('2026-2027');
  const [classYear, setClassYear] = useState(3);
  const [programLanguage, setProgramLanguage] = useState<ProgramLanguage>('Turkish');
  const [plan, setPlan] = useState<CohortRepairPlan | null>(null);
  const [reason, setReason] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  const scope: CohortRepairScope = { academicYear: academicYear.trim(), classYear, programLanguage };

  // A previewed plan belongs to the scope it was computed for. Editing any part of that scope
  // must invalidate it, or the operator could confirm a hash for a cohort they are no longer
  // looking at.
  function editScope(change: () => void) {
    change();
    setPlan(null);
    setNotice(null);
    setError(null);
  }

  async function preview() {
    if (!scope.academicYear) { setError('Akademik yıl zorunludur.'); return; }
    setBusy(true); setError(null); setNotice(null);
    try {
      setPlan(await previewCalendarRepair(scope));
    } catch (caught) {
      setPlan(null);
      setError(caught instanceof ApiError ? caught.message : 'Ön izleme alınamadı.');
    } finally { setBusy(false); }
  }

  async function confirm() {
    if (!plan || !reason.trim()) { setError('Denetim kaydı için bir gerekçe yazın.'); return; }
    setBusy(true); setError(null);
    try {
      const result = await requestCalendarRepair(scope, plan.planHash, reason.trim());
      if (result.outcome === 'NothingToRepair') {
        setNotice('Bu hatta düzeltilecek bir şey kalmamış. Hiçbir bağlantı işaretlenmedi.');
      } else {
        setNotice(
          `${result.usersRequested} öğrencinin takvimi yakınsama için işaretlendi. `
          + 'Silme ve yazma işlemlerini worker sırayla yapar; bu ekran onları beklemez.',
        );
      }
      setPlan(null);
      setReason('');
    } catch (caught) {
      // A 409 here is the plan-hash guard doing its job: the cohort moved between preview and
      // confirmation, so the operator has not seen what they would be authorizing.
      setPlan(null);
      setError(caught instanceof ApiError ? caught.message : 'Düzeltme talebi oluşturulamadı.');
    } finally { setBusy(false); }
  }

  const nothingToDo = plan !== null && plan.users.length === 0;

  return (
    <section className="card operation-control-card">
      <div className="operation-control-head">
        <div>
          <span className="eyebrow">Denetimli onarım işlemi</span>
          <AdminSectionTitle>Takvim düzeltmesi</AdminSectionTitle>
        </div>
      </div>
      <p className="muted">
        Bir hedef kitle kuralı düzeltildiğinde, eski kuralın yazdığı etkinlikler takvimlerde kalır:
        kayıtlar değişmediği için hiçbir diff onlardan söz etmez ve periyodik envanter taraması
        bilerek yokluktan silmez. Bu ekran, o artıkları kaldırmanın tek kasıtlı yoludur.
      </p>

      <div className="grid grid-2" style={{ marginTop: 18 }}>
        <div className="field">
          <label htmlFor="repair-academic-year">Akademik yıl</label>
          <input
            id="repair-academic-year"
            className="text-input"
            value={academicYear}
            onChange={(event) => editScope(() => setAcademicYear(event.target.value))}
            placeholder="2026-2027"
            autoComplete="off"
          />
        </div>
        <div className="field">
          <label htmlFor="repair-class-year">Dönem</label>
          <select
            id="repair-class-year"
            className="text-input"
            value={classYear}
            onChange={(event) => editScope(() => setClassYear(Number(event.target.value)))}
          >
            {[1, 2, 3, 4, 5, 6].map((year) => <option key={year} value={year}>Dönem {year}</option>)}
          </select>
        </div>
        <div className="field">
          <label htmlFor="repair-language">Program</label>
          <select
            id="repair-language"
            className="text-input"
            value={programLanguage}
            onChange={(event) =>
              editScope(() => setProgramLanguage(event.target.value as ProgramLanguage))}
          >
            <option value="Turkish">Türkçe</option>
            <option value="English">İngilizce</option>
          </select>
        </div>
      </div>

      <div className="cluster" style={{ marginTop: 4 }}>
        <button
          className="btn btn-secondary"
          type="button"
          disabled={busy}
          onClick={() => void preview()}
        >
          {busy && !plan ? 'Hesaplanıyor…' : 'Ön izleme al'}
        </button>
      </div>

      {plan && <RepairPlanSummary plan={plan} />}

      {nothingToDo && (
        <Banner tone="info">
          Bu hattaki <strong>{plan.cohortUserCount}</strong> öğrencinin takvimi zaten yayınlanmış
          programla uyumlu. Yakınsanacak bir şey yok.
        </Banner>
      )}

      {plan && !nothingToDo && (
        <div style={{ borderTop: '1px solid var(--border)', paddingTop: 16, marginTop: 16 }}>
          <Banner tone="danger">
            <strong>Bu işlem takvimlerden etkinlik siler.</strong> Silmeleri yayın kararına bağlı
            olan yakınsama adımı yapar: hâlâ yayında olup öğrenciye ait olmayan etkinlikler
            kaldırılır, eksik olanlar yazılır. Yayından kalkmış kayıtlara dokunulmaz.
          </Banner>

          <div className="field" style={{ marginTop: 16 }}>
            <label htmlFor="repair-reason">Düzeltme gerekçesi</label>
            <textarea
              id="repair-reason"
              className="text-input"
              value={reason}
              onChange={(event) => setReason(event.target.value)}
              placeholder="Bu düzeltme neden gerekli? (denetim kaydına yazılır)"
            />
          </div>

          <div className="cluster">
            <button
              className="btn btn-danger"
              type="button"
              disabled={busy || !reason.trim()}
              onClick={() => void confirm()}
            >
              {busy ? 'İşleniyor…' : `${plan.users.length} takvimi düzelt`}
            </button>
            <button
              className="btn btn-tertiary"
              type="button"
              disabled={busy}
              onClick={() => { setPlan(null); setReason(''); }}
            >
              Vazgeç
            </button>
          </div>
        </div>
      )}

      {notice && <Banner tone="info">{notice}</Banner>}
      {error && <div className="error" role="alert">{error}</div>}
    </section>
  );
}

/**
 * What the operator is authorizing, in the terms the backend planned it: how many events go, how
 * many arrive, and how many rows are deliberately being left alone.
 */
function RepairPlanSummary({ plan }: { plan: CohortRepairPlan }) {
  return (
    <div style={{ marginTop: 18 }}>
      <div className="grid grid-2">
        <PlanFigure
          value={plan.totalSurplusEvents}
          label="etkinlik silinecek"
          hint="Hâlâ yayında, ama artık o öğrenciye ait değil."
          tone={plan.totalSurplusEvents > 0 ? 'danger' : undefined}
        />
        <PlanFigure
          value={plan.totalMissingEvents}
          label="etkinlik yazılacak"
          hint="Öğrenciye ait, ama takviminde yok."
        />
        <PlanFigure
          value={plan.users.length}
          label={`öğrenci etkilenecek (hattaki ${plan.cohortUserCount} kişiden)`}
          hint="Yakınsanacak bir şeyi olmayan öğrenciler işaretlenmez."
        />
        <PlanFigure
          value={plan.totalUntouchableRetired}
          label="kayıt olduğu gibi bırakılacak"
          hint="Dersi artık yayında değil. Yokluktan silmek yasak (ADR-089); yalnızca raporlanır."
        />
      </div>

      {plan.users.length > 0 && (
        <details style={{ marginTop: 14 }}>
          <summary className="muted" style={{ cursor: 'pointer', fontSize: 13 }}>
            Etkilenen {plan.users.length} öğrencinin dökümü
          </summary>
          <div className="table-wrap" style={{ marginTop: 10 }}>
            <table className="data-table data-table--stack">
              <thead>
                <tr>
                  <th>Kullanıcı</th>
                  <th>Silinecek</th>
                  <th>Yazılacak</th>
                  <th>Dokunulmayan</th>
                </tr>
              </thead>
              <tbody>
                {plan.users.map((user) => (
                  <tr key={user.userId}>
                    <td className="mono">{user.userId}</td>
                    <td>{user.surplusEventCount}</td>
                    <td>{user.missingEventCount}</td>
                    <td>{user.untouchableRetiredCount}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </details>
      )}

      <p className="muted" style={{ fontSize: 12, marginTop: 10 }}>
        Plan özeti: <code className="mono">{plan.planHash.slice(0, 16)}…</code> — onay bu plana
        bağlanır. Hat bu arada değişirse istek reddedilir ve yeniden ön izleme almanız istenir.
      </p>
    </div>
  );
}

function PlanFigure({
  value,
  label,
  hint,
  tone,
}: {
  value: number;
  label: string;
  hint: string;
  tone?: 'danger';
}) {
  return (
    <div className="operation-last-change" style={{ display: 'block' }}>
      <p
        style={{
          fontSize: 32,
          lineHeight: 1.1,
          margin: 0,
          fontWeight: 700,
          color: tone === 'danger' && value > 0 ? 'var(--danger)' : undefined,
        }}
      >
        {value}
      </p>
      <strong style={{ display: 'block', fontSize: 13 }}>{label}</strong>
      <span className="muted" style={{ fontSize: 12 }}>{hint}</span>
    </div>
  );
}
