import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { map, Observable } from 'rxjs';
import { ApiResponse } from './api.models';

type Params = Record<string, string | number | boolean | null | undefined>;

/** Thin wrapper over HttpClient that unwraps ApiResponse&lt;T&gt;.data. All URLs go through the gateway. */
@Injectable({ providedIn: 'root' })
export class ApiService {
  private readonly http = inject(HttpClient);

  get<T>(url: string, params?: Params): Observable<T> {
    return this.http.get<ApiResponse<T>>(url, { params: toParams(params) }).pipe(map((r) => r.data as T));
  }

  post<T>(url: string, body: unknown = {}, params?: Params): Observable<T> {
    return this.http.post<ApiResponse<T>>(url, body, { params: toParams(params) }).pipe(map((r) => r.data as T));
  }

  put<T>(url: string, body: unknown): Observable<T> {
    return this.http.put<ApiResponse<T>>(url, body).pipe(map((r) => r.data as T));
  }

  delete<T = null>(url: string): Observable<T> {
    return this.http.delete<ApiResponse<T>>(url).pipe(map((r) => r.data as T));
  }

  /** Downloads a file (e.g. a certificate PDF) with the auth header, then saves it. */
  download(url: string, fileName: string): void {
    this.http.get(url, { responseType: 'blob' }).subscribe((blob) => {
      const link = document.createElement('a');
      link.href = URL.createObjectURL(blob);
      link.download = fileName;
      link.click();
      URL.revokeObjectURL(link.href);
    });
  }
}

function toParams(params?: Params): HttpParams | undefined {
  if (!params) {
    return undefined;
  }

  let result = new HttpParams();
  for (const [key, value] of Object.entries(params)) {
    if (value !== null && value !== undefined && value !== '') {
      result = result.set(key, String(value));
    }
  }
  return result;
}

/** Service roots behind the gateway. */
export const Api = {
  identity: '/api/identity',
  subscriptions: '/api/subscriptions',
  academic: '/api/academic',
  finance: '/api/finance',
  engagement: '/api/engagement',
} as const;
