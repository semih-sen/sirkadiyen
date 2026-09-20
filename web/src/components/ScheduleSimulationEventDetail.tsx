'use client';

import { DetailDrawer } from '@/components/AdminData';
import { formatTime } from '@/lib/calendarWeek';
import { dimensionLabel } from '@/lib/selectors';
import type { CohortSimulationEvent } from '@/lib/types';

const EVENT_TYPE_LABELS: Record<string, string> = {
  Theory: 'Teorik ders',
  Practice: 'Uygulama',
  FreeStudy: 'Serbest çalışma',
  AnatomyPractice: 'Anatomi uygulaması',
  BedsidePractice: 'Hasta başı uygulama',
  FacultyPractice: 'Öğretim üyesi uygulaması',
  VerticalCorridor: 'Dikey koridor',
  IntegratedSession: 'Entegre oturum',
  Exam: 'Sınav',
  Other: 'Diğer',
};

const AUDIENCE_SCOPE_LABELS: Record<string, string> = {
  AllStudentsInProgram: 'Programdaki tüm öğrenciler',
  SelectedGroups: 'Seçili gruplar',
};

/** Pretty-prints the parser's evidence when it is JSON; this is display, not schedule parsing. */
function formatEvidence(evidence: string): string {
  try {
    return JSON.stringify(JSON.parse(evidence), null, 2);
  } catch {
    return evidence;
  }
}

function Row({ label, value }: { label: string; value: React.ReactNode }) {
  if (value === null || value === undefined || value === '') return null;
  return (
    <tr>
      <th scope="row" style={{ whiteSpace: 'nowrap', verticalAlign: 'top' }}>{label}</th>
      <td style={{ wordBreak: 'break-word' }}>{value}</td>
    </tr>
  );
}

/**
 * One simulated lesson, from two sides: what a student would see on their calendar, and what the
 * canonical record actually says.
 *
 * The two are shown together because the interesting cases are exactly where they differ — a
 * location the presentation policy withholds, a title it rewrites, an audience the record states
 * that the operator did not expect.
 */
export function ScheduleSimulationEventDetail({
  event,
  onClose,
}: {
  event: CohortSimulationEvent;
  onClose: () => void;
}) {
  const { raw } = event;
  const timeRange = event.isAllDay || !event.startLocalTime || !event.endLocalTime
    ? 'Tüm gün'
    : `${formatTime(event.startLocalTime)} – ${formatTime(event.endLocalTime)}`;

  return (
    <DetailDrawer title={event.summary} subtitle={`${event.localDate} · ${timeRange}`} onClose={onClose}>
      {raw.sharesStableIdentity && (
        <div className="error" role="alert" style={{ marginBottom: 16 }}>
          Bu hafta aynı <code>stableIdentity</code> değerini taşıyan başka bir ders daha var.
          Takvimdeki etkinlik kimliği yalnız bu değerden türediği için ikisi tek etkinliğe düşer.
        </div>
      )}

      <section>
        <h3 className="eyebrow">Takvimde böyle görünür</h3>
        <div className="cluster" style={{ gap: 8, marginTop: 8, alignItems: 'center' }}>
          <span
            aria-hidden="true"
            style={{
              width: 14,
              height: 14,
              borderRadius: 4,
              background: event.label.backgroundColor,
              display: 'inline-block',
            }}
          />
          <span>{event.label.name}</span>
          <span className="muted">{event.label.backgroundColor}</span>
        </div>
        <table className="data-table" style={{ marginTop: 12 }}>
          <tbody>
            <Row label="Başlık" value={event.summary} />
            <Row label="Konum" value={event.location ?? <span className="muted">Gösterilmiyor</span>} />
            <Row
              label="Açıklama"
              value={
                event.description
                  ? <span style={{ whiteSpace: 'pre-line' }}>{event.description}</span>
                  : null
              }
            />
            <Row label="Saat" value={timeRange} />
            <Row label="Saat dilimi" value={event.timeZoneId} />
          </tbody>
        </table>
      </section>

      <section style={{ marginTop: 24 }}>
        <h3 className="eyebrow">Kanonik kayıt (ham)</h3>
        <table className="data-table" style={{ marginTop: 8 }}>
          <tbody>
            <Row label="Kaynak" value={<code>{raw.sourceId}</code>} />
            <Row label="Ders türü" value={EVENT_TYPE_LABELS[raw.eventType] ?? raw.eventType} />
            <Row label="Kayıt durumu" value={raw.recordStatus} />
            <Row
              label="Kitle kapsamı"
              value={AUDIENCE_SCOPE_LABELS[raw.audienceScope] ?? raw.audienceScope}
            />
            <Row
              label="Kitle seçicileri"
              value={
                raw.audienceSelectors.length === 0
                  ? <span className="muted">Belirtilmemiş</span>
                  : (
                    <ul style={{ margin: 0, paddingLeft: 18 }}>
                      {raw.audienceSelectors.map((selector, index) => (
                        <li key={`${selector.dimension}:${selector.value}:${index}`}>
                          {dimensionLabel(selector.dimension)}: <strong>{selector.value}</strong>
                        </li>
                      ))}
                    </ul>
                  )
              }
            />
            <Row label="Kaynaktaki başlık" value={raw.displayTitle} />
            <Row label="Normalleştirilmiş ders" value={raw.normalizedCourseIdentity} />
            <Row label="Öğretim üyesi" value={raw.instructor} />
            {/*
              Shown separately from the calendar location above: the presentation policy withholds
              a pointer such as "amfi programına bakınız", and an operator needs to see that the
              source really did say it rather than concluding the location went missing.
            */}
            <Row
              label="Kaynaktaki konum"
              value={raw.rawLocation ?? <span className="muted">Yok</span>}
            />
            <Row label="Dilim" value={raw.curriculumBlock} />
            <Row
              label="Anabilim dalları"
              value={raw.departments.length > 0 ? raw.departments.join(', ') : null}
            />
            <Row label="Karşılaştırılabilir AD" value={raw.comparableDepartment} />
            <Row
              label="Konu"
              value={raw.notes ? <span style={{ whiteSpace: 'pre-line' }}>{raw.notes}</span> : null}
            />
            <Row label="Güven" value={raw.confidence} />
            <Row label="stableIdentity" value={<code>{raw.stableIdentity}</code>} />
            <Row label="contentHash" value={<code>{raw.contentHash}</code>} />
            <Row label="candidateId" value={<code>{raw.candidateId}</code>} />
            <Row label="Kayıt kimliği" value={<code>{raw.canonicalRecordId}</code>} />
            <Row label="Revizyon kimliği" value={<code>{raw.scheduleRevisionId}</code>} />
          </tbody>
        </table>
      </section>

      <section style={{ marginTop: 24 }}>
        <h3 className="eyebrow">Kanıt</h3>
        <pre className="week-detail__evidence">{formatEvidence(raw.evidence)}</pre>
      </section>
    </DetailDrawer>
  );
}
