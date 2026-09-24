import { roleHome } from './core/auth/role-home';
import { Roles } from './core/auth/permissions';

describe('roleHome (US-013)', () => {
  it('sends each role to its own area', () => {
    expect(roleHome([Roles.SuperAdmin])).toBe('/platform');
    expect(roleHome([Roles.Admin])).toBe('/admin');
    expect(roleHome([Roles.Accountant])).toBe('/finance');
    expect(roleHome([Roles.Teacher])).toBe('/teacher');
    expect(roleHome([Roles.Parent])).toBe('/parent');
    expect(roleHome([Roles.Student])).toBe('/student');
  });

  it('picks the most privileged role when a user has several', () => {
    expect(roleHome([Roles.Teacher, Roles.Admin])).toBe('/admin');
  });

  it('falls back to /me for roles without a dedicated area', () => {
    expect(roleHome([Roles.Staff])).toBe('/me');
    expect(roleHome([])).toBe('/me');
  });
});
