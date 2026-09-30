// Mirrors src/BuildingBlocks/Academies.Contracts/Security/{Roles,Permissions}.cs; keep both in sync.

export const Roles = {
  SuperAdmin: 'SuperAdmin',
  Admin: 'Admin',
  Manager: 'Manager',
  Supervisor: 'Supervisor',
  Teacher: 'Teacher',
  Student: 'Student',
  Parent: 'Parent',
  Accountant: 'Accountant',
  Staff: 'Staff',
  Sales: 'Sales',
} as const;

export type Role = (typeof Roles)[keyof typeof Roles];

export const Permissions = {
  academies: { view: 'academies.view', manage: 'academies.manage' },
  users: { view: 'users.view', manage: 'users.manage' },
  plans: { view: 'plans.view', manage: 'plans.manage' },
  profiles: { view: 'profiles.view', manage: 'profiles.manage' },
  courses: { view: 'courses.view', manage: 'courses.manage' },
  sessions: { view: 'sessions.view', manage: 'sessions.manage' },
  attendance: { view: 'attendance.view', record: 'attendance.record' },
  feedback: { view: 'feedback.view', write: 'feedback.write' },
  payments: { view: 'payments.view', manage: 'payments.manage' },
  salaries: { view: 'salaries.view', manage: 'salaries.manage' },
  expenses: { manage: 'expenses.manage' },
  reports: { view: 'reports.view' },
  dashboards: { view: 'dashboards.view' },
  auditLogs: { view: 'auditlogs.view' },
  leads: { view: 'leads.view', manage: 'leads.manage' },
} as const;
