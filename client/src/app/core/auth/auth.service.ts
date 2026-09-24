import { HttpClient } from '@angular/common/http';
import { computed, inject, Injectable, signal } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, finalize, firstValueFrom, map, Observable, of, shareReplay, throwError } from 'rxjs';
import { ApiResponse } from '../api/api.models';
import { AuthResponse, UserInfo } from './auth.models';
import { Roles } from './permissions';
import { roleHome } from './role-home';

const REFRESH_KEY = 'academies.refreshToken';
const AUTH_URL = '/api/identity/auth';

interface Session {
  accessToken: string;
  user: UserInfo;
}

/**
 * Holds the signed-in session. The access token lives only in memory. The refresh token is
 * kept in localStorage so a reload can restore the session through /auth/refresh.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);

  private readonly session = signal<Session | null>(null);
  private refreshInFlight: Observable<Session> | null = null;

  readonly user = computed(() => this.session()?.user ?? null);
  readonly isAuthenticated = computed(() => this.session() !== null);
  readonly homeUrl = computed(() => roleHome(this.user()?.roles ?? []));

  accessToken(): string | null {
    return this.session()?.accessToken ?? null;
  }

  hasRefreshToken(): boolean {
    return readRefreshToken() !== null;
  }

  hasPermission(permission: string): boolean {
    const user = this.user();
    return !!user && (user.roles.includes(Roles.SuperAdmin) || user.permissions.includes(permission));
  }

  hasAnyRole(...roles: string[]): boolean {
    const user = this.user();
    return !!user && roles.some((r) => user.roles.includes(r));
  }

  login(email: string, password: string): Observable<UserInfo> {
    return this.http
      .post<ApiResponse<AuthResponse>>(`${AUTH_URL}/login`, { email, password })
      .pipe(map((res) => this.accept(res.data!).user));
  }

  /** Exchanges the stored refresh token; concurrent callers share one request. */
  refresh(): Observable<Session> {
    const refreshToken = readRefreshToken();
    if (!refreshToken) {
      return throwError(() => new Error('No refresh token'));
    }

    this.refreshInFlight ??= this.http
      .post<ApiResponse<AuthResponse>>(`${AUTH_URL}/refresh`, { refreshToken })
      .pipe(
        map((res) => this.accept(res.data!)),
        catchError((err) => {
          this.clear();
          return throwError(() => err);
        }),
        finalize(() => (this.refreshInFlight = null)),
        shareReplay(1),
      );
    return this.refreshInFlight;
  }

  /** Called once at startup: restores the session if a refresh token survives. */
  restore(): Promise<unknown> {
    return this.hasRefreshToken() ? firstValueFrom(this.refresh().pipe(catchError(() => of(null)))) : Promise.resolve();
  }

  logout(): void {
    const refreshToken = readRefreshToken();
    this.clear();
    if (refreshToken) {
      this.http.post(`${AUTH_URL}/logout`, { refreshToken }).pipe(catchError(() => of(null))).subscribe();
    }
    void this.router.navigateByUrl('/login');
  }

  forgotPassword(email: string): Observable<ApiResponse<null>> {
    return this.http.post<ApiResponse<null>>(`${AUTH_URL}/forgot-password`, { email });
  }

  resetPassword(email: string, token: string, newPassword: string): Observable<ApiResponse<null>> {
    return this.http.post<ApiResponse<null>>(`${AUTH_URL}/reset-password`, { email, token, newPassword });
  }

  private accept(response: AuthResponse): Session {
    const session = { accessToken: response.accessToken, user: response.user };
    writeRefreshToken(response.refreshToken);
    this.session.set(session);
    return session;
  }

  private clear(): void {
    writeRefreshToken(null);
    this.session.set(null);
  }
}

function readRefreshToken(): string | null {
  try {
    return localStorage.getItem(REFRESH_KEY);
  } catch {
    return null;
  }
}

function writeRefreshToken(value: string | null): void {
  try {
    if (value) {
      localStorage.setItem(REFRESH_KEY, value);
    } else {
      localStorage.removeItem(REFRESH_KEY);
    }
  } catch {
    // Storage blocked (private mode): the session just won't survive a reload.
  }
}
