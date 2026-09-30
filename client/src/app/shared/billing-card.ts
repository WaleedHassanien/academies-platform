import { Component, computed, effect, inject, input, signal } from '@angular/core';
import { forkJoin, of } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { Api, ApiService } from '../core/api/api.service';
import {
  AutoPayDto, BillingMode, BillingSummaryDto, EnrollmentDto, FinanceSettingsDto, PackageDto, StudentBillingDto, TeacherRateDto,
} from '../core/api/models';
import { AuthService } from '../core/auth/auth.service';
import { Permissions } from '../core/auth/permissions';
import { Notifier } from './notifier';
import { PAGE_IMPORTS } from './page-imports';
import { UsageBar } from './ui';

interface BillingForm {
  /** null = all subjects without their own billing. */
  courseId: number | null;
  /** A ready-made package; null = custom terms below. */
  packageId: number | null;
  mode: BillingMode;
  pricePerSession: number | null;
  sessionsPerMonth: number | null;
  monthlyPrice: number | null;
  currency: string;
  dueDay: number;
}

/**
 * A student's money at a glance, per subject: prepaid package use this month (with carried sessions)
 * or the postpaid amount so far, and the balance due, each in its own currency. Admins set each
 * subject's package or price and each teacher's rate. The payer can save a card so invoices renew
 * automatically.
 */
@Component({
  selector: 'app-billing-card',
  imports: [PAGE_IMPORTS, UsageBar],
  template: `
    @if (summaries().length || canManage || autoPay()) {
      <mat-card appearance="outlined">
        <mat-card-header>
          <mat-icon mat-card-avatar class="card-icon">account_balance_wallet</mat-icon>
          <mat-card-title>{{ 'billing.title' | translate }}</mat-card-title>
          <mat-card-subtitle>{{ 'billing.perSubject' | translate }}</mat-card-subtitle>
        </mat-card-header>
        <mat-card-content>
          @for (s of summaries(); track s.billingId) {
            <div class="billing">
              <div class="billing-head">
                <b>{{ subjectName(s.courseId) }}</b>
                <span class="muted">
                  {{ 'billing.mode_' + s.mode | translate }} ·
                  @if (s.mode === 'Prepaid') { {{ s.sessionsPerMonth }} × {{ 'billing.session' | translate }} } @else { {{ s.pricePerSession | number: '1.0-2' }} {{ s.currency }} / {{ 'billing.session' | translate }} }
                </span>
                @if (canManage) {
                  <span class="spacer"></span>
                  <button mat-icon-button (click)="edit(s.courseId)" [attr.aria-label]="'common.edit' | translate"><mat-icon>edit</mat-icon></button>
                  <button mat-icon-button (click)="stop(s)" [attr.aria-label]="'billing.stop' | translate"><mat-icon>block</mat-icon></button>
                }
              </div>
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
            </div>
          } @empty {
            @if (!editing()) { <p class="muted">{{ 'billing.notSet' | translate }}</p> }
          }

          @if (canPay() && summaries().length) {
            <div class="autopay">
              <mat-icon>credit_card</mat-icon>
              @if (autoPay(); as a) {
                @if (a.status === 'Active') {
                  <span><b>{{ 'autopay.on' | translate }}</b> · <span class="ltr">{{ a.cardBrand }} •••• {{ a.cardLast4 }}</span>
                    @if (a.lastError) { <div class="error">{{ a.lastError }}</div> }
                  </span>
                  <button mat-button (click)="stopAutoPay()">{{ 'autopay.stop' | translate }}</button>
                } @else {
                  <span class="muted">{{ 'autopay.pending' | translate }}</span>
                  <button mat-stroked-button (click)="startAutoPay()">{{ 'autopay.retry' | translate }}</button>
                }
              } @else {
                <span class="muted">{{ 'autopay.hint' | translate }}</span>
                <button mat-stroked-button (click)="startAutoPay()">{{ 'autopay.start' | translate }}</button>
              }
            </div>
          }

          @if (canManage) {
            @if (editing(); as f) {
              <div class="form-grid edit">
                <mat-form-field>
                  <mat-label>{{ 'students.subject' | translate }}</mat-label>
                  <mat-select [(ngModel)]="f.courseId" (selectionChange)="loadForm(f.courseId)">
                    <mat-option [value]="null">{{ 'billing.allSubjects' | translate }}</mat-option>
                    @for (e of subjects(); track e.id) { <mat-option [value]="e.courseId">{{ e.courseName }}</mat-option> }
                  </mat-select>
                </mat-form-field>
                <mat-form-field>
                  <mat-label>{{ 'billing.package' | translate }}</mat-label>
                  <mat-select [(ngModel)]="f.packageId" (selectionChange)="onPackage(f)">
                    <mat-option [value]="null">{{ 'billing.custom' | translate }}</mat-option>
                    @for (p of packages(); track p.id) {
                      <mat-option [value]="p.id">{{ p.name }} — <span class="ltr">{{ p.monthlyPrice | number: '1.0-2' }} {{ p.currency }}</span></mat-option>
                    }
                  </mat-select>
                  <mat-hint>{{ 'billing.packageHint' | translate }}</mat-hint>
                </mat-form-field>
                @if (f.packageId === null) {
                  <mat-form-field>
                    <mat-label>{{ 'billing.mode' | translate }}</mat-label>
                    <mat-select [(ngModel)]="f.mode">
                      <mat-option value="Prepaid">{{ 'billing.mode_Prepaid' | translate }}</mat-option>
                      <mat-option value="Postpaid">{{ 'billing.mode_Postpaid' | translate }}</mat-option>
                    </mat-select>
                    <mat-hint>{{ 'billing.hint_' + f.mode | translate }}</mat-hint>
                  </mat-form-field>
                  <mat-form-field>
                    <mat-label>{{ 'billing.currency' | translate }}</mat-label>
                    <mat-select [(ngModel)]="f.currency">@for (c of currencies(); track c) { <mat-option [value]="c">{{ c }}</mat-option> }</mat-select>
                  </mat-form-field>
                  @if (f.mode === 'Prepaid') {
                    <mat-form-field><mat-label>{{ 'billing.sessionsPerMonth' | translate }}</mat-label><input matInput type="number" min="1" max="62" [(ngModel)]="f.sessionsPerMonth" /></mat-form-field>
                    <mat-form-field><mat-label>{{ 'billing.monthlyPrice' | translate }}</mat-label><input matInput type="number" min="0" [(ngModel)]="f.monthlyPrice" /></mat-form-field>
                  } @else {
                    <mat-form-field><mat-label>{{ 'billing.price' | translate }}</mat-label><input matInput type="number" min="0" [(ngModel)]="f.pricePerSession" /></mat-form-field>
                  }
                } @else {
                  <mat-form-field><mat-label>{{ 'billing.monthlyPrice' | translate }}</mat-label><input matInput type="number" min="0" [(ngModel)]="f.monthlyPrice" /><mat-hint>{{ 'billing.overrideHint' | translate }}</mat-hint></mat-form-field>
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
                <button mat-flat-button (click)="save(f)" [disabled]="!valid(f)">{{ 'common.save' | translate }}</button>
              </div>
            } @else {
              <div class="form-actions"><button mat-stroked-button (click)="edit(null)"><mat-icon>add</mat-icon>{{ 'billing.edit' | translate }}</button></div>
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
    .billing + .billing { margin-top: 18px; padding-top: 14px; border-top: 1px dashed var(--app-border); }
    .billing-head { display: flex; align-items: center; flex-wrap: wrap; gap: 4px 10px; margin-bottom: 8px; }
    .billing-head .spacer { flex: 1; }
    .billing-head button { width: 32px; height: 32px; padding: 4px; }
    .facts { display: flex; flex-wrap: wrap; gap: 8px 20px; margin-top: 10px; font-size: 0.85rem; color: var(--app-muted); }
    .facts b { color: var(--mat-sys-on-surface); font-size: 1.05rem; }
    .facts.big b { font-size: 1.4rem; display: block; }
    .balance { display: flex; justify-content: space-between; align-items: center; margin-top: 12px; padding: 10px 14px; border-radius: 14px;
      background: var(--ok-bg); color: var(--ok-fg); }
    .balance.due { background: var(--warn-bg); color: var(--warn-fg); }
    .balance b { font-size: 1.1rem; }
    .autopay { display: flex; align-items: center; flex-wrap: wrap; gap: 8px 12px; margin-top: 16px; padding: 10px 12px; border-radius: 14px;
      background: var(--app-surface-2); border: 1px solid var(--app-border); font-size: 0.88rem; }
    .autopay > span { flex: 1; min-width: 160px; }
    .autopay .error { color: var(--bad-fg, #b91c1c); font-size: 0.8rem; }
    .edit { margin-top: 16px; }
    .sub { margin: 8px 0; font-size: 0.9rem; }
    .rate { display: flex; align-items: center; justify-content: space-between; gap: 12px; padding: 6px 0; border-bottom: 1px dashed var(--app-border); }
    .rate .cell { width: 110px; }
  `,
})
export class BillingCard {
  private readonly api = inject(ApiService);
  private readonly notify = inject(Notifier);
  private readonly auth = inject(AuthService);
  protected readonly canManage = this.auth.hasPermission(Permissions.payments.manage);

  readonly studentUserId = input.required<number>();
  /** The student's teachers, for setting each one's rate (admin view). */
  readonly teachers = input<{ userId: number; fullName: string }[]>([]);
  /** Who pays for the student; they (or finance staff) can save a card for automatic payments. */
  readonly payerUserId = input<number | null>(null);

  protected readonly summaries = signal<BillingSummaryDto[]>([]);
  protected readonly subjects = signal<EnrollmentDto[]>([]);
  protected readonly packages = signal<PackageDto[]>([]);
  protected readonly currencies = signal<string[]>(['USD', 'EUR', 'GBP', 'SAR', 'EGP']);
  protected readonly autoPay = signal<AutoPayDto | null>(null);
  protected readonly editing = signal<BillingForm | null>(null);
  protected readonly canPay = computed(() => this.canManage || (this.payerUserId() ?? -1) === this.auth.user()?.id);
  private billings: StudentBillingDto[] = [];
  private baseCurrency = 'EGP';
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
    this.api.get<BillingSummaryDto[]>(`${Api.finance}/students/${id}/billing/summary`).subscribe({
      next: (s) => this.summaries.set(s),
      error: () => this.summaries.set([]),
    });
    this.api.get<AutoPayDto | null>(`${Api.finance}/students/${id}/autopay`).subscribe({
      next: (a) => this.autoPay.set(a),
      error: () => this.autoPay.set(null),
    });
    this.api.get<EnrollmentDto[]>(`${Api.academic}/students/${id}/enrollments`).subscribe({
      next: (e) => this.subjects.set(e.filter((x) => x.status !== 'Ended')),
      error: () => this.subjects.set([]),
    });
  }

  protected subjectName(courseId: number | null): string {
    return courseId === null ? '—' : (this.subjects().find((e) => e.courseId === courseId)?.courseName ?? `#${courseId}`);
  }

  protected rateOf(teacherId: number): number | null {
    return this.rates[teacherId] ?? this.billings[0]?.rates.find((r) => r.teacherUserId === teacherId)?.ratePerSession ?? null;
  }

  protected edit(courseId: number | null): void {
    const id = this.studentUserId();
    forkJoin({
      billings: this.api.get<StudentBillingDto[]>(`${Api.finance}/students/${id}/billing`),
      packages: this.api.get<PackageDto[]>(`${Api.finance}/packages`).pipe(catchError(() => of([] as PackageDto[]))),
      settings: this.api.get<FinanceSettingsDto>(`${Api.finance}/settings`).pipe(catchError(() => of(null))),
    }).subscribe(({ billings, packages, settings }) => {
      this.billings = billings;
      this.packages.set(packages);
      if (settings) {
        this.baseCurrency = settings.currency;
        this.currencies.set(settings.available);
      }
      this.rates = {};
      this.loadForm(courseId ?? this.subjects().find((e) => !billings.some((b) => b.courseId === e.courseId))?.courseId ?? null);
    });
  }

  /** Fills the form with the subject's current billing, or defaults for a new one. */
  protected loadForm(courseId: number | null): void {
    const b = this.billings.find((x) => x.courseId === courseId);
    this.editing.set({
      courseId, packageId: b?.packageId ?? null, mode: b?.mode ?? 'Prepaid', pricePerSession: b?.pricePerSession ?? null,
      sessionsPerMonth: b?.sessionsPerMonth || 8, monthlyPrice: b?.monthlyPrice || null, currency: b?.currency ?? this.baseCurrency,
      dueDay: b?.dueDay ?? 1,
    });
  }

  protected onPackage(f: BillingForm): void {
    const p = this.packages().find((x) => x.id === f.packageId);
    f.monthlyPrice = p ? p.monthlyPrice : null;
  }

  protected valid(f: BillingForm): boolean {
    if (f.packageId !== null) {
      return true;
    }
    return f.mode === 'Prepaid' ? !!f.sessionsPerMonth && !!f.monthlyPrice : !!f.pricePerSession;
  }

  protected save(f: BillingForm): void {
    const id = this.studentUserId();
    const body = {
      courseId: f.courseId, packageId: f.packageId, mode: f.packageId !== null ? 'Prepaid' : f.mode, pricePerSession: f.pricePerSession ?? 0,
      sessionsPerMonth: f.sessionsPerMonth ?? 0, monthlyPrice: f.monthlyPrice, currency: f.packageId !== null ? null : f.currency, dueDay: f.dueDay,
    };
    this.api.put<StudentBillingDto>(`${Api.finance}/students/${id}/billing`, body).subscribe({
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

  protected stop(s: BillingSummaryDto): void {
    if (!confirm(`${this.subjectName(s.courseId)}?`)) {
      return;
    }
    this.api.delete(`${Api.finance}/billings/${s.billingId}`).subscribe({ next: () => this.reload(), error: (e) => this.notify.error(e) });
  }

  /** Sends the payer to the provider's page to save a card; they come back with ?autopay=success. */
  protected startAutoPay(): void {
    this.api.post<{ setupUrl: string }>(`${Api.finance}/students/${this.studentUserId()}/autopay/setup`).subscribe({
      next: (r) => (window.location.href = r.setupUrl),
      error: (e) => this.notify.error(e),
    });
  }

  protected stopAutoPay(): void {
    this.api.delete(`${Api.finance}/students/${this.studentUserId()}/autopay`).subscribe({
      next: () => {
        this.notify.saved();
        this.reload();
      },
      error: (e) => this.notify.error(e),
    });
  }
}
