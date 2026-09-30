// DTO shapes returned by the services (camelCase JSON). Kept in one place so pages share them.
import { PagedResult } from './api.models';

// ---------- Identity ----------
export interface AcademyDto {
  id: number;
  name: string;
  logoUrl: string | null;
  address: string | null;
  status: 'Active' | 'Suspended';
  createdOnUtc: string;
  userCount: number;
}

export interface UserDto {
  id: number;
  academyId: number | null;
  email: string;
  fullName: string;
  phoneNumber: string | null;
  isActive: boolean;
  roles: string[];
  lastLoginOnUtc: string | null;
  createdOnUtc: string;
}

export interface UsageDto {
  key: string;
  used: number;
  limit: number | null;
  percent: number | null;
}

export interface AcademyUsageDto {
  planName: string;
  status: string;
  items: UsageDto[];
}

// ---------- Subscription ----------
export interface PlanDto {
  id: number;
  code: string;
  name: string;
  monthlyPrice: number;
  trialDays: number;
  isActive: boolean;
  sortOrder: number;
  limits: Record<string, number>;
  features: Record<string, boolean>;
}

export interface Entitlements {
  academyId: number;
  planCode: string;
  planName: string;
  status: string;
  trialEndsOnUtc: string | null;
  limits: Record<string, number>;
  features: Record<string, boolean>;
}

export interface AcademySubscriptionDto {
  academyId: number;
  planId: number;
  planCode: string;
  planName: string;
  status: string;
  startsOnUtc: string;
  trialEndsOnUtc: string | null;
  endsOnUtc: string | null;
  overrides: { limitKey: string; value: number; reason: string | null }[];
  effective: Entitlements;
}

// ---------- Academic ----------
export interface PersonRef {
  userId: number;
  fullName: string;
}

export interface StudentDto {
  userId: number;
  fullName: string;
  email: string;
  level: string | null;
  enrollmentDate: string;
  status: string;
  parentUserId: number | null;
  parentName: string | null;
  /** Subjects the student is actively enrolled in. */
  subjects: string[];
  /** IANA zone, e.g. "Asia/Riyadh". */
  timeZone: string | null;
  teachers: { userId: number; fullName: string }[];
  /** Usual length of their session. */
  sessionMinutes: number;
  /** Who was chosen to pay (null = the guardian, else the student). */
  payerUserId: number | null;
  /** Who actually receives invoices and payment notices. */
  effectivePayerUserId: number;
  payerName: string | null;
}

/** The teacher's log of a session; every field optional. The Quran fields apply to Quran subjects. */
export interface SessionLogDto {
  rating: number | null;
  comment: string | null;
  accomplished: string | null;
  homework: string | null;
  memorization: string | null;
  revision: string | null;
  mistakes: number | null;
}

export type CourseKind = 'Quran' | 'Arabic' | 'IslamicStudies' | 'Other';

export interface EnrollmentDto {
  id: number;
  studentUserId: number;
  studentName: string | null;
  courseId: number;
  courseName: string | null;
  courseKind: CourseKind;
  teacherUserId: number | null;
  teacherName: string | null;
  status: 'Active' | 'Paused' | 'Ended';
  startedOn: string;
  endedOn: string | null;
}

export interface LearningPlanDto {
  id: number;
  studentUserId: number;
  courseId: number;
  courseName: string | null;
  courseKind: CourseKind | null;
  teacherUserId: number;
  teacherName: string | null;
  goal: string;
  reference: string | null;
  expectedAmount: string | null;
  targetDate: string | null;
  status: 'Active' | 'Achieved' | 'Closed';
  notes: string | null;
  createdOnUtc: string;
  updatedOnUtc: string | null;
}

// ---------- Sales: leads and trial sessions ----------
export type LeadStatus = 'New' | 'Contacted' | 'TrialScheduled' | 'TrialDone' | 'Converted' | 'Lost';
export type TrialStatus = 'Scheduled' | 'Attended' | 'NoShow' | 'Cancelled';

export interface TrialDto {
  leadId: number;
  leadName: string;
  phone: string | null;
  timeZone: string | null;
  courseId: number | null;
  courseName: string | null;
  teacherUserId: number;
  teacherName: string | null;
  startsAtUtc: string;
  endsAtUtc: string;
  status: TrialStatus;
  notes: string | null;
}

export interface LeadDto {
  id: number;
  fullName: string;
  phone: string | null;
  email: string | null;
  country: string | null;
  timeZone: string | null;
  isAdult: boolean;
  guardianName: string | null;
  courseId: number | null;
  courseName: string | null;
  source: string | null;
  status: LeadStatus;
  lostReason: string | null;
  assignedToUserId: number | null;
  assignedToName: string | null;
  nextFollowUpOn: string | null;
  notes: string | null;
  trial: TrialDto | null;
  convertedStudentUserId: number | null;
  convertedStudentName: string | null;
  createdOnUtc: string;
}

export interface LeadActivityDto {
  id: number;
  note: string;
  byUserId: number | null;
  byName: string | null;
  createdOnUtc: string;
}

export interface LeadSummaryDto {
  byStatus: Record<string, number>;
  bySource: Record<string, number>;
  total: number;
  conversionRate: number;
}

/** One session from a student's point of view (GET students/{id}/sessions). */
export interface StudentSessionDto {
  session: SessionDto;
  attendanceStatus: string | null;
  attendanceNote: string | null;
  rating: number | null;
  comment: string | null;
  log: SessionLogDto | null;
}

export interface StaffProfileDto {
  userId: number;
  fullName: string;
  email: string;
  isActive: boolean;
  specialization: string | null;
  notes: string | null;
  linkedCount: number;
  /** A teacher's subjects (empty = any). */
  courseIds: number[] | null;
}

export interface ParentDto {
  userId: number;
  fullName: string;
  email: string;
  occupation: string | null;
  children: { userId: number; fullName: string; level: string | null; status: string }[];
}

export interface WorkDayDto {
  day: string;
  isWorkingDay: boolean;
  shiftStart: string | null;
  shiftEnd: string | null;
}

export interface CourseDto {
  id: number;
  name: string;
  description: string | null;
  level: string | null;
  kind: CourseKind;
  isActive: boolean;
  materialCount: number;
}

export interface MaterialDto {
  id: number;
  courseId: number;
  title: string;
  url: string;
  type: string;
  createdOnUtc: string;
}

export interface SessionDto {
  id: number;
  title: string;
  courseId: number;
  courseName: string | null;
  courseKind: CourseKind | null;
  teacherUserId: number;
  teacherName: string | null;
  startsAtUtc: string;
  endsAtUtc: string;
  /** The teacher's room, or a link typed in by hand. Every session is online. */
  meetingUrl: string | null;
  status: 'Scheduled' | 'Completed' | 'Cancelled' | 'Excused';
  notes: string | null;
  /** Every session is one teacher with one student. */
  studentUserId: number;
  studentName: string | null;
  makeupOfSessionId: number | null;
  /** When the report went to the guardian (or adult student). */
  reportSentOnUtc: string | null;
  /** The student's attendance. */
  attendanceStatus: string | null;
  /** Supervisor's call on an unexcused absence: counts (billed and paid) or not; null = undecided. */
  absenceCounted: boolean | null;
  excuse: SessionExcuseDto | null;
  durationMinutes: number;
}

/** A personal link into a teacher's online room. */
export interface JoinLinkDto {
  url: string;
  isModerator: boolean;
  expiresAtUtc: string;
}

export type ExcuseResolution = 'Rescheduled' | 'CarriedOver' | 'NotCounted' | 'DeductedNextMonth';

export interface SessionExcuseDto {
  id: number;
  sessionId: number;
  studentUserId: number;
  reason: string | null;
  preferredStartsAtUtc: string | null;
  status: 'Pending' | 'Resolved' | 'Rejected';
  resolution: ExcuseResolution | null;
  makeupSessionId: number | null;
  resolutionNote: string | null;
  createdOnUtc: string;
}

export interface ExcuseItemDto {
  excuse: SessionExcuseDto;
  session: SessionDto;
  requestedByName: string | null;
}

export interface SessionCountsDto {
  scheduled: number;
  held: number;
  absentCounted: number;
  absentNotCounted: number;
  absentPending: number;
  excused: number;
  cancelled: number;
  heldMinutes: number;
  counted: number;
}

export interface SessionReportRowDto {
  userId: number;
  fullName: string;
  counts: SessionCountsDto;
}

export interface SessionReportDto {
  fromUtc: string;
  toUtc: string;
  totals: SessionCountsDto;
  byTeacher: SessionReportRowDto[];
  byStudent: SessionReportRowDto[];
  pendingExcuses: number;
  pendingAbsences: number;
}

// ---------- Per-session billing and teacher payouts ----------

export type BillingMode = 'Prepaid' | 'Postpaid';

export interface TeacherRateDto {
  teacherUserId: number;
  teacherName: string | null;
  studentUserId: number;
  studentName: string | null;
  ratePerSession: number;
}

/** One billing per subject (courseId) or one for all subjects (courseId null). */
export interface StudentBillingDto {
  id: number;
  studentUserId: number;
  courseId: number | null;
  mode: BillingMode;
  pricePerSession: number;
  sessionsPerMonth: number;
  monthlyPrice: number;
  dueDay: number;
  currency: string;
  packageId: number | null;
  sessionMinutes: number | null;
  rates: TeacherRateDto[];
}

export interface PackageDto {
  id: number;
  name: string;
  sessionsPerMonth: number;
  sessionMinutes: number;
  currency: string;
  monthlyPrice: number;
  pricePerSession: number;
  isActive: boolean;
}

/** A payer's saved card that renews the student's invoices automatically. */
export interface AutoPayDto {
  id: number;
  studentUserId: number;
  payerUserId: number;
  provider: string;
  status: 'Pending' | 'Active' | 'Cancelled';
  cardBrand: string | null;
  cardLast4: string | null;
  activatedOnUtc: string | null;
  lastError: string | null;
}

export interface BillingSummaryDto {
  billingId: number;
  courseId: number | null;
  studentUserId: number;
  mode: BillingMode;
  pricePerSession: number;
  sessionsPerMonth: number;
  currency: string;
  year: number;
  month: number;
  countedThisMonth: number;
  carriedIn: number;
  packageRemaining: number;
  unbilledAmount: number;
  outstanding: number;
}

export interface UnpaidSessionDto {
  sessionId: number;
  studentUserId: number;
  studentName: string | null;
  startsAtUtc: string;
  durationMinutes: number;
  outcome: string;
  rate: number | null;
}

export interface UnpaidTeacherDto {
  teacherUserId: number;
  teacherName: string | null;
  sessions: UnpaidSessionDto[];
  total: number;
  missingRates: number;
  currency: string;
}

export interface PayoutLineDto {
  sessionId: number;
  studentUserId: number;
  studentName: string | null;
  sessionStartsAtUtc: string;
  durationMinutes: number;
  outcome: string;
  rate: number;
}

export interface PayoutDto {
  id: number;
  teacherUserId: number;
  teacherName: string | null;
  kind: 'Interim' | 'MonthEnd';
  year: number;
  month: number;
  sessionsCount: number;
  amount: number;
  currency: string;
  status: 'Pending' | 'Paid';
  createdOnUtc: string;
  paidOnUtc: string | null;
  reference: string | null;
  note: string | null;
  lines: PayoutLineDto[] | null;
}

export interface MyEarningsDto {
  unpaid: UnpaidTeacherDto;
  payouts: PayoutDto[];
}

export interface MonthCloseResultDto {
  year: number;
  month: number;
  payouts: number;
  paidSessions: number;
  sessionsMissingRates: number;
  invoicesCreated: number;
  invoicesUpdated: number;
}

export interface RosterItemDto {
  studentUserId: number;
  fullName: string;
  attendanceStatus: string | null;
  attendanceNote: string | null;
  rating: number | null;
  comment: string | null;
  log: SessionLogDto | null;
}

export interface FeedbackDto {
  id: number;
  sessionId: number;
  sessionTitle: string;
  sessionStartsAtUtc: string;
  studentUserId: number;
  studentName: string | null;
  teacherUserId: number;
  teacherName: string | null;
  rating: number | null;
  comment: string | null;
  createdOnUtc: string;
  log: SessionLogDto | null;
  courseName: string | null;
}

export interface AttendanceSummary {
  present: number;
  late: number;
  absent: number;
  total: number;
  rate: number;
}

export interface StudentOverviewDto {
  userId: number;
  fullName: string;
  level: string | null;
  attendance: AttendanceSummary;
  upcoming: SessionDto[];
  recentFeedback: FeedbackDto[];
  points: number;
}

export interface MyOverviewDto {
  teacher: {
    students: PersonRef[];
    upcoming: SessionDto[];
    pendingToComplete: number;
    completedThisMonth: number;
    trials: TrialDto[] | null;
  } | null;
  supervisor: {
    teachers: { userId: number; fullName: string; students: number; completedThisMonth: number }[];
    upcoming: SessionDto[];
  } | null;
  student: StudentOverviewDto | null;
  children: StudentOverviewDto[] | null;
}

export interface SubmissionDto {
  id: number;
  assignmentId: number;
  studentUserId: number;
  studentName: string | null;
  content: string | null;
  attachmentUrl: string | null;
  submittedAtUtc: string;
  isLate: boolean;
  score: number | null;
  teacherFeedback: string | null;
  gradedAtUtc: string | null;
}

export interface AssignmentDto {
  id: number;
  courseId: number;
  courseName: string | null;
  /** For one student; null = every student enrolled in the subject. */
  studentUserId: number | null;
  studentName: string | null;
  teacherUserId: number;
  teacherName: string | null;
  title: string;
  description: string | null;
  dueAtUtc: string;
  maxScore: number;
  submissionCount: number;
  mySubmission: SubmissionDto | null;
}

export interface CertificateDto {
  id: number;
  studentUserId: number;
  studentName: string | null;
  courseId: number;
  courseName: string | null;
  number: string;
  issuedOnUtc: string;
}

export interface StudentPointsDto {
  studentUserId: number;
  fullName: string | null;
  total: number;
  badges: { code: string; name: string; threshold: number; awardedOnUtc: string | null }[];
  recent: { points: number; reason: string; createdOnUtc: string }[];
}

export interface LeaderboardEntry {
  rank: number;
  studentUserId: number;
  fullName: string;
  total: number;
}

export interface AcademicStats {
  activeStudents: number;
  teachers: number;
  sessionsScheduled: number;
  sessionsCompleted: number;
  sessionsCancelled: number;
  attendanceRate: number;
  monthly: { month: string; sessionsCompleted: number; attendanceRate: number }[];
  topTeachers: { userId: number; fullName: string; sessions: number }[];
}

// ---------- Finance ----------
export interface PaymentPlanDto {
  id: number;
  studentUserId: number;
  studentName: string | null;
  monthlyAmount: number;
  currency: string;
  startDate: string;
  endDate: string;
  dueDay: number;
  status: string;
  months: number;
  totalAmount: number;
  paidAmount: number;
}

export interface StudentPaymentDto {
  id: number;
  paymentPlanId: number;
  studentUserId: number;
  monthNumber: number;
  periodStart: string;
  dueDate: string;
  amount: number;
  paidAmount: number;
  remaining: number;
  currency: string;
  status: string;
  paidOnUtc: string | null;
}

export interface StudentPaymentsDto {
  studentUserId: number;
  studentName: string | null;
  months: StudentPaymentDto[];
  totalDue: number;
  totalPaid: number;
  outstanding: number;
  currency: string;
}

export interface PaymentLogDto {
  id: number;
  studentPaymentId: number;
  studentUserId: number;
  studentName: string | null;
  parentUserId: number | null;
  monthNumber: number;
  amount: number;
  currency: string;
  action: string;
  paidByRole: string | null;
  method: string | null;
  reference: string | null;
  note: string | null;
  createdAt: string;
}

export interface PaymentLogPageDto {
  logs: PagedResult<PaymentLogDto>;
  totalPaid: number;
  totalRefunded: number;
}

export interface CompensationDto {
  id: number;
  userId: number;
  payType: 'MonthlyFixed' | 'PerSession';
  amount: number;
  effectiveFrom: string;
  note: string | null;
  createdOnUtc: string;
}

export interface StaffPayDto {
  userId: number;
  fullName: string;
  roles: string;
  isActive: boolean;
  current: CompensationDto | null;
}

export interface SalaryDto {
  id: number;
  userId: number;
  fullName: string | null;
  role: string;
  year: number;
  month: number;
  payType: string;
  sessionsCount: number | null;
  ratePerSession: number | null;
  amount: number;
  status: 'Pending' | 'Paid';
  paidOnUtc: string | null;
  note: string | null;
}

export interface GenerateResultDto {
  year: number;
  month: number;
  created: number;
  regenerated: number;
  unchanged: number;
  skippedPaid: number;
  salaries: SalaryDto[];
}

export interface SalaryLogDto {
  id: number;
  salaryId: number;
  userId: number;
  fullName: string | null;
  role: string;
  year: number;
  month: number;
  payType: string;
  sessionsCount: number | null;
  ratePerSession: number | null;
  amount: number;
  action: string;
  paidByUserId: number | null;
  note: string | null;
  createdAt: string;
}

export interface ExpenseDto {
  id: number;
  category: string;
  description: string | null;
  amount: number;
  spentOn: string;
  reference: string | null;
  createdOnUtc: string;
}

export interface FinanceSummaryDto {
  from: string;
  to: string;
  revenue: number;
  refunds: number;
  salaries: number;
  expenses: number;
  net: number;
  outstanding: number;
  monthly: { month: string; revenue: number; salaries: number; expenses: number; net: number }[];
  expensesByCategory: Record<string, number>;
  /** Base currency of the totals; other currencies are converted with the academy's rates. */
  currency: string;
  revenueByCurrency: Record<string, number> | null;
  outstandingByCurrency: Record<string, number> | null;
  /** Currencies left out of the totals for lack of an exchange rate. */
  missingRates: string[] | null;
}

export interface FinanceSettingsDto {
  currency: string;
  available: string[];
  /** One unit of each currency in the base currency. */
  exchangeRates: Record<string, number>;
}

/** PayPal can't charge EGP or SAR: such a month shows both the amount due and what the card is charged. */
export interface CheckoutDto {
  checkoutUrl: string;
  reference: string;
  amount: number;
  currency: string;
  chargedAmount: number;
  chargedCurrency: string;
}

// ---------- Engagement ----------
export interface NotificationDto {
  id: number;
  type: string;
  titleAr: string;
  titleEn: string;
  bodyAr: string | null;
  bodyEn: string | null;
  link: string | null;
  isRead: boolean;
  createdOnUtc: string;
}

export interface DashboardDto {
  from: string;
  to: string;
  scope: string;
  academic: AcademicStats;
  finance: FinanceSummaryDto | null;
}

export interface AuditLogDto {
  id: number;
  academyId: number | null;
  userId: number | null;
  userName: string | null;
  service: string;
  action: string;
  entityName: string;
  entityId: string | null;
  dataJson: string | null;
  occurredOnUtc: string;
}
