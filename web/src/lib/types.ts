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

export type DepartmentDivision = 'Basic' | 'Internal' | 'Surgical';

export interface DepartmentColorView {
  key: string;
  name: string;
  division: DepartmentDivision;
  systemDefaultColor: string;
  adminDefaultColor?: string | null;
  userColor?: string | null;
  effectiveColor: string;
}

export interface DepartmentColorMutationResponse {
  changed: boolean;
  calendarRefreshQueued: boolean;
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

// GET/POST /api/operations/freeze (SuperAdmin only)
export interface OperationalFreezeSnapshot {
  isFrozen: boolean;
  changedBy?: string | null;
  reason?: string | null;
  correlationId?: string | null;
  changedAtUtc?: string | null;
}

export type OperationalFreezeChangeOutcome = 'Changed' | 'AlreadyInRequestedState' | string;

export interface OperationalFreezeChangeResult {
  outcome: OperationalFreezeChangeOutcome;
  state: OperationalFreezeSnapshot;
}

// Revision review (SuperAdmin): GET /api/revisions/?state=…, GET /api/revisions/{id},
// POST /api/revisions/{id}/approve
export type RevisionState =
  | 'Parsed'
  | 'Validated'
  | 'ReviewRequired'
  | 'Rejected'
  | 'Published'
  | 'Superseded'
  | string;

export type ValidationSeverity = 'Error' | 'Warning' | 'Information' | string;

export interface ScheduleRevisionSummary {
  revisionId: string;
  sourceId: string;
  state: RevisionState;
  createdAtUtc: string;
  recordCount: number;
  stateReason?: string | null;
}

export interface RevisionFindingView {
  rule: string;
  severity: ValidationSeverity;
  message: string;
  affectedRecordCount: number;
  createdAtUtc: string;
  /** JSON evidence string the rule recorded (may be empty). */
  detail: string;
}

export interface ScheduleRevisionDetail {
  summary: ScheduleRevisionSummary;
  findings: RevisionFindingView[];
  approvedBy?: string | null;
  approvalReason?: string | null;
  approvedAtUtc?: string | null;
  publishedAtUtc?: string | null;
}

// Administrative acquisition (SuperAdmin): GET /api/sources/uploadable,
// POST /api/sources/{sourceId}/document, GET /api/sources/{sourceId}/document/uploads
// (ADR-079, ADR-080).
export type ScheduleDocumentFormat = 'GoogleSheet' | 'Xlsx' | 'Docx' | string;

/** A source whose document is handed out, so an administrator uploads it. */
export interface UploadableSourceView {
  sourceId: string;
  displayName: string;
  academicYear: string;
  classYear: number;
  programLanguage: ProgramLanguage;
  documentFormat: ScheduleDocumentFormat;
  /** Set when the same file serves several sources; one upload serves them all. */
  sharedDocumentGroup?: string | null;
}

/** Whether the upload became a new snapshot or normalized to content already held. */
export type SourceDocumentUploadOutcome = 'Stored' | 'Unchanged' | string;

export interface SourceDocumentUploadTarget {
  sourceId: string;
  classYear: number;
  programLanguage: ProgramLanguage;
  outcome: SourceDocumentUploadOutcome;
  snapshotId: string;
}

export interface SourceDocumentUploadResponse {
  /** The digest of the uploaded bytes, which identifies the file itself. */
  contentSha256: string;
  /** What happened for every source the document serves. */
  targets: SourceDocumentUploadTarget[];
}

export interface SourceDocumentUploadAuditEntry {
  sourceId: string;
  uploadedBy: string;
  fileName: string;
  byteCount: number;
  contentSha256: string;
  outcome: SourceDocumentUploadOutcome;
  uploadedAtUtc: string;
}

export interface ApproveRevisionResponse {
  revisionId: string;
  approved: boolean;
  publicationOutcome: string;
  supersededRevisionId?: string | null;
}

/** RFC 7807 problem details, as returned by AddProblemDetails / Results.Problem. */
export interface ProblemDetails {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  errors?: Record<string, string[]>;
}
