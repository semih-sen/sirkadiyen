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

// GET /api/calendar/sync/progress. These are durable-ledger counts, not one-run
// success/failure counters.
export interface CalendarSyncProgressResponse extends CalendarSyncStatusResponse {
  createdEventCount: number;
  updatedEventCount: number;
  firstWrittenAtUtc?: string | null;
  lastWrittenAtUtc?: string | null;
}

export interface ReconciliationResponse {
  requested: boolean;
}

export type UserLicenseState = 'None' | 'Active' | 'Suspended' | string;
export type LicenseKind = 'Code' | 'Manual' | string;
export type LicenseStatus = 'Created' | 'Active' | 'Redeemed' | 'Revoked' | 'Expired' | string;

export interface LicenseStatusResponse {
  state: UserLicenseState;
  kind?: LicenseKind | null;
  activatedAtUtc?: string | null;
  revokedAtUtc?: string | null;
}

export type ScheduleEventType = string;

export interface UserScheduleEventView {
  stableIdentity: string;
  title: string;
  localDate: string;
  startLocalTime?: string | null;
  endLocalTime?: string | null;
  isAllDay: boolean;
  timeZoneId: string;
  location?: string | null;
  instructor?: string | null;
  eventType: ScheduleEventType;
  departments: string[];
}

export type UserScheduleChangeKind = 'Created' | 'Updated';

export interface UserScheduleChangeView {
  stableIdentity: string;
  title: string;
  localDate: string;
  kind: UserScheduleChangeKind;
  changedAtUtc: string;
}

export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}

export interface AdminUserListItem {
  id: string;
  email: string;
  displayName?: string | null;
  role: UserRole;
  licenseState: UserLicenseState;
  hasProfile: boolean;
  createdAtUtc: string;
  lastSignedInAtUtc: string;
}

export interface AdminUserProfile {
  academicYear: string;
  classYear: number;
  programLanguage: ProgramLanguage;
  studentNumber: string;
  selectorSchemaVersion: string;
  selectors: Record<string, string>;
}

export interface AdminUserLicense {
  licenseId: string;
  kind: LicenseKind;
  status: LicenseStatus;
  createdAtUtc: string;
  redeemedAtUtc?: string | null;
  revokedAtUtc?: string | null;
}

export interface AdminUserDetail {
  summary: AdminUserListItem;
  profile?: AdminUserProfile | null;
  managedEventCount: number;
  licenses: AdminUserLicense[];
}

export interface AdminUserDetailResponse {
  user: AdminUserDetail;
  onboardingState: OnboardingState;
  recentSignIns: AuditEventView[];
}

export interface AdminLicenseListItem {
  licenseId: string;
  kind: LicenseKind;
  status: LicenseStatus;
  createdByEmail: string;
  createdAtUtc: string;
  expiresAtUtc?: string | null;
  redeemedByUserId?: string | null;
  redeemedAtUtc?: string | null;
  revokedAtUtc?: string | null;
  notes?: string | null;
}

export interface AdminLicenseAuditEntry {
  action: string;
  actorEmail: string;
  reason: string;
  occurredAtUtc: string;
}

export interface AdminLicenseDetail {
  summary: AdminLicenseListItem;
  audit: AdminLicenseAuditEntry[];
}

export type ScheduleSourceTransport = string;
export type ParseRunStatus = string;

export interface SourceStatusListItem {
  sourceId: string;
  displayName: string;
  classYear: number;
  programLanguage: ProgramLanguage;
  transport: ScheduleSourceTransport;
  isPollingEnabled: boolean;
  lastPolledAtUtc?: string | null;
  lastChangedAtUtc?: string | null;
  latestParseRunStatus?: ParseRunStatus | null;
  latestParseRunAtUtc?: string | null;
  latestParseWarningCount?: number | null;
  latestParseErrorCount?: number | null;
  latestRevisionId?: string | null;
  latestRevisionState?: RevisionState | null;
  latestRevisionAtUtc?: string | null;
}

export interface SourceSnapshotSummary {
  snapshotId: string;
  acquiredAtUtc: string;
  contentHash: string;
  worksheetCount: number;
  cellCount: number;
  diagnosticCount: number;
  hasPayload: boolean;
}

export interface SourceStatusDetail {
  summary: SourceStatusListItem;
  parserProfile: string;
  parserProfileVersion: string;
  recentSnapshots: SourceSnapshotSummary[];
}

export type AuditEventCategory = 'SignIn' | 'ReconcileRequested' | 'IpUnmasked' | string;

export interface AuditEventView {
  id: string;
  category: AuditEventCategory;
  occurredAtUtc: string;
  actorUserId?: string | null;
  actorEmail?: string | null;
  subjectType?: string | null;
  subjectId?: string | null;
  correlationId?: string | null;
  maskedIp?: string | null;
  hasProtectedIp: boolean;
  userAgent?: string | null;
  reason?: string | null;
  metadata?: string | null;
}

export interface UnmaskAuditIpResponse {
  auditEventId: string;
  ip: string;
}

export interface AdminMetricsSnapshot {
  generatedAtUtc: string;
  totalUsers: number;
  activeLicenses: number;
  initialSyncsInProgress: number;
  completedConnections: number;
  revisionsAwaitingReview: number;
  heldDiffs: number;
  pollingSourcesOverdue: number;
  operationalFreezeActive: boolean;
}

export interface HealthStatus {
  ok: boolean;
  status: number;
  text: string;
}

export interface CalendarSyncResponse {
  connection: GoogleCalendarConnectionView;
  onboarding: OnboardingSnapshot;
}

export type DepartmentDivision = 'Basic' | 'Internal' | 'Surgical';
export type CalendarColorKind = 'EventCategory' | 'Department';

export interface DepartmentColorView {
  key: string;
  name: string;
  kind: CalendarColorKind;
  division?: DepartmentDivision | null;
  description?: string | null;
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

export interface CreatedLicense {
  licenseId: string;
  plaintextCode: string;
  status: string;
  expiresAtUtc?: string | null;
  createdAtUtc: string;
}

export interface LicenseRevocationResult {
  outcome: string;
  affectedUserId?: string | null;
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
