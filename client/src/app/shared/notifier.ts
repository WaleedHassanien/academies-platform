import { inject, Injectable } from '@angular/core';
import { MatSnackBar } from '@angular/material/snack-bar';
import { TranslateService } from '@ngx-translate/core';
import { apiErrorMessage } from '../core/api/api.models';

/** Toasts for save/failure feedback. Error text comes from the API's ApiResponse message. */
@Injectable({ providedIn: 'root' })
export class Notifier {
  private readonly snack = inject(MatSnackBar);
  private readonly translate = inject(TranslateService);

  saved(): void {
    this.snack.open(this.translate.instant('common.saved'), undefined, { duration: 2500 });
  }

  info(key: string, params?: Record<string, unknown>): void {
    this.snack.open(this.translate.instant(key, params), undefined, { duration: 3000 });
  }

  error(err: unknown): void {
    this.snack.open(apiErrorMessage(err, this.translate.instant('common.error')), this.translate.instant('common.close'), {
      duration: 6000,
    });
  }
}

/** Current month as 'YYYY-MM', and first/last day helpers, all in local time. */
export function monthKey(date = new Date()): string {
  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}`;
}

export function isoDate(date: Date): string {
  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`;
}

export function addDays(date: Date, days: number): Date {
  const d = new Date(date);
  d.setDate(d.getDate() + days);
  return d;
}
