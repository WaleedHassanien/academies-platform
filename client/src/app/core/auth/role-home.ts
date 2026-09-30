import { Roles } from './permissions';

/** Landing area per role, most privileged first (US-013: each role lands on its own dashboard). */
const ROLE_HOMES: ReadonlyArray<[role: string, path: string]> = [
  [Roles.SuperAdmin, '/platform'],
  [Roles.Admin, '/admin'],
  [Roles.Manager, '/admin'],
  [Roles.Accountant, '/finance'],
  [Roles.Sales, '/leads'],
  [Roles.Supervisor, '/supervisor'],
  [Roles.Teacher, '/teacher'],
  [Roles.Parent, '/parent'],
  [Roles.Student, '/student'],
];

export const HOME_AREAS = ['platform', 'admin', 'finance', 'supervisor', 'teacher', 'parent', 'student', 'me'] as const;

export function roleHome(roles: readonly string[]): string {
  return ROLE_HOMES.find(([role]) => roles.includes(role))?.[1] ?? '/me';
}
