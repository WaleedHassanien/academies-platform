import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { TranslateService } from '@ngx-translate/core';
import { Api, ApiService } from '../../core/api/api.service';
import { DashboardDto } from '../../core/api/models';
import { addDays, isoDate, Notifier } from '../../shared/notifier';
import { PAGE_IMPORTS } from '../../shared/page-imports';
import { BarChart, ChartSeries, Stat } from '../../shared/ui';

/** KPIs and charts for attendance, revenue and performance (US-038). Supervisors see their teachers only. */
@Component({
  selector: 'app-dashboard',
  imports: [PAGE_IMPORTS, Stat, BarChart],
  template: `
    <div class="page-header">
      <h1>{{ 'nav.dashboard' | translate }}</h1>
      <div class="toolbar" style="margin: 0">
        <mat-form-field><mat-label>{{ 'common.from' | translate }}</mat-label><input matInput type="date" [(ngModel)]="from" /></mat-form-field>
        <mat-form-field><mat-label>{{ 'common.to' | translate }}</mat-label><input matInput type="date" [(ngModel)]="to" /></mat-form-field>
        <button mat-stroked-button (click)="load()">{{ 'common.apply' | translate }}</button>
      </div>
    </div>

    @if (data(); as d) {
      <div class="stats">
        <app-stat icon="school" [label]="'dashboard.activeStudents' | translate" [value]="d.academic.activeStudents" />
        <app-stat icon="co_present" [label]="'dashboard.teachers' | translate" [value]="d.academic.teachers" />
        <app-stat icon="event_available" [label]="'dashboard.sessionsCompleted' | translate" [value]="d.academic.sessionsCompleted" [hint]="('dashboard.scheduled' | translate) + ': ' + d.academic.sessionsScheduled" />
        <app-stat icon="how_to_reg" [label]="'dashboard.attendanceRate' | translate" [value]="d.academic.attendanceRate + '%'" />
        @if (d.finance; as f) {
          <app-stat icon="trending_up" [label]="'finance.revenue' | translate" [value]="(f.revenue | number: '1.0-0') ?? ''" />
          <app-stat icon="account_balance" [label]="'finance.net' | translate" [value]="(f.net | number: '1.0-0') ?? ''" />
          <app-stat icon="pending_actions" [label]="'finance.outstanding' | translate" [value]="(f.outstanding | number: '1.0-0') ?? ''" />
        }
      </div>

      <div class="grid">
        <mat-card appearance="outlined">
          <mat-card-header><mat-card-title>{{ 'dashboard.sessionsAndAttendance' | translate }}</mat-card-title></mat-card-header>
          <mat-card-content><app-bar-chart [labels]="months()" [series]="academicSeries()" /></mat-card-content>
        </mat-card>
        @if (d.finance) {
          <mat-card appearance="outlined">
            <mat-card-header><mat-card-title>{{ 'dashboard.revenueVsCosts' | translate }}</mat-card-title></mat-card-header>
            <mat-card-content><app-bar-chart [labels]="financeMonths()" [series]="financeSeries()" /></mat-card-content>
          </mat-card>
        }
      </div>

      <mat-card appearance="outlined">
        <mat-card-header><mat-card-title>{{ 'dashboard.topTeachers' | translate }}</mat-card-title></mat-card-header>
        <mat-card-content>
          <table class="data-table">
            <tbody>
              @for (t of d.academic.topTeachers; track t.userId) {
                <tr><td>{{ t.fullName }}</td><td class="num">{{ t.sessions }}</td></tr>
              } @empty {
                <tr><td class="empty">{{ 'common.noData' | translate }}</td></tr>
              }
            </tbody>
          </table>
        </mat-card-content>
      </mat-card>
    } @else if (error()) {
      <p class="empty">{{ error() }}</p>
    } @else {
      <mat-progress-bar mode="indeterminate" />
    }
  `,
})
export class DashboardPage implements OnInit {
  private readonly api = inject(ApiService);
  private readonly notify = inject(Notifier);
  private readonly translate = inject(TranslateService);

  protected readonly data = signal<DashboardDto | null>(null);
  protected readonly error = signal<string | null>(null);
  protected from = isoDate(addDays(new Date(), -180));
  protected to = isoDate(new Date());

  protected readonly months = computed(() => this.data()?.academic.monthly.map((m) => m.month.slice(2)) ?? []);
  protected readonly academicSeries = computed<ChartSeries[]>(() => {
    const monthly = this.data()?.academic.monthly ?? [];
    return [
      { name: this.translate.instant('dashboard.sessionsCompleted'), values: monthly.map((m) => m.sessionsCompleted), color: '#6d4aff' },
      { name: this.translate.instant('dashboard.attendanceRate'), values: monthly.map((m) => m.attendanceRate), color: '#10b981' },
    ];
  });
  protected readonly financeMonths = computed(() => this.data()?.finance?.monthly.map((m) => m.month.slice(2)) ?? []);
  protected readonly financeSeries = computed<ChartSeries[]>(() => {
    const monthly = this.data()?.finance?.monthly ?? [];
    return [
      { name: this.translate.instant('finance.revenue'), values: monthly.map((m) => m.revenue), color: '#10b981' },
      { name: this.translate.instant('finance.salaries'), values: monthly.map((m) => m.salaries), color: '#f59e0b' },
      { name: this.translate.instant('finance.expenses'), values: monthly.map((m) => m.expenses), color: '#f43f5e' },
    ];
  });

  ngOnInit(): void {
    this.load();
  }

  protected load(): void {
    this.error.set(null);
    this.api.get<DashboardDto>(`${Api.engagement}/dashboards`, { from: this.from, to: this.to }).subscribe({
      next: (d) => this.data.set(d),
      error: (e) => {
        this.data.set(null);
        this.error.set(this.translate.instant('dashboard.unavailable'));
        this.notify.error(e);
      },
    });
  }
}
