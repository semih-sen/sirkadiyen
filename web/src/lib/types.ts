// TypeScript mirrors of the backend contracts. The API serializes enums as
// strings (JsonStringEnumConverter in Program.cs), so every enum crosses the wire
// as its member name, e.g. "LicenseRequired", "Turkish", "Pending".

export type OnboardingState =
  | 'LicenseRequired'
  | 'ProfileRequired'
  | 'CalendarAuthorizationRequired'
  | 'ReadyForInitialSync'
  | 'InitialSyncInProgress'
  | 'Active'
  | 'ActionRequired'
  | 'Suspended';

export type OnboardingNextAction =
  | 'RedeemLicense'
  | 'CompleteAcademicProfile'
  | 'AuthorizeCalendar'
  | 'StartInitialSync'
  | 'WaitForInitialSync'
  | string;

export interface OnboardingSnapshot {
  state: OnboardingState;
  hasActiveLicense: boolean;
  nextAction: OnboardingNextAction;
}

export type UserRole = 'Student' | 'SuperAdmin' | string;

export interface CurrentUser {
  userId: string;
  email: string;
  displayName: string | null;
  role: UserRole;
  onboardingState: OnboardingState;
}

export type ProgramLanguage = 'Turkish' | 'English';

export type GoogleCalendarInitialSyncState = 'Pending' | 'InProgress' | 'Completed';

export type GoogleCalendarConnectionStatus =
  | 'Authorized'
  | 'NeedsReauthorization'
  | string;

// GET /api/profile/options
export interface SupportedProfileOptions {
  academicYear: string;
  schemaVersion: string;
  programs: SupportedProfileProgram[];
}

export interface SupportedProfileProgram {
  classYear: number;
  programLanguage: ProgramLanguage;
  dimensions: SupportedProfileDimension[];
}

export interface SupportedProfileDimension {
  key: string;
  required: boolean;
  /** Present for an independent dimension. */
  values?: string[] | null;
  /** Present for a dependent dimension: the parent dimension key. */
  dependsOn?: string | null;
  /** Present for a dependent dimension: allowed child values keyed by parent value. */
  valuesByParent?: Record<string, string[]> | null;
}

// GET /api/profile
export interface StudentProfileView {
  userId: string;
  academicYear: string;
  classYear: number;
  programLanguage: ProgramLanguage;
  studentNumber: string;
  selectorSchemaVersion: string;
  selectors: Record<string, string>;
  updatedAtUtc: string;
}

// PUT /api/profile body
export interface SaveStudentProfileRequest {
  classYear: number;
  programLanguage: ProgramLanguage;
  studentNumber: string;
  selectors: Record<string, string>;
}

export interface SaveStudentProfileResponse {
  profile: StudentProfileView;
  onboarding: OnboardingSnapshot;
}

// GET /api/calendar/authorization/options
export interface CalendarAuthorizationOptions {
  clientId: string;
  scope: string;
}

// GET /api/calendar/authorization
export interface GoogleCalendarConnectionView {
  userId: string;
  grantedScopes: string;
  status: GoogleCalendarConnectionStatus;
  initialSyncState: GoogleCalendarInitialSyncState;
  managedCalendarId?: string | null;
  managedCalendarUnavailableAtUtc?: string | null;
  lastCalendarInventoryAtUtc?: string | null;
}

export interface CalendarAuthorizationResponse {
  connection: GoogleCalendarConnectionView;
  onboarding: OnboardingSnapshot;
}

// GET /api/calendar/sync
export interface CalendarSyncStatusResponse {
  initialSyncState: GoogleCalendarInitialSyncState;
  hasManagedCalendar: boolean;
  mappedEventCount: number;
  onboarding: OnboardingSnapshot;
}

export interface CalendarSyncResponse {
  connection: GoogleCalendarConnectionView;
  onboarding: OnboardingSnapshot;
}

export type LicenseRedemptionOutcome =
  | 'Redeemed'
  | 'AlreadyRedeemedByCurrentUser'
  | 'UserAlreadyActivated'
  | 'Invalid'
  | string;

export interface RedeemLicenseResponse {
  outcome: LicenseRedemptionOutcome;
  licenseId?: string | null;
  onboarding: OnboardingSnapshot;
}

/** RFC 7807 problem details, as returned by AddProblemDetails / Results.Problem. */
export interface ProblemDetails {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  errors?: Record<string, string[]>;
}
