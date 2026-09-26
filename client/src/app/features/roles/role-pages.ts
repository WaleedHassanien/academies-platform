import { Component, inject, input, OnInit, signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { TranslateService } from '@ngx-translate/core';
import { PagedResult } from '../../core/api/api.models';
import { Api, ApiService } from '../../core/api/api.service';
import {
  CheckoutDto, LeaderboardEntry, MyEarningsDto, MyOverviewDto, NotificationDto, SalaryLogDto, SessionDto, StudentOverviewDto, StudentPaymentsDto,
  StudentPointsDto, WorkDayDto,
} from '../../core/api/models';
import { BillingCard } from '../../shared/billing-card';
import { MySessions, SessionActions } from '../../shared/sessions-kit';
import { AuthService } from '../../core/auth/auth.service';
import { Permissions, Roles } from '../../core/auth/permissions';
import { LanguageService } from '../../core/i18n/language.service';
import { Notifier } from '../../shared/notifier';
import { PAGE_IMPORTS } from '../../shared/page-imports';
import { Stat } from '../../shared/ui';

/** Upcoming sessions as a compact list, shared by the role pages. */
@Component({
  selector: 'app-session-list',
  imports: [PAGE_IMPORTS],
  template: `
    <table class="data-table">
      <tbody>
        @for (s of sessions(); track s.id) {
          <tr>
            <td>{{ s.startsAtUtc | date: 'EEE d MMM, HH:mm' }}</td>
            <td><b>{{ s.title }}</b><div class="muted">{{ s.courseName }} · {{ s.teacherName }}</div></td>
            <td>
              @if (s.meetingUrl && s.status === 'Scheduled') { <button mat-button (click)="actions.join(s)"><mat-icon>videocam</mat-icon>{{ 'sessions.join' | translate }}</button> }
              @else { {{ s.location }} }
            </td>
          </tr>
        } @empty {
          <tr><td class="empty">{{ 'sessions.none' | translate }}</td></tr>
        }
      </tbody>
    </table>
  `,
})
export class SessionList {
  readonly sessions = input.required<SessionDto[]>();
  protected readonly actions = inject(SessionActions);
}

/** A student's summary card: attendance, points, next sessions, latest feedback (US-028, US-040). */
@Component({
  selector: 'app-student-card',
  imports: [PAGE_IMPORTS, Stat, SessionList],
  template: `
    @let s = student();
    <div class="stats">
      <app-stat icon="how_to_reg" [label]="'dashboard.attendanceRate' | translate" [value]="s.attendance.rate + '%'"
                [hint]="('status.Present' | translate) + ' ' + s.attendance.present + ' · ' + ('status.Late' | translate) + ' ' + s.attendance.late + ' · ' + ('status.Absent' | translate) + ' ' + s.attendance.absent" />
      <app-stat icon="stars" [label]="'student.points' | translate" [value]="s.points" />
    </div>
    <h3 class="section-title">{{ 'student.upcoming' | translate }}</h3>
    <div class="table-wrap"><app-session-list [sessions]="s.upcoming" /></div>
    <h3 class="section-title">{{ 'student.feedback' | translate }}</h3>
    <div class="table-wrap">
      <table class="data-table">
        <tbody>
          @for (f of s.recentFeedback; track f.id) {
            <tr><td>{{ f.sessionStartsAtUtc | date: 'mediumDate' }}</td><td>{{ f.sessionTitle }}</td><td>{{ stars(f.rating) }}</td><td>{{ f.comment }}</td><td class="muted">{{ f.teacherName }}</td></tr>
          } @empty { <tr><td class="empty">{{ 'common.noData' | translate }}</td></tr> }
        </tbody>
      </table>
    </div>
  `,
})
export class StudentCard {
  readonly student = input.required<StudentOverviewDto>();

  protected stars(n: number): string {
    return '★'.repeat(n) + '☆'.repeat(5 - n);
  }
}

/** Teacher: my sessions this week or month, my students, and what I've earned (US-028). */
@Component({
  selector: 'app-teacher-home',
  imports: [PAGE_IMPORTS, Stat, MySessions],
  template: `
    <div class="page-header">
      <h1>{{ 'home.welcome' | translate: { name: auth.user()?.fullName } }}</h1>
      <div class="toolbar">
        <button mat-flat-button (click)="actions.openMyRoom()"><mat-icon>video_call</mat-icon>{{ 'room.myRoom' | translate }}</button>
        <a mat-stroked-button routerLink="/sessions"><mat-icon>calendar_month</mat-icon>{{ 'nav.sessions' | translate }}</a>
      </div>
    </div>

    @if (overview()?.teacher; as t) {
      <div class="stats">
        <app-stat icon="groups" [label]="'teacher.students' | translate" [value]="t.students.length" />
        <app-stat icon="hourglass_top" [label]="'teacher.pending' | translate" [value]="t.pendingToComplete" [hint]="'teacher.pendingHint' | translate" />
        @if (earnings(); as e) {
          <app-stat icon="account_balance_wallet" [label]="'earnings.owed' | translate" [value]="(e.unpaid.total | number: '1.0-2') + ' ' + e.unpaid.currency"
                    [hint]="e.unpaid.sessions.length + ' ' + ('payouts.sessions' | translate)" />
          @if (e.payouts[0]; as last) {
            <app-stat icon="paid" [label]="'earnings.lastPayout' | translate" [value]="(last.amount | number: '1.0-2') + ' ' + last.currency"
                      [hint]="(last.paidOnUtc | utcDate: 'd MMM') ?? ('payouts.status_Pending' | translate)" />
          }
        }
      </div>

      <div class="grid">
        <mat-card appearance="outlined">
          <mat-card-header><mat-card-title>{{ 'teacher.students' | translate }}</mat-card-title></mat-card-header>
          <mat-card-content>
            <div class="people">
              @for (s of t.students; track s.userId) {
                <a class="person" [routerLink]="['/students', s.userId]"><span class="avatar">{{ initials(s.fullName) }}</span>{{ s.fullName }}</a>
              } @empty { <p class="muted">{{ 'common.noData' | translate }}</p> }
            </div>
          </mat-card-content>
        </mat-card>
        @if (earnings(); as e) {
          <mat-card appearance="outlined">
            <mat-card-header><mat-card-title>{{ 'earnings.title' | translate }}</mat-card-title></mat-card-header>
            <mat-card-content>
              <table class="data-table">
                <tbody>
                  @for (p of e.payouts.slice(0, 5); track p.id) {
                    <tr>
                      <td>{{ 'payouts.kind_' + p.kind | translate }} · {{ p.month }}/{{ p.year }}</td>
                      <td class="num">{{ p.sessionsCount }} {{ 'payouts.sessions' | translate }}</td>
                      <td class="num">{{ p.amount | number: '1.0-2' }} {{ p.currency }}</td>
                      <td><span class="status" [class.ok]="p.status === 'Paid'" [class.warn]="p.status === 'Pending'">{{ 'payouts.status_' + p.status | translate }}</span></td>
                    </tr>
                  } @empty { <tr><td class="empty">{{ 'earnings.none' | translate }}</td></tr> }
                </tbody>
              </table>
            </mat-card-content>
          </mat-card>
        }
      </div>
    }

    <h2 class="block-title">{{ 'mySessions.title' | translate }}</h2>
    <app-my-sessions [showTeacher]="false" breakdown="student" />
  `,
  styles: `
    .people { display: flex; flex-wrap: wrap; gap: 8px; }
    .person { display: inline-flex; align-items: center; gap: 8px; padding: 4px 12px 4px 4px; border-radius: 999px; text-decoration: none; color: inherit;
      background: var(--app-surface-2); border: 1px solid var(--app-border); transition: border-color 0.15s, transform 0.15s; }
    .person:hover { border-color: var(--mat-sys-primary); transform: translateY(-1px); }
    .avatar { display: grid; place-items: center; width: 28px; height: 28px; border-radius: 50%; color: #fff; font-size: 0.7rem; font-weight: 700; background: var(--app-gradient); }
    .block-title { margin: 8px 0 16px; font-size: 1.2rem; }
  `,
})
export class TeacherHomePage implements OnInit {
  private readonly api = inject(ApiService);
  protected readonly actions = inject(SessionActions);
  protected readonly auth = inject(AuthService);
  protected readonly overview = signal<MyOverviewDto | null>(null);
  protected readonly earnings = signal<MyEarningsDto | null>(null);

  ngOnInit(): void {
    this.api.get<MyOverviewDto>(`${Api.academic}/me/overview`).subscribe((o) => this.overview.set(o));
    this.api.get<MyEarningsDto>(`${Api.finance}/me/earnings`).subscribe({ next: (e) => this.earnings.set(e), error: () => this.earnings.set(null) });
  }

  protected initials(name: string): string {
    return name.trim().split(/\s+/).slice(0, 2).map((p) => p[0]).join('').toUpperCase();
  }
}

/** Supervisor: the teachers I follow and their students, a session report per teacher and student, and my shift (US-021, US-028). */
@Component({
  selector: 'app-supervisor-home',
  imports: [PAGE_IMPORTS, MySessions],
  template: `
    <div class="page-header">
      <h1>{{ 'home.welcome' | translate: { name: auth.user()?.fullName } }}</h1>
      <div class="toolbar">
        <a mat-flat-button routerLink="/requests"><mat-icon>pending_actions</mat-icon>{{ 'nav.requests' | translate }}</a>
        <a mat-stroked-button routerLink="/dashboard"><mat-icon>insights</mat-icon>{{ 'nav.dashboard' | translate }}</a>
      </div>
    </div>
    @if (overview()?.supervisor; as s) {
      <mat-card appearance="outlined" class="panel">
        <mat-card-header><mat-card-title>{{ 'supervisor.teachers' | translate }}</mat-card-title></mat-card-header>
        <mat-card-content>
          <table class="data-table">
            <thead><tr><th>{{ 'common.fullName' | translate }}</th><th>{{ 'teacher.students' | translate }}</th><th>{{ 'teacher.completedThisMonth' | translate }}</th><th></th></tr></thead>
            <tbody>
              @for (t of s.teachers; track t.userId) {
                <tr>
                  <td>{{ t.fullName }}</td><td class="num">{{ t.students }}</td><td class="num">{{ t.completedThisMonth }}</td>
                  <td class="actions">
                    <button mat-stroked-button (click)="actions.shareTeacherRoom(t)"><mat-icon>link</mat-icon>{{ 'room.link' | translate }}</button>
                  </td>
                </tr>
              }
              @empty { <tr><td colspan="4" class="empty">{{ 'common.noData' | translate }}</td></tr> }
            </tbody>
          </table>
        </mat-card-content>
      </mat-card>
    }

    <h2 class="block-title">{{ 'mySessions.teamTitle' | translate }}</h2>
    <app-my-sessions breakdown="both" [showPending]="true" />

    <mat-card appearance="outlined" class="shift">
      <mat-card-header><mat-card-title>{{ 'staff.workSchedule' | translate }}</mat-card-title></mat-card-header>
      <mat-card-content>
        <table class="data-table">
          <tbody>
            @for (d of schedule(); track d.day) {
              <tr [class.muted]="!d.isWorkingDay">
                <td>{{ 'days.' + d.day | translate }}</td>
                <td class="ltr">@if (d.isWorkingDay) { {{ d.shiftStart?.slice(0, 5) }} – {{ d.shiftEnd?.slice(0, 5) }} } @else { {{ 'staff.dayOff' | translate }} }</td>
              </tr>
            }
          </tbody>
        </table>
      </mat-card-content>
    </mat-card>
  `,
  styles: `
    .block-title { margin: 8px 0 16px; font-size: 1.2rem; }
    .shift { margin-top: 24px; }
  `,
})
export class SupervisorHomePage implements OnInit {
  private readonly api = inject(ApiService);
  protected readonly actions = inject(SessionActions);
  protected readonly auth = inject(AuthService);
  protected readonly overview = signal<MyOverviewDto | null>(null);
  protected readonly schedule = signal<WorkDayDto[]>([]);

  ngOnInit(): void {
    this.api.get<MyOverviewDto>(`${Api.academic}/me/overview`).subscribe((o) => this.overview.set(o));
    this.api.get<WorkDayDto[]>(`${Api.academic}/me/work-schedule`).subscribe((d) => this.schedule.set(d));
  }
}

/** Student: this week's (or month's) sessions with excuses, my package or bill, feedback, points and badges (US-028, US-042). */
@Component({
  selector: 'app-student-home',
  imports: [PAGE_IMPORTS, MySessions, BillingCard],
  template: `
    <div class="page-header"><h1>{{ 'home.welcome' | translate: { name: auth.user()?.fullName } }}</h1></div>

    <app-my-sessions [showStudent]="false" [canExcuse]="true" (changed)="billing.reload()" />

    <div class="grid" style="margin-top: 24px">
      <app-billing-card #billing [studentUserId]="auth.user()?.id ?? 0" />
      @if (overview()?.student; as s) {
        <mat-card appearance="outlined">
          <mat-card-header><mat-card-title>{{ 'student.feedback' | translate }}</mat-card-title></mat-card-header>
          <mat-card-content>
            @for (f of s.recentFeedback; track f.id) {
              <div class="feedback">
                <div><b>{{ f.sessionTitle }}</b> <span class="stars">{{ stars(f.rating) }}</span></div>
                @if (f.comment) { <p>{{ f.comment }}</p> }
                <small class="muted">{{ f.teacherName }} · {{ f.sessionStartsAtUtc | utcDate: 'd MMM' }}</small>
              </div>
            } @empty { <p class="muted">{{ 'common.noData' | translate }}</p> }
          </mat-card-content>
        </mat-card>
      }
      @if (points(); as p) {
        <mat-card appearance="outlined">
          <mat-card-header><mat-card-title>{{ 'student.badges' | translate }}</mat-card-title></mat-card-header>
          <mat-card-content>
            @for (b of p.badges; track b.code) {
              <div class="badge" [class.earned]="b.awardedOnUtc">
                <mat-icon>{{ b.awardedOnUtc ? 'military_tech' : 'lock' }}</mat-icon>
                <span>{{ 'badges.' + b.code | translate }}</span>
                <span class="muted">{{ b.threshold }} {{ 'student.pointsShort' | translate }}</span>
              </div>
            }
          </mat-card-content>
        </mat-card>
      }
      <mat-card appearance="outlined">
        <mat-card-header><mat-card-title>{{ 'student.leaderboard' | translate }}</mat-card-title></mat-card-header>
        <mat-card-content>
          <table class="data-table">
            <tbody>
              @for (l of leaderboard(); track l.studentUserId) {
                <tr [class.selected]="l.studentUserId === auth.user()?.id"><td>#{{ l.rank }}</td><td>{{ l.fullName }}</td><td class="num">{{ l.total }}</td></tr>
              } @empty { <tr><td class="empty">{{ 'common.noData' | translate }}</td></tr> }
            </tbody>
          </table>
        </mat-card-content>
      </mat-card>
    </div>
  `,
  styles: `
    .badge { display: flex; align-items: center; gap: 8px; padding: 4px 0; opacity: 0.5; }
    .badge.earned { opacity: 1; color: #b7791f; }
    .feedback { padding: 8px 0; border-bottom: 1px dashed var(--app-border); }
    .feedback:last-child { border-bottom: 0; }
    .feedback p { margin: 4px 0; }
    .stars { color: #f59e0b; letter-spacing: 1px; }
  `,
})
export class StudentHomePage implements OnInit {
  private readonly api = inject(ApiService);
  protected readonly auth = inject(AuthService);
  protected readonly overview = signal<MyOverviewDto | null>(null);
  protected readonly points = signal<StudentPointsDto | null>(null);
  protected readonly leaderboard = signal<LeaderboardEntry[]>([]);

  protected stars(n: number): string {
    return '★'.repeat(n) + '☆'.repeat(5 - n);
  }

  ngOnInit(): void {
    this.api.get<MyOverviewDto>(`${Api.academic}/me/overview`).subscribe((o) => this.overview.set(o));
    this.api.get<StudentPointsDto>(`${Api.academic}/students/${this.auth.user()?.id}/points`).subscribe((p) => this.points.set(p));
    this.api.get<LeaderboardEntry[]>(`${Api.academic}/leaderboard`, { top: 10 }).subscribe((l) => this.leaderboard.set(l));
  }
}

/** Parent portal: each child's attendance, feedback, sessions and monthly payments, with online pay (US-039, US-040). */
@Component({
  selector: 'app-parent-home',
  imports: [PAGE_IMPORTS, StudentCard, MySessions],
  template: `
    <div class="page-header"><h1>{{ 'nav.parentPortal' | translate }}</h1></div>
    @if (paymentResult(); as r) {
      <p class="status" [class.ok]="r === 'success'" [class.warn]="r === 'pending'" [class.bad]="r === 'failed' || r === 'cancelled'">{{ 'parent.payment_' + r | translate }}</p>
    }
    <app-my-sessions [canExcuse]="true" breakdown="student" />
    <div style="height: 24px"></div>
    @for (child of overview()?.children ?? []; track child.userId) {
      <mat-card appearance="outlined" class="panel">
        <mat-card-header><mat-card-title>{{ child.fullName }}</mat-card-title><mat-card-subtitle>{{ child.level }}</mat-card-subtitle></mat-card-header>
        <mat-card-content>
          <app-student-card [student]="child" />
          <h3 class="section-title">{{ 'parent.payments' | translate }}</h3>
          @if (payments()[child.userId]; as p) {
            <p class="muted">{{ 'payments.paid' | translate }}: {{ p.totalPaid | number: '1.0-2' }} {{ p.currency }} · {{ 'finance.outstanding' | translate }}: {{ p.outstanding | number: '1.0-2' }} {{ p.currency }}</p>
            <div class="table-wrap">
              <table class="data-table">
                <tbody>
                  @for (m of p.months; track m.id) {
                    <tr>
                      <td>{{ 'payments.monthN' | translate: { n: m.monthNumber } }}</td>
                      <td>{{ m.dueDate | date: 'mediumDate' }}</td>
                      <td class="num">{{ m.amount | number: '1.0-2' }} {{ m.currency }}</td>
                      <td><span class="status" [class]="m.status">{{ 'status.' + m.status | translate }}</span></td>
                      <td class="actions">
                        @if (m.remaining > 0 && m.status !== 'Cancelled') {
                          <button mat-flat-button (click)="payOnline(m.id)" [disabled]="redirecting()"><mat-icon>account_balance_wallet</mat-icon>{{ 'parent.payOnline' | translate }}</button>
                        }
                      </td>
                    </tr>
                  } @empty { <tr><td class="empty">{{ 'common.noData' | translate }}</td></tr> }
                </tbody>
              </table>
            </div>
          }
        </mat-card-content>
      </mat-card>
    } @empty {
      <p class="empty">{{ 'parent.noChildren' | translate }}</p>
    }
  `,
})
export class ParentHomePage implements OnInit {
  private readonly api = inject(ApiService);
  private readonly notify = inject(Notifier);
  protected readonly overview = signal<MyOverviewDto | null>(null);
  protected readonly payments = signal<Record<number, StudentPaymentsDto>>({});
  private readonly translate = inject(TranslateService);

  /** success | pending | failed | cancelled, set by the PayPal return redirect. */
  protected readonly paymentResult = signal(inject(ActivatedRoute).snapshot.queryParamMap.get('payment'));
  protected readonly redirecting = signal(false);

  ngOnInit(): void {
    this.api.get<MyOverviewDto>(`${Api.academic}/me/overview`).subscribe((o) => {
      this.overview.set(o);
      for (const child of o.children ?? []) {
        this.api.get<StudentPaymentsDto>(`${Api.finance}/students/${child.userId}/payments`).subscribe((p) =>
          this.payments.update((all) => ({ ...all, [child.userId]: p })),
        );
      }
    });
  }

  protected payOnline(studentPaymentId: number): void {
    this.redirecting.set(true);
    this.api.post<CheckoutDto>(`${Api.finance}/student-payments/${studentPaymentId}/checkout`).subscribe({
      next: (c) => {
        // PayPal can't charge EGP, so an EGP month is charged in USD. Show the payer both amounts first.
        const converted = c.chargedCurrency !== c.currency;
        if (converted && !confirm(this.translate.instant('parent.chargeConfirm', {
          amount: c.amount.toFixed(2), currency: c.currency, charged: c.chargedAmount.toFixed(2), chargedCurrency: c.chargedCurrency,
        }))) {
          this.redirecting.set(false);
          return;
        }
        window.location.href = c.checkoutUrl;
      },
      error: (e) => {
        this.redirecting.set(false);
        this.notify.error(e);
      },
    });
  }
}

/** Anyone paid by the academy: my salary log (US-033). Supervisors also see their shift (US-021). */
@Component({
  selector: 'app-me',
  imports: [PAGE_IMPORTS],
  template: `
    <div class="page-header"><h1>{{ 'nav.me' | translate }}</h1></div>
    @if (canSeeSalary) {
      <h2 class="section-title">{{ 'nav.salaryLogs' | translate }}</h2>
      <div class="table-wrap">
        <table class="data-table">
          <thead><tr><th>{{ 'payments.month' | translate }}</th><th>{{ 'payments.action' | translate }}</th><th>{{ 'salaries.calculation' | translate }}</th><th>{{ 'common.amount' | translate }}</th><th>{{ 'common.notes' | translate }}</th></tr></thead>
          <tbody>
            @for (l of logs(); track l.id) {
              <tr>
                <td class="ltr">{{ l.year }}-{{ l.month }}</td>
                <td>{{ 'salaryAction.' + l.action | translate }}</td>
                <td class="ltr">@if (l.sessionsCount != null) { {{ l.sessionsCount }} × {{ l.ratePerSession | number: '1.0-2' }} } @else { {{ 'pay.MonthlyFixed' | translate }} }</td>
                <td class="num">{{ l.amount | number: '1.0-2' }}</td>
                <td>{{ l.note }}</td>
              </tr>
            } @empty { <tr><td colspan="5" class="empty">{{ 'common.noData' | translate }}</td></tr> }
          </tbody>
        </table>
      </div>
    }
    @if (schedule().length) {
      <h2 class="section-title">{{ 'staff.workSchedule' | translate }}</h2>
      <div class="table-wrap">
        <table class="data-table"><tbody>
          @for (d of schedule(); track d.day) {
            <tr><td>{{ 'days.' + d.day | translate }}</td><td class="ltr">@if (d.isWorkingDay) { {{ d.shiftStart?.slice(0, 5) }} – {{ d.shiftEnd?.slice(0, 5) }} } @else { {{ 'staff.dayOff' | translate }} }</td></tr>
          }
        </tbody></table>
      </div>
    }
  `,
})
export class MePage implements OnInit {
  private readonly api = inject(ApiService);
  private readonly auth = inject(AuthService);
  protected readonly canSeeSalary = this.auth.hasPermission(Permissions.salaries.view);
  protected readonly logs = signal<SalaryLogDto[]>([]);
  protected readonly schedule = signal<WorkDayDto[]>([]);

  ngOnInit(): void {
    if (this.canSeeSalary) {
      this.api.get<PagedResult<SalaryLogDto>>(`${Api.finance}/me/salary-logs`, { pageSize: 100 }).subscribe((l) => this.logs.set(l.items));
    }
    if (this.auth.hasAnyRole(Roles.Supervisor)) {
      this.api.get<WorkDayDto[]>(`${Api.academic}/me/work-schedule`).subscribe((d) => this.schedule.set(d));
    }
  }
}

/** In-app notifications (US-035). */
@Component({
  selector: 'app-notifications',
  imports: [PAGE_IMPORTS],
  template: `
    <div class="page-header">
      <h1>{{ 'nav.notifications' | translate }}</h1>
      <button mat-stroked-button (click)="readAll()">{{ 'notifications.readAll' | translate }}</button>
    </div>
    <div class="table-wrap">
      <table class="data-table">
        <tbody>
          @for (n of items(); track n.id) {
            <tr [class.unread]="!n.isRead" (click)="read(n)">
              <td style="width: 32px"><mat-icon>{{ icon(n.type) }}</mat-icon></td>
              <td><b>{{ lang.language() === 'ar' ? n.titleAr : n.titleEn }}</b><div class="muted">{{ lang.language() === 'ar' ? n.bodyAr : n.bodyEn }}</div></td>
              <td>{{ n.createdOnUtc | date: 'short' }}</td>
            </tr>
          } @empty { <tr><td class="empty">{{ 'notifications.none' | translate }}</td></tr> }
        </tbody>
      </table>
    </div>
  `,
  styles: `tr.unread td { background: var(--mat-sys-primary-container); } tr { cursor: pointer; }`,
})
export class NotificationsPage implements OnInit {
  private readonly api = inject(ApiService);
  protected readonly lang = inject(LanguageService);
  protected readonly items = signal<NotificationDto[]>([]);

  ngOnInit(): void {
    this.load();
  }

  protected icon(type: string): string {
    return (
      { session_reminder: 'event', absence: 'event_busy', late: 'schedule', payment_due: 'payments', payment_overdue: 'warning',
        payment_received: 'paid', payment_refunded: 'undo', salary_paid: 'account_balance_wallet' }[type] ?? 'notifications'
    );
  }

  protected read(n: NotificationDto): void {
    if (!n.isRead) {
      this.api.post(`${Api.engagement}/notifications/${n.id}/read`).subscribe(() => this.load());
    }
  }

  protected readAll(): void {
    this.api.post(`${Api.engagement}/notifications/me/read-all`).subscribe(() => this.load());
  }

  private load(): void {
    this.api
      .get<{ items: PagedResult<NotificationDto> }>(`${Api.engagement}/notifications/me`, { pageSize: 50 })
      .subscribe((r) => this.items.set(r.items.items));
  }
}

