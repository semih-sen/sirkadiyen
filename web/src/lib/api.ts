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
  CohortRepairPlan,
  CohortRepairRequestResult,
  CohortRepairScope,
  ProfileRolloverPlan,
  ProfileRolloverRequestResult,
  ProfileRolloverScope,
  ManagedCalendarRebuildAssessment,
  ManagedCalendarRebuildResult,
  OperationalFreezeChangeResult,
  OperationalFreezeSnapshot,
  OperationalFreezeScope,
  ProblemDetails,
  RejectRevisionResponse,
  ReleaseDiffResponse,
  RetryDiffResponse,
  DiscardDiffResponse,
  RequestSourcePollResponse,
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
  StudentRosterLookupResponse,
  SupportedProfileOptions,
  UploadableSourceView,
  AdminLicenseDetail,
  AdminLicenseListItem,
  AdminMetricsSnapshot,
  AdminServiceHealthSnapshot,
  WorkerInstancesResponse,
  AdminUserCalendarEventsResponse,
  CalendarVerificationResult,
  AdminUserDetailResponse,
  AdminUserFilters,
  AdminUserListItem,
  ManualLicenseActivationResult,
  AuditEventCategory,
  AuditEventView,
  CalendarSyncProgressResponse,
  HealthStatus,
  LicenseKind,
  LicenseStatus,
  LicenseStatusResponse,
  PagedResult,
  ReconciliationResponse,
  ScheduleSourceCatalogApplyResult,
  ScheduleSourceCatalogDocument,
  ScheduleSourceCatalogPlan,
  ScheduleSourceCatalogRevisionDetail,
  ScheduleSourceCatalogRevisionSummary,
  StudentRosterCatalogApplyResult,
  StudentRosterCatalogDocument,
  StudentRosterCatalogPlan,
  StudentRosterCatalogRevisionDetail,
  StudentRosterCatalogRevisionSummary,
  PruneSnapshotPayloadResponse,
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
  AnnouncementComposition,
  AnnouncementCompositionOptions,
  AnnouncementDeliveryView,
  AnnouncementDetail,
  AnnouncementPreview,
  AnnouncementSummary,
  CalendarAnnouncementDeliveryState,
  CalendarAnnouncementKind,
  CalendarAnnouncementStatus,
  CancelAnnouncementResult,
  CreateAnnouncementResult,
  UpdateAnnouncementResult,
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

// ---- Account deletion -----------------------------------------------------

/** What the deletion did to the account's external Google footprint (ADR-118). */
export interface AccountDeletionResponse {
  hadManagedCalendar: boolean;
  googleCalendarDeleted: boolean;
  googleTokenRevoked: boolean;
}

/**
 * Permanently deletes the caller's own account ("Hesabımı sil", ADR-118). The confirmation phrase
 * is the caller's own e-mail. The backend ends the session, so the cached CSRF token is dropped and
 * the caller must be sent back to a signed-out screen.
 */
export async function deleteOwnAccount(confirmEmail: string): Promise<AccountDeletionResponse> {
  const result = await request<AccountDeletionResponse>('/api/account/delete', {
    method: 'POST',
    body: { confirmEmail },
  });
  cachedCsrf = null;
  return result;
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

/**
 * Looks the student number up in the published faculty lists.
 *
 * A POST although it reads: a student number does not belong in a URL or in an
 * access log. The endpoint is rate limited, so a caller must not retry it in a
 * loop.
 */
export function lookUpStudentRoster(studentNumber: string): Promise<StudentRosterLookupResponse> {
  return request<StudentRosterLookupResponse>('/api/profile/roster-lookup', {
    method: 'POST',
    body: { studentNumber },
  });
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

/** Computes what repairing one program's calendars would converge, changing nothing (ADR-111). */
export function previewCalendarRepair(scope: CohortRepairScope): Promise<CohortRepairPlan> {
  return request<CohortRepairPlan>('/api/operations/calendar-repairs/preview', {
    method: 'POST',
    body: scope,
  });
}

/**
 * Authorizes the repair that was previewed. The `planHash` binds the confirmation to that plan;
 * the backend replans and refuses with 409 if the cohort has moved since.
 */
export function requestCalendarRepair(
  scope: CohortRepairScope,
  planHash: string,
  reason: string,
): Promise<CohortRepairRequestResult> {
  return request<CohortRepairRequestResult>('/api/operations/calendar-repairs', {
    method: 'POST',
    body: { ...scope, planHash, reason },
  });
}

/**
 * Asks what moving a program's stored profiles onto the year its sources state would do (ADR-115).
 * Changes nothing; the returned `planHash` is what a confirmation is bound to.
 */
export function previewProfileRollover(scope: ProfileRolloverScope): Promise<ProfileRolloverPlan> {
  return request<ProfileRolloverPlan>('/api/operations/profile-rollovers/preview', {
    method: 'POST',
    body: scope,
  });
}

/** Authorizes the rollover that was previewed. A 409 means the program moved since. */
export function requestProfileRollover(
  scope: ProfileRolloverScope,
  planHash: string,
  reason: string,
): Promise<ProfileRolloverRequestResult> {
  return request<ProfileRolloverRequestResult>('/api/operations/profile-rollovers', {
    method: 'POST',
    body: { ...scope, planHash, reason },
  });
}

/**
 * Asks what re-synchronizing one student's calendar would converge (ADR-115). It is the cohort
 * repair narrowed to one row, so it returns the same plan shape.
 */
export function previewUserCalendarRecheck(userId: string): Promise<CohortRepairPlan> {
  return request<CohortRepairPlan>(
    `/api/admin/users/${encodeURIComponent(userId)}/calendar-recheck/preview`,
    { method: 'POST' },
  );
}

/** Authorizes the re-check that was previewed for one student. */
export function requestUserCalendarRecheck(
  userId: string,
  planHash: string,
  reason: string,
): Promise<CohortRepairRequestResult> {
  return request<CohortRepairRequestResult>(
    `/api/admin/users/${encodeURIComponent(userId)}/calendar-recheck`,
    { method: 'POST', body: { planHash, reason } },
  );
}

/**
 * Whether the signed-in user's managed calendar needs rebuilding, and since when it has been
 * unreachable (ADR-116). Changes nothing.
 */
export function assessCalendarRebuild(): Promise<ManagedCalendarRebuildAssessment> {
  return request<ManagedCalendarRebuildAssessment>('/api/calendar/rebuild');
}

/**
 * Rebuilds the signed-in user's own managed calendar after they deleted it. It discards the event
 * ledger and returns the connection to the state initial synchronization starts from; the user
 * then starts that synchronization themselves.
 */
export function rebuildCalendar(): Promise<ManagedCalendarRebuildResult> {
  return request<ManagedCalendarRebuildResult>('/api/calendar/rebuild', { method: 'POST' });
}

/** The operator's view of the same question, for one user. */
export function assessUserCalendarRebuild(
  userId: string,
): Promise<ManagedCalendarRebuildAssessment> {
  return request<ManagedCalendarRebuildAssessment>(
    `/api/admin/users/${encodeURIComponent(userId)}/calendar-rebuild`,
  );
}

/** Rebuilds one user's deleted managed calendar on their behalf. A reason is required. */
export function rebuildUserCalendar(
  userId: string,
  reason: string,
): Promise<ManagedCalendarRebuildResult> {
  return request<ManagedCalendarRebuildResult>(
    `/api/admin/users/${encodeURIComponent(userId)}/calendar-rebuild`,
    { method: 'POST', body: { reason } },
  );
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
export function activateUser(userId: string, reason: string): Promise<ManualLicenseActivationResult> {
  return request<ManualLicenseActivationResult>(
    `/api/admin/users/${encodeURIComponent(userId)}/activate`,
    { method: 'POST', body: { reason } },
  );
}

/** The result of an operator permanently deleting a user's account (ADR-118). */
export interface AdminAccountDeletionResult {
  outcome: string;
  hadManagedCalendar: boolean;
  googleCalendarDeleted: boolean;
  googleTokenRevoked: boolean;
  anonymizedAuditEvents: number;
}

/**
 * Permanently deletes a user's account on their behalf. The reason is audited and the confirmation
 * e-mail must match the target account's e-mail exactly.
 */
export function deleteUser(
  userId: string,
  reason: string,
  confirmEmail: string,
): Promise<AdminAccountDeletionResult> {
  return request<AdminAccountDeletionResult>(
    `/api/admin/users/${encodeURIComponent(userId)}/delete`,
    { method: 'POST', body: { reason, confirmEmail } },
  );
}

/** The result of an operator changing a user's authorization role (ADR-119). */
export interface ChangeUserRoleResult {
  outcome: string;
  previousRole: string;
  newRole: string;
}

/**
 * Promotes a user to operator or removes operator rights. The reason is audited. The backend refuses
 * changing your own role and demoting the bootstrap operator (surfaced as a 409).
 */
export function changeUserRole(
  userId: string,
  role: 'User' | 'SuperAdmin',
  reason: string,
): Promise<ChangeUserRoleResult> {
  return request<ChangeUserRoleResult>(
    `/api/admin/users/${encodeURIComponent(userId)}/role`,
    { method: 'POST', body: { role, reason } },
  );
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

/**
 * The account directory. Selectors travel as repeated `selector=key:value` parameters, because a
 * nested object has no single conventional query encoding and the backend refuses a malformed pair
 * rather than silently widening the result.
 */
export function listAdminUsers(
  filters: AdminUserFilters = {},
): Promise<PagedResult<AdminUserListItem>> {
  const { selectors, ...rest } = filters;
  const path = withQuery('/api/admin/users/', { ...rest });
  const pairs = Object.entries(selectors ?? {}).filter(([key, value]) => key && value);
  if (pairs.length === 0) return request(path);

  const query = new URLSearchParams();
  pairs.forEach(([key, value]) => query.append('selector', `${key}:${value}`));
  return request(`${path}${path.includes('?') ? '&' : '?'}${query.toString()}`);
}

export function getAdminUser(userId: string): Promise<AdminUserDetailResponse> {
  return request(`/api/admin/users/${encodeURIComponent(userId)}`);
}

/**
 * What is on the user's managed calendar over a local-date window, read from the mapping ledger —
 * not what the published schedule says should be there.
 */
export function getAdminUserCalendarEvents(
  userId: string,
  values: { from?: string; to?: string; limit?: number } = {},
): Promise<AdminUserCalendarEventsResponse> {
  return request(
    withQuery(`/api/admin/users/${encodeURIComponent(userId)}/calendar-events`, { ...values }),
  );
}

export function getAdminUserCalendarChanges(
  userId: string,
  limit = 20,
): Promise<UserScheduleChangeView[]> {
  return request(
    withQuery(`/api/admin/users/${encodeURIComponent(userId)}/calendar-changes`, { limit }),
  );
}

/**
 * Reads the user's actual Google calendar and compares it with our records (ADR-121). Read-only: it
 * makes one live Google read and changes nothing. A live call, so expect a short delay. Non-verifiable
 * states (no connection, needs re-authorization, not yet synced) come back with an `outcome` and a
 * `detail` rather than throwing.
 */
export function verifyAdminUserCalendar(userId: string): Promise<CalendarVerificationResult> {
  return request(`/api/admin/users/${encodeURIComponent(userId)}/calendar-verify`);
}

/**
 * Queues a non-destructive reconciliation (ADR-123): the worker's fenced inventory pass re-writes the
 * events the ledger records but Google is missing and patches drifted ones. It never deletes, so it
 * fixes "missing on Google"/"content drift" but leaves surplus/previous-year events. Records intent
 * only — the worker does the actual writes on its next cycle. Requires a reason.
 */
export function repairAdminUserCalendar(userId: string, reason: string): Promise<{ requested: boolean }> {
  return request(`/api/admin/users/${encodeURIComponent(userId)}/calendar-repair`, {
    method: 'POST',
    body: { reason },
    allowEmpty: true,
  });
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

/**
 * Removes one snapshot's stored payload (ADR-120), keeping its immutable metadata and the whole
 * downstream parse/revision/diff trail. The backend refuses the newest snapshot, the year's
 * baseline, a snapshot still needed for parser recovery, and a frozen scope, with a reason in the
 * problem detail. An audited action, so a reason is required.
 */
export function pruneSnapshotPayload(
  snapshotId: string,
  reason: string,
): Promise<PruneSnapshotPayloadResponse> {
  return request(`/api/admin/sources/snapshots/${encodeURIComponent(snapshotId)}/prune-payload`, {
    method: 'POST',
    body: { reason },
  });
}

// --- Schedule source catalog (ADR-114) --------------------------------------
//
// The catalog document is edited as text. `contentHash` is the concurrency token: every preview
// and every apply carries the hash of the document the editor was opened on, and the backend
// refuses the request when the file has moved on. `planHash` binds a confirmation to the exact
// change plan the operator was shown.

export function getSourceCatalog(): Promise<ScheduleSourceCatalogDocument> {
  return request('/api/admin/source-catalog/');
}

export function previewSourceCatalog(
  content: string,
  baseContentHash: string,
): Promise<ScheduleSourceCatalogPlan> {
  return request<ScheduleSourceCatalogPlan>('/api/admin/source-catalog/preview', {
    method: 'POST',
    body: { content, baseContentHash },
  });
}

export function applySourceCatalog(
  content: string,
  baseContentHash: string,
  planHash: string,
  reason: string,
): Promise<ScheduleSourceCatalogApplyResult> {
  return request<ScheduleSourceCatalogApplyResult>('/api/admin/source-catalog/apply', {
    method: 'POST',
    body: { content, baseContentHash, planHash, reason },
  });
}

export function listSourceCatalogRevisions(): Promise<ScheduleSourceCatalogRevisionSummary[]> {
  return request('/api/admin/source-catalog/revisions');
}

export function getSourceCatalogRevision(
  revisionId: string,
): Promise<ScheduleSourceCatalogRevisionDetail> {
  return request(`/api/admin/source-catalog/revisions/${encodeURIComponent(revisionId)}`);
}

// The roster catalog is edited under exactly the rules the source catalog is (ADR-134):
// `contentHash` is the concurrency token and `planHash` binds a confirmation to the plan the
// operator was shown.

export function getRosterCatalog(): Promise<StudentRosterCatalogDocument> {
  return request('/api/admin/roster-catalog/');
}

export function previewRosterCatalog(
  content: string,
  baseContentHash: string,
): Promise<StudentRosterCatalogPlan> {
  return request<StudentRosterCatalogPlan>('/api/admin/roster-catalog/preview', {
    method: 'POST',
    body: { content, baseContentHash },
  });
}

export function applyRosterCatalog(
  content: string,
  baseContentHash: string,
  planHash: string,
  reason: string,
): Promise<StudentRosterCatalogApplyResult> {
  return request<StudentRosterCatalogApplyResult>('/api/admin/roster-catalog/apply', {
    method: 'POST',
    body: { content, baseContentHash, planHash, reason },
  });
}

export function listRosterCatalogRevisions(): Promise<StudentRosterCatalogRevisionSummary[]> {
  return request('/api/admin/roster-catalog/revisions');
}

export function getRosterCatalogRevision(
  revisionId: string,
): Promise<StudentRosterCatalogRevisionDetail> {
  return request(`/api/admin/roster-catalog/revisions/${encodeURIComponent(revisionId)}`);
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

/**
 * Lists every worker instance's last heartbeat (ADR-124). Unlike the single-URL service-health probe,
 * this reveals when more than one instance is running — the condition behind the double-sync incident.
 */
export function getAdminWorkers(): Promise<WorkerInstancesResponse> {
  return request('/api/admin/workers');
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

/**
 * Discards a held diff so it is never dispatched (ADR-127). Terminal: the schedule is corrected by a
 * superseding revision, not by un-discarding this one.
 */
export function discardDiff(
  scheduleDiffId: string,
  discardReason: string,
): Promise<DiscardDiffResponse> {
  return request<DiscardDiffResponse>(
    `/api/diffs/${encodeURIComponent(scheduleDiffId)}/discard`,
    { method: 'POST', body: { discardReason } },
  );
}

/** The most recent diffs in any state, newest first — the history view (ADR-127). */
export function listRecentDiffs(
  sourceId?: string,
  limit = 50,
): Promise<ScheduleDiffSummary[]> {
  return request<ScheduleDiffSummary[]>(withQuery('/api/diffs/recent', { sourceId, limit }));
}

/** The most recent revisions in any state, newest first — the history view (ADR-127). */
export function listRecentRevisions(
  sourceId?: string,
  limit = 50,
): Promise<ScheduleRevisionSummary[]> {
  return request<ScheduleRevisionSummary[]>(
    withQuery('/api/revisions/recent', { sourceId, limit }),
  );
}

/**
 * Queues an immediate poll of one source, executed by the worker next cycle (ADR-127). With
 * `force`, a new parse run is opened even if the stored document is unchanged.
 */
export function requestSourcePoll(
  sourceId: string,
  force = false,
): Promise<RequestSourcePollResponse> {
  return request<RequestSourcePollResponse>(
    `/api/admin/sources/${encodeURIComponent(sourceId)}/poll`,
    { method: 'POST', body: { force } },
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

// ---- Calendar announcements (SuperAdmin) ----------------------------------
// The audience is resolved and the plan hashed on the server; the browser only carries
// the hash back with a hand-typed confirmation phrase, so it can neither choose
// recipients nor confirm a plan the server did not compute (ADR-107).

const ANNOUNCEMENT_PATH = '/api/admin/announcements';

export function getAnnouncementOptions(): Promise<AnnouncementCompositionOptions> {
  return request(`${ANNOUNCEMENT_PATH}/options`);
}

export function previewAnnouncement(
  announcement: AnnouncementComposition,
): Promise<AnnouncementPreview> {
  return request(`${ANNOUNCEMENT_PATH}/preview`, { method: 'POST', body: { announcement } });
}

/**
 * Confirms an announcement. A repeated confirmation of the same campaign key returns
 * `AlreadyExists` rather than a second copy on every recipient's calendar.
 */
export function createAnnouncement(body: {
  announcement: AnnouncementComposition;
  planHash: string;
  confirmationPhrase: string;
  reason: string;
}): Promise<CreateAnnouncementResult> {
  return request(`${ANNOUNCEMENT_PATH}/`, { method: 'POST', body });
}

export function listAnnouncements(values: {
  kind?: CalendarAnnouncementKind;
  status?: CalendarAnnouncementStatus;
  /** Narrows to the warnings addressed to one account. */
  targetUserId?: string;
  limit?: number;
} = {}): Promise<AnnouncementSummary[]> {
  return request(withQuery(`${ANNOUNCEMENT_PATH}/`, { ...values }));
}

export function getAnnouncement(announcementId: string): Promise<AnnouncementDetail> {
  return request(`${ANNOUNCEMENT_PATH}/${encodeURIComponent(announcementId)}`);
}

export function listAnnouncementDeliveries(
  announcementId: string,
  values: { state?: CalendarAnnouncementDeliveryState; page?: number; pageSize?: number } = {},
): Promise<PagedResult<AnnouncementDeliveryView>> {
  return request(
    withQuery(`${ANNOUNCEMENT_PATH}/${encodeURIComponent(announcementId)}/deliveries`, { ...values }),
  );
}

/** Corrects what an announcement says; every copy already written is patched, not duplicated. */
export function updateAnnouncement(
  announcementId: string,
  announcement: AnnouncementComposition,
  reason: string,
): Promise<UpdateAnnouncementResult> {
  return request(`${ANNOUNCEMENT_PATH}/${encodeURIComponent(announcementId)}`, {
    method: 'PUT', body: { announcement, reason },
  });
}

/** Removes every copy already written to a calendar. */
export function cancelAnnouncement(
  announcementId: string,
  reason: string,
): Promise<CancelAnnouncementResult> {
  return request(`${ANNOUNCEMENT_PATH}/${encodeURIComponent(announcementId)}/cancel`, {
    method: 'POST', body: { reason },
  });
}
