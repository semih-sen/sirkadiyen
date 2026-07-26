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
  GoogleCalendarConnectionView,
  OnboardingSnapshot,
  ApproveRevisionResponse,
  OperationalFreezeChangeResult,
  OperationalFreezeSnapshot,
  ProblemDetails,
  RevisionState,
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
