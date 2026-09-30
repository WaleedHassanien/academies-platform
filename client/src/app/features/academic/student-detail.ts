import { NgTemplateOutlet } from '@angular/common';
import { Component, computed, inject, input, OnDestroy, OnInit, signal } from '@angular/core';
import { TranslateService } from '@ngx-translate/core';
import { Api, ApiService } from '../../core/api/api.service';
import { SessionDto, StudentDto, StudentSessionDto } from '../../core/api/models';
import { AuthService } from '../../core/auth/auth.service';
import { Permissions } from '../../core/auth/permissions';
import { LanguageService } from '../../core/i18n/language.service';
import { Notifier } from '../../shared/notifier';
import { PAGE_IMPORTS } from '../../shared/page-imports';
import { BillingCard } from '../../shared/billing-card';
import { LearningCard } from '../../shared/learning-card';
import { canBeExcused, OutcomeChip, SessionActions } from '../../shared/sessions-kit';
import { TimeZoneField } from '../../shared/time-zone-field';
import { ACADEMY_TIME_ZONE, dayKey, formatClock, formatDate, formatTime, parseUtc, utcOffset, zoneLabel } from '../../shared/time-zones';
import { Stat } from '../../shared/ui';

interface SessionRow {
  item: StudentSessionDto;
  egyptDate: string;
  egyptTime: string;
  studentTime: string | null;
  /** The student's date, only when it differs from Egypt's (e.g. a late session is already tomorrow for them). */
  studentDate: string | null;
}

/** One student: their subjects and plans, teachers, time zone, money, and every session, in Egypt time and theirs. */
@Component({
  selector: 'app-student-detail',
  imports: [PAGE_IMPORTS, NgTemplateOutlet, Stat, TimeZoneField, BillingCard, LearningCard, OutcomeChip],
  template: `
    <a mat-button routerLink="/students" class="back"><mat-icon class="flip">arrow_back</mat-icon>{{ 'nav.students' | translate }}</a>

    @if (student(); as s) {
      <section class="profile">
        <span class="avatar">{{ initials(s.fullName) }}</span>
        <div class="who">
          <h1>{{ s.fullName }}</h1>
          <div class="muted ltr-text">{{ s.email }}</div>
          <div class="facts">
            <span class="status" [class]="s.status">{{ 'status.' + s.status | translate }}</span>
            @if (s.level) { <span class="fact"><mat-icon>signal_cellular_alt</mat-icon>{{ s.level }}</span> }
            @for (g of s.subjects; track g) { <span class="fact"><mat-icon>menu_book</mat-icon>{{ g }}</span> }
            @if (s.parentName) {
              <span class="fact"><mat-icon>family_restroom</mat-icon>{{ s.parentName }}</span>
            } @else {
              <span class="fact"><mat-icon>person</mat-icon>{{ 'students.noGuardian' | translate }}</span>
            }
            <span class="fact"><mat-icon>payments</mat-icon>{{ 'students.payer' | translate }}:
              {{ s.effectivePayerUserId === s.userId ? ('students.payerSelf' | translate) : (s.payerName ?? '—') }}</span>
            <span class="fact"><mat-icon>event</mat-icon>{{ 'students.enrolled' | translate }} {{ s.enrollmentDate | date: 'mediumDate' }}</span>
          </div>
        </div>
      </section>

      @if (next(); as n) {
        <section class="next">
          <div class="next-main">
            <span class="next-label"><span class="live"></span>{{ 'students.nextSession' | translate }}</span>
            <h2>{{ n.row.item.session.title }}</h2>
            <div class="next-meta">
              <span><mat-icon>menu_book</mat-icon>{{ n.row.item.session.courseName }}</span>
              <span><mat-icon>co_present</mat-icon>{{ n.row.item.session.teacherName }}</span>
              <span class="ltr-text"><mat-icon>schedule</mat-icon>{{ 'students.egyptTime' | translate }}: {{ n.row.egyptDate }} · {{ n.row.egyptTime }}</span>
              @if (n.row.studentTime) {
                <span class="ltr-text"><mat-icon>public</mat-icon>{{ 'students.studentTime' | translate }}: {{ n.row.studentDate ?? n.row.egyptDate }} · {{ n.row.studentTime }}</span>
              }
            </div>
          </div>
          <div class="countdown" [attr.aria-label]="'students.startsIn' | translate">
            @for (part of n.parts; track part.key) {
              <div class="unit"><b>{{ part.value }}</b><small>{{ 'students.' + part.key | translate }}</small></div>
            }
          </div>
          @if (n.row.item.session.meetingUrl) {
            <button mat-flat-button class="join" (click)="actions.join(n.row.item.session)"><mat-icon>videocam</mat-icon>{{ 'sessions.join' | translate }}</button>
          }
        </section>
      }

      <div class="grid">
        <mat-card appearance="outlined">
          <mat-card-header>
            <mat-icon mat-card-avatar class="card-icon">co_present</mat-icon>
            <mat-card-title>{{ 'students.teachers' | translate }}</mat-card-title>
            <mat-card-subtitle>{{ 'students.teachersHint' | translate }}</mat-card-subtitle>
          </mat-card-header>
          <mat-card-content>
            @for (t of s.teachers; track t.userId) {
              <div class="person"><span class="avatar sm">{{ initials(t.fullName) }}</span><b>{{ t.fullName }}</b></div>
            } @empty {
              <p class="muted none">{{ 'students.noTeachers' | translate }}</p>
            }
          </mat-card-content>
        </mat-card>

        <mat-card appearance="outlined">
          <mat-card-header>
            <mat-icon mat-card-avatar class="card-icon">schedule</mat-icon>
            <mat-card-title>{{ 'students.timeZones' | translate }}</mat-card-title>
            <mat-card-subtitle>{{ 'students.timeZonesHint' | translate }}</mat-card-subtitle>
          </mat-card-header>
          <mat-card-content>
            <div class="clocks">
              <div class="clock">
                <span class="muted">{{ 'students.egyptTime' | translate }}</span>
                <b class="ltr-text">{{ nowIn(egypt) }}</b>
                <small class="muted ltr-text">{{ zone(egypt) }}</small>
              </div>
              <div class="clock">
                <span class="muted">{{ 'students.studentTime' | translate }}</span>
                @if (s.timeZone) {
                  <b class="ltr-text">{{ nowIn(s.timeZone) }}</b>
                  <small class="muted ltr-text">{{ zone(s.timeZone) }}</small>
                } @else {
                  <b class="muted">{{ 'students.notSet' | translate }}</b>
                }
              </div>
            </div>
            @if (s.timeZone && offsetNote(); as note) { <p class="note"><mat-icon>info</mat-icon>{{ note }}</p> }
            @if (canManage) {
              <div class="tz-edit">
                <app-time-zone-field [(value)]="timeZone" [label]="'students.timeZone' | translate" [clearLabel]="'common.remove' | translate" />
                <mat-form-field class="minutes">
                  <mat-label>{{ 'students.sessionMinutes' | translate }}</mat-label>
                  <input matInput type="number" min="15" max="240" step="5" [ngModel]="minutes()" (ngModelChange)="minutes.set($event)" />
                </mat-form-field>
                <button mat-flat-button (click)="saveTimeZone(s)" [disabled]="timeZone() === s.timeZone && minutes() === s.sessionMinutes">{{ 'common.save' | translate }}</button>
              </div>
            }
          </mat-card-content>
        </mat-card>

        <app-learning-card [studentUserId]="s.userId" />
        <app-billing-card [studentUserId]="s.userId" [teachers]="s.teachers" [payerUserId]="s.effectivePayerUserId" />
      </div>

      <!-- Clicking a tile filters the table below. -->
      <div class="stats">
        <app-stat icon="event_note" [label]="'students.totalSessions' | translate" [value]="history().length"
                  class="clickable" [class.active]="tab() === 0 && filter() === 'all'" tabindex="0" role="button"
                  (click)="show(0, 'all')" (keydown.enter)="show(0, 'all')" />
        <app-stat icon="how_to_reg" [label]="'students.attended' | translate" [value]="counts().attended"
                  class="clickable" [class.active]="filter() === 'attended'" tabindex="0" role="button"
                  (click)="show(0, 'attended')" (keydown.enter)="show(0, 'attended')" />
        <app-stat icon="event_busy" [label]="'status.Absent' | translate" [value]="counts().absent"
                  class="clickable" [class.active]="filter() === 'absent'" tabindex="0" role="button"
                  (click)="show(0, 'absent')" (keydown.enter)="show(0, 'absent')" />
        <app-stat icon="percent" [label]="'dashboard.attendanceRate' | translate" [value]="counts().rate === null ? '—' : counts().rate + '%'" />
        <app-stat icon="upcoming" [label]="'students.upcoming' | translate" [value]="upcoming().length"
                  class="clickable" [class.active]="tab() === 1" tabindex="0" role="button"
                  (click)="show(1, 'all')" (keydown.enter)="show(1, 'all')" />
      </div>

      @if (filter() !== 'all' && tab() === 0) {
        <div class="filter-chip">
          <mat-icon>filter_alt</mat-icon>{{ (filter() === 'attended' ? 'students.attended' : 'status.Absent') | translate }}
          <button mat-icon-button (click)="filter.set('all')" [attr.aria-label]="'common.remove' | translate"><mat-icon>close</mat-icon></button>
        </div>
      }

      <mat-tab-group mat-stretch-tabs="false" animationDuration="200ms" [selectedIndex]="tab()" (selectedIndexChange)="tab.set($event)">
        <mat-tab [label]="('students.history' | translate) + ' (' + history().length + ')'">
          <ng-container *ngTemplateOutlet="table; context: { rows: filteredHistory(), past: true }" />
        </mat-tab>
        <mat-tab [label]="('students.upcoming' | translate) + ' (' + upcoming().length + ')'">
          <ng-container *ngTemplateOutlet="table; context: { rows: upcoming(), past: false }" />
        </mat-tab>
      </mat-tab-group>

      <ng-template #table let-rows="rows" let-past="past">
        <div class="table-wrap tab-body">
          <table class="data-table">
            <thead>
              <tr>
                <th>{{ 'common.date' | translate }}</th>
                <th>{{ 'students.egyptTime' | translate }}</th>
                <th>{{ 'students.studentTime' | translate }}</th>
                <th>{{ 'students.session' | translate }}</th>
                <th>{{ 'roles.Teacher' | translate }}</th>
                <th>{{ 'common.status' | translate }}</th>
                <th></th>
              </tr>
            </thead>
            <tbody>
              @for (r of rows; track r.item.session.id) {
                <tr>
                  <td class="nowrap">{{ r.egyptDate }}</td>
                  <td class="ltr nowrap"><b>{{ r.egyptTime }}</b></td>
                  <td class="ltr nowrap">
                    @if (r.studentTime) {
                      <b>{{ r.studentTime }}</b>
                      @if (r.studentDate) { <div class="muted">{{ r.studentDate }}</div> }
                    } @else { <span class="muted">—</span> }
                  </td>
                  <td>
                    <a [routerLink]="['/sessions', r.item.session.id]" class="title">{{ r.item.session.title }}</a>
                    <div class="muted">{{ r.item.session.courseName }}</div>
                    @if (r.item.log?.accomplished) { <div class="muted small-log">{{ r.item.log?.accomplished }}</div> }
                  </td>
                  <td>{{ r.item.session.teacherName ?? '—' }}</td>
                  <td>
                    <app-outcome [session]="r.item.session" />
                    @if (r.item.rating) { <div class="stars" [matTooltip]="r.item.comment ?? ''">{{ stars(r.item.rating) }}</div> }
                    @if (r.item.log?.memorization) { <div class="muted small-log">{{ 'log.memorization' | translate }}: {{ r.item.log?.memorization }}</div> }
                    @if (r.item.log?.homework) { <div class="muted small-log">{{ 'log.homework' | translate }}: {{ r.item.log?.homework }}</div> }
                  </td>
                  <td class="actions">
                    @if (canBeExcused(r.item.session)) {
                      <button mat-button (click)="excuse(r.item.session)"><mat-icon>event_busy</mat-icon>{{ 'excuse.action' | translate }}</button>
                    }
                    @if (canManage && r.item.session.excuse?.status === 'Pending') {
                      <button mat-flat-button (click)="resolve(r.item.session)"><mat-icon>gavel</mat-icon>{{ 'requests.decide' | translate }}</button>
                    }
                  </td>
                </tr>
              } @empty {
                <tr><td colspan="7" class="empty">{{ 'sessions.none' | translate }}</td></tr>
              }
            </tbody>
          </table>
        </div>
      </ng-template>
    } @else if (!failed()) {
      <mat-progress-bar mode="indeterminate" />
    }
  `,
  styles: `
    .back { margin: -8px 0 12px; margin-inline-start: -8px; }
    :host-context([dir='rtl']) .flip { transform: scaleX(-1); }
    .profile { display: flex; align-items: center; gap: 20px; margin-bottom: 24px; padding: 24px; border-radius: var(--app-radius);
      background: var(--app-surface); border: 1px solid var(--app-border); box-shadow: var(--app-shadow); }
    .profile h1 { margin: 0; font-size: clamp(1.3rem, 1.1rem + 1vw, 1.7rem); letter-spacing: -0.02em; }
    .who { min-width: 0; }
    .facts { display: flex; flex-wrap: wrap; gap: 8px; margin-top: 12px; }
    .fact { display: inline-flex; align-items: center; gap: 6px; padding: 3px 10px; border-radius: 999px; font-size: 0.8rem;
      background: var(--app-surface-2); border: 1px solid var(--app-border); }
    .fact mat-icon { font-size: 16px; width: 16px; height: 16px; color: var(--app-muted); }
    .avatar { flex: none; display: grid; place-items: center; width: 72px; height: 72px; border-radius: 22px; color: #fff; font-weight: 700;
      font-size: 1.5rem; background: var(--app-gradient); }
    .avatar.sm { width: 36px; height: 36px; border-radius: 50%; font-size: 0.8rem; }
    .ltr-text { direction: ltr; unicode-bidi: isolate; }

    .card-icon { display: grid; place-items: center; border-radius: 12px; color: var(--mat-sys-primary);
      background: color-mix(in srgb, var(--mat-sys-primary) 12%, transparent); }
    .person { display: flex; align-items: center; gap: 12px; padding: 8px 0; }
    .person + .person { border-top: 1px dashed var(--app-border); }
    .none { margin: 8px 0; }

    .clocks { display: grid; grid-template-columns: 1fr 1fr; gap: 12px; }
    .clock { display: flex; flex-direction: column; gap: 2px; padding: 14px; border-radius: 14px; background: var(--app-surface-2);
      border: 1px solid var(--app-border); }
    .clock b { font-size: 1.5rem; font-variant-numeric: tabular-nums; }
    .clock span { font-size: 0.8rem; }
    .clock small { font-size: 0.75rem; }
    .note { display: flex; align-items: center; gap: 8px; margin: 12px 0 0; font-size: 0.85rem; color: var(--info-fg); }
    .note mat-icon { font-size: 18px; width: 18px; height: 18px; }
    .tz-edit { display: flex; align-items: flex-start; gap: 8px; margin-top: 16px; }
    .tz-edit app-time-zone-field { flex: 1; min-width: 0; }
    .tz-edit .minutes { width: 130px; flex: none; }
    .tz-edit button { margin-top: 8px; }

    .next { position: relative; overflow: hidden; display: flex; align-items: center; flex-wrap: wrap; gap: 20px 28px; margin-bottom: 24px;
      padding: 24px 28px; border-radius: var(--app-radius); color: #fff; background: var(--app-gradient); background-size: 160% 160%;
      box-shadow: 0 18px 40px -18px rgb(79 70 229 / 60%); animation: rise-in 0.5s cubic-bezier(0.2, 0.7, 0.2, 1) both, drift 12s ease-in-out infinite alternate; }
    .next::after { content: ''; position: absolute; width: 260px; height: 260px; inset-inline-end: -80px; top: -120px; border-radius: 50%;
      background: rgb(255 255 255 / 10%); pointer-events: none; }
    @keyframes drift { to { background-position: 100% 100%; } }
    .next-main { flex: 1 1 320px; min-width: 0; position: relative; z-index: 1; }
    .next-label { display: inline-flex; align-items: center; gap: 8px; font-size: 0.78rem; font-weight: 600; letter-spacing: 0.04em; opacity: 0.9; }
    .live { width: 8px; height: 8px; border-radius: 50%; background: #4ade80; animation: pulse-dot 1.8s ease-out infinite; color: #4ade80; }
    .next h2 { margin: 6px 0 10px; font-size: clamp(1.2rem, 1rem + 1vw, 1.5rem); }
    .next-meta { display: flex; flex-wrap: wrap; gap: 6px 16px; font-size: 0.85rem; opacity: 0.95; }
    .next-meta span { display: inline-flex; align-items: center; gap: 6px; }
    .next-meta mat-icon { font-size: 17px; width: 17px; height: 17px; opacity: 0.85; }
    .countdown { position: relative; z-index: 1; display: flex; gap: 8px; direction: ltr; }
    .unit { display: flex; flex-direction: column; align-items: center; min-width: 64px; padding: 10px 8px; border-radius: 14px;
      background: rgb(255 255 255 / 16%); box-shadow: inset 0 0 0 1px rgb(255 255 255 / 22%); backdrop-filter: blur(4px); }
    .unit b { font-size: 1.6rem; line-height: 1.1; font-variant-numeric: tabular-nums; }
    .unit small { font-size: 0.7rem; opacity: 0.85; }
    .join { position: relative; z-index: 1; background: #fff !important; color: #4f46e5 !important; }

    .clock b { transition: color 0.3s; }
    .filter-chip { display: inline-flex; align-items: center; gap: 6px; margin: -8px 0 12px; padding: 2px 4px 2px 12px; border-radius: 999px;
      font-size: 0.85rem; font-weight: 500; color: var(--mat-sys-primary); background: color-mix(in srgb, var(--mat-sys-primary) 10%, transparent);
      animation: pop-in 0.2s ease both; }
    .filter-chip mat-icon { font-size: 18px; width: 18px; height: 18px; }
    .filter-chip button { width: 32px; height: 32px; padding: 4px; }
    .tab-body { margin-top: 16px; }
    .nowrap { white-space: nowrap; }
    .title { font-weight: 600; text-decoration: none; }
    .stars { color: #f59e0b; letter-spacing: 1px; margin-top: 4px; cursor: default; }


    @media (max-width: 639px) {
      .profile { flex-direction: column; text-align: center; padding: 20px 16px; }
      .facts { justify-content: center; }
      .avatar { width: 64px; height: 64px; }
      .tz-edit { flex-direction: column; align-items: stretch; }
      .tz-edit button { margin-top: 0; }
      .next { padding: 20px; }
      .countdown { width: 100%; justify-content: space-between; }
      .unit { flex: 1; min-width: 0; }
      .join { width: 100%; }
    }
  `,
})
export class StudentDetailPage implements OnInit, OnDestroy {
  private readonly api = inject(ApiService);
  private readonly notify = inject(Notifier);
  private readonly translate = inject(TranslateService);
  private readonly language = inject(LanguageService);
  protected readonly actions = inject(SessionActions);
  protected readonly canBeExcused = canBeExcused;
  protected readonly minutes = signal(30);
  protected readonly canManage = inject(AuthService).hasPermission(Permissions.profiles.manage);

  /** Route param, bound by withComponentInputBinding(). */
  readonly id = input.required<string>();

  protected readonly egypt = ACADEMY_TIME_ZONE;
  protected readonly student = signal<StudentDto | null>(null);
  protected readonly sessions = signal<StudentSessionDto[]>([]);
  protected readonly failed = signal(false);
  protected readonly timeZone = signal<string | null>(null);
  protected readonly tab = signal(0);
  /** Narrows the past-sessions tab; set by clicking the stat tiles. */
  protected readonly filter = signal<'all' | 'attended' | 'absent'>('all');

  /** Ticks every second: live clocks and the countdown. */
  private readonly now = signal(new Date());
  private timer?: ReturnType<typeof setInterval>;

  private readonly rows = computed<SessionRow[]>(() => {
    const lang = this.language.language();
    const tz = this.student()?.timeZone ?? null;
    return this.sessions().map((item) => {
      const at = item.session.startsAtUtc;
      const otherDay = tz !== null && dayKey(at, tz) !== dayKey(at, ACADEMY_TIME_ZONE);
      return {
        item,
        egyptDate: formatDate(at, ACADEMY_TIME_ZONE, lang),
        egyptTime: `${formatTime(at, ACADEMY_TIME_ZONE, lang)} – ${formatTime(item.session.endsAtUtc, ACADEMY_TIME_ZONE, lang)}`,
        studentTime: tz ? `${formatTime(at, tz, lang)} – ${formatTime(item.session.endsAtUtc, tz, lang)}` : null,
        studentDate: otherDay ? formatDate(at, tz, lang) : null,
      };
    });
  });

  /** Sessions that have started, newest first (the API sends newest first). */
  protected readonly history = computed(() => this.rows().filter((r) => parseUtc(r.item.session.startsAtUtc) <= this.now()));

  /** Scheduled sessions still ahead, soonest first. */
  protected readonly upcoming = computed(() =>
    this.rows()
      .filter((r) => parseUtc(r.item.session.startsAtUtc) > this.now() && (r.item.session.status === 'Scheduled' || r.item.session.status === 'Excused'))
      .reverse(),
  );

  protected readonly filteredHistory = computed(() => {
    const f = this.filter();
    if (f === 'all') {
      return this.history();
    }
    const wanted = f === 'attended' ? ['Present', 'Late'] : ['Absent'];
    return this.history().filter((r) => wanted.includes(r.item.attendanceStatus ?? ''));
  });

  /** The soonest upcoming session and a days/hours/minutes/seconds countdown to it. */
  protected readonly next = computed(() => {
    const row = this.upcoming().find((r) => r.item.session.status === 'Scheduled');
    if (!row) {
      return null;
    }
    let left = Math.max(0, Math.floor((parseUtc(row.item.session.startsAtUtc).getTime() - this.now().getTime()) / 1000));
    const days = Math.floor(left / 86_400);
    left %= 86_400;
    const pad = (n: number) => String(n).padStart(2, '0');
    const parts = [
      { key: 'days', value: String(days) },
      { key: 'hours', value: pad(Math.floor(left / 3600)) },
      { key: 'minutes', value: pad(Math.floor((left % 3600) / 60)) },
      { key: 'seconds', value: pad(left % 60) },
    ];
    return { row, parts: days ? parts : parts.slice(1) };
  });

  protected readonly counts = computed(() => {
    const statuses = this.history().map((r) => r.item.attendanceStatus);
    const attended = statuses.filter((s) => s === 'Present' || s === 'Late').length;
    const absent = statuses.filter((s) => s === 'Absent').length;
    return { attended, absent, rate: attended + absent ? Math.round((attended * 100) / (attended + absent)) : null };
  });

  /** "The student is 1 hour ahead of Egypt", from the current offsets. */
  protected readonly offsetNote = computed(() => {
    const tz = this.student()?.timeZone;
    if (!tz) {
      return null;
    }
    const minutes = offsetMinutes(tz, this.now()) - offsetMinutes(ACADEMY_TIME_ZONE, this.now());
    if (minutes === 0) {
      return this.translate.instant('students.sameAsEgypt');
    }
    const hours = Math.abs(minutes) / 60;
    return this.translate.instant(minutes > 0 ? 'students.aheadOfEgypt' : 'students.behindEgypt', { hours: +hours.toFixed(2) });
  });

  ngOnInit(): void {
    this.load();
    this.timer = setInterval(() => this.now.set(new Date()), 1000);
  }

  ngOnDestroy(): void {
    clearInterval(this.timer);
  }

  protected show(tab: number, filter: 'all' | 'attended' | 'absent'): void {
    this.tab.set(tab);
    this.filter.set(filter);
  }

  protected excuse(session: SessionDto): void {
    this.actions.excuse(session).subscribe(() => this.loadSessions());
  }

  protected resolve(session: SessionDto): void {
    this.actions.resolve(session.excuse!, session).subscribe({ next: () => this.loadSessions(), error: (e) => this.notify.error(e) });
  }

  protected nowIn(timeZone: string): string {
    return formatClock(this.now(), timeZone);
  }

  protected zone(timeZone: string): string {
    return zoneLabel(timeZone);
  }

  protected initials(name: string): string {
    return name.trim().split(/\s+/).slice(0, 2).map((p) => p[0]).join('').toUpperCase();
  }

  protected stars(n: number): string {
    return '★'.repeat(n) + '☆'.repeat(5 - n);
  }

  protected saveTimeZone(s: StudentDto): void {
    this.api
      .put<StudentDto>(`${Api.academic}/students/${s.userId}`, {
        level: s.level,
        enrollmentDate: s.enrollmentDate,
        status: s.status,
        timeZone: this.timeZone(),
        sessionMinutes: this.minutes(),
      })
      .subscribe({
        next: (updated) => {
          this.student.set(updated);
          this.notify.saved();
        },
        error: (e) => this.notify.error(e),
      });
  }

  private load(): void {
    const id = this.id();
    this.api.get<StudentDto>(`${Api.academic}/students/${id}`).subscribe({
      next: (s) => {
        this.student.set(s);
        this.timeZone.set(s.timeZone);
        this.minutes.set(s.sessionMinutes);
      },
      error: (e) => {
        this.failed.set(true);
        this.notify.error(e);
      },
    });
    this.loadSessions();
  }

  private loadSessions(): void {
    this.api.get<StudentSessionDto[]>(`${Api.academic}/students/${this.id()}/sessions`).subscribe({
      next: (list) => this.sessions.set(list),
      error: (e) => this.notify.error(e),
    });
  }
}

/** Minutes east of UTC for a zone at a moment, e.g. Cairo in summer → 180. */
function offsetMinutes(timeZone: string, at: Date): number {
  const match = /GMT([+-])(\d{1,2})(?::(\d{2}))?/.exec(utcOffset(timeZone, at));
  if (!match) {
    return 0;
  }
  const minutes = Number(match[2]) * 60 + Number(match[3] ?? 0);
  return match[1] === '-' ? -minutes : minutes;
}
