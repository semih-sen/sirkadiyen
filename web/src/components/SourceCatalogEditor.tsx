'use client';

import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import {
  ApiError,
  applySourceCatalog,
  getSourceCatalog,
  getSourceCatalogRevision,
  listSourceCatalogRevisions,
  previewSourceCatalog,
} from '@/lib/api';
import { LoadState, Tabs, formatDateTime } from '@/components/AdminData';
import { Banner } from '@/components/ui';
import type {
  ScheduleSourceCatalogDocument,
  ScheduleSourceCatalogEntry,
  ScheduleSourceCatalogFile,
  ScheduleSourceCatalogPlan,
  ScheduleSourceCatalogRevisionSummary,
  ScheduleSourceCatalogSourceChange,
} from '@/lib/types';

/**
 * The administrative editor for the schedule source catalog document (ADR-114).
 *
 * The catalog states which document belongs to which program and which parser reads it, so an
 * edit here can hand a whole cohort's published lessons to different students without any parse
 * or publication being wrong. The screen is therefore built around the backend's plan rather than
 * around the text box: nothing is written until the operator has previewed a server-computed
 * change plan and confirmed it with a reason, and the `planHash` travelling back with that
 * confirmation is what stops an approved preview from applying a document that has since changed.
 *
 * Two editors, one document. The form is the safe path for ordinary corrections; the raw JSON
 * editor exists because the catalog has fields no form should pretend to model (selector maps,
 * companion evidence) and because a broken catalog has to be repairable from here rather than
 * from a server shell. Both edit the same string, so neither can drop what the other wrote.
 */
export function SourceCatalogEditor() {
  const [document, setDocument] = useState<ScheduleSourceCatalogDocument | null>(null);
  const [draft, setDraft] = useState('');
  const [mode, setMode] = useState('form');
  const [plan, setPlan] = useState<ScheduleSourceCatalogPlan | null>(null);
  const [reason, setReason] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  const load = useCallback(async () => {
    setError(null);
    setNotice(null);
    setPlan(null);
    try {
      const loaded = await getSourceCatalog();
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
      setPlan(await previewSourceCatalog(draft, document.contentHash));
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
      const result = await applySourceCatalog(
        draft,
        document.contentHash,
        plan.planHash,
        reason.trim(),
      );
      setReason('');
      setPlan(null);
      await load();
      setNotice(
        `Katalog güncellendi. ${result.sourceRowsChanged} kaynak satırı yazıldı`
        + (result.pollingDisabledSourceIds.length > 0
          ? `, ${result.pollingDisabledSourceIds.join(', ')} için polling kapatıldı`
          : '')
        + '. Değişiklik kalıcı sürüm geçmişine ve denetim kaydına işlendi.',
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
              <strong>Diskteki katalog geçerli değil.</strong> Worker bu dosyayla başlamaz.
              {document.validationError ? ` Sebep: ${document.validationError}` : ''}
            </Banner>
          )}

          <Tabs
            value={mode}
            onChange={setMode}
            items={[
              { value: 'form', label: 'Kaynak düzenleyici' },
              { value: 'json', label: 'JSON' },
              { value: 'history', label: 'Sürüm geçmişi' },
            ]}
          />

          {mode === 'form' && (
            parsed.catalog
              ? <SourceFormEditor catalog={parsed.catalog} onChange={(next) => edit(serialize(next))} />
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
  document: ScheduleSourceCatalogDocument;
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
          {document.sourceCount ?? '—'} kaynak · sürüm {document.catalogVersion ?? '—'} ·
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

/** The fields an edit to which can move published lessons between students. */
const HIGH_RISK_FIELDS = new Set<keyof ScheduleSourceCatalogEntry>([
  'sourceId',
  'transport',
  'documentFormat',
  'sourceUri',
  'externalId',
  'sheetGid',
  'discoveryFolderId',
  'parserProfile',
  'parserProfileVersion',
  'academicYear',
  'classYear',
  'programLanguage',
  'timeZoneId',
  'sharedDocumentGroup',
  'companionSourceIds',
  'groupRotationSourceIds',
]);

function SourceFormEditor({
  catalog,
  onChange,
}: {
  catalog: ScheduleSourceCatalogFile;
  onChange: (next: ScheduleSourceCatalogFile) => void;
}) {
  const [query, setQuery] = useState('');
  const [openId, setOpenId] = useState<string | null>(null);

  const sources = catalog.sources ?? [];
  const filtered = sources.filter((source) => matches(source, query));

  function replace(index: number, next: ScheduleSourceCatalogEntry) {
    const copy = [...sources];
    copy[index] = next;
    onChange({ ...catalog, sources: copy });
  }

  function removeAt(index: number) {
    onChange({ ...catalog, sources: sources.filter((_, position) => position !== index) });
  }

  function add() {
    const created = blankSource(sources.length + 1);
    onChange({ ...catalog, sources: [...sources, created] });
    setQuery('');
    setOpenId(created.sourceId);
  }

  return (
    <div className="catalog-form">
      <div className="catalog-toolbar">
        <input
          className="text-input"
          value={query}
          placeholder="Kaynak ara (kimlik, ad, parser profili)"
          aria-label="Kaynak ara"
          onChange={(event) => setQuery(event.target.value)}
        />
        <button className="btn btn-secondary btn-sm" type="button" onClick={add}>
          + Yeni kaynak
        </button>
      </div>

      {filtered.length === 0 && (
        <p className="muted">Aramayla eşleşen kaynak yok.</p>
      )}

      <div className="catalog-source-list">
        {filtered.map((source) => {
          const index = sources.indexOf(source);
          const open = openId === source.sourceId;
          return (
            <article className={`catalog-source${open ? ' catalog-source--open' : ''}`} key={`${source.sourceId}-${index}`}>
              <button
                className="catalog-source-head"
                type="button"
                aria-expanded={open}
                onClick={() => setOpenId(open ? null : source.sourceId)}
              >
                <span>
                  <strong>{source.displayName || '(adsız kaynak)'}</strong>
                  <small className="mono muted">{source.sourceId}</small>
                </span>
                <span className="cluster">
                  <span className="badge badge-neutral">Dönem {source.classYear} · {source.programLanguage}</span>
                  <span className="badge">{source.transport}</span>
                  <span aria-hidden="true">{open ? '▲' : '▼'}</span>
                </span>
              </button>

              {open && (
                <div className="catalog-source-body">
                  <div className="grid grid-2">
                    <Field label="Kaynak kimliği" name="sourceId" source={source} onChange={(next) => replace(index, next)} />
                    <Field label="Görünen ad" name="displayName" source={source} onChange={(next) => replace(index, next)} />
                    <Select
                      label="Taşıma"
                      name="transport"
                      options={['googleSheets', 'googleDriveFile', 'httpFile', 'administrativeUpload']}
                      source={source}
                      onChange={(next) => replace(index, next)}
                    />
                    <Select
                      label="Belge biçimi"
                      name="documentFormat"
                      options={['googleSheet', 'xlsx', 'docx']}
                      source={source}
                      onChange={(next) => replace(index, next)}
                    />
                    <Field label="Kaynak URI" name="sourceUri" source={source} onChange={(next) => replace(index, next)} wide />
                    <Field label="Dış kimlik" name="externalId" source={source} onChange={(next) => replace(index, next)} />
                    <Field label="Sayfa gid" name="sheetGid" source={source} onChange={(next) => replace(index, next)} numeric />
                    <Field label="Keşif klasörü kimliği" name="discoveryFolderId" source={source} onChange={(next) => replace(index, next)} />
                    <Field label="Parser profili" name="parserProfile" source={source} onChange={(next) => replace(index, next)} />
                    <Field label="Parser sürümü" name="parserProfileVersion" source={source} onChange={(next) => replace(index, next)} />
                    <Field label="Akademik yıl" name="academicYear" source={source} onChange={(next) => replace(index, next)} />
                    <Select
                      label="Dönem"
                      name="classYear"
                      options={['1', '2', '3', '4', '5', '6']}
                      numeric
                      source={source}
                      onChange={(next) => replace(index, next)}
                    />
                    <Select
                      label="Program dili"
                      name="programLanguage"
                      options={['turkish', 'english']}
                      source={source}
                      onChange={(next) => replace(index, next)}
                    />
                    <Field label="Saat dilimi" name="timeZoneId" source={source} onChange={(next) => replace(index, next)} />
                    <Field label="Ortak belge grubu" name="sharedDocumentGroup" source={source} onChange={(next) => replace(index, next)} />
                    <Field label="Fixture yolu" name="fixturePath" source={source} onChange={(next) => replace(index, next)} wide />
                  </div>

                  <ListField
                    label="Yardımcı kaynaklar (virgülle)"
                    name="companionSourceIds"
                    source={source}
                    onChange={(next) => replace(index, next)}
                  />
                  <ListField
                    label="Grup rotasyonu sahibi kaynaklar (virgülle)"
                    name="groupRotationSourceIds"
                    source={source}
                    onChange={(next) => replace(index, next)}
                  />
                  <JsonField
                    label="Desteklenen hedef kitle seçicileri"
                    name="supportedAudienceSelectors"
                    source={source}
                    onChange={(next) => replace(index, next)}
                  />
                  <JsonField
                    label="Yetkili olduğu hedef kitle seçicileri"
                    name="authoritativeAudienceSelectors"
                    source={source}
                    onChange={(next) => replace(index, next)}
                  />
                  <div className="field">
                    <label htmlFor={`notes-${index}`}>Notlar</label>
                    <textarea
                      id={`notes-${index}`}
                      className="text-input"
                      value={source.notes ?? ''}
                      onChange={(event) => replace(index, {
                        ...source,
                        notes: event.target.value === '' ? null : event.target.value,
                      })}
                    />
                  </div>

                  <div className="cluster catalog-source-actions">
                    <button className="btn btn-danger btn-sm" type="button" onClick={() => removeAt(index)}>
                      Kaynağı katalogdan çıkar
                    </button>
                    <span className="muted catalog-hint">
                      Çıkarılan kaynağın pollingi kapatılır; yayınlanmış dersleri ve takvim
                      kayıtları silinmez.
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

function Field({
  label,
  name,
  source,
  onChange,
  numeric,
  wide,
}: {
  label: string;
  name: keyof ScheduleSourceCatalogEntry;
  source: ScheduleSourceCatalogEntry;
  onChange: (next: ScheduleSourceCatalogEntry) => void;
  numeric?: boolean;
  wide?: boolean;
}) {
  const id = `${name}-${source.sourceId}`;
  const value = source[name];
  return (
    <div className={`field${wide ? ' field--wide' : ''}`}>
      <label htmlFor={id}>
        {label} {HIGH_RISK_FIELDS.has(name) && <span className="badge badge-warning badge-xs">riskli</span>}
      </label>
      <input
        id={id}
        className="text-input"
        value={value === null || value === undefined ? '' : String(value)}
        inputMode={numeric ? 'numeric' : undefined}
        autoComplete="off"
        onChange={(event) => {
          const text = event.target.value;
          onChange({
            ...source,
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
  source,
  onChange,
  numeric,
}: {
  label: string;
  name: keyof ScheduleSourceCatalogEntry;
  options: string[];
  source: ScheduleSourceCatalogEntry;
  onChange: (next: ScheduleSourceCatalogEntry) => void;
  numeric?: boolean;
}) {
  const id = `${name}-${source.sourceId}`;
  return (
    <div className="field">
      <label htmlFor={id}>
        {label} {HIGH_RISK_FIELDS.has(name) && <span className="badge badge-warning badge-xs">riskli</span>}
      </label>
      <select
        id={id}
        className="text-input"
        value={String(source[name] ?? '')}
        onChange={(event) => onChange({
          ...source,
          [name]: numeric ? Number(event.target.value) : event.target.value,
        })}
      >
        {options.map((option) => <option key={option} value={option}>{option}</option>)}
      </select>
    </div>
  );
}

function ListField({
  label,
  name,
  source,
  onChange,
}: {
  label: string;
  name: 'companionSourceIds' | 'groupRotationSourceIds';
  source: ScheduleSourceCatalogEntry;
  onChange: (next: ScheduleSourceCatalogEntry) => void;
}) {
  const id = `${name}-${source.sourceId}`;
  return (
    <div className="field">
      <label htmlFor={id}>
        {label} <span className="badge badge-warning badge-xs">riskli</span>
      </label>
      <input
        id={id}
        className="text-input"
        value={(source[name] ?? []).join(', ')}
        autoComplete="off"
        onChange={(event) => {
          const values = event.target.value
            .split(',')
            .map((value) => value.trim())
            .filter((value) => value.length > 0);
          onChange({ ...source, [name]: values.length === 0 ? null : values });
        }}
      />
    </div>
  );
}

/**
 * A selector map, edited as JSON.
 *
 * Deliberately not modelled as a widget: the map's meaning is precise — an absent dimension is
 * "not declared" and an empty list is "may not appear" — and a form that blurred those two would
 * change what a source is allowed to publish while looking like a formatting choice.
 */
function JsonField({
  label,
  name,
  source,
  onChange,
}: {
  label: string;
  name: 'supportedAudienceSelectors' | 'authoritativeAudienceSelectors';
  source: ScheduleSourceCatalogEntry;
  onChange: (next: ScheduleSourceCatalogEntry) => void;
}) {
  const id = `${name}-${source.sourceId}`;
  const stored = source[name];
  const external = stored ? JSON.stringify(stored, null, 2) : '';
  const [text, setText] = useState(external);
  const [invalid, setInvalid] = useState(false);

  // The box holds the operator's keystrokes, which are not always parseable, so it cannot simply
  // render the prop. It does have to follow the document when the document changes underneath it
  // - loading a stored revision into the editor, for instance - and `emitted` is how the two are
  // told apart: an incoming value this box did not produce replaces what is in it.
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
        {label} <span className="badge badge-warning badge-xs">riskli</span>
      </label>
      <textarea
        id={id}
        className={`text-input mono${invalid ? ' text-input--invalid' : ''}`}
        rows={4}
        value={text}
        placeholder={'{\n  "practiceGroup": ["A", "B"]\n}'}
        onChange={(event) => {
          const next = event.target.value;
          setText(next);
          if (next.trim() === '') {
            setInvalid(false);
            emitted.current = '';
            onChange({ ...source, [name]: null });
            return;
          }
          try {
            const value = JSON.parse(next) as Record<string, string[]>;
            setInvalid(false);
            emitted.current = JSON.stringify(value, null, 2);
            onChange({ ...source, [name]: value });
          } catch {
            // Left in the box for the operator to fix; the document keeps its last valid value,
            // and the backend would refuse the edit anyway.
            setInvalid(true);
          }
        }}
      />
      {invalid && <small className="error-text">Bu alan geçerli JSON değil; son geçerli değer korunuyor.</small>}
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
          Belge olduğu gibi yazılır; yalnızca satır sonları normalize edilir. Sunucu, worker’ın
          açılışta uyguladığı kuralların aynısını uygular.
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
        aria-label="Katalog JSON belgesi"
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
  plan: ScheduleSourceCatalogPlan | null;
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
              <strong>Bu değişiklik veri hattının davranışını değiştiriyor.</strong> Aşağıdaki
              uyarıları okuyup onaylıyorsanız devam edin. Değişiklik anında veritabanına uygulanır;
              bir sonraki poll ve parse yeni yapılandırmayı kullanır.
            </Banner>
          )}

          <div className="field">
            <label htmlFor="catalog-reason">Değişiklik gerekçesi</label>
            <textarea
              id="catalog-reason"
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

function PlanSummary({ plan }: { plan: ScheduleSourceCatalogPlan }) {
  if (!plan.hasChanges) {
    return <Banner tone="info">Gönderilen belge diskteki katalogla aynı; uygulanacak değişiklik yok.</Banner>;
  }

  return (
    <div className="catalog-plan">
      <div className="grid grid-2">
        <Figure value={plan.added.length} label="kaynak ekleniyor" />
        <Figure value={plan.removed.length} label="kaynak çıkarılıyor" tone={plan.removed.length > 0 ? 'danger' : undefined} />
        <Figure value={plan.modified.length} label="kaynak değişiyor" />
        <Figure value={plan.unchangedCount} label="kaynak aynı kalıyor" />
      </div>

      {plan.warnings.map((warning) => (
        <Banner key={warning.code} tone={warning.risk === 'High' ? 'warning' : 'info'}>
          {warning.message}
        </Banner>
      ))}

      {[...plan.added, ...plan.removed, ...plan.modified].map((change) => (
        <ChangeCard key={`${change.kind}-${change.sourceId}`} change={change} />
      ))}
    </div>
  );
}

function ChangeCard({ change }: { change: ScheduleSourceCatalogSourceChange }) {
  const label = change.kind === 'Added' ? 'Eklenen' : change.kind === 'Removed' ? 'Çıkarılan' : 'Değişen';
  return (
    <section className="catalog-change">
      <div className="cluster catalog-change-head">
        <span className={`badge ${change.kind === 'Removed' ? 'badge-danger' : change.kind === 'Added' ? 'badge-success' : 'badge-neutral'}`}>
          {label}
        </span>
        <strong>{change.displayName}</strong>
        <small className="mono muted">{change.sourceId}</small>
        <small className="muted">{change.program}</small>
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
  const [revisions, setRevisions] = useState<ScheduleSourceCatalogRevisionSummary[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busyId, setBusyId] = useState<string | null>(null);

  const load = useCallback(async () => {
    setError(null);
    try {
      setRevisions(await listSourceCatalogRevisions());
    } catch (caught) {
      setError(caught instanceof ApiError ? caught.message : 'Sürüm geçmişi alınamadı.');
    }
  }, []);

  useEffect(() => { void load(); }, [load]);

  async function restore(id: string) {
    setBusyId(id);
    setError(null);
    try {
      const detail = await getSourceCatalogRevision(id);
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
        değişiklik yine ön izleme ve gerekçeli onaydan geçer. Depodaki katalog her deployda
        kurulur; burada yaptığınız bir düzenleme depoya işlenmezse bir sonraki deploy onun yerine
        depodakini yazar — kaybolmaz, o deployun sürümü değiştirdiği belgeyi bütünüyle saklar.</p>
      <p className="muted">
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
              <tr><th>Tarih</th><th>Kim</th><th>Gerekçe</th><th>Kaynak</th><th /></tr>
            </thead>
            <tbody>
              {revisions.map((revision) => (
                <tr key={revision.id}>
                  <td data-label="Tarih">
                    {formatDateTime(revision.recordedAtUtc)}
                    {revision.isCurrent && <span className="badge badge-success">yürürlükte</span>}
                    {revision.kind === 'Baseline' && <small className="muted" style={{ display: 'block' }}>ilk düzenleme öncesi</small>}
                    {/* A deployment writes the catalog too (ADR-138), and the row must not read as
                        though a person did it: the reason column carries the release, not a
                        typed justification. */}
                    {revision.kind === 'Deployment' && <small className="muted" style={{ display: 'block' }}>deploy ile kuruldu</small>}
                  </td>
                  <td data-label="Kim">
                    {revision.actorEmail
                      ?? <span className="muted">{revision.kind === 'Deployment' ? 'deploy' : 'sistem'}</span>}
                  </td>
                  <td data-label="Gerekçe">{revision.reason ?? <span className="muted">—</span>}</td>
                  <td data-label="Kaynak">{revision.sourceCount}</td>
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

function parseCatalog(content: string): { catalog: ScheduleSourceCatalogFile | null; error: string | null } {
  if (content.trim() === '') {
    return { catalog: null, error: 'Belge boş.' };
  }
  try {
    const value = JSON.parse(content) as ScheduleSourceCatalogFile;
    if (typeof value !== 'object' || value === null || !Array.isArray(value.sources)) {
      return { catalog: null, error: 'Belge bir katalog nesnesi değil (sources dizisi yok).' };
    }
    return { catalog: value, error: null };
  } catch (caught) {
    return { catalog: null, error: caught instanceof Error ? caught.message : 'Ayrıştırılamadı.' };
  }
}

/** Two-space JSON with a trailing newline: the shape the committed catalog has always had. */
function serialize(catalog: ScheduleSourceCatalogFile): string {
  return `${JSON.stringify(catalog, null, 2)}\n`;
}

function matches(source: ScheduleSourceCatalogEntry, query: string): boolean {
  const needle = query.trim().toLocaleLowerCase('tr');
  if (needle === '') return true;
  return [source.sourceId, source.displayName, source.parserProfile, source.academicYear]
    .some((value) => (value ?? '').toLocaleLowerCase('tr').includes(needle));
}

function blankSource(ordinal: number): ScheduleSourceCatalogEntry {
  return {
    sourceId: `YENI-KAYNAK-${ordinal}`,
    displayName: 'Yeni kaynak',
    transport: 'googleSheets',
    documentFormat: 'googleSheet',
    sourceUri: 'https://docs.google.com/spreadsheets/d/.../edit?gid=0',
    externalId: '',
    sheetGid: 0,
    parserProfile: '',
    parserProfileVersion: '1.0.0',
    academicYear: '',
    classYear: 1,
    programLanguage: 'turkish',
    timeZoneId: 'Europe/Istanbul',
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
