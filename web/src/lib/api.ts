// Typed browser client for the same-site backend.
//
// Because dev and prod are same-origin (the Next edge proxies /api/* to Kestrel),
// every call is same-origin and credentials flow with the request. State-changing
// requests carry the antiforgery token from GET /api/auth/csrf in the header the
// backend expects (default X-CSRF-TOKEN), matching the double-submit cookie the
// same GET set.

import type {
  CalendarAuthorizationOptions,
  CalendarAuthorizationResponse,
  CalendarSyncResponse,
  CalendarSyncStatusResponse,
  CurrentUser,
  CreatedLicense,
  DepartmentColorMutationResponse,
  DepartmentColorView,
  LicenseRevocationResult,
  GoogleCalendarConnectionView,
  OnboardingSnapshot,
  ApproveRevisionResponse,
  OperationalFreezeChangeResult,
  OperationalFreezeSnapshot,
  OperationalFreezeScope,
  ProblemDetails,
  RejectRevisionResponse,
  ReleaseDiffResponse,
  RetryDiffResponse,
  RevisionState,
  CalendarDispatchState,
  ScheduleDiffDetail,
  ScheduleDiffState,
  ScheduleDiffSummary,
  ScheduleRevisionDetail,
  ScheduleRevisionSummary,
  RedeemLicenseResponse,
  SaveStudentProfileRequest,
  SaveStudentProfileResponse,
  SourceDocumentUploadAuditEntry,
  SourceDocumentUploadResponse,
  StudentProfileView,
  SupportedProfileOptions,
  UploadableSourceView,
  AdminLicenseDetail,
  AdminLicenseListItem,
  AdminMetricsSnapshot,
  AdminServiceHealthSnapshot,
  AdminUserDetailResponse,
  AdminUserListItem,
  AuditEventCategory,
  AuditEventView,
  CalendarSyncProgressResponse,
  HealthStatus,
  LicenseKind,
  LicenseStatus,
  LicenseStatusResponse,
  PagedResult,
  ReconciliationResponse,
  SourceStatusDetail,
  SourceStatusListItem,
  UnmaskAuditIpResponse,
  UserRole,
  UserScheduleChangeView,
  UserScheduleEventView,
  FinanceAccountHolderListItem,
  FinanceAccountHolderMutationResult,
  FinanceAccountKind,
  FinanceAccountListItem,
  FinanceAccountMutationResult,
  FinanceAuditAction,
  FinanceAuditDetail,
  FinanceAuditListItem,
  FinanceCategory,
  FinanceDistributionListItem,
  FinanceDistributionPlan,
  FinanceDistributionResult,
  FinanceObligationDirection,
  FinanceObligationListItem,
  FinanceObligationMutationResult,
  FinanceObligationStatus,
  FinancePeriodSelector,
  FinanceSummary,
  FinanceTransactionDetail,
  FinanceTransactionFilters,
  FinanceTransactionKind,
  FinanceTransactionListItem,
  FinanceTransactionMutationResult,
  FinanceTrendPoint,
} from './types';

export class ApiError extends Error {
  readonly status: number;
  readonly problem: ProblemDetails | null;

  constructor(status: number, problem: ProblemDetails | null, fallback: string) {
    super(problem?.detail ?? problem?.title ?? fallback);
    this.name = 'ApiError';
    this.status = status;
    this.problem = problem;
  }
}

interface CsrfToken {
  headerName: string;
  requestToken: string;
}

/**
 * The last issued antiforgery token.
 *
 * It is bound to the claims-based user it was issued for, so it survives only as
 * long as the identity does: a token minted while anonymous is refused once a
 * session exists ("meant for a different claims-based user"). Every identity
 * transition must therefore discard it — see signInWithGoogle and logout.
 */
let cachedCsrf: CsrfToken | null = null;

async function fetchCsrfToken(): Promise<CsrfToken> {
  const response = await fetch('/api/auth/csrf', {
    method: 'GET',
    credentials: 'include',
    headers: { Accept: 'application/json' },
  });
  if (!response.ok) {
    throw new ApiError(response.status, await readProblem(response), 'Could not obtain a CSRF token.');
  }
  const token = (await response.json()) as CsrfToken;
  cachedCsrf = token;
  return token;
}

async function getCsrfToken(force = false): Promise<CsrfToken> {
  if (!force && cachedCsrf) {
    return cachedCsrf;
  }
  return fetchCsrfToken();
}

async function readProblem(response: Response): Promise<ProblemDetails | null> {
  const contentType = response.headers.get('content-type') ?? '';
  if (!contentType.includes('json')) {
    return null;
  }
  try {
    return (await response.json()) as ProblemDetails;
  } catch {
    return null;
  }
}

type Method = 'GET' | 'POST' | 'PUT' | 'DELETE';

interface RequestOptions {
  method?: Method;
  /**
   * JSON-serialized, unless it is a FormData: a multipart body is sent as-is so
   * the browser writes the Content-Type with its own boundary. Setting that header
   * by hand produces a boundary-less type the backend cannot read the file from.
   */
  body?: unknown;
  /** Treat 204/205 as a valid empty result rather than a parse target. */
  allowEmpty?: boolean;
}

type QueryValue = string | number | boolean | null | undefined;

function withQuery(path: string, values: Record<string, QueryValue>): string {
  const query = new URLSearchParams();
  Object.entries(values).forEach(([key, value]) => {
    if (value !== undefined && value !== null && value !== '') query.set(key, String(value));
  });
  const suffix = query.toString();
  return suffix ? `${path}?${suffix}` : path;
}

/**
 * Core request helper. Injects the CSRF header on mutating verbs and retries once
 * on a 400 antiforgery failure with a freshly issued token (the cached token can
 * go stale after a session rotation).
 */
async function request<T>(path: string, options: RequestOptions = {}): Promise<T> {
  const method = options.method ?? 'GET';
  const mutating = method !== 'GET';

  const multipart = options.body instanceof FormData;

  const send = async (csrf: CsrfToken | null): Promise<Response> => {
    const headers: Record<string, string> = { Accept: 'application/json' };
    if (options.body !== undefined && !multipart) {
      headers['Content-Type'] = 'application/json';
    }
    if (csrf) {
      headers[csrf.headerName] = csrf.requestToken;
    }
    return fetch(path, {
      method,
      credentials: 'include',
      headers,
      body:
        options.body === undefined
          ? undefined
          : multipart
            ? (options.body as FormData)
            : JSON.stringify(options.body),
    });
  };

  // A multipart request always takes a freshly issued token. Its antiforgery
  // failure is not recoverable the way a JSON one is: the token is validated while
  // binding IFormFile, which throws instead of returning a problem this client
  // could recognize and retry (in Development that surfaces as a 500). Uploads are
  // rare, so one extra same-origin GET is the cheap side of that trade.
  let response = await send(mutating ? await getCsrfToken(multipart) : null);

  if (mutating && response.status === 400) {
    // Possibly a stale antiforgery token; refresh once and retry.
    const problem = await readProblem(response.clone());
    if (isAntiforgeryFailure(problem, response)) {
      response = await send(await getCsrfToken(true));
    }
  }

  if (response.status === 204 || response.status === 205) {
    return undefined as T;
  }

  if (!response.ok) {
    throw new ApiError(response.status, await readProblem(response), `Request to ${path} failed.`);
  }

  if (options.allowEmpty && response.headers.get('content-length') === '0') {
    return undefined as T;
  }

  return (await response.json()) as T;
}

function isAntiforgeryFailure(problem: ProblemDetails | null, response: Response): boolean {
  // The antiforgery middleware returns 400; there is no machine field for it, so
  // this is a best-effort heuristic used only to justify one token-refresh retry.
  const text = `${problem?.title ?? ''} ${problem?.detail ?? ''}`.toLowerCase();
  return response.status === 400 && (text.includes('antiforgery') || text.includes('csrf') || text === ' ');
}

// ---- Authentication -------------------------------------------------------

/** Returns the current session, or null when not signed in (401). */
export async function getMe(): Promise<CurrentUser | null> {
  try {
    return await request<CurrentUser>('/api/auth/me');
  } catch (error) {
    if (error instanceof ApiError && error.status === 401) {
      return null;
    }
    throw error;
  }
}

export async function signInWithGoogle(credential: string): Promise<CurrentUser> {
  const user = await request<CurrentUser>('/api/auth/google', {
    method: 'POST',
    body: { credential },
  });
  // The token that authorized this very request was issued to the anonymous user.
  // The session now belongs to a real one, so keeping it would send every later
  // mutation a token minted for someone else.
  cachedCsrf = null;
  return user;
}

export async function logout(): Promise<void> {
  await request<void>('/api/auth/logout', { method: 'POST', allowEmpty: true });
  cachedCsrf = null;
}

// ---- Onboarding -----------------------------------------------------------

export function getOnboarding(): Promise<OnboardingSnapshot> {
  return request<OnboardingSnapshot>('/api/onboarding');
}

// ---- Licensing ------------------------------------------------------------

export function redeemLicense(code: string): Promise<RedeemLicenseResponse> {
  return request<RedeemLicenseResponse>('/api/licenses/redeem', { method: 'POST', body: { code } });
}

// ---- Student profile ------------------------------------------------------

export function getProfileOptions(): Promise<SupportedProfileOptions> {
  return request<SupportedProfileOptions>('/api/profile/options');
}

/** Returns the stored profile, or null when none is set yet (204). */
export async function getProfile(): Promise<StudentProfileView | null> {
  const result = await request<StudentProfileView | undefined>('/api/profile/', { allowEmpty: true });
  return result ?? null;
}

export function saveProfile(body: SaveStudentProfileRequest): Promise<SaveStudentProfileResponse> {
  return request<SaveStudentProfileResponse>('/api/profile/', { method: 'PUT', body });
}

// ---- Calendar authorization ----------------------------------------------

export function getCalendarAuthorizationOptions(): Promise<CalendarAuthorizationOptions> {
  return request<CalendarAuthorizationOptions>('/api/calendar/authorization/options');
}

/** Returns the current Calendar grant, or null when none exists yet (204). */
export async function getCalendarAuthorization(): Promise<GoogleCalendarConnectionView | null> {
  const result = await request<GoogleCalendarConnectionView | undefined>('/api/calendar/authorization/', {
    allowEmpty: true,
  });
  return result ?? null;
}

export function authorizeCalendar(authorizationCode: string): Promise<CalendarAuthorizationResponse> {
  return request<CalendarAuthorizationResponse>('/api/calendar/authorization/', {
    method: 'POST',
    body: { authorizationCode },
  });
}

// ---- Calendar sync --------------------------------------------------------

/** Returns initial-sync progress, or null before a Calendar grant exists (204). */
export async function getSyncStatus(): Promise<CalendarSyncStatusResponse | null> {
  const result = await request<CalendarSyncStatusResponse | undefined>('/api/calendar/sync/', {
    allowEmpty: true,
  });
  return result ?? null;
}

export function startSync(): Promise<CalendarSyncResponse> {
  return request<CalendarSyncResponse>('/api/calendar/sync/', { method: 'POST' });
}

export async function getSyncProgress(): Promise<CalendarSyncProgressResponse | null> {
  const result = await request<CalendarSyncProgressResponse | undefined>('/api/calendar/sync/progress', {
    allowEmpty: true,
  });
  return result ?? null;
}

export function requestReconciliation(): Promise<ReconciliationResponse> {
  return request<ReconciliationResponse>('/api/calendar/reconcile', { method: 'POST' });
}

export function getUpcomingSchedule(days = 14): Promise<UserScheduleEventView[]> {
  return request<UserScheduleEventView[]>(withQuery('/api/schedule/upcoming', { days }));
}

export function getScheduleChanges(limit = 20): Promise<UserScheduleChangeView[]> {
  return request<UserScheduleChangeView[]>(withQuery('/api/schedule/changes', { limit }));
}

export function getLicenseStatus(): Promise<LicenseStatusResponse> {
  return request<LicenseStatusResponse>('/api/licenses/status');
}

// ---- Calendar appearance -------------------------------------------------

export function getDepartmentColors(): Promise<DepartmentColorView[]> {
  return request<DepartmentColorView[]>('/api/calendar/colors/');
}

export function setDepartmentColor(
  departmentKey: string,
  color: string,
): Promise<DepartmentColorMutationResponse> {
  return request<DepartmentColorMutationResponse>(
    `/api/calendar/colors/${encodeURIComponent(departmentKey)}`,
    { method: 'PUT', body: { color } },
  );
}

export function resetDepartmentColor(
  departmentKey: string,
): Promise<DepartmentColorMutationResponse> {
  return request<DepartmentColorMutationResponse>(
    `/api/calendar/colors/${encodeURIComponent(departmentKey)}`,
    { method: 'DELETE' },
  );
}

// ---- Administration (SuperAdmin) -----------------------------------------
// These endpoints are enforced by the SuperAdmin policy server-side; the frontend
// only navigates by the backend-authoritative role (AI_GUIDELINE §6, §16).

export function getFreeze(): Promise<OperationalFreezeSnapshot> {
  return request<OperationalFreezeSnapshot>('/api/operations/freeze');
}

export function setFreeze(isFrozen: boolean, reason: string): Promise<OperationalFreezeChangeResult> {
  return request<OperationalFreezeChangeResult>('/api/operations/freeze', {
    method: 'POST',
    body: { isFrozen, reason },
  });
}

export function listScopedFreezes(): Promise<OperationalFreezeSnapshot[]> {
  return request<OperationalFreezeSnapshot[]>('/api/operations/freeze/scopes');
}

export function setScopedFreeze(
  scope: OperationalFreezeScope,
  isFrozen: boolean,
  reason: string,
): Promise<OperationalFreezeChangeResult> {
  return request<OperationalFreezeChangeResult>('/api/operations/freeze/scopes', {
    method: 'POST',
    body: { ...scope, isFrozen, reason },
  });
}

export function getAdminDepartmentColors(): Promise<DepartmentColorView[]> {
  return request<DepartmentColorView[]>('/api/admin/calendar-colors/');
}

export function setAdminDepartmentColor(
  departmentKey: string,
  color: string,
  reason: string,
): Promise<DepartmentColorMutationResponse> {
  return request<DepartmentColorMutationResponse>(
    `/api/admin/calendar-colors/${encodeURIComponent(departmentKey)}`,
    { method: 'PUT', body: { color, reason } },
  );
}

export function resetAdminDepartmentColor(
  departmentKey: string,
  reason: string,
): Promise<DepartmentColorMutationResponse> {
  return request<DepartmentColorMutationResponse>(
    `/api/admin/calendar-colors/${encodeURIComponent(departmentKey)}/reset`,
    { method: 'POST', body: { reason } },
  );
}

/**
 * Manual, audited activation of a user without a license code. A SuperAdmin can
 * point this at their own userId to give themselves a student activation for
 * testing the sync flow (ADR-053 manual activation).
 */
export function activateUser(userId: string, reason: string): Promise<unknown> {
  return request<unknown>(`/api/admin/users/${userId}/activate`, {
    method: 'POST',
    body: { reason },
  });
}

export function createLicense(expiresAtUtc: string | null, notes: string | null): Promise<CreatedLicense> {
  return request<CreatedLicense>('/api/admin/licenses/', {
    method: 'POST',
    body: { expiresAtUtc, notes },
  });
}

export function revokeLicense(licenseId: string, reason: string): Promise<LicenseRevocationResult> {
  return request<LicenseRevocationResult>(`/api/admin/licenses/${encodeURIComponent(licenseId)}/revoke`, {
    method: 'POST',
    body: { reason },
  });
}

export function listAdminUsers(values: {
  search?: string; role?: UserRole; page?: number; pageSize?: number;
} = {}): Promise<PagedResult<AdminUserListItem>> {
  return request(withQuery('/api/admin/users/', { ...values }));
}

export function getAdminUser(userId: string): Promise<AdminUserDetailResponse> {
  return request(`/api/admin/users/${encodeURIComponent(userId)}`);
}

export function listAdminLicenses(values: {
  status?: LicenseStatus; kind?: LicenseKind; page?: number; pageSize?: number;
} = {}): Promise<PagedResult<AdminLicenseListItem>> {
  return request(withQuery('/api/admin/licenses/', { ...values }));
}

export function getAdminLicense(licenseId: string): Promise<AdminLicenseDetail> {
  return request(`/api/admin/licenses/${encodeURIComponent(licenseId)}`);
}

export function listAdminSources(): Promise<SourceStatusListItem[]> {
  return request('/api/admin/sources/');
}

export function getAdminSource(sourceId: string): Promise<SourceStatusDetail> {
  return request(`/api/admin/sources/${encodeURIComponent(sourceId)}`);
}

interface AuditFilters {
  userId?: string;
  category?: AuditEventCategory;
  actorUserId?: string;
  actorEmail?: string;
  subjectType?: string;
  subjectId?: string;
  from?: string;
  to?: string;
  page?: number;
  pageSize?: number;
}

export function listAccessLogs(filters: AuditFilters = {}): Promise<PagedResult<AuditEventView>> {
  return request(withQuery('/api/admin/access-logs/', { ...filters }));
}

export function listAuditEvents(filters: AuditFilters = {}): Promise<PagedResult<AuditEventView>> {
  return request(withQuery('/api/admin/audit/', { ...filters }));
}

export function unmaskAuditIp(id: string, reason: string): Promise<UnmaskAuditIpResponse> {
  return request(`/api/admin/access-logs/${encodeURIComponent(id)}/unmask`, {
    method: 'POST', body: { reason },
  });
}

export function getAdminMetrics(): Promise<AdminMetricsSnapshot> {
  return request('/api/admin/metrics');
}

export function getAdminServiceHealth(): Promise<AdminServiceHealthSnapshot> {
  return request('/api/admin/services/health');
}

export async function getHealth(path: 'live' | 'ready'): Promise<HealthStatus> {
  const response = await fetch(`/health/${path}`, { headers: { Accept: 'text/plain' } });
  return { ok: response.ok, status: response.status, text: await response.text() };
}

export function listRevisions(
  state: RevisionState = 'ReviewRequired',
  limit = 50,
): Promise<ScheduleRevisionSummary[]> {
  return request<ScheduleRevisionSummary[]>(
    `/api/revisions/?state=${encodeURIComponent(state)}&limit=${limit}`,
  );
}

export function getRevision(revisionId: string): Promise<ScheduleRevisionDetail> {
  return request<ScheduleRevisionDetail>(`/api/revisions/${revisionId}`);
}

export function approveRevision(
  revisionId: string,
  approvalReason: string,
): Promise<ApproveRevisionResponse> {
  return request<ApproveRevisionResponse>(`/api/revisions/${revisionId}/approve`, {
    method: 'POST',
    body: { approvalReason },
  });
}

/**
 * Closes a quarantined revision terminally (ADR-097). There is no rollback: the correction is a
 * newer revision published over this one from a corrected source (ADR-033).
 */
export function rejectRevision(
  revisionId: string,
  rejectionReason: string,
): Promise<RejectRevisionResponse> {
  return request<RejectRevisionResponse>(`/api/revisions/${revisionId}/reject`, {
    method: 'POST',
    body: { rejectionReason },
  });
}

// ---- Diff review (SuperAdmin) --------------------------------------------
// Two queues behind one route. Listing by `state` gives the held-review queue; listing by
// `dispatchState` gives the failed fan-out queue, which the state filter can never show because a
// terminally failed diff is still Ready or Released (ADR-042, ADR-097).

export function listDiffs(
  state: ScheduleDiffState = 'Held',
  limit = 50,
): Promise<ScheduleDiffSummary[]> {
  return request<ScheduleDiffSummary[]>(withQuery('/api/diffs/', { state, limit }));
}

export function listDiffsByDispatchState(
  dispatchState: CalendarDispatchState,
  limit = 50,
): Promise<ScheduleDiffSummary[]> {
  return request<ScheduleDiffSummary[]>(withQuery('/api/diffs/', { dispatchState, limit }));
}

export function getDiff(scheduleDiffId: string, entryLimit = 100): Promise<ScheduleDiffDetail> {
  return request<ScheduleDiffDetail>(
    withQuery(`/api/diffs/${encodeURIComponent(scheduleDiffId)}`, { entryLimit }),
  );
}

/** Releases a held diff for dispatch on a named operator's behalf. */
export function releaseDiff(
  scheduleDiffId: string,
  releaseReason: string,
): Promise<ReleaseDiffResponse> {
  return request<ReleaseDiffResponse>(
    `/api/diffs/${encodeURIComponent(scheduleDiffId)}/release`,
    { method: 'POST', body: { releaseReason } },
  );
}

/**
 * Returns a terminally failed diff to the dispatch queue. It grants no new authority: the same
 * idempotent, ledger-resumable fan-out re-runs against the same immutable diff.
 */
export function retryDiff(
  scheduleDiffId: string,
  retryReason: string,
): Promise<RetryDiffResponse> {
  return request<RetryDiffResponse>(
    `/api/diffs/${encodeURIComponent(scheduleDiffId)}/retry`,
    { method: 'POST', body: { retryReason } },
  );
}

// ---- Administrative acquisition (SuperAdmin) ------------------------------
// A source whose document is handed out rather than published is acquired by an
// administrator uploading the file (ADR-079, ADR-080). The upload only acquires:
// the worker parses, validates and publishes it on its next cycle under the same
// rules as a polled source, so the UI must not report a published schedule.

/** The sources that accept an upload, as the server-owned catalog declares them. */
export function listUploadableSources(): Promise<UploadableSourceView[]> {
  return request<UploadableSourceView[]>('/api/sources/uploadable');
}

/**
 * Uploads the document for one source. Every source served by literally the same
 * file gets its own snapshot from these bytes, so the response reports one target
 * per source rather than a single outcome.
 */
export function uploadSourceDocument(
  sourceId: string,
  file: File,
): Promise<SourceDocumentUploadResponse> {
  const form = new FormData();
  // The field name the endpoint binds its IFormFile from.
  form.append('file', file, file.name);
  return request<SourceDocumentUploadResponse>(
    `/api/sources/${encodeURIComponent(sourceId)}/document`,
    { method: 'POST', body: form },
  );
}

/** The recent upload audit trail for one source, newest first. */
export function listSourceDocumentUploads(
  sourceId: string,
): Promise<SourceDocumentUploadAuditEntry[]> {
  return request<SourceDocumentUploadAuditEntry[]>(
    `/api/sources/${encodeURIComponent(sourceId)}/document/uploads`,
  );
}

// ---- Finance administration (SuperAdmin) ---------------------------------

const FINANCE_PATH = '/api/admin/finance';

export function listFinanceHolders(): Promise<FinanceAccountHolderListItem[]> {
  return request(`${FINANCE_PATH}/holders`);
}

export function createFinanceHolder(body: {
  displayName: string; userId?: string | null; shareBasisPoints: number;
}): Promise<FinanceAccountHolderMutationResult> {
  return request(`${FINANCE_PATH}/holders`, { method: 'POST', body });
}

export function setFinanceHolderShare(holderId: string, shareBasisPoints: number): Promise<FinanceAccountHolderMutationResult> {
  return request(`${FINANCE_PATH}/holders/${encodeURIComponent(holderId)}/share`, {
    method: 'POST', body: { shareBasisPoints },
  });
}

export function deactivateFinanceHolder(holderId: string): Promise<FinanceAccountHolderMutationResult> {
  return request(`${FINANCE_PATH}/holders/${encodeURIComponent(holderId)}/deactivate`, { method: 'POST' });
}

export function listFinanceAccounts(asOfOn?: string): Promise<FinanceAccountListItem[]> {
  return request(withQuery(`${FINANCE_PATH}/accounts`, { asOfOn }));
}

export function getFinanceAccount(accountId: string, asOfOn?: string): Promise<FinanceAccountListItem> {
  return request(withQuery(`${FINANCE_PATH}/accounts/${encodeURIComponent(accountId)}`, { asOfOn }));
}

export function openFinanceAccount(body: {
  financeAccountHolderId: string; name: string; kind: FinanceAccountKind; openedOn: string;
}): Promise<FinanceAccountMutationResult> {
  return request(`${FINANCE_PATH}/accounts`, { method: 'POST', body });
}

export function closeFinanceAccount(accountId: string, reason: string): Promise<FinanceAccountMutationResult> {
  return request(`${FINANCE_PATH}/accounts/${encodeURIComponent(accountId)}/close`, {
    method: 'POST', body: { reason },
  });
}

function financeTransactionQuery(filters: FinanceTransactionFilters): Record<string, QueryValue> {
  return {
    from: filters.from, to: filters.to, kind: filters.kind, category: filters.category,
    accountId: filters.accountId, holderId: filters.holderId, search: filters.search,
    page: filters.page, pageSize: filters.pageSize,
  };
}

export function listFinanceTransactions(filters: FinanceTransactionFilters = {}): Promise<PagedResult<FinanceTransactionListItem>> {
  return request(withQuery(`${FINANCE_PATH}/transactions`, financeTransactionQuery(filters)));
}

export function getFinanceTransaction(transactionId: string): Promise<FinanceTransactionDetail> {
  return request(`${FINANCE_PATH}/transactions/${encodeURIComponent(transactionId)}`);
}

export function getFinanceTransactionHistory(transactionId: string): Promise<FinanceAuditDetail[]> {
  return request(`${FINANCE_PATH}/transactions/${encodeURIComponent(transactionId)}/history`);
}

export function recordFinanceOpeningBalance(body: {
  accountId: string; signedAmount: number; occurredOn: string; description: string;
}): Promise<FinanceTransactionMutationResult> {
  return request(`${FINANCE_PATH}/transactions/opening-balance`, { method: 'POST', body });
}

export function recordFinanceIncome(body: {
  accountId: string; amount: number; category: FinanceCategory; occurredOn: string;
  description: string; reference?: string | null; counterpartyName?: string | null;
}): Promise<FinanceTransactionMutationResult> {
  return request(`${FINANCE_PATH}/transactions/income`, { method: 'POST', body });
}

export function recordFinanceExpense(body: {
  accountId: string; amount: number; category: FinanceCategory; occurredOn: string;
  description: string; reference?: string | null; counterpartyName?: string | null;
}): Promise<FinanceTransactionMutationResult> {
  return request(`${FINANCE_PATH}/transactions/expense`, { method: 'POST', body });
}

export function recordFinanceTransfer(body: {
  fromAccountId: string; toAccountId: string; amount: number; occurredOn: string;
  description: string; reference?: string | null;
}): Promise<FinanceTransactionMutationResult> {
  return request(`${FINANCE_PATH}/transactions/transfer`, { method: 'POST', body });
}

export function updateFinanceTransaction(transactionId: string, body: {
  kind: FinanceTransactionKind; category?: FinanceCategory | null; amount: number; occurredOn: string;
  description: string; reference?: string | null; counterpartyName?: string | null;
  accountId: string; toAccountId?: string | null; rowVersion: number; reason: string;
}): Promise<FinanceTransactionMutationResult> {
  return request(`${FINANCE_PATH}/transactions/${encodeURIComponent(transactionId)}`, { method: 'PUT', body });
}

export function deleteFinanceTransaction(transactionId: string, rowVersion: number, reason: string): Promise<FinanceTransactionMutationResult> {
  return request(`${FINANCE_PATH}/transactions/${encodeURIComponent(transactionId)}/delete`, {
    method: 'POST', body: { rowVersion, reason },
  });
}

export async function exportFinanceTransactions(filters: FinanceTransactionFilters = {}): Promise<Blob> {
  const path = withQuery(`${FINANCE_PATH}/transactions/export`, financeTransactionQuery(filters));
  const response = await fetch(path, { method: 'GET', credentials: 'include', headers: { Accept: 'text/csv' } });
  if (!response.ok) throw new ApiError(response.status, await readProblem(response), 'Finans işlemleri dışa aktarılamadı.');
  return response.blob();
}

export function getFinanceSummary(values: {
  period?: FinancePeriodSelector; startOn?: string; endOn?: string; accountId?: string;
} = {}): Promise<FinanceSummary> {
  return request(withQuery(`${FINANCE_PATH}/summary`, values));
}

export function getFinanceTrend(months = 12): Promise<FinanceTrendPoint[]> {
  return request(withQuery(`${FINANCE_PATH}/trend`, { months }));
}

export function listFinanceObligations(values: {
  direction?: FinanceObligationDirection; status?: FinanceObligationStatus; page?: number; pageSize?: number;
} = {}): Promise<PagedResult<FinanceObligationListItem>> {
  return request(withQuery(`${FINANCE_PATH}/obligations/`, values));
}

export function getFinanceObligation(obligationId: string): Promise<FinanceObligationListItem> {
  return request(`${FINANCE_PATH}/obligations/${encodeURIComponent(obligationId)}`);
}

export function createFinanceObligation(body: {
  direction: FinanceObligationDirection; category: FinanceCategory; counterpartyName: string;
  description?: string | null; amount: number; issuedOn: string; dueOn?: string | null;
}): Promise<FinanceObligationMutationResult> {
  return request(`${FINANCE_PATH}/obligations/`, { method: 'POST', body });
}

export function settleFinanceObligation(obligationId: string, body: {
  accountId: string; amount: number; settledOn: string; reference?: string | null;
}): Promise<FinanceObligationMutationResult> {
  return request(`${FINANCE_PATH}/obligations/${encodeURIComponent(obligationId)}/settle`, { method: 'POST', body });
}

export function cancelFinanceObligationSettlement(obligationId: string, settlementId: string, reason: string): Promise<FinanceObligationMutationResult> {
  return request(`${FINANCE_PATH}/obligations/${encodeURIComponent(obligationId)}/settlements/${encodeURIComponent(settlementId)}/cancel`, {
    method: 'POST', body: { reason },
  });
}

export function writeOffFinanceObligation(obligationId: string, on: string, reason: string): Promise<FinanceObligationMutationResult> {
  return request(`${FINANCE_PATH}/obligations/${encodeURIComponent(obligationId)}/write-off`, {
    method: 'POST', body: { on, reason },
  });
}

export function cancelFinanceObligation(obligationId: string, on: string, reason: string): Promise<FinanceObligationMutationResult> {
  return request(`${FINANCE_PATH}/obligations/${encodeURIComponent(obligationId)}/cancel`, {
    method: 'POST', body: { on, reason },
  });
}

export function listFinanceDistributions(): Promise<FinanceDistributionListItem[]> {
  return request(`${FINANCE_PATH}/distributions/`);
}

export function getFinanceDistribution(distributionId: string): Promise<FinanceDistributionListItem> {
  return request(`${FINANCE_PATH}/distributions/${encodeURIComponent(distributionId)}`);
}

export function previewFinanceDistribution(body: {
  periodStartOn: string; periodEndOn: string; sourceAccountId: string;
}): Promise<FinanceDistributionPlan> {
  return request(`${FINANCE_PATH}/distributions/preview`, { method: 'POST', body });
}

export function executeFinanceDistribution(body: {
  periodStartOn: string; periodEndOn: string; sourceAccountId: string; confirmationToken: string;
  planHash: string; expectedConfirmationPhrase: string; reason: string;
}): Promise<FinanceDistributionResult> {
  return request(`${FINANCE_PATH}/distributions/execute`, { method: 'POST', body });
}

export function reverseFinanceDistribution(distributionId: string, reason: string): Promise<FinanceDistributionResult> {
  return request(`${FINANCE_PATH}/distributions/${encodeURIComponent(distributionId)}/reverse`, {
    method: 'POST', body: { reason },
  });
}

export function listFinanceAudit(values: {
  subjectType?: string; subjectId?: string; action?: FinanceAuditAction; actorUserId?: string;
  from?: string; to?: string; page?: number; pageSize?: number;
} = {}): Promise<PagedResult<FinanceAuditListItem>> {
  return request(withQuery(`${FINANCE_PATH}/audit`, values));
}
