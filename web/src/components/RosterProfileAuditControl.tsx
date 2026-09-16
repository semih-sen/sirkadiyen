'use client';

import { useState } from 'react';
import { AdminSectionTitle } from '@/components/AdminShell';
import { Banner } from '@/components/ui';
import { ApiError, previewRosterProfileAudit, requestRosterProfileAudit } from '@/lib/api';
import type {
  ProgramLanguage,
  RosterProfileAuditPlan,
  RosterProfileAuditScope,
} from '@/lib/types';

/**
 * The audited, one-time check of whether a cohort's students entered their cohort correctly, and
 * the correction of the ones who did not, against the published faculty lists (ADR-159).
 *
 * The screen exists because of a dated gap. A dimension the lists did not state at onboarding was
 * entered by the student by hand: the Grade 3 Turkish faculty-practice group had no roster column
 * until the faculty published one, so everyone who onboarded before it chose their group
 * themselves, and some chose wrong. Now that the column exists, the authoritative value can be
 * compared with what each student entered and the disagreements put right — through the student's
 * own write path, so the calendar converges on the corrected audience exactly as it would after the
 * student's own edit.
 *
 * Two-step for the same reason a rollover is: the preview is the backend's plan, and the `planHash`
 * travelling back with the confirmation stops an approved preview from authorizing a correction of
 * a different set of students, or to different values. Editing the scope drops the preview rather
 * than leaving a stale hash attached to a changed form (the ADR-107 pattern).
 */
export function RosterProfileAuditControl() {
  const [classYear, setClassYear] = useState(3);
  const [programLanguage, setProgramLanguage] = useState<ProgramLanguage>('Turkish');
  const [plan, setPlan] = useState<RosterProfileAuditPlan | null>(null);
  const [reason, setReason] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  const scope: RosterProfileAuditScope = { classYear, programLanguage };

  function editScope(change: () => void) {
    change();
    setPlan(null);
    setNotice(null);
    setError(null);
  }

  async function preview() {
    setBusy(true); setError(null); setNotice(null);
    try {
      setPlan(await previewRosterProfileAudit(scope));
    } catch (caught) {
      setPlan(null);
      setError(caught instanceof ApiError ? caught.message : 'Ön izleme alınamadı.');
    } finally { setBusy(false); }
  }

  async function confirm() {
    if (!plan || !reason.trim()) { setError('Denetim kaydı için bir gerekçe yazın.'); return; }
    setBusy(true); setError(null);
    try {
      const result = await requestRosterProfileAudit(scope, plan.planHash, reason.trim());
      if (result.outcome === 'NothingToCorrect') {
        setNotice('Bu kohortta düzeltilecek profil kalmamış. Hiçbir kayıt değiştirilmedi.');
      } else {
        const skipped = result.profilesSkipped > 0
          ? ` ${result.profilesSkipped} profil atlandı (hesabı aktif değil ya da seçicileri artık şemaya uymuyor).`
          : '';
        setNotice(
          `${result.profilesCorrected} profil düzeltildi; `
          + `${result.calendarResyncRequested} takvim yakınsama için işaretlendi.${skipped} `
          + 'Dersleri worker sırayla yazar; bu ekran onları beklemez.',
        );
      }
      setPlan(null);
      setReason('');
    } catch (caught) {
      setPlan(null);
      setError(caught instanceof ApiError ? caught.message : 'Düzeltme talebi oluşturulamadı.');
    } finally { setBusy(false); }
  }

  // An empty academic year is how the backend says the deployed schema declares no program for the
  // scope, so there is nothing to check against the lists.
  const unsupported = plan !== null && plan.academicYear === '';
  const nothingToDo = plan !== null && !unsupported && plan.users.length === 0;

  return (
    <section className="card operation-control-card">
      <div className="operation-control-head">
        <div>
          <span className="eyebrow">Denetimli profil işlemi</span>
          <AdminSectionTitle>Öğretim üyesi grubu denetimi</AdminSectionTitle>
        </div>
      </div>
      <p className="muted">
        Onboarding sırasında listelerin söylemediği bir seçiciyi öğrenci elle girer. Dönem 3 Türkçe
        öğretim üyesi (faculty-practice) grubunun roster'da bir sütunu yoktu; fakülte yayımlayana
        kadar herkes grubunu kendisi seçti ve bazıları yanlış seçti. Sütun artık var, bu ekran her
        öğrencinin girdiği değeri yayımlanan listeyle karşılaştırır ve uyuşmayanları öğrencinin
        kendi yazma yolundan düzeltir. Tek seferlik bir onarımdır; steady state'te düzeltecek bir
        şey bulmaz.
      </p>

      <div className="grid grid-2" style={{ marginTop: 18 }}>
        <div className="field">
          <label htmlFor="roster-audit-class-year">Dönem</label>
          <select
            id="roster-audit-class-year"
            className="text-input"
            value={classYear}
            onChange={(event) => editScope(() => setClassYear(Number(event.target.value)))}
          >
            {[1, 2, 3, 4, 5, 6].map((year) => <option key={year} value={year}>Dönem {year}</option>)}
          </select>
        </div>
        <div className="field">
          <label htmlFor="roster-audit-language">Program</label>
          <select
            id="roster-audit-language"
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
          Sunucudaki profil şeması bu dönem/dil için bir program tanımlamıyor, dolayısıyla listelerle
          karşılaştırılacak bir kohort yok.
        </Banner>
      )}

      {plan && !unsupported && <AuditPlanSummary plan={plan} />}

      {nothingToDo && (
        <Banner tone="info">
          Bu kohortta yayımlanan listeyle çelişen profil bulunamadı. İncelenen{' '}
          <strong>{plan?.profilesExamined}</strong> profilin{' '}
          <strong>{plan?.profilesInAgreement}</strong> tanesi listeyle uyumlu.
        </Banner>
      )}

      {plan && !unsupported && !nothingToDo && (
        <div style={{ borderTop: '1px solid var(--border)', paddingTop: 16, marginTop: 16 }}>
          <Banner tone="danger">
            <strong>Bu işlem öğrencilerin kendi girdiği profil verisini değiştirir.</strong> Yalnızca
            listenin söylediği seçiciler, listenin söylediği değere getirilir; öğrenci numarasına ve
            listenin belirtmediği seçicilere dokunulmaz. Ardından etkilenen takvimler yakınsama için
            işaretlenir.
          </Banner>

          <div className="field" style={{ marginTop: 16 }}>
            <label htmlFor="roster-audit-reason">Düzeltme gerekçesi</label>
            <textarea
              id="roster-audit-reason"
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
              {busy ? 'İşleniyor…' : `${plan.users.length} profili düzelt`}
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
 * What the operator is authorizing: how many profiles were examined, how many already agree, and
 * exactly which students would be corrected from what to what.
 */
function AuditPlanSummary({ plan }: { plan: RosterProfileAuditPlan }) {
  return (
    <div style={{ marginTop: 18 }}>
      <p className="muted" style={{ marginBottom: 12 }}>
        <strong>Dönem {plan.scope.classYear}</strong>{' '}
        {plan.scope.programLanguage === 'Turkish' ? 'Türkçe' : 'İngilizce'} —{' '}
        <strong>{plan.academicYear}</strong> (şema sürümü{' '}
        <code className="mono">{plan.schemaVersion}</code>)
      </p>

      <div className="grid grid-2">
        <PlanFigure
          value={plan.profilesExamined}
          label="profil incelendi"
          hint="Bu kohortta kayıtlı tüm profiller."
        />
        <PlanFigure
          value={plan.profilesInAgreement}
          label="profil zaten doğru"
          hint="Listenin söylediği her değeri doğru girmiş öğrenciler."
        />
        <PlanFigure
          value={plan.users.length}
          label="profil düzeltilecek"
          hint="Listeyle çelişen ya da listenin yeni belirttiği bir değeri eksik olan profiller."
        />
        <PlanFigure
          value={plan.unresolvedByRoster.length}
          label="profil listede bulunamadı"
          hint="Numarası hiçbir listeye denk gelmeyen ya da çelişen; elle bakılmalı, tahminle düzeltilmez."
        />
      </div>

      {plan.unreadableRosterIds.length > 0 && (
        <Banner tone="warning">
          <strong>Şu listeler bu sırada okunamadı:</strong>{' '}
          {plan.unreadableRosterIds.join(', ')}. Bunların doğrulayacağı hiçbir değer &ldquo;düzeltilmedi&rdquo;;
          öğrencinin girdiği değer olduğu gibi bırakıldı. Listeler yeniden okunabildiğinde ön izlemeyi
          tekrar alın.
        </Banner>
      )}

      {plan.users.length > 0 && (
        <details style={{ marginTop: 14 }} open>
          <summary className="muted" style={{ cursor: 'pointer', fontSize: 13 }}>
            Düzeltilecek {plan.users.length} profilin dökümü ({plan.totalCorrections} değişiklik)
          </summary>
          <div className="table-wrap" style={{ marginTop: 10 }}>
            <table className="data-table data-table--stack">
              <thead>
                <tr>
                  <th>Kullanıcı</th>
                  <th>Seçici</th>
                  <th>Girilen</th>
                  <th>Listedeki</th>
                </tr>
              </thead>
              <tbody>
                {plan.users.flatMap((user) =>
                  user.corrections.map((correction) => (
                    <tr key={`${user.userId}:${correction.dimension}`}>
                      <td className="mono" data-label="Kullanıcı">{user.userId}</td>
                      <td data-label="Seçici">{correction.dimension}</td>
                      <td data-label="Girilen">
                        {correction.storedValue
                          ? <code className="mono">{correction.storedValue}</code>
                          : <span className="muted">— (boş)</span>}
                      </td>
                      <td data-label="Listedeki"><code className="mono">{correction.rosterValue}</code></td>
                    </tr>
                  )),
                )}
              </tbody>
            </table>
          </div>
        </details>
      )}

      <p className="muted" style={{ fontSize: 12, marginTop: 10 }}>
        Plan özeti: <code className="mono">{plan.planHash.slice(0, 16)}…</code> — onay bu plana
        bağlanır. Kohort ya da listeler bu arada değişirse istek reddedilir ve yeniden ön izleme
        almanız istenir.
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
