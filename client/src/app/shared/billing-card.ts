import { Component, effect, inject, input, signal } from '@angular/core';
import { Api, ApiService } from '../core/api/api.service';
import { BillingMode, BillingSummaryDto, StudentBillingDto, TeacherRateDto } from '../core/api/models';
import { AuthService } from '../core/auth/auth.service';
import { Permissions } from '../core/auth/permissions';
import { Notifier } from './notifier';
import { PAGE_IMPORTS } from './page-imports';
import { UsageBar } from './ui';

interface BillingForm {
  mode: BillingMode;
  pricePerSession: number | null;
  sessionsPerMonth: number | null;
  dueDay: number;
}

/**
 * A student's money at a glance: prepaid package use this month (with carried sessions) or the
 * postpaid amount so far, and the balance due. Admins also set the price, mode and each teacher's rate.
 */
@Component({
  selector: 'app-billing-card',
  imports: [PAGE_IMPORTS, UsageBar],
  template: `
    @if (summary() || canManage) {
      <mat-card appearance="outlined">
        <mat-card-header>
          <mat-icon mat-card-avatar class="card-icon">account_balance_wallet</mat-icon>
          <mat-card-title>{{ 'billing.title' | translate }}</mat-card-title>
          @if (summary(); as s) {
            <mat-card-subtitle>{{ 'billing.mode_' + s.mode | translate }} · {{ s.pricePerSession | number: '1.0-2' }} {{ s.currency }} / {{ 'billing.session' | translate }}</mat-card-subtitle>
          }
        </mat-card-header>
        <mat-card-content>
          @if (summary(); as s) {
            @if (s.mode === 'Prepaid') {
              <app-usage-bar [label]="'billing.packageUse' | translate" [used]="s.countedThisMonth" [limit]="s.sessionsPerMonth + s.carriedIn" />
              <div class="facts">
                <span><b>{{ s.packageRemaining }}</b> {{ 'billing.left' | translate }}</span>
                @if (s.carriedIn) { <span><b>+{{ s.carriedIn }}</b> {{ 'billing.carried' | translate }}</span> }
              </div>
            } @else {
              <div class="facts big">
                <span><b>{{ s.countedThisMonth }}</b> {{ 'billing.countedThisMonth' | translate }}</span>
                <span><b class="ltr">{{ s.unbilledAmount | number: '1.0-2' }} {{ s.currency }}</b> {{ 'billing.soFar' | translate }}</span>
              </div>
            }
            <div class="balance" [class.due]="s.outstanding > 0">
              <span>{{ 'billing.balance' | translate }}</span>
              <b class="ltr">{{ s.outstanding | number: '1.0-2' }} {{ s.currency }}</b>
            </div>
          } @else if (!editing()) {
            <p class="muted">{{ 'billing.notSet' | translate }}</p>
          }

          @if (canManage) {
            @if (editing(); as f) {
              <div class="form-grid edit">
                <mat-form-field>
                  <mat-label>{{ 'billing.mode' | translate }}</mat-label>
                  <mat-select [(ngModel)]="f.mode">
                    <mat-option value="Prepaid">{{ 'billing.mode_Prepaid' | translate }}</mat-option>
                    <mat-option value="Postpaid">{{ 'billing.mode_Postpaid' | translate }}</mat-option>
                  </mat-select>
                  <mat-hint>{{ 'billing.hint_' + f.mode | translate }}</mat-hint>
                </mat-form-field>
                <mat-form-field><mat-label>{{ 'billing.price' | translate }}</mat-label><input matInput type="number" min="0" [(ngModel)]="f.pricePerSession" /></mat-form-field>
                @if (f.mode === 'Prepaid') {
                  <mat-form-field><mat-label>{{ 'billing.sessionsPerMonth' | translate }}</mat-label><input matInput type="number" min="1" max="62" [(ngModel)]="f.sessionsPerMonth" /></mat-form-field>
                }
                <mat-form-field><mat-label>{{ 'payments.dueDay' | translate }}</mat-label><input matInput type="number" min="1" max="28" [(ngModel)]="f.dueDay" /></mat-form-field>
              </div>

              @if (teachers().length) {
                <h4 class="sub">{{ 'billing.teacherRates' | translate }}</h4>
                @for (t of teachers(); track t.userId) {
                  <div class="rate">
                    <span>{{ t.fullName }}</span>
                    <input class="cell" type="number" min="0" [ngModel]="rateOf(t.userId)" (ngModelChange)="rates[t.userId] = $event" [placeholder]="'payouts.rate' | translate" />
                  </div>
                }
              }
              <div class="form-actions">
                <button mat-button (click)="editing.set(null)">{{ 'common.cancel' | translate }}</button>
                <button mat-flat-button (click)="save(f)" [disabled]="!f.pricePerSession || (f.mode === 'Prepaid' && !f.sessionsPerMonth)">{{ 'common.save' | translate }}</button>
              </div>
            } @else {
              <div class="form-actions"><button mat-stroked-button (click)="edit()"><mat-icon>tune</mat-icon>{{ 'billing.edit' | translate }}</button></div>
            }
          }
        </mat-card-content>
      </mat-card>
    }
  `,
  styles: `
    :host { display: block; }
    .card-icon { display: grid; place-items: center; border-radius: 12px; color: var(--tone-green);
      background: color-mix(in srgb, var(--tone-green) 12%, transparent); }
    .facts { display: flex; flex-wrap: wrap; gap: 8px 20px; margin-top: 10px; font-size: 0.85rem; color: var(--app-muted); }
    .facts b { color: var(--mat-sys-on-surface); font-size: 1.05rem; }
    .facts.big b { font-size: 1.4rem; display: block; }
    .balance { display: flex; justify-content: space-between; align-items: center; margin-top: 16px; padding: 12px 14px; border-radius: 14px;
      background: var(--ok-bg); color: var(--ok-fg); }
    .balance.due { background: var(--warn-bg); color: var(--warn-fg); }
    .balance b { font-size: 1.15rem; }
    .edit { margin-top: 16px; }
    .sub { margin: 8px 0; font-size: 0.9rem; }
    .rate { display: flex; align-items: center; justify-content: space-between; gap: 12px; padding: 6px 0; border-bottom: 1px dashed var(--app-border); }
    .rate .cell { width: 110px; }
  `,
})
export class BillingCard {
  private readonly api = inject(ApiService);
  private readonly notify = inject(Notifier);
  protected readonly canManage = inject(AuthService).hasPermission(Permissions.payments.manage);

  readonly studentUserId = input.required<number>();
  /** The student's teachers, for setting each one's rate (admin view). */
  readonly teachers = input<{ userId: number; fullName: string }[]>([]);

  protected readonly summary = signal<BillingSummaryDto | null>(null);
  protected readonly editing = signal<BillingForm | null>(null);
  private billing: StudentBillingDto | null = null;
  protected rates: Record<number, number | null> = {};

  constructor() {
    effect(() => {
      if (this.studentUserId()) {
        this.reload();
      }
    });
  }

  reload(): void {
    const id = this.studentUserId();
    this.api.get<BillingSummaryDto | null>(`${Api.finance}/students/${id}/billing/summary`).subscribe({
      next: (s) => this.summary.set(s),
      error: () => this.summary.set(null),
    });
  }

  protected rateOf(teacherId: number): number | null {
    return this.rates[teacherId] ?? this.billing?.rates.find((r) => r.teacherUserId === teacherId)?.ratePerSession ?? null;
  }

  protected edit(): void {
    this.api.get<StudentBillingDto | null>(`${Api.finance}/students/${this.studentUserId()}/billing`).subscribe((b) => {
      this.billing = b;
      this.rates = {};
      this.editing.set({
        mode: b?.mode ?? 'Postpaid', pricePerSession: b?.pricePerSession ?? null, sessionsPerMonth: b?.sessionsPerMonth || 8, dueDay: b?.dueDay ?? 1,
      });
    });
  }

  protected save(f: BillingForm): void {
    const id = this.studentUserId();
    this.api.put<StudentBillingDto>(`${Api.finance}/students/${id}/billing`, f).subscribe({
      next: () => {
        const changed = Object.entries(this.rates).filter(([, v]) => v !== null && v !== undefined && `${v}` !== '');
        let left = changed.length;
        const done = () => {
          this.notify.saved();
          this.editing.set(null);
          this.reload();
        };
        if (!left) {
          done();
        }
        for (const [teacherId, rate] of changed) {
          this.api.put<TeacherRateDto>(`${Api.finance}/teachers/${teacherId}/rates/${id}`, { ratePerSession: Number(rate) }).subscribe({
            next: () => --left === 0 && done(),
            error: (e) => this.notify.error(e),
          });
        }
      },
      error: (e) => this.notify.error(e),
    });
  }
}
