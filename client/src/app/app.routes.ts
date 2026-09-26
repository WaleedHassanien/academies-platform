import { Routes } from '@angular/router';
import { authGuard, guestGuard, permissionGuard, roleHomeRedirect } from './core/auth/auth.guards';
import { Permissions } from './core/auth/permissions';

const academic = () => import('./features/academic/groups-courses');
const sessions = () => import('./features/academic/sessions');
const learning = () => import('./features/academic/learning');
const payments = () => import('./features/finance/payments');
const salaries = () => import('./features/finance/salaries');
const roles = () => import('./features/roles/role-pages');

export const routes: Routes = [
  {
    path: 'login',
    canActivate: [guestGuard],
    loadComponent: () => import('./features/auth/login').then((m) => m.LoginPage),
  },
  {
    path: 'forgot-password',
    canActivate: [guestGuard],
    loadComponent: () => import('./features/auth/forgot-password').then((m) => m.ForgotPasswordPage),
  },
  {
    path: '',
    canActivate: [authGuard],
    loadComponent: () => import('./layout/shell').then((m) => m.Shell),
    children: [
      { path: '', pathMatch: 'full', canActivate: [roleHomeRedirect], children: [] },

      // ---- Role landing areas (US-013, US-028) ----
      { path: 'platform', canActivate: [permissionGuard(Permissions.academies.manage)], loadComponent: () => import('./features/platform/academies').then((m) => m.AcademiesPage) },
      { path: 'admin', canActivate: [permissionGuard(Permissions.dashboards.view)], loadComponent: () => import('./features/admin/dashboard').then((m) => m.DashboardPage) },
      { path: 'finance', canActivate: [permissionGuard(Permissions.reports.view)], loadComponent: () => salaries().then((m) => m.ReportsPage) },
      { path: 'teacher', loadComponent: () => roles().then((m) => m.TeacherHomePage) },
      { path: 'supervisor', loadComponent: () => roles().then((m) => m.SupervisorHomePage) },
      { path: 'student', loadComponent: () => roles().then((m) => m.StudentHomePage) },
      { path: 'parent', loadComponent: () => roles().then((m) => m.ParentHomePage) },
      { path: 'me', loadComponent: () => roles().then((m) => m.MePage) },
      { path: 'notifications', loadComponent: () => roles().then((m) => m.NotificationsPage) },

      // ---- Platform (SuperAdmin) ----
      { path: 'platform/plans', canActivate: [permissionGuard(Permissions.plans.manage)], loadComponent: () => import('./features/platform/plans').then((m) => m.PlansPage) },

      // ---- Academy administration ----
      { path: 'dashboard', canActivate: [permissionGuard(Permissions.dashboards.view)], loadComponent: () => import('./features/admin/dashboard').then((m) => m.DashboardPage) },
      { path: 'users', canActivate: [permissionGuard(Permissions.users.view)], loadComponent: () => import('./features/admin/users').then((m) => m.UsersPage) },
      { path: 'subscription', canActivate: [permissionGuard(Permissions.plans.view)], loadComponent: () => import('./features/admin/my-subscription').then((m) => m.MySubscriptionPage) },
      { path: 'audit', canActivate: [permissionGuard(Permissions.auditLogs.view)], loadComponent: () => import('./features/admin/audit').then((m) => m.AuditPage) },

      // ---- Academic ----
      { path: 'students', canActivate: [permissionGuard(Permissions.profiles.view)], loadComponent: () => import('./features/academic/students').then((m) => m.StudentsPage) },
      { path: 'students/:id', canActivate: [permissionGuard(Permissions.profiles.view)], loadComponent: () => import('./features/academic/student-detail').then((m) => m.StudentDetailPage) },
      { path: 'staff', canActivate: [permissionGuard(Permissions.profiles.view)], loadComponent: () => import('./features/academic/staff').then((m) => m.StaffPage) },
      { path: 'groups', canActivate: [permissionGuard(Permissions.profiles.view)], loadComponent: () => academic().then((m) => m.GroupsPage) },
      { path: 'courses', canActivate: [permissionGuard(Permissions.courses.view)], loadComponent: () => academic().then((m) => m.CoursesPage) },
      { path: 'sessions', canActivate: [permissionGuard(Permissions.sessions.view)], loadComponent: () => sessions().then((m) => m.SessionsPage) },
      { path: 'sessions/:id', canActivate: [permissionGuard(Permissions.sessions.view)], loadComponent: () => sessions().then((m) => m.SessionDetailPage) },
      { path: 'requests', canActivate: [permissionGuard(Permissions.sessions.view)], loadComponent: () => import('./features/academic/requests').then((m) => m.RequestsPage) },
      { path: 'assignments', canActivate: [permissionGuard(Permissions.courses.view)], loadComponent: () => learning().then((m) => m.AssignmentsPage) },
      { path: 'certificates', canActivate: [permissionGuard(Permissions.courses.view)], loadComponent: () => learning().then((m) => m.CertificatesPage) },

      // ---- Finance ----
      { path: 'payments', canActivate: [permissionGuard(Permissions.payments.manage)], loadComponent: () => payments().then((m) => m.PaymentsPage) },
      { path: 'payment-logs', canActivate: [permissionGuard(Permissions.payments.view)], loadComponent: () => payments().then((m) => m.PaymentLogsPage) },
      { path: 'pay', canActivate: [permissionGuard(Permissions.salaries.manage)], loadComponent: () => salaries().then((m) => m.CompensationPage) },
      { path: 'salaries', canActivate: [permissionGuard(Permissions.salaries.manage)], loadComponent: () => salaries().then((m) => m.SalariesPage) },
      { path: 'payouts', canActivate: [permissionGuard(Permissions.salaries.manage)], loadComponent: () => import('./features/finance/payouts').then((m) => m.PayoutsPage) },
      { path: 'expenses', canActivate: [permissionGuard(Permissions.expenses.manage)], loadComponent: () => salaries().then((m) => m.ExpensesPage) },
      { path: 'reports', canActivate: [permissionGuard(Permissions.reports.view)], loadComponent: () => salaries().then((m) => m.ReportsPage) },
    ],
  },
  { path: '**', redirectTo: '' },
];
