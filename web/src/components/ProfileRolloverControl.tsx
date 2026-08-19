'use client';

import { useState } from 'react';
import { AdminSectionTitle } from '@/components/AdminShell';
import { Banner } from '@/components/ui';
import { ApiError, previewProfileRollover, requestProfileRollover } from '@/lib/api';
import type { ProfileRolloverPlan, ProfileRolloverScope, ProgramLanguage } from '@/lib/types';

/**
 * The audited move of a program's stored profiles onto the year its sources now state (ADR-115).
 *
 * The screen exists because of a failure that reported success at every level. A profile is
 * stamped with its program's academic year once, when the student saves it, and nothing restamps
 * it. When the catalog moves a cohort's sources to a new year, deletions still fire — they are
 * driven from the mapping ledger, which never asks about a year — while insertions resolve to
 * nobody, because the cohort query filters profiles on the record's year. The class watches a
 * year of lessons disappear and nothing come back.
 *
 * Two-step for the same reason a calendar repair is: the preview is the backend's plan, and the
 * `planHash` travelling back with the confirmation stops an approved preview from authorizing a
 * move of a different set of students. Editing the scope drops the preview rather than leaving a
 * stale hash attached to a changed form (the ADR-107 pattern).
 */
export function ProfileRolloverControl() {
  const [fromAcademicYear, setFromAcademicYear] = useState('2025-2026');
  const [classYear, setClassYear] = useState(2);
  const [programLanguage, setProgramLanguage] = useState<ProgramLanguage>('Turkish');
  const [plan, setPlan] = useState<ProfileRolloverPlan | null>(null);
  const [reason, setReason] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  const scope: ProfileRolloverScope = {
    fromAcademicYear: fromAcademicYear.trim(),
    classYear,
    programLanguage,
  };

  function editScope(change: () => void) {
    change();
    setPlan(null);
    setNotice(null);
    setError(null);
  }

  async function preview() {
    if (!scope.fromAcademicYear) { setError('Taşınacak akademik yıl zorunludur.'); return; }
    setBusy(true); setError(null); setNotice(null);
    try {
      setPlan(await previewProfileRollover(scope));
    } catch (caught) {
      setPlan(null);
      setError(caught instanceof ApiError ? caught.message : 'Ön izleme alınamadı.');
    } finally { setBusy(false); }
  }

  async function confirm() {
    if (!plan || !reason.trim()) { setError('Denetim kaydı için bir gerekçe yazın.'); return; }
    setBusy(true); setError(null);
    try {
      const result = await requestProfileRollover(scope, plan.planHash, reason.trim());
      if (result.outcome === 'NothingToMove') {
        setNotice('Bu programda taşınacak profil kalmamış. Hiçbir kayıt değiştirilmedi.');
      } else {
        setNotice(
          `${result.profilesMoved} profil ${plan.toAcademicYear} yılına taşındı; `
          + `${result.convergenceRequested} takvim yakınsama için işaretlendi. `
          + 'Dersleri worker sırayla yazar; bu ekran onları beklemez.',
        );
      }
      setPlan(null);
      setReason('');
    } catch (caught) {
      setPlan(null);
      setError(caught instanceof ApiError ? caught.message : 'Taşıma talebi oluşturulamadı.');
    } finally { setBusy(false); }
  }

  // An empty target year is how the backend says the deployed schema does not support this move:
  // it declares no such program, or still states the year being left.
  const unsupported = plan !== null && plan.toAcademicYear === '';
  const nothingToDo = plan !== null && !unsupported && plan.users.length === 0;

  return (
    <section className="card operation-control-card">
      <div className="operation-control-head">
        <div>
          <span className="eyebrow">Denetimli profil işlemi</span>
          <AdminSectionTitle>Akademik yıl taşıması</AdminSectionTitle>
        </div>
      </div>
      <p className="muted">
        Bir öğrencinin profiline akademik yıl yalnızca bir kez, kaydettiği anda damgalanır. Bir
        programın kaynakları yeni yıla taşındığında silmeler çalışır — onlar eşleşme defterinden
        sürülür ve yıl sormaz — ama eklemeler kimseye denk gelmez: kohort sorgusu profilleri kaydın
        yılına göre süzer. Sonuç, her katmanın &ldquo;başarılı&rdquo; dediği boş bir takvimdir. Bu
        ekran, saklanan profilleri kaynakların söylediği yıla taşımanın tek kasıtlı yoludur.
      </p>

      <div className="grid grid-2" style={{ marginTop: 18 }}>
        <div className="field">
          <label htmlFor="rollover-from-year">Taşınacak akademik yıl</label>
          <input
            id="rollover-from-year"
            className="text-input"
            value={fromAcademicYear}
            onChange={(event) => editScope(() => setFromAcademicYear(event.target.value))}
            placeholder="2025-2026"
            autoComplete="off"
          />
          <span className="muted" style={{ fontSize: 12 }}>
            Hedef yıl yazılmaz: sunucudaki şema hangi yılı söylüyorsa o kullanılır.
          </span>
        </div>
        <div className="field">
          <label htmlFor="rollover-class-year">Dönem</label>
          <select
            id="rollover-class-year"
            className="text-input"
            value={classYear}
            onChange={(event) => editScope(() => setClassYear(Number(event.target.value)))}
          >
            {[1, 2, 3, 4, 5, 6].map((year) => <option key={year} value={year}>Dönem {year}</option>)}
          </select>
        </div>
        <div className="field">
          <label htmlFor="rollover-language">Program</label>
          <select
            id="rollover-language"
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

      {unsupported && (
        <Banner tone="danger">
          Sunucudaki profil şeması bu programa hâlâ <strong>{scope.fromAcademicYear}</strong>
          {' '}diyor ya da bu dönem/dil için bir program tanımlamıyor. Önce yeni yılı söyleyen
          şemayı yayına alın; profilleri şemanın vermediği bir yıla damgalamak, kohortu ikiye
          bölerdi.
        </Banner>
      )}

      {plan && !unsupported && <RolloverPlanSummary plan={plan} />}

      {nothingToDo && (
        <Banner tone="info">
          Bu programda <strong>{scope.fromAcademicYear}</strong> yılında kalmış profil yok.
          Taşınacak bir şey bulunamadı.
        </Banner>
      )}

      {plan && !unsupported && !nothingToDo && (
        <div style={{ borderTop: '1px solid var(--border)', paddingTop: 16, marginTop: 16 }}>
          <Banner tone="danger">
            <strong>Bu işlem öğrencilerin kendi girdiği profil verisini değiştirir.</strong> Yalnızca
            akademik yıl ve şema sürümü yazılır; seçicilere ve öğrenci numarasına dokunulmaz.
            Ardından her takvim yakınsama için işaretlenir ve yeni yılın dersleri yazılır.
          </Banner>

          <div className="field" style={{ marginTop: 16 }}>
            <label htmlFor="rollover-reason">Taşıma gerekçesi</label>
            <textarea
              id="rollover-reason"
              className="text-input"
              value={reason}
              onChange={(event) => setReason(event.target.value)}
              placeholder="Bu taşıma neden gerekli? (denetim kaydına yazılır)"
            />
          </div>

          <div className="cluster">
            <button
              className="btn btn-danger"
              type="button"
              disabled={busy || !reason.trim()}
              onClick={() => void confirm()}
            >
              {busy ? 'İşleniyor…' : `${plan.users.length} profili ${plan.toAcademicYear} yılına taşı`}
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
 * What the operator is authorizing: how many profiles move, how many lessons that puts back, and
 * what the move deliberately leaves behind.
 */
function RolloverPlanSummary({ plan }: { plan: ProfileRolloverPlan }) {
  return (
    <div style={{ marginTop: 18 }}>
      <p className="muted" style={{ marginBottom: 12 }}>
        <strong>{plan.scope.fromAcademicYear}</strong> → <strong>{plan.toAcademicYear}</strong>
        {' '}(şema sürümü <code className="mono">{plan.toSchemaVersion}</code>)
      </p>

      <div className="grid grid-2">
        <PlanFigure
          value={plan.users.length}
          label="profil taşınacak"
          hint="Yalnızca akademik yıl ve şema sürümü değişir."
        />
        <PlanFigure
          value={plan.totalGainedEvents}
          label="ders takvimlere yazılacak"
          hint="Yeni yılda yayında olan, öğrenciye ait ama takviminde olmayan dersler."
        />
        <PlanFigure
          value={plan.totalStrandedEvents}
          label="eski yıl kaydı olduğu gibi kalacak"
          hint="Yeni yılda yayında olmadığı için yakınsama bunlara dokunmaz (ADR-089)."
        />
        <PlanFigure
          value={plan.profilesWithoutSyncReadyConnection}
          label="profil takvim bağlantısı olmadan taşınacak"
          hint="Yılı yine de düzelir; bağlandıklarında doğru dersleri alırlar."
        />
      </div>

      {plan.blockedByInvalidSelectors.length > 0 && (
        <Banner tone="danger">
          <strong>{plan.blockedByInvalidSelectors.length} profil taşınmayacak:</strong> seçicileri
          hedef yılın programında geçerli değil. Yeniden damgalansalardı şemanın reddettiği bir
          profil saklanmış olurdu ve öğrenci kendi ayar sayfasında hiç değiştirmediği bir profilin
          reddedildiğini görürdü. Bunlar elle ele alınmalı.
          <div className="table-wrap" style={{ marginTop: 10 }}>
            <ul>
              {plan.blockedByInvalidSelectors.map((userId) => (
                <li key={userId} className="mono">{userId}</li>
              ))}
            </ul>
          </div>
        </Banner>
      )}

      {plan.users.length > 0 && (
        <details style={{ marginTop: 14 }}>
          <summary className="muted" style={{ cursor: 'pointer', fontSize: 13 }}>
            Taşınacak {plan.users.length} profilin dökümü
          </summary>
          <div className="table-wrap" style={{ marginTop: 10 }}>
            <table className="data-table data-table--stack">
              <thead>
                <tr>
                  <th>Kullanıcı</th>
                  <th>Yazılacak ders</th>
                  <th>Kalacak eski kayıt</th>
                  <th>Takvim</th>
                </tr>
              </thead>
              <tbody>
                {plan.users.map((user) => (
                  <tr key={user.userId}>
                    <td className="mono">{user.userId}</td>
                    <td>{user.gainedEventCount}</td>
                    <td>{user.strandedEventCount}</td>
                    <td>{user.convergenceQueueable ? 'İşaretlenecek' : 'Bağlantı yok'}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </details>
      )}

      <p className="muted" style={{ fontSize: 12, marginTop: 10 }}>
        Plan özeti: <code className="mono">{plan.planHash.slice(0, 16)}…</code> — onay bu plana
        bağlanır. Program bu arada değişirse istek reddedilir ve yeniden ön izleme almanız istenir.
      </p>
    </div>
  );
}

function PlanFigure({
  value,
  label,
  hint,
}: {
  value: number;
  label: string;
  hint: string;
}) {
  return (
    <div className="operation-last-change" style={{ display: 'block' }}>
      <p style={{ fontSize: 32, lineHeight: 1.1, margin: 0, fontWeight: 700 }}>{value}</p>
      <strong style={{ display: 'block', fontSize: 13 }}>{label}</strong>
      <span className="muted" style={{ fontSize: 12 }}>{hint}</span>
    </div>
  );
}
