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
  /**
   * The academic year this program's own sources were captured for. It may
   * differ from the schema's during a rollover, because the faculty publishes
   * one grade at a time (ADR-103).
   */
  academicYear: string;
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
  /**
   * Whether the change altered the audience the profile resolves and therefore queued a calendar
   * re-synchronization (ADR-096). It reports that the work was *requested*: the worker converges
   * the calendar on its next cycle, so no screen may present it as a finished synchronization.
   * It is false for a first profile and for a change the audience rule does not read.
   */
  calendarResyncRequested: boolean;
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
  /** Null when the account has no academic profile yet. */
  academicYear?: string | null;
  classYear?: number | null;
  programLanguage?: ProgramLanguage | null;
  studentNumber?: string | null;
  /** Null when the account has never authorized Calendar access. */
  calendarStatus?: GoogleCalendarConnectionStatus | null;
  initialSyncState?: GoogleCalendarInitialSyncState | null;
  /** What the mapping ledger says is on this user's managed calendar. */
  managedEventCount: number;
  createdAtUtc: string;
  lastSignedInAtUtc: string;
}

export type AdminUserSort = 'CreatedAtUtc' | 'LastSignedInAtUtc' | 'Email';

/**
 * Every filter the account directory accepts. An absent value means "do not filter on this",
 * never a default — a narrower result set is always explained by a filter that was chosen.
 */
export interface AdminUserFilters {
  /** Matched against e-mail, display name, and the student number as a prefix. */
  search?: string;
  role?: UserRole;
  licenseState?: UserLicenseState;
  hasProfile?: boolean;
  academicYear?: string;
  classYear?: number;
  programLanguage?: ProgramLanguage;
  /** Academic-profile selectors that must all match, e.g. `{ practiceGroup: 'A' }`. */
  selectors?: Record<string, string>;
  hasCalendarConnection?: boolean;
  calendarStatus?: GoogleCalendarConnectionStatus;
  initialSyncState?: GoogleCalendarInitialSyncState;
  createdFromUtc?: string;
  createdToUtc?: string;
  lastSignedInFromUtc?: string;
  lastSignedInToUtc?: string;
  sort?: AdminUserSort;
  descending?: boolean;
  page?: number;
  pageSize?: number;
}

export interface AdminUserProfile {
  academicYear: string;
  classYear: number;
  programLanguage: ProgramLanguage;
  studentNumber: string;
  selectorSchemaVersion: string;
  selectors: Record<string, string>;
  updatedAtUtc: string;
}

/**
 * What an operator may know about a user's Calendar authorization. The refresh token and the
 * granted scopes are deliberately absent from the backend projection.
 */
export interface AdminUserCalendarConnection {
  status: GoogleCalendarConnectionStatus;
  initialSyncState: GoogleCalendarInitialSyncState;
  hasManagedCalendar: boolean;
  managedCalendarUnavailableAtUtc?: string | null;
  lastCalendarInventoryAtUtc?: string | null;
  /** Set while a profile change waits for the re-synchronization stage (ADR-096). */
  profileResyncRequiredSinceUtc?: string | null;
  /** Set while a dead credential's missed diffs wait for replay (ADR-060). */
  reconciliationRequiredSinceUtc?: string | null;
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
  calendarConnection?: AdminUserCalendarConnection | null;
}

export interface AdminUserDetailResponse {
  user: AdminUserDetail;
  onboardingState: OnboardingState;
  recentSignIns: AuditEventView[];
  /** Recent audit events across every category, not only sign-ins. */
  recentActivity: AuditEventView[];
}

/**
 * What is actually on a user's managed calendar over a local-date window, read from the mapping
 * ledger. The server echoes the window it resolved, so a caller that passed no dates does not have
 * to guess which days it is looking at.
 */
export interface AdminUserCalendarEventsResponse {
  fromLocalDate: string;
  toLocalDate: string;
  timeZoneId: string;
  events: UserScheduleEventView[];
}

export interface ManualLicenseActivationResult {
  outcome: 'Activated' | 'AlreadyActivated' | 'UserNotFound' | string;
  licenseId?: string | null;
  userId: string;
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

export type ParserWarningSeverity = 'Information' | 'Warning' | 'Error' | string;

export interface ParserSourceEvidence {
  sheetId: string;
  sheetTitle: string;
  range: string;
  rawText?: string | null;
  extractionRule: string;
}

export interface ParserWarningView {
  severity: ParserWarningSeverity;
  code: string;
  message: string;
  candidateId?: string | null;
  evidence?: ParserSourceEvidence | null;
}

export interface SourceStatusDetail {
  summary: SourceStatusListItem;
  parserProfile: string;
  parserProfileVersion: string;
  latestParseWarnings: ParserWarningView[];
  recentSnapshots: SourceSnapshotSummary[];
}

/**
 * The editable schedule source catalog document (ADR-114).
 *
 * The browser holds the document as text and parses it itself, so the form editor and the raw
 * JSON editor are two views of one string and neither can silently drop a field the backend
 * model does not mirror here.
 */
export interface ScheduleSourceCatalogDocument {
  path: string;
  content: string;
  contentHash: string;
  lastModifiedUtc?: string | null;
  isWritable: boolean;
  isValid: boolean;
  validationError?: string | null;
  catalogVersion?: string | null;
  sourceCount?: number | null;
}

/** One source entry as the catalog document states it. Mirrors the backend definition. */
export interface ScheduleSourceCatalogEntry {
  sourceId: string;
  displayName: string;
  transport: string;
  documentFormat: string;
  sourceUri: string;
  externalId?: string | null;
  sheetGid?: number | null;
  parserProfile: string;
  parserProfileVersion: string;
  academicYear: string;
  classYear: number;
  programLanguage: string;
  timeZoneId: string;
  supportedAudienceSelectors?: Record<string, string[]> | null;
  authoritativeAudienceSelectors?: Record<string, string[]> | null;
  sharedDocumentGroup?: string | null;
  companionSourceIds?: string[] | null;
  fixturePath?: string | null;
  notes?: string | null;
}

export interface ScheduleSourceCatalogFile {
  catalogVersion: string;
  sources: ScheduleSourceCatalogEntry[];
}

export type ScheduleSourceCatalogChangeRisk = 'Low' | 'High';

export type ScheduleSourceCatalogChangeKind = 'Added' | 'Removed' | 'Modified';

export interface ScheduleSourceCatalogFieldChange {
  field: string;
  before?: string | null;
  after?: string | null;
  risk: ScheduleSourceCatalogChangeRisk;
}

export interface ScheduleSourceCatalogSourceChange {
  sourceId: string;
  displayName: string;
  program: string;
  kind: ScheduleSourceCatalogChangeKind;
  fields: ScheduleSourceCatalogFieldChange[];
  isHighRisk: boolean;
}

export interface ScheduleSourceCatalogWarning {
  code: string;
  message: string;
  risk: ScheduleSourceCatalogChangeRisk;
}

export interface ScheduleSourceCatalogPlan {
  planHash: string;
  baseContentHash: string;
  proposedContentHash: string;
  normalizedContent: string;
  sourceCount: number;
  added: ScheduleSourceCatalogSourceChange[];
  removed: ScheduleSourceCatalogSourceChange[];
  modified: ScheduleSourceCatalogSourceChange[];
  unchangedCount: number;
  warnings: ScheduleSourceCatalogWarning[];
  hasHighRiskChange: boolean;
  hasChanges: boolean;
}

export interface ScheduleSourceCatalogApplyResult {
  revisionId: string;
  contentHash: string;
  appliedAtUtc: string;
  sourceRowsChanged: number;
  pollingDisabledSourceIds: string[];
  plan: ScheduleSourceCatalogPlan;
}

export interface ScheduleSourceCatalogRevisionSummary {
  id: string;
  kind: 'Baseline' | 'Edit' | string;
  recordedAtUtc: string;
  contentHash: string;
  previousContentHash?: string | null;
  sourceCount: number;
  actorUserId?: string | null;
  actorEmail?: string | null;
  reason?: string | null;
  changeSummary?: string | null;
  isCurrent: boolean;
}

export interface ScheduleSourceCatalogRevisionDetail {
  summary: ScheduleSourceCatalogRevisionSummary;
  content: string;
}

export type AuditEventCategory =
  | 'SignIn'
  | 'ReconcileRequested'
  | 'IpUnmasked'
  | 'ProfileUpdated'
  | 'FinanceTransactionDeleted'
  | 'FinanceDistributionExecuted'
  | 'ScheduleSourceCatalogUpdated'
  | string;

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

export type ServiceHealthState = 'Healthy' | 'Unhealthy' | 'Unknown' | string;

export interface ServiceHealthView {
  service: string;
  state: ServiceHealthState;
  lastSeenAtUtc?: string | null;
  detail?: string | null;
}

export interface AdminServiceHealthSnapshot {
  checkedAtUtc: string;
  worker: ServiceHealthView;
  parser: ServiceHealthView;
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
  scope?: OperationalFreezeScope | null;
  changedBy?: string | null;
  reason?: string | null;
  correlationId?: string | null;
  changedAtUtc?: string | null;
}

export interface OperationalFreezeScope {
  classYear: number;
  programLanguage: ProgramLanguage;
}

export type OperationalFreezeChangeOutcome = 'Changed' | 'AlreadyInRequestedState' | string;

export interface OperationalFreezeChangeResult {
  outcome: OperationalFreezeChangeOutcome;
  state: OperationalFreezeSnapshot;
}

// Revision review (SuperAdmin): GET /api/revisions/?state=…, GET /api/revisions/{id},
// POST /api/revisions/{id}/approve, POST /api/revisions/{id}/reject (ADR-032, ADR-097)
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
  /** Set only on a terminally rejected revision; never the approval fields (ADR-097). */
  rejectedBy?: string | null;
  rejectionReason?: string | null;
  rejectedAtUtc?: string | null;
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
export type SourceDocumentUploadOutcome = 'Stored' | 'Unchanged' | 'Frozen' | string;

export interface SourceDocumentUploadTarget {
  sourceId: string;
  classYear: number;
  programLanguage: ProgramLanguage;
  outcome: SourceDocumentUploadOutcome;
  snapshotId?: string | null;
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

export interface RejectRevisionResponse {
  revisionId: string;
  rejected: boolean;
}

// Diff review (SuperAdmin): GET /api/diffs/?state=…|?dispatchState=…, GET /api/diffs/{id},
// POST /api/diffs/{id}/release (ADR-042), POST /api/diffs/{id}/retry (ADR-097).
//
// The two operator queues are orthogonal. `state` answers "may this diff be acted on" — the
// held-review queue. `dispatchState` answers "has it been" — the only way to find a diff whose
// fan-out failed terminally, since such a diff is still Ready or Released in its review state.
export type ScheduleDiffState = 'Ready' | 'Held' | 'Released' | string;

export type CalendarDispatchState = 'Pending' | 'Dispatched' | 'Failed' | string;

export type ScheduleDiffChange = 'Created' | 'Updated' | 'Deleted' | 'Unchanged' | 'Ambiguous' | string;

export type ScheduleDiffMatch = 'None' | 'ExactStableIdentity' | 'SecondaryAttributes' | string;

export interface ScheduleDiffSummary {
  scheduleDiffId: string;
  sourceId: string;
  state: ScheduleDiffState;
  currentRevisionId: string;
  previousRevisionId?: string | null;
  createdCount: number;
  updatedCount: number;
  deletedCount: number;
  unchangedCount: number;
  ambiguousCount: number;
  previousRecordCount: number;
  currentRecordCount: number;
  createdAtUtc: string;
  holdReason?: string | null;
  /**
   * Whether an operator may release it. False on an ambiguity hold, which is only ever fixed at
   * the source (ADR-042) — the UI must present that as a refusal, not a disabled button with no
   * explanation.
   */
  isReleasable: boolean;
  releasedBy?: string | null;
  releaseReason?: string | null;
  releasedAtUtc?: string | null;
  calendarDispatchState: CalendarDispatchState;
  dispatchAttempts: number;
  dispatchedAtUtc?: string | null;
  /** Why the last dispatch attempt failed; the operator's evidence for a retry. */
  dispatchFailureReason?: string | null;
  isDispatchRetriable: boolean;
  /** How many times an operator has already retried it. A rising count is the real signal. */
  dispatchRetryCount: number;
  lastDispatchRetriedBy?: string | null;
  lastDispatchRetryReason?: string | null;
  lastDispatchRetriedAtUtc?: string | null;
}

/** One lesson as a revision published it. Times are null on both for an all-day item. */
export interface ScheduleDiffRecordView {
  recordId: string;
  displayTitle: string;
  localDate: string;
  startLocalTime?: string | null;
  endLocalTime?: string | null;
  isAllDay: boolean;
  audienceSelectors: string;
  instructor?: string | null;
  location?: string | null;
}

export interface ScheduleDiffEntryView {
  change: ScheduleDiffChange;
  match: ScheduleDiffMatch;
  matchScore?: number | null;
  previous?: ScheduleDiffRecordView | null;
  current?: ScheduleDiffRecordView | null;
}

export interface ScheduleDiffDetail {
  summary: ScheduleDiffSummary;
  entries: ScheduleDiffEntryView[];
  /** How many actionable entries exist, which may exceed the returned ones. */
  actionableEntryCount: number;
}

export interface ReleaseDiffResponse {
  scheduleDiffId: string;
  released: boolean;
  releasedAtUtc?: string | null;
}

export interface RetryDiffResponse {
  scheduleDiffId: string;
  retried: boolean;
  retriedAtUtc?: string | null;
  dispatchRetryCount: number;
}

// Finance administration (SuperAdmin): /api/admin/finance/*
export type FinanceAccountKind = 'Cash' | 'Bank';
export type FinanceAccountStatus = 'Active' | 'Closed';
export type FinanceAccountHolderStatus = 'Active' | 'Inactive';
export type FinanceTransactionKind = 'OpeningBalance' | 'Income' | 'Expense' | 'Transfer' | 'Distribution';
export type FinanceLedgerLeg = 'Single' | 'From' | 'To';
export type FinanceCategory =
  | 'LicenseSales' | 'Sponsorship' | 'Donation' | 'OtherIncome'
  | 'Servers' | 'Domains' | 'ExternalServices' | 'SoftwareLicenses'
  | 'Marketing' | 'Operational' | 'Charitable' | 'OtherExpense';
export type FinancePeriodSelector =
  | 'CurrentMonth' | 'PreviousMonth' | 'NextMonth'
  | 'LastThreeMonths' | 'NextThreeMonths' | 'Custom';

export interface FinanceAccountHolderListItem {
  holderId: string;
  displayName: string;
  userId?: string | null;
  shareBasisPoints: number;
  status: FinanceAccountHolderStatus;
}

export interface FinanceAccountListItem {
  accountId: string;
  financeAccountHolderId: string;
  holderDisplayName: string;
  name: string;
  kind: FinanceAccountKind;
  currencyCode: string;
  status: FinanceAccountStatus;
  openedOn: string;
  currentBalance: number;
  balanceAsOfOn: string;
}

export interface FinanceTransactionListItemEntry {
  financeAccountId: string;
  accountName: string;
  leg: FinanceLedgerLeg;
  amount: number;
}

export interface FinanceTransactionListItem {
  transactionId: string;
  kind: FinanceTransactionKind;
  category?: FinanceCategory | null;
  amount: number;
  occurredOn: string;
  description: string;
  reference?: string | null;
  counterpartyName?: string | null;
  revisionNumber: number;
  entries: FinanceTransactionListItemEntry[];
}

export interface FinanceTransactionDetail {
  transaction: FinanceTransactionListItem;
  rowVersion: number;
  createdByUserId: string;
  createdByEmail: string;
  createdAtUtc: string;
  updatedByUserId: string;
  updatedByEmail: string;
  updatedAtUtc: string;
}

export interface FinanceTransactionFilters {
  from?: string;
  to?: string;
  kind?: FinanceTransactionKind;
  category?: FinanceCategory;
  accountId?: string;
  holderId?: string;
  search?: string;
  page?: number;
  pageSize?: number;
}

export interface FinanceCategoryTotal {
  category: FinanceCategory;
  kind: FinanceTransactionKind;
  total: number;
}

export interface FinanceSummary {
  periodStartOn: string;
  periodEndOn: string;
  accountId?: string | null;
  carriedOver: number;
  income: number;
  expenses: number;
  balance: number;
  currentBalance: number;
  asOfOn: string;
  toBeCarriedOver: number;
  receivables: number;
  collections: number;
  debts: number;
  payments: number;
  periodStartsInFuture: boolean;
  periodIsClosed: boolean;
  categoryTotals: FinanceCategoryTotal[];
}

export interface FinanceTrendPoint {
  year: number;
  month: number;
  income: number;
  expenses: number;
  net: number;
}

export interface FinanceAccountHolderMutationResult {
  outcome: string;
  holderId?: string | null;
}

export interface FinanceAccountMutationResult {
  outcome: string;
  accountId?: string | null;
}

export interface FinanceTransactionMutationResult {
  outcome: string;
  transactionId?: string | null;
  revisionNumber?: number | null;
}

export type FinanceObligationDirection = 'Receivable' | 'Payable';
export type FinanceObligationStatus = 'Open' | 'PartiallySettled' | 'Settled' | 'WrittenOff' | 'Cancelled';

export interface FinanceObligationSettlementListItem {
  settlementId: string;
  transactionId: string;
  amount: number;
  settledOn: string;
  recordedAtUtc: string;
  reference?: string | null;
}

export interface FinanceObligationListItem {
  obligationId: string;
  direction: FinanceObligationDirection;
  category: FinanceCategory;
  counterpartyName: string;
  description?: string | null;
  amount: number;
  settledAmount: number;
  issuedOn: string;
  dueOn?: string | null;
  status: FinanceObligationStatus;
  rowVersion: number;
  settlements: FinanceObligationSettlementListItem[];
}

export interface FinanceObligationMutationResult {
  outcome: string;
  obligationId?: string | null;
  settlementId?: string | null;
  transactionId?: string | null;
}

export type FinanceDistributionPlanOutcome =
  | 'Ready' | 'NothingToDistribute' | 'NoEligiblePartners' | 'SharesDoNotSumToTotal'
  | 'SourceAccountNotFound' | 'SourceAccountClosed' | 'AlreadyDistributedForPeriod';
export type FinanceDistributionStatus = 'Executed' | 'Reversed';

export interface FinanceDistributionPlanShare {
  holderId: string;
  holderDisplayName: string;
  shareBasisPoints: number;
  exactShareMinorUnits: number;
  allocatedAmount: number;
  remainderUnitAwarded: boolean;
}

export interface FinanceDistributionExclusion {
  holderId: string;
  holderDisplayName: string;
  reason: 'NotAPartner' | 'HolderInactive' | 'HolderHasNoShare' | string;
}

export interface FinanceDistributionPlan {
  outcome: FinanceDistributionPlanOutcome;
  periodStartOn: string;
  periodEndOn: string;
  sourceAccountId?: string | null;
  distributableAmount: number;
  shares: FinanceDistributionPlanShare[];
  exclusions: FinanceDistributionExclusion[];
  confirmationToken?: string | null;
  planHash?: string | null;
  expectedConfirmationPhrase?: string | null;
}

export interface FinanceDistributionResult {
  outcome: string;
  distributionId?: string | null;
}

export interface FinanceDistributionListItem {
  distributionId: string;
  periodStartOn: string;
  periodEndOn: string;
  sourceFinanceAccountId: string;
  distributableAmount: number;
  status: FinanceDistributionStatus;
  executedAtUtc: string;
}

export type FinanceAuditAction =
  | 'AccountOpened' | 'AccountUpdated' | 'AccountClosed'
  | 'HolderCreated' | 'HolderUpdated' | 'HolderDeactivated' | 'PartnerSharesChanged'
  | 'TransactionCreated' | 'TransactionUpdated' | 'TransactionDeleted'
  | 'ObligationCreated' | 'ObligationUpdated' | 'ObligationSettled'
  | 'ObligationSettlementCancelled' | 'ObligationWrittenOff' | 'ObligationCancelled'
  | 'DistributionExecuted' | 'DistributionReversed';

export interface FinanceAuditListItem {
  sequence: number;
  action: FinanceAuditAction;
  subjectType: string;
  subjectId: string;
  actorUserId: string;
  actorEmail: string;
  occurredAtUtc: string;
  correlationId?: string | null;
  reason?: string | null;
  amountDelta: number;
  revisionNumber: number;
  changedFields: string[];
}

export interface FinanceAuditDetail {
  summary: FinanceAuditListItem;
  beforeState?: string | null;
  afterState?: string | null;
}

/** RFC 7807 problem details, as returned by AddProblemDetails / Results.Problem. */
export interface ProblemDetails {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  errors?: Record<string, string[]>;
}

// ---- Calendar announcements (ADR-107) --------------------------------------
// One backend domain behind two screens: a bulk cohort event and a single-user
// warning. They differ only in how the recipient set is decided, so the delivery
// ledger, the deduplication key and the cancel path are shared.

export type CalendarAnnouncementKind = 'Bulk' | 'UserWarning';

export type CalendarAnnouncementStatus =
  | 'Queued'
  | 'Delivering'
  | 'Delivered'
  | 'Cancelling'
  | 'Cancelled'
  | 'Failed';

export type CalendarAnnouncementDeliveryState =
  | 'Pending'
  | 'Written'
  | 'Skipped'
  | 'Removed'
  | 'Failed';

/**
 * Why a candidate cannot receive an announcement. These are facts about the account,
 * not filters an operator chose: none can be waived, because there is no calendar to
 * write to.
 */
export type AnnouncementExclusionReason =
  | 'NoStudentProfile'
  | 'LicenseInactive'
  | 'NoCalendarConnection'
  | 'CalendarAuthorizationRevoked'
  | 'InitialSyncIncomplete'
  | 'ManagedCalendarUnavailable';

export interface AnnouncementCategoryView {
  key: string;
  name: string;
  backgroundColor: string;
}

export interface AnnouncementTemplateView {
  key: string;
  name: string;
  suggestedTitle: string;
  suggestedBody: string;
  categoryKey: string;
}

/** GET /api/admin/announcements/options */
export interface AnnouncementCompositionOptions {
  categories: AnnouncementCategoryView[];
  templates: AnnouncementTemplateView[];
  /** Not operator-selectable; the server interprets every announcement in this zone. */
  timeZoneId: string;
  earliestLocalDate: string;
}

/** What the operator composed. The time zone is deliberately absent — the server owns it. */
export interface AnnouncementComposition {
  kind: CalendarAnnouncementKind;
  academicYear?: string | null;
  classYear?: number | null;
  programLanguage?: ProgramLanguage | null;
  selectors?: Record<string, string>;
  targetUserId?: string | null;
  templateKey?: string | null;
  title: string;
  body: string;
  location?: string | null;
  isAllDay: boolean;
  localDate: string;
  startLocalTime?: string | null;
  endLocalTime?: string | null;
  reminderMinutesBefore?: number | null;
  categoryKey: string;
  internalNote?: string | null;
}

export interface AnnouncementAudienceCandidate {
  userId: string;
  email: string;
  displayName?: string | null;
  classYear?: number | null;
  programLanguage?: ProgramLanguage | null;
  managedCalendarId?: string | null;
  exclusionReason?: AnnouncementExclusionReason | null;
}

export interface AnnouncementExclusionGroup {
  reason: AnnouncementExclusionReason;
  count: number;
}

export interface AnnouncementDeliveryCounts {
  pending: number;
  written: number;
  skipped: number;
  removed: number;
  failed: number;
  total: number;
}

export interface AnnouncementSummary {
  announcementId: string;
  kind: CalendarAnnouncementKind;
  campaignKey: string;
  title: string;
  status: CalendarAnnouncementStatus;
  contentVersion: number;
  localDate: string;
  isAllDay: boolean;
  startLocalTime?: string | null;
  endLocalTime?: string | null;
  recipientCount: number;
  counts: AnnouncementDeliveryCounts;
  createdBy: string;
  createdAtUtc: string;
  completedAtUtc?: string | null;
  lastFailureReason?: string | null;
  cancelledBy?: string | null;
  cancellationReason?: string | null;
}

export interface AnnouncementDetail {
  summary: AnnouncementSummary;
  body: string;
  location?: string | null;
  timeZoneId: string;
  reminderMinutesBefore?: number | null;
  categoryKey: string;
  templateKey?: string | null;
  internalNote?: string | null;
  audienceAcademicYear: string;
  audienceClassYear?: number | null;
  audienceProgramLanguage?: ProgramLanguage | null;
  audienceSelectors: Record<string, string>;
  targetUserId?: string | null;
  creationReason: string;
  lastUpdatedBy?: string | null;
  lastUpdateReason?: string | null;
  updatedAtUtc: string;
  planHash?: string | null;
  deliveryAttempts: number;
  exclusions: AnnouncementExclusionGroup[];
}

/**
 * The server-computed plan a confirmation is bound to. `planHash` covers the recipient
 * identities, not merely their count, so approving "412 recipients" cannot authorize
 * writing to a different 412 people.
 */
export interface AnnouncementPreview {
  campaignKey: string;
  planHash: string;
  recipientCount: number;
  excludedCount: number;
  exclusions: AnnouncementExclusionGroup[];
  recipients: AnnouncementAudienceCandidate[];
  excludedRecipients: AnnouncementAudienceCandidate[];
  /** Set when this campaign key already exists: confirming would be a replay. */
  existingAnnouncement?: AnnouncementSummary | null;
  confirmationPhrase: string;
}

export type CreateAnnouncementOutcome =
  | 'Queued'
  | 'AlreadyExists'
  | 'PlanChangedSincePreview'
  | 'ConfirmationMismatch'
  | 'NoRecipients'
  | 'Invalid';

export interface CreateAnnouncementResult {
  outcome: CreateAnnouncementOutcome;
  announcement?: AnnouncementSummary | null;
  detail?: string | null;
}

export interface UpdateAnnouncementResult {
  outcome: 'Updated' | 'NotFound' | 'Cancelled' | 'Invalid' | 'ConcurrentChange';
  announcement?: AnnouncementSummary | null;
  detail?: string | null;
}

export interface CancelAnnouncementResult {
  outcome: 'CancellationRequested' | 'AlreadyCancelled' | 'NotFound' | 'ConcurrentChange';
  announcement?: AnnouncementSummary | null;
}

export interface AnnouncementDeliveryView {
  userId: string;
  email: string;
  displayName?: string | null;
  state: CalendarAnnouncementDeliveryState;
  skipReason?: AnnouncementExclusionReason | null;
  appliedContentVersion?: number | null;
  failureReason?: string | null;
  updatedAtUtc: string;
}

/** The program a calendar repair is scoped to (ADR-111). */
export interface CohortRepairScope {
  academicYear: string;
  classYear: number;
  programLanguage: ProgramLanguage;
}

/** What a repair would converge for one student. */
export interface CohortRepairUserPlan {
  userId: string;
  /** Still-published events the student holds that are no longer theirs. These get deleted. */
  surplusEventCount: number;
  /** Events that apply to them and are not on the calendar. These get written. */
  missingEventCount: number;
  /** Rows whose lesson is no longer published; counted, never touched (ADR-089). */
  untouchableRetiredCount: number;
}

/**
 * The server-computed plan a confirmation is bound to. `planHash` covers the per-user counts,
 * not only the totals, so confirming one plan cannot authorize repairing a different set of
 * students.
 */
export interface CohortRepairPlan {
  scope: CohortRepairScope;
  users: CohortRepairUserPlan[];
  cohortUserCount: number;
  totalSurplusEvents: number;
  totalMissingEvents: number;
  /** Cohort-wide, and deliberately not the sum of `users` — see ADR-111. */
  totalUntouchableRetired: number;
  planHash: string;
}

export type CohortRepairOutcome =
  | 'Requested'
  | 'PlanChanged'
  | 'NothingToRepair'
  | 'Frozen'
  | string;

export interface CohortRepairRequestResult {
  outcome: CohortRepairOutcome;
  usersRequested: number;
  plan?: CohortRepairPlan | null;
}

/**
 * The program whose stored profiles a rollover moves onto the year its sources now state
 * (ADR-115).
 *
 * Only the year moved *from* is named. The target comes from the deployed schema, so an operator
 * cannot stamp a year new sign-ups would not get and split one cohort across two.
 */
export interface ProfileRolloverScope {
  fromAcademicYear: string;
  classYear: number;
  programLanguage: ProgramLanguage;
}

/** What rolling one student's profile forward would mean for their calendar. */
export interface ProfileRolloverUserPlan {
  userId: string;
  /** Lessons published for the target year that resolve to them and are not on the calendar. */
  gainedEventCount: number;
  /** Rows from the year being left that convergence will not remove — see ADR-089. */
  strandedEventCount: number;
  /** Whether a connection exists that can actually take the convergence request. */
  convergenceQueueable: boolean;
}

/**
 * The server-computed plan a confirmation is bound to. As with a repair, `planHash` covers the
 * per-user counts rather than only the totals.
 */
export interface ProfileRolloverPlan {
  scope: ProfileRolloverScope;
  /** Empty when the deployed schema does not state a different year for this program. */
  toAcademicYear: string;
  toSchemaVersion: string;
  users: ProfileRolloverUserPlan[];
  totalGainedEvents: number;
  totalStrandedEvents: number;
  profilesWithoutSyncReadyConnection: number;
  /** Profiles whose selectors the target program refuses; excluded from the move entirely. */
  blockedByInvalidSelectors: string[];
  planHash: string;
}

export type ProfileRolloverOutcome =
  | 'Moved'
  | 'PlanChanged'
  | 'NothingToMove'
  | 'Frozen'
  | 'NotSupportedBySchema'
  | string;

export interface ProfileRolloverRequestResult {
  outcome: ProfileRolloverOutcome;
  profilesMoved: number;
  convergenceRequested: number;
  plan?: ProfileRolloverPlan | null;
  refusal?: string | null;
}

export type ManagedCalendarRebuildOutcome =
  | 'Reset'
  | 'NotEligible'
  | 'NoConnection'
  | 'Frozen'
  | string;

/** Whether a managed calendar needs rebuilding, computed without changing anything (ADR-116). */
export interface ManagedCalendarRebuildAssessment {
  outcome: ManagedCalendarRebuildOutcome;
  /** When the calendar was first proven unreachable. Null unless `outcome` is `Reset`. */
  unavailableSinceUtc?: string | null;
}

export interface ManagedCalendarRebuildResult {
  outcome: ManagedCalendarRebuildOutcome;
  /** Ledger rows discarded — also the number of lessons the next sync will write again. */
  discardedMappings: number;
}
