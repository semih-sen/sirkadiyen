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
  ProblemDetails,
  RedeemLicenseResponse,
  SaveStudentProfileRequest,
  SaveStudentProfileResponse,
  StudentProfileView,
  SupportedProfileOptions,
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

  const send = async (csrf: CsrfToken | null): Promise<Response> => {
    const headers: Record<string, string> = { Accept: 'application/json' };
    if (options.body !== undefined) {
      headers['Content-Type'] = 'application/json';
    }
    if (csrf) {
      headers[csrf.headerName] = csrf.requestToken;
    }
    return fetch(path, {
      method,
      credentials: 'include',
      headers,
      body: options.body !== undefined ? JSON.stringify(options.body) : undefined,
    });
  };

  let response = await send(mutating ? await getCsrfToken() : null);

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

export function signInWithGoogle(credential: string): Promise<CurrentUser> {
  return request<CurrentUser>('/api/auth/google', { method: 'POST', body: { credential } });
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
