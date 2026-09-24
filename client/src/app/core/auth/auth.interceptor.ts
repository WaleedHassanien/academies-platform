import { HttpErrorResponse, HttpInterceptorFn, HttpRequest } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, switchMap, throwError } from 'rxjs';
import { AuthService } from './auth.service';

/** Endpoints that must never carry a token or trigger a refresh loop. */
const ANONYMOUS = /\/api\/identity\/auth\/(login|refresh|logout|forgot-password|reset-password)$/;

const withToken = (req: HttpRequest<unknown>, token: string | null) =>
  token ? req.clone({ setHeaders: { Authorization: `Bearer ${token}` } }) : req;

/**
 * Attaches the access token to /api calls. On a 401 it refreshes once and retries. If the
 * refresh also fails, the user is logged out.
 */
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  if (!req.url.startsWith('/api/') || ANONYMOUS.test(req.url)) {
    return next(req);
  }

  const auth = inject(AuthService);
  return next(withToken(req, auth.accessToken())).pipe(
    catchError((error: unknown) => {
      if (!(error instanceof HttpErrorResponse) || error.status !== 401 || !auth.hasRefreshToken()) {
        return throwError(() => error);
      }

      return auth.refresh().pipe(
        switchMap((session) => next(withToken(req, session.accessToken))),
        catchError((refreshError: unknown) => {
          auth.logout();
          return throwError(() => refreshError);
        }),
      );
    }),
  );
};
