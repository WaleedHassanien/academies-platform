import { Component, inject, OnInit, signal } from '@angular/core';
import { PagedResult } from '../../core/api/api.models';
import { Api, ApiService } from '../../core/api/api.service';
import { FinanceSettingsDto, PaymentLogPageDto, PaymentPlanDto, StudentDto, StudentPaymentDto, StudentPaymentsDto } from '../../core/api/models';
import { AuthService } from '../../core/auth/auth.service';
import { Permissions } from '../../core/auth/permissions';
import { isoDate, Notifier } from '../../shared/notifier';
import { PAGE_IMPORTS } from '../../shared/page-imports';

/** Student payment plans and the month-by-month table: record payments and refunds (US-029, US-030). */
@Component({
  selector: 'app-payments',
  imports: [PAGE_IMPORTS],
  template: `
    <div class="page-header">
      <h1>{{ 'nav.payments' | translate }}</h1>
      @if (settings(); as s) {
        <div class="toolbar" style="margin: 0">
          <mat-form-field>
            <mat-label>{{ 'payments.currency' | translate }}</mat-label>
            <mat-select [value]="s.currency" (selectionChange)="saveCurrency($event.value)" [disabled]="!canManage">
              @for (c of s.available; track c) { <mat-option [value]="c">{{ 'currency.' + c | translate }}</mat-option> }
            </mat-select>
            <mat-hint>{{ 'payments.currencyHint' | translate }}</mat-hint>
          </mat-form-field>
          @if (canManage) { <button mat-stroked-button (click)="showRates.set(!showRates())"><mat-icon>currency_exchange</mat-icon>{{ 'payments.rates' | translate }}</button> }
        </div>
      }
    </div>

    @if (showRates() && settings(); as s) {
      <mat-card appearance="outlined" class="panel">
        <mat-card-header>
          <mat-card-title>{{ 'payments.rates' | translate }}</mat-card-title>
          <mat-card-subtitle>{{ 'payments.ratesHint' | translate: { base: s.currency } }}</mat-card-subtitle>
        </mat-card-header>
        <mat-card-content>
          <div class="form-grid">
            @for (c of otherCurrencies(); track c) {
              <mat-form-field>
                <mat-label class="ltr">1 {{ c }} =</mat-label>
                <input matInput type="number" min="0" step="0.0001" [(ngModel)]="rates[c]" />
                <span matTextSuffix>&nbsp;{{ s.currency }}</span>
              </mat-form-field>
            }
          </div>
          <div class="form-actions"><button mat-flat-button (click)="saveRates()">{{ 'common.save' | translate }}</button></div>
        </mat-card-content>
      </mat-card>
    }

    <mat-card appearance="outlined" class="panel">
      <mat-card-header><mat-card-title>{{ 'payments.newPlan' | translate }}</mat-card-title></mat-card-header>
      <mat-card-content>
        <div class="form-grid">
          <mat-form-field>
            <mat-label>{{ 'roles.Student' | translate }}</mat-label>
            <mat-select [(ngModel)]="plan.studentUserId">@for (s of students(); track s.userId) { <mat-option [value]="s.userId">{{ s.fullName }}</mat-option> }</mat-select>
          </mat-form-field>
          <mat-form-field><mat-label>{{ 'payments.monthlyAmount' | translate }}</mat-label><input matInput type="number" [(ngModel)]="plan.monthlyAmount" /></mat-form-field>
          <mat-form-field>
            <mat-label>{{ 'billing.currency' | translate }}</mat-label>
            <mat-select [(ngModel)]="plan.currency">@for (c of settings()?.available ?? []; track c) { <mat-option [value]="c">{{ c }}</mat-option> }</mat-select>
          </mat-form-field>
          <mat-form-field><mat-label>{{ 'payments.startDate' | translate }}</mat-label><input matInput type="date" [(ngModel)]="plan.startDate" /></mat-form-field>
          <mat-form-field><mat-label>{{ 'payments.months' | translate }}</mat-label><input matInput type="number" [(ngModel)]="plan.months" /></mat-form-field>
          <mat-form-field><mat-label>{{ 'payments.dueDay' | translate }}</mat-label><input matInput type="number" min="1" max="28" [(ngModel)]="plan.dueDay" /></mat-form-field>
        </div>
        <div class="form-actions"><button mat-flat-button (click)="createPlan()" [disabled]="!plan.studentUserId || !plan.monthlyAmount">{{ 'common.create' | translate }}</button></div>
      </mat-card-content>
    </mat-card>

    <div class="table-wrap">
      <table class="data-table">
        <thead><tr><th>{{ 'roles.Student' | translate }}</th><th>{{ 'payments.monthlyAmount' | translate }}</th><th>{{ 'payments.period' | translate }}</th><th>{{ 'payments.paid' | translate }}</th><th>{{ 'common.status' | translate }}</th><th></th></tr></thead>
        <tbody>
          @for (p of plans(); track p.id) {
            <tr [class.selected]="months()?.studentUserId === p.studentUserId">
              <td>{{ p.studentName }}</td>
              <td class="num">{{ p.monthlyAmount | number: '1.0-2' }} {{ p.currency }}</td>
              <td>{{ p.startDate | date: 'MMM y' }} – {{ p.endDate | date: 'MMM y' }} ({{ p.months }})</td>
              <td class="num">{{ p.paidAmount | number: '1.0-2' }} / {{ p.totalAmount | number: '1.0-2' }} {{ p.currency }}</td>
              <td><span class="status" [class]="p.status">{{ 'status.' + p.status | translate }}</span></td>
              <td class="actions">
                <button mat-button (click)="openStudent(p.studentUserId)">{{ 'payments.months' | translate }}</button>
                @if (p.status === 'Active') { <button mat-button (click)="cancelPlan(p)">{{ 'common.cancel' | translate }}</button> }
              </td>
            </tr>
          } @empty { <tr><td colspan="6" class="empty">{{ 'common.noData' | translate }}</td></tr> }
        </tbody>
      </table>
    </div>

    @if (months(); as m) {
      <mat-card appearance="outlined" class="panel" style="margin-top: 16px">
        <mat-card-header>
          <mat-card-title>{{ m.studentName }}</mat-card-title>
          <mat-card-subtitle>{{ 'payments.paid' | translate }}: {{ m.totalPaid | number: '1.0-2' }} {{ m.currency }} · {{ 'finance.outstanding' | translate }}: {{ m.outstanding | number: '1.0-2' }} {{ m.currency }}</mat-card-subtitle>
        </mat-card-header>
        <mat-card-content>
          <table class="data-table">
            <thead><tr><th>{{ 'payments.month' | translate }}</th><th>{{ 'payments.due' | translate }}</th><th>{{ 'common.amount' | translate }}</th><th>{{ 'payments.paid' | translate }}</th><th>{{ 'common.status' | translate }}</th><th></th></tr></thead>
            <tbody>
              @for (row of m.months; track row.id) {
                <tr>
                  <td>{{ 'payments.monthN' | translate: { n: row.monthNumber } }}</td>
                  <td>{{ row.dueDate | date: 'mediumDate' }}</td>
                  <td class="num">{{ row.amount | number: '1.0-2' }} {{ row.currency }}</td>
                  <td class="num">{{ row.paidAmount | number: '1.0-2' }}</td>
                  <td><span class="status" [class]="row.status">{{ 'status.' + row.status | translate }}</span></td>
                  <td class="actions">
                    @if (canManage && row.status !== 'Cancelled') {
                      @if (row.remaining > 0) { <button mat-button (click)="startPay(row)">{{ 'payments.record' | translate }}</button> }
                      @if (row.paidAmount > 0) { <button mat-button (click)="refund(row)">{{ 'payments.refund' | translate }}</button> }
                    }
                  </td>
                </tr>
              }
            </tbody>
          </table>
          @if (paying(); as row) {
            <div class="toolbar" style="margin-top: 12px">
              <b>{{ 'payments.monthN' | translate: { n: row.monthNumber } }}</b>
              <mat-form-field><mat-label>{{ 'common.amount' | translate }}</mat-label><input matInput type="number" [(ngModel)]="payAmount" /></mat-form-field>
              <mat-form-field>
                <mat-label>{{ 'payments.method' | translate }}</mat-label>
                <mat-select [(ngModel)]="payMethod">@for (x of methods; track x) { <mat-option [value]="x">{{ 'method.' + x | translate }}</mat-option> }</mat-select>
              </mat-form-field>
              <mat-form-field><mat-label>{{ 'payments.reference' | translate }}</mat-label><input matInput [(ngModel)]="payReference" /></mat-form-field>
              <button mat-flat-button (click)="pay(row)" [disabled]="!payAmount">{{ 'common.save' | translate }}</button>
              <button mat-button (click)="paying.set(null)">{{ 'common.cancel' | translate }}</button>
            </div>
          }
        </mat-card-content>
      </mat-card>
    }
  `,
})
export class PaymentsPage implements OnInit {
  private readonly api = inject(ApiService);
  private readonly notify = inject(Notifier);
  protected readonly canManage = inject(AuthService).hasPermission(Permissions.payments.manage);

  protected readonly methods = ['Cash', 'BankTransfer', 'Card'];
  protected readonly plans = signal<PaymentPlanDto[]>([]);
  protected readonly students = signal<StudentDto[]>([]);
  protected readonly months = signal<StudentPaymentsDto | null>(null);
  protected readonly paying = signal<StudentPaymentDto | null>(null);
  protected readonly settings = signal<FinanceSettingsDto | null>(null);
  protected plan = {
    studentUserId: null as number | null, monthlyAmount: 0, startDate: isoDate(new Date()), months: 12, dueDay: 1, currency: null as string | null,
  };
  protected readonly showRates = signal(false);
  protected rates: Record<string, number | null> = {};
  protected readonly otherCurrencies = () => (this.settings()?.available ?? []).filter((c) => c !== this.settings()?.currency);
  protected payAmount = 0;
  protected payMethod = 'Cash';
  protected payReference = '';

  ngOnInit(): void {
    this.load();
    this.api.get<PagedResult<StudentDto>>(`${Api.academic}/students`, { pageSize: 100 }).subscribe((r) => this.students.set(r.items));
    this.api.get<FinanceSettingsDto>(`${Api.finance}/settings`).subscribe((s) => this.applySettings(s));
  }

  private applySettings(s: FinanceSettingsDto): void {
    this.settings.set(s);
    this.rates = { ...s.exchangeRates };
    this.plan.currency ??= s.currency;
  }

  /** Rates used to add payments in other currencies into the report totals. */
  protected saveRates(): void {
    const s = this.settings();
    if (!s) {
      return;
    }
    const exchangeRates = Object.fromEntries(Object.entries(this.rates).filter(([, v]) => v !== null && Number(v) > 0).map(([k, v]) => [k, Number(v)]));
    this.api.put<FinanceSettingsDto>(`${Api.finance}/settings`, { currency: s.currency, exchangeRates }).subscribe({
      next: (updated) => {
        this.applySettings(updated);
        this.showRates.set(false);
        this.notify.saved();
      },
      error: (e) => this.notify.error(e),
    });
  }

  /** New plans use the new currency; existing plans keep theirs. */
  protected saveCurrency(currency: string): void {
    this.api.put<FinanceSettingsDto>(`${Api.finance}/settings`, { currency }).subscribe({
      next: (s) => {
        this.applySettings(s);
        this.notify.saved();
      },
      error: (e) => this.notify.error(e),
    });
  }

  protected createPlan(): void {
    this.api.post(`${Api.finance}/payment-plans`, this.plan).subscribe({
      next: () => {
        this.notify.saved();
        this.openStudent(this.plan.studentUserId!);
        this.load();
      },
      error: (e) => this.notify.error(e),
    });
  }

  protected cancelPlan(p: PaymentPlanDto): void {
    if (confirm(`${p.studentName}?`)) {
      this.api.post(`${Api.finance}/payment-plans/${p.id}/cancel`).subscribe({ next: () => this.load(), error: (e) => this.notify.error(e) });
    }
  }

  protected openStudent(studentUserId: number): void {
    this.paying.set(null);
    this.api.get<StudentPaymentsDto>(`${Api.finance}/students/${studentUserId}/payments`).subscribe((m) => this.months.set(m));
  }

  protected startPay(row: StudentPaymentDto): void {
    this.paying.set(row);
    this.payAmount = row.remaining;
    this.payReference = '';
  }

  protected pay(row: StudentPaymentDto): void {
    this.api
      .post(`${Api.finance}/student-payments/${row.id}/payments`, { amount: this.payAmount, method: this.payMethod, reference: this.payReference || null })
      .subscribe({ next: () => this.refreshStudent(row.studentUserId), error: (e) => this.notify.error(e) });
  }

  protected refund(row: StudentPaymentDto): void {
    const amount = Number(prompt('Refund amount / مبلغ الاسترداد', String(row.paidAmount)));
    const note = amount ? prompt('Reason / السبب') : null;
    if (amount && note) {
      this.api.post(`${Api.finance}/student-payments/${row.id}/refunds`, { amount, note }).subscribe({
        next: () => this.refreshStudent(row.studentUserId),
        error: (e) => this.notify.error(e),
      });
    }
  }

  private refreshStudent(studentUserId: number): void {
    this.notify.saved();
    this.openStudent(studentUserId);
    this.load();
  }

  private load(): void {
    this.api.get<PaymentPlanDto[]>(`${Api.finance}/payment-plans`).subscribe((p) => this.plans.set(p));
  }
}

/** The payment log, month by month with totals (US-030). Parents see only their children's. */
@Component({
  selector: 'app-payment-logs',
  imports: [PAGE_IMPORTS],
  template: `
    <div class="page-header"><h1>{{ 'nav.paymentLogs' | translate }}</h1></div>
    <div class="toolbar">
      <mat-form-field><mat-label>{{ 'common.from' | translate }}</mat-label><input matInput type="date" [(ngModel)]="from" /></mat-form-field>
      <mat-form-field><mat-label>{{ 'common.to' | translate }}</mat-label><input matInput type="date" [(ngModel)]="to" /></mat-form-field>
      <button mat-stroked-button (click)="load(1)">{{ 'common.apply' | translate }}</button>
    </div>
    @if (data(); as d) {
      <p>{{ 'payments.totalPaid' | translate }}: <b class="ltr">{{ d.totalPaid | number: '1.0-2' }}</b> · {{ 'payments.totalRefunded' | translate }}: <b class="ltr">{{ d.totalRefunded | number: '1.0-2' }}</b></p>
      <div class="table-wrap">
        <table class="data-table">
          <thead><tr><th>{{ 'roles.Student' | translate }}</th><th>{{ 'payments.month' | translate }}</th><th>{{ 'payments.action' | translate }}</th><th>{{ 'common.amount' | translate }}</th><th>{{ 'payments.method' | translate }}</th><th>{{ 'payments.reference' | translate }}</th><th>{{ 'common.date' | translate }}</th></tr></thead>
          <tbody>
            @for (l of d.logs.items; track l.id) {
              <tr>
                <td>{{ l.studentName }}</td>
                <td>{{ 'payments.monthN' | translate: { n: l.monthNumber } }}</td>
                <td>{{ 'paymentAction.' + l.action | translate }}</td>
                <td class="num">{{ l.amount | number: '1.0-2' }} {{ l.currency }}</td>
                <td>{{ l.method ? ('method.' + l.method | translate) : '—' }}</td>
                <td class="ltr">{{ l.reference ?? '' }} {{ l.note ?? '' }}</td>
                <td>{{ l.createdAt | date: 'short' }}</td>
              </tr>
            } @empty { <tr><td colspan="7" class="empty">{{ 'common.noData' | translate }}</td></tr> }
          </tbody>
        </table>
      </div>
      <div class="form-actions">
        <button mat-button [disabled]="d.logs.page <= 1" (click)="load(d.logs.page - 1)">{{ 'common.previous' | translate }}</button>
        <span class="muted">{{ d.logs.page }} / {{ d.logs.totalPages || 1 }}</span>
        <button mat-button [disabled]="d.logs.page >= d.logs.totalPages" (click)="load(d.logs.page + 1)">{{ 'common.next' | translate }}</button>
      </div>
    }
  `,
})
export class PaymentLogsPage implements OnInit {
  private readonly api = inject(ApiService);
  protected readonly data = signal<PaymentLogPageDto | null>(null);
  protected from = '';
  protected to = '';

  ngOnInit(): void {
    this.load(1);
  }

  protected load(page: number): void {
    this.api
      .get<PaymentLogPageDto>(`${Api.finance}/payment-logs`, { from: this.from, to: this.to, page, pageSize: 50 })
      .subscribe((d) => this.data.set(d));
  }
}
