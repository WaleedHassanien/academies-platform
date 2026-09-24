import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';

/** Signed-in users only; others go to /login and come back afterwards. */
export const authGuard: CanActivateFn = (_route, state) => {
  const auth = inject(AuthService);
  return auth.isAuthenticated()
    ? true
    : inject(Router).createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });
};

/** Keeps signed-in users off the login/reset pages. */
export const guestGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  return auth.isAuthenticated() ? inject(Router).parseUrl(auth.homeUrl()) : true;
};

/** Requires one permission code (SuperAdmin always passes). */
export const permissionGuard =
  (permission: string): CanActivateFn =>
  () => {
    const auth = inject(AuthService);
    return auth.hasPermission(permission) ? true : inject(Router).parseUrl(auth.homeUrl());
  };

/** Sends "/" to the signed-in user's role landing page (US-013). */
export const roleHomeRedirect: CanActivateFn = () => inject(Router).parseUrl(inject(AuthService).homeUrl());
