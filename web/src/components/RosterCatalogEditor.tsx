'use client';

import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import {
  ApiError,
  applyRosterCatalog,
  getRosterCatalog,
  getRosterCatalogRevision,
  listRosterCatalogRevisions,
  previewRosterCatalog,
} from '@/lib/api';
import { LoadState, Tabs, formatDateTime } from '@/components/AdminData';
import { Banner } from '@/components/ui';
import type {
  StudentRosterCatalogDimensionColumn,
  StudentRosterCatalogDocument,
  StudentRosterCatalogEntry,
  StudentRosterCatalogFile,
  StudentRosterCatalogPlan,
  StudentRosterCatalogRevisionSummary,
  StudentRosterCatalogRosterChange,
} from '@/lib/types';

/**
 * The administrative editor for the student roster catalog document (ADR-134).
 *
 * The catalog states which published student list belongs to which cohort and what each of its
 * columns means, so an edit here decides what a student's profile is filled in with during
 * onboarding. Two kinds of mistake are possible and they are not alike: a wrong header makes the
 * whole list unreadable and says so at the next lookup, while a wrong value map keeps working and
 * quietly enrols a cohort in another group's practicals. The screen is therefore built around the
 * backend's plan rather than around the text box — nothing is written until a server-computed
 * change plan has been previewed and confirmed with a reason — and the value maps are rendered in
 * full in that plan rather than summarized.
 *
 * Two editors, one document, exactly as the source catalog has: the form is the safe path for
 * ordinary corrections, and the raw JSON editor exists because a broken catalog has to be
 * repairable from here rather than from a server shell.
 */
export function RosterCatalogEditor() {
  const [document, setDocument] = useState<StudentRosterCatalogDocument | null>(null);
  const [draft, setDraft] = useState('');
  const [mode, setMode] = useState('form');
  const [plan, setPlan] = useState<StudentRosterCatalogPlan | null>(null);
  const [reason, setReason] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  const load = useCallback(async () => {
    setError(null);
    setNotice(null);
    setPlan(null);
    try {
      const loaded = await getRosterCatalog();
      setDocument(loaded);
      setDraft(loaded.content);
    } catch (caught) {
      setError(caught instanceof ApiError ? caught.message : 'Katalog dosyası okunamadı.');
    }
  }, []);

  useEffect(() => { void load(); }, [load]);

  // A plan belongs to the exact text it was computed for. Any edit drops it rather than leaving a
  // stale hash attached to a document the operator has since changed.
  const edit = useCallback((next: string) => {
    setDraft(next);
    setPlan(null);
    setNotice(null);
  }, []);

  const parsed = useMemo(() => parseCatalog(draft), [draft]);
  const dirty = document !== null && draft !== document.content;

  async function preview() {
    if (!document) return;
    setBusy(true);
    setError(null);
    setNotice(null);
    try {
      setPlan(await previewRosterCatalog(draft, document.contentHash));
    } catch (caught) {
      setPlan(null);
      setError(caught instanceof ApiError ? caught.message : 'Ön izleme alınamadı.');
    } finally {
      setBusy(false);
    }
  }

  async function apply() {
    if (!document || !plan || !reason.trim()) {
      setError('Denetim kaydı için bir gerekçe yazın.');
      return;
    }
    setBusy(true);
    setError(null);
    try {
      const result = await applyRosterCatalog(
        draft,
        document.contentHash,
        plan.planHash,
        reason.trim(),
      );
      setReason('');
      setPlan(null);
      await load();
      setNotice(
        `Katalog güncellendi. ${result.plan.rosterCount} liste yapılandırıldı`
        + (result.readingInvalidated
          ? '; listelerin bellekteki okuması düşürüldü, bir sonraki öğrenci araması belgeleri '
            + 'yeniden okuyacak'
          : '')
        + '. Değişiklik kalıcı sürüm geçmişine ve denetim kaydına işlendi. Daha önce '
        + 'kaydedilmiş öğrenci profilleri değişmez.',
      );
    } catch (caught) {
      // A 409 here is the concurrency guard: the file moved between preview and confirmation, so
      // the operator has not seen what they would be authorizing.
      setPlan(null);
      setError(caught instanceof ApiError ? caught.message : 'Katalog uygulanamadı.');
    } finally {
      setBusy(false);
    }
  }

  return (
    <section className="card admin-workspace-card">
      <LoadState loading={document === null && !error} error={document === null ? error : null} onRetry={() => void load()} />
      {document && (
        <>
          <CatalogHeader document={document} dirty={dirty} onReload={() => void load()} busy={busy} />

          {!document.isWritable && (
            <Banner tone="warning">
              Katalog dosyası bu sunucuda yazılabilir değil (<span className="mono">{document.path}</span>).
              Düzenlemeyi kaydedebilmek için servis biriminin bu dizine yazma izni olmalı.
            </Banner>
          )}

          {!document.isValid && (
            <Banner tone="danger">
              <strong>Diskteki katalog geçerli değil.</strong> Öğrenci numarasıyla arama bu
              dosyayla çalışmaz.
              {document.validationError ? ` Sebep: ${document.validationError}` : ''}
            </Banner>
          )}

          <Tabs
            value={mode}
            onChange={setMode}
            items={[
              { value: 'form', label: 'Liste düzenleyici' },
              { value: 'json', label: 'JSON' },
              { value: 'history', label: 'Sürüm geçmişi' },
            ]}
          />

          {mode === 'form' && (
            parsed.catalog
              ? <RosterFormEditor catalog={parsed.catalog} onChange={(next) => edit(serialize(next))} />
              : (
                <Banner tone="warning">
                  Belge JSON olarak ayrıştırılamadığı için form düzenleyici kapalı. JSON sekmesinden
                  düzeltebilirsiniz. {parsed.error}
                </Banner>
              )
          )}

          {mode === 'json' && (
            <RawJsonEditor
              value={draft}
              parseError={parsed.error}
              onChange={edit}
              onFormat={() => parsed.catalog && edit(serialize(parsed.catalog))}
              onReset={() => edit(document.content)}
            />
          )}

          {mode === 'history' && <RevisionHistory onRestore={(content) => { edit(content); setMode('json'); }} />}

          {mode !== 'history' && (
            <ChangeReview
              dirty={dirty}
              busy={busy}
              plan={plan}
              reason={reason}
              canWrite={document.isWritable}
              onReason={setReason}
              onPreview={() => void preview()}
              onApply={() => void apply()}
              onDiscard={() => { edit(document.content); setReason(''); }}
            />
          )}

          {notice && <Banner tone="info">{notice}</Banner>}
          {error && document && <div className="error" role="alert">{error}</div>}
        </>
      )}
    </section>
  );
}

function CatalogHeader({
  document,
  dirty,
  busy,
  onReload,
}: {
  document: StudentRosterCatalogDocument;
  dirty: boolean;
  busy: boolean;
  onReload: () => void;
}) {
  return (
    <div className="catalog-header">
      <div>
        <span className="eyebrow">Sunucudaki dosya</span>
        <p className="mono catalog-path">{document.path}</p>
        <p className="muted catalog-meta">
          {document.rosterCount ?? '—'} liste · sürüm {document.catalogVersion ?? '—'} ·
          {' '}son değişiklik {formatDateTime(document.lastModifiedUtc)} ·
          {' '}<span className="mono">{document.contentHash.slice(0, 12)}…</span>
        </p>
      </div>
      <div className="cluster">
        {dirty && <span className="badge badge-warning">Kaydedilmemiş değişiklik</span>}
        {!dirty && document.isValid && <span className="badge badge-success">Diskle aynı</span>}
        <button className="btn btn-tertiary btn-sm" type="button" disabled={busy} onClick={onReload}>
          Dosyayı yeniden oku
        </button>
      </div>
    </div>
  );
}

// --- Form editor ------------------------------------------------------------

/** The fields an edit to which changes what a student's profile is filled in with. */
const HIGH_RISK_FIELDS = new Set<keyof StudentRosterCatalogEntry>([
  'rosterId',
  'transport',
  'documentFormat',
  'sourceUri',
  'externalId',
  'sheetGid',
  'academicYear',
  'classYear',
  'programLanguage',
]);

function RosterFormEditor({
  catalog,
  onChange,
}: {
  catalog: StudentRosterCatalogFile;
  onChange: (next: StudentRosterCatalogFile) => void;
}) {
  const [query, setQuery] = useState('');
  const [openId, setOpenId] = useState<string | null>(null);

  const rosters = catalog.rosters ?? [];
  const filtered = rosters.filter((roster) => matches(roster, query));

  function replace(index: number, next: StudentRosterCatalogEntry) {
    const copy = [...rosters];
    copy[index] = next;
    onChange({ ...catalog, rosters: copy });
  }

  function removeAt(index: number) {
    onChange({ ...catalog, rosters: rosters.filter((_, position) => position !== index) });
  }

  function add() {
    const created = blankRoster(rosters.length + 1);
    onChange({ ...catalog, rosters: [...rosters, created] });
    setQuery('');
    setOpenId(created.rosterId);
  }

  return (
    <div className="catalog-form">
      <div className="catalog-toolbar">
        <input
          className="text-input"
          value={query}
          placeholder="Liste ara (kimlik, ad, akademik yıl)"
          aria-label="Liste ara"
          onChange={(event) => setQuery(event.target.value)}
        />
        <button className="btn btn-secondary btn-sm" type="button" onClick={add}>
          + Yeni liste
        </button>
      </div>

      {filtered.length === 0 && <p className="muted">Aramayla eşleşen liste yok.</p>}

      <div className="catalog-source-list">
        {filtered.map((roster) => {
          const index = rosters.indexOf(roster);
          const open = openId === roster.rosterId;
          return (
            <article className={`catalog-source${open ? ' catalog-source--open' : ''}`} key={`${roster.rosterId}-${index}`}>
              <button
                className="catalog-source-head"
                type="button"
                aria-expanded={open}
                onClick={() => setOpenId(open ? null : roster.rosterId)}
              >
                <span>
                  <strong>{roster.displayName || '(adsız liste)'}</strong>
                  <small className="mono muted">{roster.rosterId}</small>
                </span>
                <span className="cluster">
                  <span className="badge badge-neutral">Dönem {roster.classYear} · {roster.programLanguage}</span>
                  <span className="badge">{roster.academicYear}</span>
                  <span aria-hidden="true">{open ? '▲' : '▼'}</span>
                </span>
              </button>

              {open && (
                <div className="catalog-source-body">
                  <div className="grid grid-2">
                    <Field label="Liste kimliği" name="rosterId" roster={roster} onChange={(next) => replace(index, next)} />
                    <Field label="Görünen ad" name="displayName" roster={roster} onChange={(next) => replace(index, next)} />
                    <Select
                      label="Taşıma"
                      name="transport"
                      options={['googleSheets', 'googleDriveFile', 'httpFile']}
                      roster={roster}
                      onChange={(next) => replace(index, next)}
                    />
                    <Select
                      label="Belge biçimi"
                      name="documentFormat"
                      options={['googleSheet', 'xlsx', 'docx']}
                      roster={roster}
                      onChange={(next) => replace(index, next)}
                    />
                    <Field label="Kaynak URI" name="sourceUri" roster={roster} onChange={(next) => replace(index, next)} wide />
                    <Field label="Dış kimlik" name="externalId" roster={roster} onChange={(next) => replace(index, next)} />
                    <Field label="Sayfa gid" name="sheetGid" roster={roster} onChange={(next) => replace(index, next)} numeric />
                    <Field label="Akademik yıl" name="academicYear" roster={roster} onChange={(next) => replace(index, next)} />
                    <Select
                      label="Dönem"
                      name="classYear"
                      options={['1', '2', '3', '4', '5', '6']}
                      numeric
                      roster={roster}
                      onChange={(next) => replace(index, next)}
                    />
                    <Select
                      label="Program dili"
                      name="programLanguage"
                      options={['turkish', 'english']}
                      roster={roster}
                      onChange={(next) => replace(index, next)}
                    />
                  </div>

                  <LayoutEditor roster={roster} onChange={(next) => replace(index, next)} />

                  <div className="field">
                    <label htmlFor={`roster-notes-${index}`}>Notlar</label>
                    <textarea
                      id={`roster-notes-${index}`}
                      className="text-input"
                      value={roster.notes ?? ''}
                      onChange={(event) => replace(index, {
                        ...roster,
                        notes: event.target.value === '' ? null : event.target.value,
                      })}
                    />
                  </div>

                  <div className="cluster catalog-source-actions">
                    <button className="btn btn-danger btn-sm" type="button" onClick={() => removeAt(index)}>
                      Listeyi katalogdan çıkar
                    </button>
                    <span className="muted catalog-hint">
                      Çıkarılan listenin kohortundaki öğrenciler kayıt sırasında numaralarıyla
                      bulunamaz; kaydedilmiş profiller değişmez.
                    </span>
                  </div>
                </div>
              )}
            </article>
          );
        })}
      </div>
    </div>
  );
}

/**
 * Where the columns are and what each one states.
 *
 * Rendered as its own block because it is the half of a roster that decides meaning rather than
 * location: the headers say which columns are read, and the dimension columns say what the values
 * in them are taken to mean.
 */
function LayoutEditor({
  roster,
  onChange,
}: {
  roster: StudentRosterCatalogEntry;
  onChange: (next: StudentRosterCatalogEntry) => void;
}) {
  const layout = roster.layout;
  const columns = layout?.dimensionColumns ?? [];

  function setLayout(next: Partial<StudentRosterCatalogEntry['layout']>) {
    onChange({ ...roster, layout: { ...layout, ...next } });
  }

  function replaceColumn(index: number, next: StudentRosterCatalogDimensionColumn) {
    const copy = [...columns];
    copy[index] = next;
    setLayout({ dimensionColumns: copy });
  }

  if (!layout) {
    return (
      <Banner tone="warning">
        Bu listenin yerleşimi (<span className="mono">layout</span>) tanımlı değil. JSON sekmesinden
        ekleyin.
      </Banner>
    );
  }

  return (
    <div className="catalog-source-body">
      <h4>Yerleşim</h4>
      <div className="grid grid-2">
        <LayoutField label="Çalışma sayfası" name="worksheetTitle" layout={layout} onChange={setLayout} />
        <LayoutField label="Başlık satırı" name="headerRow" layout={layout} onChange={setLayout} numeric />
        <LayoutField label="Öğrenci no sütun başlığı" name="studentNumberHeader" layout={layout} onChange={setLayout} />
        <LayoutField label="Ad sütun başlığı" name="givenNameHeader" layout={layout} onChange={setLayout} />
        <LayoutField label="Soyad sütun başlığı" name="familyNameHeader" layout={layout} onChange={setLayout} />
      </div>

      <p className="muted catalog-hint">
        Başlıklar sütun sırasına göre değil, metnine göre eşleşir: yayınlanan listeler sütunları
        farklı sıralarda yazıyor. Yanlış bir başlık listeyi okunamaz yapar ve bu hemen görülür.
      </p>

      {columns.map((column, index) => (
        <div className="catalog-change" key={`${column.dimension}-${index}`}>
          <div className="grid grid-2">
            <div className="field">
              <label htmlFor={`column-header-${roster.rosterId}-${index}`}>
                Sütun başlığı <span className="badge badge-warning badge-xs">riskli</span>
              </label>
              <input
                id={`column-header-${roster.rosterId}-${index}`}
                className="text-input"
                value={column.header}
                autoComplete="off"
                onChange={(event) => replaceColumn(index, { ...column, header: event.target.value })}
              />
            </div>
            <div className="field">
              <label htmlFor={`column-dimension-${roster.rosterId}-${index}`}>
                Profil boyutu <span className="badge badge-warning badge-xs">riskli</span>
              </label>
              <input
                id={`column-dimension-${roster.rosterId}-${index}`}
                className="text-input"
                value={column.dimension}
                autoComplete="off"
                onChange={(event) => replaceColumn(index, { ...column, dimension: event.target.value })}
              />
            </div>
          </div>

          <label className="cluster" htmlFor={`column-merged-${roster.rosterId}-${index}`}>
            <input
              id={`column-merged-${roster.rosterId}-${index}`}
              type="checkbox"
              checked={column.statedOncePerMergedRun ?? false}
              onChange={(event) => replaceColumn(index, {
                ...column,
                statedOncePerMergedRun: event.target.checked,
              })}
            />
            <span>Değer, birleştirilmiş hücrede öğrenci grubunun tamamı için bir kez yazılıyor</span>
          </label>

          <ValueMapField
            column={column}
            rosterId={roster.rosterId}
            index={index}
            onChange={(next) => replaceColumn(index, next)}
          />

          <button
            className="btn btn-tertiary btn-sm"
            type="button"
            onClick={() => setLayout({
              dimensionColumns: columns.filter((_, position) => position !== index),
            })}
          >
            Bu sütunu kaldır
          </button>
        </div>
      ))}

      <button
        className="btn btn-secondary btn-sm"
        type="button"
        onClick={() => setLayout({
          dimensionColumns: [
            ...columns,
            { header: '', dimension: '', valueMap: {}, statedOncePerMergedRun: false },
          ],
        })}
      >
        + Seçici sütunu ekle
      </button>
    </div>
  );
}

function LayoutField({
  label,
  name,
  layout,
  onChange,
  numeric,
}: {
  label: string;
  name: keyof StudentRosterCatalogEntry['layout'];
  layout: StudentRosterCatalogEntry['layout'];
  onChange: (next: Partial<StudentRosterCatalogEntry['layout']>) => void;
  numeric?: boolean;
}) {
  const id = `layout-${name}-${layout.worksheetTitle}`;
  const value = layout[name];
  return (
    <div className="field">
      <label htmlFor={id}>
        {label} <span className="badge badge-warning badge-xs">riskli</span>
      </label>
      <input
        id={id}
        className="text-input"
        value={typeof value === 'string' || typeof value === 'number' ? String(value) : ''}
        inputMode={numeric ? 'numeric' : undefined}
        autoComplete="off"
        onChange={(event) => onChange({
          [name]: numeric ? Number(event.target.value) : event.target.value,
        })}
      />
    </div>
  );
}

/**
 * The value map, edited as JSON.
 *
 * Deliberately not a table of dropdowns: the map is exhaustive and a value outside it is refused
 * rather than transformed, so what matters is seeing every stated value beside the profile value
 * it means. This is the one field that can be wrong without anything failing.
 */
function ValueMapField({
  column,
  rosterId,
  index,
  onChange,
}: {
  column: StudentRosterCatalogDimensionColumn;
  rosterId: string;
  index: number;
  onChange: (next: StudentRosterCatalogDimensionColumn) => void;
}) {
  const id = `column-values-${rosterId}-${index}`;
  const external = JSON.stringify(column.valueMap ?? {}, null, 2);
  const [text, setText] = useState(external);
  const [invalid, setInvalid] = useState(false);

  // The box holds the operator's keystrokes, which are not always parseable, so it cannot simply
  // render the prop. It does have to follow the document when the document changes underneath it —
  // loading a stored revision into the editor, for instance — and `emitted` is how the two are
  // told apart.
  const emitted = useRef(external);
  useEffect(() => {
    if (external !== emitted.current) {
      emitted.current = external;
      setText(external);
      setInvalid(false);
    }
  }, [external]);

  return (
    <div className="field">
      <label htmlFor={id}>
        Değer eşlemesi (belgedeki değer → profil değeri){' '}
        <span className="badge badge-warning badge-xs">riskli</span>
      </label>
      <textarea
        id={id}
        className={`text-input mono${invalid ? ' text-input--invalid' : ''}`}
        rows={6}
        value={text}
        placeholder={'{\n  "a1": "A1",\n  "a2": "A2"\n}'}
        onChange={(event) => {
          const next = event.target.value;
          setText(next);
          try {
            const value = JSON.parse(next) as Record<string, string>;
            setInvalid(false);
            emitted.current = JSON.stringify(value, null, 2);
            onChange({ ...column, valueMap: value });
          } catch {
            // Left in the box for the operator to fix; the document keeps its last valid value,
            // and the backend would refuse the edit anyway.
            setInvalid(true);
          }
        }}
      />
      {invalid
        ? <small className="error-text">Bu alan geçerli JSON değil; son geçerli değer korunuyor.</small>
        : (
          <small className="muted">
            Büyük-küçük harf birebir eşleşir. Türkçe harf dönüşümü uygulanmaz: <span className="mono">i</span>
            {' '}ve <span className="mono">İ</span> farklı değerlerdir, ikisi de yazılmalıdır.
          </small>
        )}
    </div>
  );
}

function Field({
  label,
  name,
  roster,
  onChange,
  numeric,
  wide,
}: {
  label: string;
  name: keyof StudentRosterCatalogEntry;
  roster: StudentRosterCatalogEntry;
  onChange: (next: StudentRosterCatalogEntry) => void;
  numeric?: boolean;
  wide?: boolean;
}) {
  const id = `${name}-${roster.rosterId}`;
  const value = roster[name];
  return (
    <div className={`field${wide ? ' field--wide' : ''}`}>
      <label htmlFor={id}>
        {label} {HIGH_RISK_FIELDS.has(name) && <span className="badge badge-warning badge-xs">riskli</span>}
      </label>
      <input
        id={id}
        className="text-input"
        value={value === null || value === undefined || typeof value === 'object' ? '' : String(value)}
        inputMode={numeric ? 'numeric' : undefined}
        autoComplete="off"
        onChange={(event) => {
          const text = event.target.value;
          onChange({
            ...roster,
            [name]: numeric ? (text === '' ? null : Number(text)) : (text === '' ? null : text),
          });
        }}
      />
    </div>
  );
}

function Select({
  label,
  name,
  options,
  roster,
  onChange,
  numeric,
}: {
  label: string;
  name: keyof StudentRosterCatalogEntry;
  options: string[];
  roster: StudentRosterCatalogEntry;
  onChange: (next: StudentRosterCatalogEntry) => void;
  numeric?: boolean;
}) {
  const id = `${name}-${roster.rosterId}`;
  const value = roster[name];
  return (
    <div className="field">
      <label htmlFor={id}>
        {label} {HIGH_RISK_FIELDS.has(name) && <span className="badge badge-warning badge-xs">riskli</span>}
      </label>
      <select
        id={id}
        className="text-input"
        value={typeof value === 'string' || typeof value === 'number' ? String(value) : ''}
        onChange={(event) => onChange({
          ...roster,
          [name]: numeric ? Number(event.target.value) : event.target.value,
        })}
      >
        {options.map((option) => <option key={option} value={option}>{option}</option>)}
      </select>
    </div>
  );
}

// --- Raw JSON editor --------------------------------------------------------

function RawJsonEditor({
  value,
  parseError,
  onChange,
  onFormat,
  onReset,
}: {
  value: string;
  parseError: string | null;
  onChange: (next: string) => void;
  onFormat: () => void;
  onReset: () => void;
}) {
  return (
    <div className="catalog-json">
      <div className="catalog-toolbar">
        <span className="muted">
          Belge olduğu gibi yazılır; yalnızca satır sonları normalize edilir. Sunucu, aramanın
          kullandığı kuralların aynısını uygular.
        </span>
        <div className="cluster">
          <button className="btn btn-tertiary btn-sm" type="button" onClick={onFormat} disabled={parseError !== null}>
            Biçimlendir
          </button>
          <button className="btn btn-tertiary btn-sm" type="button" onClick={onReset}>
            Diskteki hâline dön
          </button>
        </div>
      </div>
      <textarea
        className={`text-input mono catalog-textarea${parseError ? ' text-input--invalid' : ''}`}
        value={value}
        spellCheck={false}
        aria-label="Öğrenci listesi kataloğu JSON belgesi"
        onChange={(event) => onChange(event.target.value)}
      />
      {parseError
        ? <div className="error" role="alert">JSON hatası: {parseError}</div>
        : <p className="muted">JSON sözdizimi geçerli. Katalog kuralları ön izlemede doğrulanır.</p>}
    </div>
  );
}

// --- Change review ----------------------------------------------------------

function ChangeReview({
  dirty,
  busy,
  plan,
  reason,
  canWrite,
  onReason,
  onPreview,
  onApply,
  onDiscard,
}: {
  dirty: boolean;
  busy: boolean;
  plan: StudentRosterCatalogPlan | null;
  reason: string;
  canWrite: boolean;
  onReason: (value: string) => void;
  onPreview: () => void;
  onApply: () => void;
  onDiscard: () => void;
}) {
  return (
    <div className="catalog-review">
      <div className="cluster">
        <button
          className="btn btn-secondary"
          type="button"
          disabled={busy || !dirty || !canWrite}
          onClick={onPreview}
        >
          {busy && !plan ? 'Hesaplanıyor…' : 'Değişiklikleri incele'}
        </button>
        {dirty && (
          <button className="btn btn-tertiary" type="button" disabled={busy} onClick={onDiscard}>
            Değişiklikleri at
          </button>
        )}
        {!dirty && <span className="muted">Düzenleme yapılmadı.</span>}
      </div>

      {plan && <PlanSummary plan={plan} />}

      {plan && plan.hasChanges && (
        <div className="catalog-confirm">
          {plan.hasHighRiskChange && (
            <Banner tone="danger">
              <strong>Bu değişiklik öğrencilere önerilen profil bilgisini değiştiriyor.</strong>{' '}
              Aşağıdaki uyarıları okuyup onaylıyorsanız devam edin. Değişiklik anında geçerli olur;
              bir sonraki öğrenci araması yeni katalogla cevap verir. Daha önce kaydedilmiş
              profiller geri alınmaz.
            </Banner>
          )}

          <div className="field">
            <label htmlFor="roster-catalog-reason">Değişiklik gerekçesi</label>
            <textarea
              id="roster-catalog-reason"
              className="text-input"
              value={reason}
              placeholder="Bu değişiklik neden gerekli? (denetim kaydına ve sürüm geçmişine yazılır)"
              onChange={(event) => onReason(event.target.value)}
            />
          </div>

          <div className="cluster">
            <button
              className={`btn ${plan.hasHighRiskChange ? 'btn-danger' : 'btn-primary'}`}
              type="button"
              disabled={busy || !reason.trim()}
              onClick={onApply}
            >
              {busy ? 'Uygulanıyor…' : 'Katalogu uygula'}
            </button>
            <span className="muted catalog-hint">
              Plan özeti <code className="mono">{plan.planHash.slice(0, 16)}…</code> — onay bu plana
              bağlanır. Dosya bu arada değişirse istek reddedilir.
            </span>
          </div>
        </div>
      )}
    </div>
  );
}

function PlanSummary({ plan }: { plan: StudentRosterCatalogPlan }) {
  if (!plan.hasChanges) {
    return <Banner tone="info">Gönderilen belge diskteki katalogla aynı; uygulanacak değişiklik yok.</Banner>;
  }

  return (
    <div className="catalog-plan">
      <div className="grid grid-2">
        <Figure value={plan.added.length} label="liste ekleniyor" />
        <Figure value={plan.removed.length} label="liste çıkarılıyor" tone={plan.removed.length > 0 ? 'danger' : undefined} />
        <Figure value={plan.modified.length} label="liste değişiyor" />
        <Figure value={plan.unchangedCount} label="liste aynı kalıyor" />
      </div>

      {plan.warnings.map((warning) => (
        <Banner key={warning.code} tone={warning.risk === 'High' ? 'warning' : 'info'}>
          {warning.message}
        </Banner>
      ))}

      {[...plan.added, ...plan.removed, ...plan.modified].map((change) => (
        <ChangeCard key={`${change.kind}-${change.rosterId}`} change={change} />
      ))}
    </div>
  );
}

function ChangeCard({ change }: { change: StudentRosterCatalogRosterChange }) {
  const label = change.kind === 'Added' ? 'Eklenen' : change.kind === 'Removed' ? 'Çıkarılan' : 'Değişen';
  return (
    <section className="catalog-change">
      <div className="cluster catalog-change-head">
        <span className={`badge ${change.kind === 'Removed' ? 'badge-danger' : change.kind === 'Added' ? 'badge-success' : 'badge-neutral'}`}>
          {label}
        </span>
        <strong>{change.displayName}</strong>
        <small className="mono muted">{change.rosterId}</small>
        <small className="muted">{change.cohort}</small>
        {change.isHighRisk && <span className="badge badge-warning">yüksek risk</span>}
      </div>
      {change.fields.length > 0 && (
        <div className="table-wrap">
          <table className="data-table data-table--stack">
            <thead><tr><th>Alan</th><th>Önce</th><th>Sonra</th></tr></thead>
            <tbody>
              {change.fields.map((field) => (
                <tr key={field.field}>
                  <td data-label="Alan">
                    <span className="mono">{field.field}</span>
                    {field.risk === 'High' && <span className="badge badge-warning badge-xs">riskli</span>}
                  </td>
                  <td className="mono catalog-before" data-label="Önce">{field.before ?? '—'}</td>
                  <td className="mono catalog-after" data-label="Sonra">{field.after ?? '—'}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </section>
  );
}

// --- Revision history -------------------------------------------------------

function RevisionHistory({ onRestore }: { onRestore: (content: string) => void }) {
  const [revisions, setRevisions] = useState<StudentRosterCatalogRevisionSummary[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busyId, setBusyId] = useState<string | null>(null);

  const load = useCallback(async () => {
    setError(null);
    try {
      setRevisions(await listRosterCatalogRevisions());
    } catch (caught) {
      setError(caught instanceof ApiError ? caught.message : 'Sürüm geçmişi alınamadı.');
    }
  }, []);

  useEffect(() => { void load(); }, [load]);

  async function restore(id: string) {
    setBusyId(id);
    setError(null);
    try {
      const detail = await getRosterCatalogRevision(id);
      onRestore(detail.content);
    } catch (caught) {
      setError(caught instanceof ApiError ? caught.message : 'Sürüm içeriği alınamadı.');
    } finally {
      setBusyId(null);
    }
  }

  return (
    <div className="catalog-history">
      <p className="muted">
        Uygulanan her belge bütünüyle saklanır. Bir sürümü editöre yüklemek onu uygulamaz:
        değişiklik yine ön izleme ve gerekçeli onaydan geçer.
      </p>
      <LoadState
        loading={revisions === null && !error}
        error={error}
        empty={revisions?.length === 0}
        onRetry={() => void load()}
      />
      {revisions && revisions.length > 0 && (
        <div className="table-wrap">
          <table className="data-table data-table--stack">
            <thead>
              <tr><th>Tarih</th><th>Kim</th><th>Gerekçe</th><th>Liste</th><th /></tr>
            </thead>
            <tbody>
              {revisions.map((revision) => (
                <tr key={revision.id}>
                  <td data-label="Tarih">
                    {formatDateTime(revision.recordedAtUtc)}
                    {revision.isCurrent && <span className="badge badge-success">yürürlükte</span>}
                    {revision.kind === 'Baseline' && <small className="muted" style={{ display: 'block' }}>ilk düzenleme öncesi</small>}
                  </td>
                  <td data-label="Kim">{revision.actorEmail ?? <span className="muted">sistem</span>}</td>
                  <td data-label="Gerekçe">{revision.reason ?? <span className="muted">—</span>}</td>
                  <td data-label="Liste">{revision.rosterCount}</td>
                  <td>
                    <button
                      className="btn btn-tertiary btn-sm"
                      type="button"
                      disabled={busyId === revision.id}
                      onClick={() => void restore(revision.id)}
                    >
                      {busyId === revision.id ? 'Yükleniyor…' : 'Editöre yükle'}
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}

// --- Document helpers -------------------------------------------------------

function parseCatalog(content: string): { catalog: StudentRosterCatalogFile | null; error: string | null } {
  if (content.trim() === '') {
    return { catalog: null, error: 'Belge boş.' };
  }
  try {
    const value = JSON.parse(content) as StudentRosterCatalogFile;
    if (typeof value !== 'object' || value === null || !Array.isArray(value.rosters)) {
      return { catalog: null, error: 'Belge bir katalog nesnesi değil (rosters dizisi yok).' };
    }
    return { catalog: value, error: null };
  } catch (caught) {
    return { catalog: null, error: caught instanceof Error ? caught.message : 'Ayrıştırılamadı.' };
  }
}

/** Two-space JSON with a trailing newline: the shape the committed catalog has always had. */
function serialize(catalog: StudentRosterCatalogFile): string {
  return `${JSON.stringify(catalog, null, 2)}\n`;
}

function matches(roster: StudentRosterCatalogEntry, query: string): boolean {
  const needle = query.trim().toLocaleLowerCase('tr');
  if (needle === '') return true;
  return [roster.rosterId, roster.displayName, roster.academicYear]
    .some((value) => (value ?? '').toLocaleLowerCase('tr').includes(needle));
}

function blankRoster(ordinal: number): StudentRosterCatalogEntry {
  return {
    rosterId: `YENI-LISTE-${ordinal}`,
    displayName: 'Yeni öğrenci listesi',
    transport: 'googleSheets',
    documentFormat: 'googleSheet',
    sourceUri: 'https://docs.google.com/spreadsheets/d/.../edit?gid=0',
    externalId: '',
    sheetGid: 0,
    academicYear: '',
    classYear: 1,
    programLanguage: 'turkish',
    layout: {
      worksheetTitle: 'Sayfa1',
      headerRow: 1,
      studentNumberHeader: 'Öğrenci No',
      givenNameHeader: 'Ad',
      familyNameHeader: 'Soyad',
      dimensionColumns: [],
    },
  };
}

function Figure({ value, label, tone }: { value: number; label: string; tone?: 'danger' }) {
  return (
    <div className="operation-last-change" style={{ display: 'block' }}>
      <p className="catalog-figure" style={{ color: tone === 'danger' && value > 0 ? 'var(--danger)' : undefined }}>
        {value}
      </p>
      <strong className="catalog-figure-label">{label}</strong>
    </div>
  );
}
