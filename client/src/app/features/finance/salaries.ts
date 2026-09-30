import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { TranslateService } from '@ngx-translate/core';
import { PagedResult } from '../../core/api/api.models';
import { Api, ApiService } from '../../core/api/api.service';
import { CompensationDto, ExpenseDto, FinanceSummaryDto, GenerateResultDto, SalaryDto, SalaryLogDto, StaffPayDto } from '../../core/api/models';
import { addDays, isoDate, monthKey, Notifier } from '../../shared/notifier';
import { PAGE_IMPORTS } from '../../shared/page-imports';
import { BarChart, ChartSeries, Stat } from '../../shared/ui';

/** Pay settings: fixed monthly salary or rate per session, effective from a date (US-031). */
@Component({
  selector: 'app-compensation',
  imports: [PAGE_IMPORTS],
  template: `
    <div class="page-header"><h1>{{ 'nav.pay' | translate }}</h1></div>
    <p class="muted">{{ 'pay.hint' | translate }}</p>
    <div class="table-wrap">
      <table class="data-table">
        <thead><tr><th>{{ 'common.fullName' | translate }}</th><th>{{ 'users.roles' | translate }}</th><th>{{ 'pay.payType' | translate }}</th><th>{{ 'common.amount' | translate }}</th><th>{{ 'pay.effectiveFrom' | translate }}</th><th></th></tr></thead>
        <tbody>
          @for (s of staff(); track s.userId) {
            <tr [class.selected]="editing()?.userId === s.userId">
              <td>{{ s.fullName }}</td>
              <td>{{ roleNames(s.roles) }}</td>
              <td>{{ s.current ? ('pay.' + s.current.payType | translate) : '—' }}</td>
              <td class="num">{{ s.current?.amount | number: '1.0-2' }}</td>
              <td>{{ s.current?.effectiveFrom | date: 'mediumDate' }}</td>
              <td class="actions"><button mat-button (click)="edit(s)">{{ 'common.edit' | translate }}</button></td>
            </tr>
          } @empty { <tr><td colspan="6" class="empty">{{ 'common.noData' | translate }}</td></tr> }
        </tbody>
      </table>
    </div>
    @if (editing(); as e) {
      <mat-card appearance="outlined" class="panel" style="margin-top: 16px">
        <mat-card-header><mat-card-title>{{ e.fullName }}</mat-card-title></mat-card-header>
        <mat-card-content>
          <div class="form-grid">
            <mat-form-field>
              <mat-label>{{ 'pay.payType' | translate }}</mat-label>
              <mat-select [(ngModel)]="payType"><mat-option value="PerSession">{{ 'pay.PerSession' | translate }}</mat-option><mat-option value="MonthlyFixed">{{ 'pay.MonthlyFixed' | translate }}</mat-option></mat-select>
            </mat-form-field>
            <mat-form-field><mat-label>{{ (payType === 'PerSession' ? 'pay.ratePerSession' : 'pay.monthlySalary') | translate }}</mat-label><input matInput type="number" [(ngModel)]="amount" /></mat-form-field>
            <mat-form-field><mat-label>{{ 'pay.effectiveFrom' | translate }}</mat-label><input matInput type="date" [(ngModel)]="effectiveFrom" /></mat-form-field>
          </div>
          @if (history().length) {
            <h3 class="section-title">{{ 'pay.history' | translate }}</h3>
            <table class="data-table"><tbody>
              @for (h of history(); track h.id) { <tr><td>{{ h.effectiveFrom | date: 'mediumDate' }}</td><td>{{ 'pay.' + h.payType | translate }}</td><td class="num">{{ h.amount | number: '1.0-2' }}</td></tr> }
            </tbody></table>
          }
          <div class="form-actions">
            <button mat-button (click)="editing.set(null)">{{ 'common.cancel' | translate }}</button>
            <button mat-flat-button (click)="save(e)" [disabled]="!amount">{{ 'common.save' | translate }}</button>
          </div>
        </mat-card-content>
      </mat-card>
    }
  `,
})
export class CompensationPage implements OnInit {
  private readonly api = inject(ApiService);
  private readonly notify = inject(Notifier);
  private readonly translate = inject(TranslateService);
  protected readonly staff = signal<StaffPayDto[]>([]);
  protected readonly editing = signal<StaffPayDto | null>(null);
  protected readonly history = signal<CompensationDto[]>([]);
  protected payType: 'PerSession' | 'MonthlyFixed' = 'MonthlyFixed';
  protected amount = 0;
  protected effectiveFrom = isoDate(new Date(new Date().getFullYear(), new Date().getMonth(), 1));

  ngOnInit(): void {
    this.load();
  }

  protected roleNames(roles: string): string {
    return roles.split(',').filter(Boolean).map((r) => this.translate.instant('roles.' + r)).join('، ');
  }

  protected edit(s: StaffPayDto): void {
    this.editing.set(s);
    this.payType = s.current?.payType ?? (s.roles.includes('Teacher') ? 'PerSession' : 'MonthlyFixed');
    this.amount = s.current?.amount ?? 0;
    this.api.get<CompensationDto[]>(`${Api.finance}/compensations/${s.userId}`).subscribe((h) => this.history.set(h));
  }

  protected save(s: StaffPayDto): void {
    this.api.put(`${Api.finance}/compensations/${s.userId}`, { payType: this.payType, amount: this.amount, effectiveFrom: this.effectiveFrom }).subscribe({
      next: () => {
        this.notify.saved();
        this.editing.set(null);
        this.load();
      },
      error: (e) => this.notify.error(e),
    });
  }

  private load(): void {
    this.api.get<StaffPayDto[]>(`${Api.finance}/compensations`).subscribe((s) => this.staff.set(s));
  }
}

/** Generate, pay and adjust monthly salaries (US-032), and the full salary log (US-033). */
@Component({
  selector: 'app-salaries',
  imports: [PAGE_IMPORTS],
  template: `
    <div class="page-header">
      <h1>{{ 'nav.salaries' | translate }}</h1>
      <div class="toolbar" style="margin: 0">
        <mat-form-field><mat-label>{{ 'payments.month' | translate }}</mat-label><input matInput type="month" [(ngModel)]="month" (change)="load()" /></mat-form-field>
        <button mat-flat-button (click)="generate()"><mat-icon>calculate</mat-icon>{{ 'salaries.generate' | translate }}</button>
      </div>
    </div>
    @if (result(); as r) {
      <p class="muted">{{ 'salaries.result' | translate: r }}</p>
    }
    <mat-tab-group>
      <mat-tab [label]="'salaries.thisMonth' | translate">
        <div class="table-wrap tab-body">
          <table class="data-table">
            <thead><tr><th>{{ 'common.fullName' | translate }}</th><th>{{ 'users.role' | translate }}</th><th>{{ 'salaries.calculation' | translate }}</th><th>{{ 'common.amount' | translate }}</th><th>{{ 'common.status' | translate }}</th><th></th></tr></thead>
            <tbody>
              @for (s of salaries(); track s.id) {
                <tr>
                  <td>{{ s.fullName }}</td>
                  <td>{{ 'roles.' + s.role | translate }}</td>
                  <td class="ltr">@if (s.payType === 'PerSession') { {{ s.sessionsCount }} × {{ s.ratePerSession | number: '1.0-2' }} } @else { {{ 'pay.MonthlyFixed' | translate }} }</td>
                  <td class="num"><b>{{ s.amount | number: '1.0-2' }}</b></td>
                  <td><span class="status" [class]="s.status">{{ 'status.' + s.status | translate }}</span> @if (s.note) { <span class="muted">{{ s.note }}</span> }</td>
                  <td class="actions">
                    @if (s.status === 'Pending') { <button mat-button (click)="pay(s)">{{ 'salaries.pay' | translate }}</button> }
                    <button mat-button (click)="adjust(s)">{{ 'salaries.adjust' | translate }}</button>
                  </td>
                </tr>
              } @empty { <tr><td colspan="6" class="empty">{{ 'salaries.none' | translate }}</td></tr> }
            </tbody>
          </table>
        </div>
      </mat-tab>
      <mat-tab [label]="'nav.salaryLogs' | translate">
        <div class="table-wrap tab-body">
          <table class="data-table">
            <thead><tr><th>{{ 'common.fullName' | translate }}</th><th>{{ 'payments.month' | translate }}</th><th>{{ 'payments.action' | translate }}</th><th>{{ 'salaries.calculation' | translate }}</th><th>{{ 'common.amount' | translate }}</th><th>{{ 'common.notes' | translate }}</th><th>{{ 'common.date' | translate }}</th></tr></thead>
            <tbody>
              @for (l of logs(); track l.id) {
                <tr>
                  <td>{{ l.fullName }}</td><td class="ltr">{{ l.year }}-{{ l.month }}</td><td>{{ 'salaryAction.' + l.action | translate }}</td>
                  <td class="ltr">@if (l.sessionsCount != null) { {{ l.sessionsCount }} × {{ l.ratePerSession | number: '1.0-2' }} }</td>
                  <td class="num">{{ l.amount | number: '1.0-2' }}</td><td>{{ l.note }}</td><td>{{ l.createdAt | date: 'short' }}</td>
                </tr>
              } @empty { <tr><td colspan="7" class="empty">{{ 'common.noData' | translate }}</td></tr> }
            </tbody>
          </table>
        </div>
      </mat-tab>
    </mat-tab-group>
  `,
  styles: `.tab-body { margin: 16px 0; }`,
})
export class SalariesPage implements OnInit {
  private readonly api = inject(ApiService);
  private readonly notify = inject(Notifier);
  protected readonly salaries = signal<SalaryDto[]>([]);
  protected readonly logs = signal<SalaryLogDto[]>([]);
  protected readonly result = signal<GenerateResultDto | null>(null);
  protected month = monthKey();

  ngOnInit(): void {
    this.load();
  }

  protected load(): void {
    this.api.get<SalaryDto[]>(`${Api.finance}/salaries`, { month: this.month }).subscribe((s) => this.salaries.set(s));
    this.api.get<PagedResult<SalaryLogDto>>(`${Api.finance}/salary-logs`, { pageSize: 100 }).subscribe((l) => this.logs.set(l.items));
  }

  protected generate(): void {
    this.api.post<GenerateResultDto>(`${Api.finance}/salaries/generate`, {}, { month: this.month }).subscribe({
      next: (r) => {
        this.result.set(r);
        this.load();
      },
      error: (e) => this.notify.error(e),
    });
  }

  protected pay(s: SalaryDto): void {
    this.api.post(`${Api.finance}/salaries/${s.id}/pay`).subscribe({ next: () => this.load(), error: (e) => this.notify.error(e) });
  }

  protected adjust(s: SalaryDto): void {
    const amount = Number(prompt('New amount / المبلغ الجديد', String(s.amount)));
    const note = !isNaN(amount) ? prompt('Reason (required) / السبب (إلزامي)') : null;
    if (!isNaN(amount) && note) {
      this.api.post(`${Api.finance}/salaries/${s.id}/adjust`, { amount, note }).subscribe({ next: () => this.load(), error: (e) => this.notify.error(e) });
    }
  }
}

/** Expenses (US-034). */
@Component({
  selector: 'app-expenses',
  imports: [PAGE_IMPORTS],
  template: `
    <div class="page-header"><h1>{{ 'nav.expenses' | translate }}</h1></div>
    <mat-card appearance="outlined" class="panel">
      <mat-card-content>
        <div class="form-grid">
          <mat-form-field><mat-label>{{ 'expenses.category' | translate }}</mat-label><input matInput [(ngModel)]="form.category" list="categories" /></mat-form-field>
          <datalist id="categories">@for (c of categories(); track c) { <option [value]="c"></option> }</datalist>
          <mat-form-field><mat-label>{{ 'common.amount' | translate }}</mat-label><input matInput type="number" [(ngModel)]="form.amount" /></mat-form-field>
          <mat-form-field><mat-label>{{ 'common.date' | translate }}</mat-label><input matInput type="date" [(ngModel)]="form.spentOn" /></mat-form-field>
          <mat-form-field><mat-label>{{ 'common.description' | translate }}</mat-label><input matInput [(ngModel)]="form.description" /></mat-form-field>
          <mat-form-field><mat-label>{{ 'payments.reference' | translate }}</mat-label><input matInput [(ngModel)]="form.reference" /></mat-form-field>
        </div>
        <div class="form-actions">
          @if (editingId) { <button mat-button (click)="reset()">{{ 'common.cancel' | translate }}</button> }
          <button mat-flat-button (click)="save()" [disabled]="!form.category || !form.amount">{{ 'common.save' | translate }}</button>
        </div>
      </mat-card-content>
    </mat-card>
    <div class="table-wrap">
      <table class="data-table">
        <thead><tr><th>{{ 'common.date' | translate }}</th><th>{{ 'expenses.category' | translate }}</th><th>{{ 'common.description' | translate }}</th><th>{{ 'common.amount' | translate }}</th><th></th></tr></thead>
        <tbody>
          @for (e of expenses(); track e.id) {
            <tr>
              <td>{{ e.spentOn | date: 'mediumDate' }}</td><td>{{ e.category }}</td><td>{{ e.description }}</td><td class="num">{{ e.amount | number: '1.0-2' }}</td>
              <td class="actions"><button mat-button (click)="edit(e)">{{ 'common.edit' | translate }}</button><button mat-button (click)="remove(e)">{{ 'common.delete' | translate }}</button></td>
            </tr>
          } @empty { <tr><td colspan="5" class="empty">{{ 'common.noData' | translate }}</td></tr> }
        </tbody>
      </table>
    </div>
  `,
})
export class ExpensesPage implements OnInit {
  private readonly api = inject(ApiService);
  private readonly notify = inject(Notifier);
  protected readonly expenses = signal<ExpenseDto[]>([]);
  protected readonly categories = computed(() => [...new Set(this.expenses().map((e) => e.category))]);
  protected editingId: number | null = null;
  protected form = this.blank();

  ngOnInit(): void {
    this.load();
  }

  protected edit(e: ExpenseDto): void {
    this.editingId = e.id;
    this.form = { category: e.category, amount: e.amount, spentOn: e.spentOn, description: e.description ?? '', reference: e.reference ?? '' };
  }

  protected reset(): void {
    this.editingId = null;
    this.form = this.blank();
  }

  protected save(): void {
    const body = { ...this.form, description: this.form.description || null, reference: this.form.reference || null };
    const request = this.editingId ? this.api.put(`${Api.finance}/expenses/${this.editingId}`, body) : this.api.post(`${Api.finance}/expenses`, body);
    request.subscribe({
      next: () => {
        this.notify.saved();
        this.reset();
        this.load();
      },
      error: (e) => this.notify.error(e),
    });
  }

  protected remove(e: ExpenseDto): void {
    if (confirm(`${e.category} ${e.amount}?`)) {
      this.api.delete(`${Api.finance}/expenses/${e.id}`).subscribe({ next: () => this.load(), error: (err) => this.notify.error(err) });
    }
  }

  private blank() {
    return { category: '', amount: 0, spentOn: isoDate(new Date()), description: '', reference: '' };
  }

  private load(): void {
    this.api.get<ExpenseDto[]>(`${Api.finance}/expenses`).subscribe((e) => this.expenses.set(e));
  }
}

/** Revenue − salaries − expenses by period and month (US-034). */
@Component({
  selector: 'app-reports',
  imports: [PAGE_IMPORTS, Stat, BarChart],
  template: `
    <div class="page-header">
      <h1>{{ 'nav.reports' | translate }} @if (summary(); as s) { <span class="muted">({{ 'currency.' + s.currency | translate }})</span> }</h1>
      <div class="toolbar" style="margin: 0">
        <mat-form-field><mat-label>{{ 'common.from' | translate }}</mat-label><input matInput type="date" [(ngModel)]="from" /></mat-form-field>
        <mat-form-field><mat-label>{{ 'common.to' | translate }}</mat-label><input matInput type="date" [(ngModel)]="to" /></mat-form-field>
        <button mat-stroked-button (click)="load()">{{ 'common.apply' | translate }}</button>
      </div>
    </div>
    @if (summary(); as s) {
      <div class="stats">
        <app-stat icon="trending_up" [label]="'finance.revenue' | translate" [value]="(s.revenue | number: '1.0-2') ?? ''" [hint]="('finance.refunds' | translate) + ': ' + (s.refunds | number: '1.0-2')" />
        <app-stat icon="account_balance_wallet" [label]="'finance.salaries' | translate" [value]="(s.salaries | number: '1.0-2') ?? ''" />
        <app-stat icon="shopping_cart" [label]="'finance.expenses' | translate" [value]="(s.expenses | number: '1.0-2') ?? ''" />
        <app-stat icon="account_balance" [label]="'finance.net' | translate" [value]="(s.net | number: '1.0-2') ?? ''" />
        <app-stat icon="pending_actions" [label]="'finance.outstanding' | translate" [value]="(s.outstanding | number: '1.0-2') ?? ''" />
      </div>
      @if (s.missingRates?.length) {
        <p class="status warn">{{ 'finance.missingRates' | translate: { currencies: s.missingRates!.join(', ') } }}</p>
      }
      @if (byCurrency().length > 1) {
        <div class="table-wrap currencies">
          <table class="data-table">
            <thead><tr><th>{{ 'billing.currency' | translate }}</th><th>{{ 'finance.revenue' | translate }}</th><th>{{ 'finance.outstanding' | translate }}</th></tr></thead>
            <tbody>
              @for (c of byCurrency(); track c.currency) {
                <tr><td>{{ 'currency.' + c.currency | translate }}</td><td class="num">{{ c.revenue | number: '1.0-2' }}</td><td class="num">{{ c.outstanding | number: '1.0-2' }}</td></tr>
              }
            </tbody>
          </table>
        </div>
      }
      <mat-card appearance="outlined" class="panel">
        <mat-card-content><app-bar-chart [labels]="labels()" [series]="series()" /></mat-card-content>
      </mat-card>
      <div class="grid">
        <div class="table-wrap">
          <table class="data-table">
            <thead><tr><th>{{ 'payments.month' | translate }}</th><th>{{ 'finance.revenue' | translate }}</th><th>{{ 'finance.salaries' | translate }}</th><th>{{ 'finance.expenses' | translate }}</th><th>{{ 'finance.net' | translate }}</th></tr></thead>
            <tbody>
              @for (m of s.monthly; track m.month) {
                <tr><td class="ltr">{{ m.month }}</td><td class="num">{{ m.revenue | number: '1.0-2' }}</td><td class="num">{{ m.salaries | number: '1.0-2' }}</td><td class="num">{{ m.expenses | number: '1.0-2' }}</td><td class="num"><b>{{ m.net | number: '1.0-2' }}</b></td></tr>
              }
            </tbody>
          </table>
        </div>
        <div class="table-wrap">
          <table class="data-table">
            <thead><tr><th>{{ 'expenses.category' | translate }}</th><th>{{ 'common.amount' | translate }}</th></tr></thead>
            <tbody>
              @for (c of categories(); track c[0]) { <tr><td>{{ c[0] }}</td><td class="num">{{ c[1] | number: '1.0-2' }}</td></tr> }
            </tbody>
          </table>
        </div>
      </div>
    }
  `,
})
export class ReportsPage implements OnInit {
  private readonly api = inject(ApiService);
  private readonly translate = inject(TranslateService);
  protected readonly summary = signal<FinanceSummaryDto | null>(null);
  protected from = isoDate(addDays(new Date(), -180));
  protected to = isoDate(new Date());

  protected readonly labels = computed(() => this.summary()?.monthly.map((m) => m.month.slice(2)) ?? []);
  protected readonly categories = computed(() => Object.entries(this.summary()?.expensesByCategory ?? {}));
  /** Money received and still due in each billing currency, before conversion. */
  protected readonly byCurrency = computed(() => {
    const s = this.summary();
    const currencies = new Set([...Object.keys(s?.revenueByCurrency ?? {}), ...Object.keys(s?.outstandingByCurrency ?? {})]);
    return [...currencies].sort().map((currency) => ({
      currency, revenue: s?.revenueByCurrency?.[currency] ?? 0, outstanding: s?.outstandingByCurrency?.[currency] ?? 0,
    }));
  });
  protected readonly series = computed<ChartSeries[]>(() => {
    const m = this.summary()?.monthly ?? [];
    return [
      { name: this.translate.instant('finance.revenue'), values: m.map((x) => x.revenue), color: '#10b981' },
      { name: this.translate.instant('finance.salaries'), values: m.map((x) => x.salaries), color: '#f59e0b' },
      { name: this.translate.instant('finance.expenses'), values: m.map((x) => x.expenses), color: '#f43f5e' },
      { name: this.translate.instant('finance.net'), values: m.map((x) => x.net), color: '#6d4aff' },
    ];
  });

  ngOnInit(): void {
    this.load();
  }

  protected load(): void {
    this.api.get<FinanceSummaryDto>(`${Api.finance}/reports/summary`, { from: this.from, to: this.to }).subscribe((s) => this.summary.set(s));
  }
}
