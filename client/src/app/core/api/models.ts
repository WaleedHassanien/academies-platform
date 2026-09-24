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
  groups: string[];
}

export interface StaffProfileDto {
  userId: number;
  fullName: string;
  email: string;
  isActive: boolean;
  specialization: string | null;
  notes: string | null;
  linkedCount: number;
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

export interface GroupDto {
  id: number;
  name: string;
  courseId: number | null;
  courseName: string | null;
  teacherUserId: number | null;
  teacherName: string | null;
  students: PersonRef[];
}

export interface CourseDto {
  id: number;
  name: string;
  description: string | null;
  level: string | null;
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
  groupId: number | null;
  groupName: string | null;
  teacherUserId: number;
  teacherName: string | null;
  startsAtUtc: string;
  endsAtUtc: string;
  type: 'Online' | 'Offline';
  meetingUrl: string | null;
  location: string | null;
  status: 'Scheduled' | 'Completed' | 'Cancelled';
  notes: string | null;
}

export interface RosterItemDto {
  studentUserId: number;
  fullName: string;
  attendanceStatus: string | null;
  attendanceNote: string | null;
  rating: number | null;
  comment: string | null;
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
  rating: number;
  comment: string | null;
  createdOnUtc: string;
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
  teacher: { students: PersonRef[]; upcoming: SessionDto[]; pendingToComplete: number; completedThisMonth: number } | null;
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
  groupId: number | null;
  groupName: string | null;
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
  groups: number;
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
  currency: string;
}

export interface FinanceSettingsDto {
  currency: string;
  available: string[];
}

/** PayPal charges USD: an EGP month shows both the amount due and what the card is charged. */
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
