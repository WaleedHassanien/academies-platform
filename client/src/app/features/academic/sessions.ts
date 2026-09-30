import { Component, computed, inject, input, OnInit, signal } from '@angular/core';
import { Router } from '@angular/router';
import { Api, ApiService } from '../../core/api/api.service';
import { PagedResult } from '../../core/api/api.models';
import { CourseDto, RosterItemDto, SessionDto, StaffProfileDto, StudentDto } from '../../core/api/models';
import { AuthService } from '../../core/auth/auth.service';
import { Permissions, Roles } from '../../core/auth/permissions';
import { addDays, isoDate, Notifier } from '../../shared/notifier';
import { PAGE_IMPORTS } from '../../shared/page-imports';
import { canBeExcused, OutcomeChip, SessionActions, startOfWeek } from '../../shared/sessions-kit';
import { parseUtc } from '../../shared/time-zones';

interface SessionForm {
  id: number | null;
  title: string;
  courseId: number | null;
  studentUserId: number | null;
  teacherUserId: number | null;
  date: string;
  time: string;
  durationMinutes: number;
  /** Empty: the teacher's own room. */
  meetingUrl: string;
  notes: string;
  repeatWeeks: number;
}

/**
 * Weekly timetable with scheduling and clash detection (US-025). Every session is online, in the
 * teacher's room, with one student (US-037).
 */
@Component({
  selector: 'app-sessions',
  imports: [PAGE_IMPORTS, OutcomeChip],
  template: `
    <div class="page-header">
      <h1>{{ 'nav.sessions' | translate }}</h1>
      @if (canSchedule) { <button mat-flat-button (click)="edit(null)"><mat-icon>event</mat-icon>{{ 'sessions.new' | translate }}</button> }
    </div>

    @if (form(); as f) {
      <mat-card appearance="outlined" class="panel">
        <mat-card-content>
          <div class="form-grid">
            <mat-form-field>
              <mat-label>{{ 'sessions.student' | translate }}</mat-label>
              <mat-select [(ngModel)]="f.studentUserId" (selectionChange)="onStudent(f)">
                @for (st of students(); track st.userId) { <mat-option [value]="st.userId">{{ st.fullName }}</mat-option> }
              </mat-select>
              <mat-hint>{{ 'sessions.studentHint' | translate }}</mat-hint>
            </mat-form-field>
            <mat-form-field>
              <mat-label>{{ 'students.subject' | translate }}</mat-label>
              <mat-select [(ngModel)]="f.courseId">@for (c of courses(); track c.id) { <mat-option [value]="c.id">{{ c.name }}</mat-option> }</mat-select>
            </mat-form-field>
            <mat-form-field><mat-label>{{ 'common.title' | translate }}</mat-label><input matInput [(ngModel)]="f.title" /></mat-form-field>
            <mat-form-field>
              <mat-label>{{ 'roles.Teacher' | translate }}</mat-label>
              <mat-select [(ngModel)]="f.teacherUserId" [disabled]="!isStaff">
                @for (t of teachersFor(f.courseId); track t.userId) { <mat-option [value]="t.userId">{{ t.fullName }}</mat-option> }
              </mat-select>
            </mat-form-field>
            <mat-form-field><mat-label>{{ 'common.date' | translate }}</mat-label><input matInput type="date" [(ngModel)]="f.date" /></mat-form-field>
            <mat-form-field><mat-label>{{ 'common.time' | translate }}</mat-label><input matInput type="time" [(ngModel)]="f.time" /></mat-form-field>
            <mat-form-field><mat-label>{{ 'sessions.duration' | translate }}</mat-label><input matInput type="number" [(ngModel)]="f.durationMinutes" /></mat-form-field>
            <mat-form-field>
              <mat-label>{{ 'sessions.meetingUrl' | translate }}</mat-label>
              <input matInput [(ngModel)]="f.meetingUrl" dir="ltr" />
              <mat-hint>{{ 'sessions.meetingUrlHint' | translate }}</mat-hint>
            </mat-form-field>
            @if (!f.id) {
              <mat-form-field><mat-label>{{ 'sessions.repeatWeeks' | translate }}</mat-label><input matInput type="number" min="1" max="52" [(ngModel)]="f.repeatWeeks" /></mat-form-field>
            }
            <mat-form-field><mat-label>{{ 'common.notes' | translate }}</mat-label><input matInput [(ngModel)]="f.notes" /></mat-form-field>
          </div>
          <div class="form-actions">
            <button mat-button (click)="form.set(null)">{{ 'common.cancel' | translate }}</button>
            <button mat-flat-button (click)="save(f)" [disabled]="!f.title || !f.courseId || !f.teacherUserId || !f.studentUserId">{{ 'common.save' | translate }}</button>
          </div>
        </mat-card-content>
      </mat-card>
    }

    <div class="toolbar">
      <button mat-icon-button (click)="shift(-7)"><mat-icon>{{ rtl() ? 'chevron_right' : 'chevron_left' }}</mat-icon></button>
      <b>{{ weekStart() | date: 'mediumDate' }} – {{ weekEnd() | date: 'mediumDate' }}</b>
      <button mat-icon-button (click)="shift(7)"><mat-icon>{{ rtl() ? 'chevron_left' : 'chevron_right' }}</mat-icon></button>
      <button mat-button (click)="today()">{{ 'sessions.thisWeek' | translate }}</button>
      @if (isStaff) {
        <mat-form-field>
          <mat-label>{{ 'roles.Teacher' | translate }}</mat-label>
          <mat-select [(ngModel)]="teacherFilter" (selectionChange)="load()">
            <mat-option [value]="null">{{ 'common.all' | translate }}</mat-option>
            @for (t of teachers(); track t.userId) { <mat-option [value]="t.userId">{{ t.fullName }}</mat-option> }
          </mat-select>
        </mat-form-field>
      }
    </div>

    @for (day of days(); track day.date) {
      <h3 class="section-title">{{ day.date | date: 'EEEE d MMM' }}</h3>
      <div class="table-wrap">
        <table class="data-table">
          <tbody>
            @for (s of day.sessions; track s.id) {
              <tr>
                <td class="ltr" style="width: 110px">{{ s.startsAtUtc | utcDate: 'HH:mm' }} – {{ s.endsAtUtc | utcDate: 'HH:mm' }}</td>
                <td><b>{{ s.title }}</b><div class="muted">{{ s.studentName ?? '—' }} · {{ s.courseName }} · {{ s.teacherName }}</div></td>
                <td>
                  @if (s.status === 'Scheduled') { <button mat-button (click)="actions.join(s)"><mat-icon>videocam</mat-icon>{{ 'sessions.join' | translate }}</button> }
                </td>
                <td><app-outcome [session]="s" /></td>
                <td class="actions">
                  @if (canDecide && s.excuse?.status === 'Pending') {
                    <button mat-flat-button (click)="resolve(s)"><mat-icon>gavel</mat-icon>{{ 'requests.decide' | translate }}</button>
                  }
                  @if (canDecide && s.attendanceStatus === 'Absent' && s.absenceCounted === null) {
                    <button mat-stroked-button [matMenuTriggerFor]="absence"><mat-icon>person_off</mat-icon>{{ 'requests.decide' | translate }}</button>
                    <mat-menu #absence="matMenu">
                      <button mat-menu-item (click)="decideAbsence(s, true)"><mat-icon>check</mat-icon>{{ 'requests.counts' | translate }}</button>
                      <button mat-menu-item (click)="decideAbsence(s, false)"><mat-icon>block</mat-icon>{{ 'requests.notCounted' | translate }}</button>
                    </mat-menu>
                  }
                  @if (canRun(s) && s.status !== 'Excused') {
                    <button mat-button (click)="open(s)">{{ 'sessions.log' | translate }}</button>
                  }
                  @if (canRun(s) || canBeExcused(s)) {
                    <button mat-icon-button [matMenuTriggerFor]="menu"><mat-icon>more_vert</mat-icon></button>
                    <mat-menu #menu="matMenu">
                      @if (canBeExcused(s)) {
                        <button mat-menu-item (click)="excuse(s)"><mat-icon>event_busy</mat-icon>{{ 'excuse.action' | translate }}</button>
                      }
                      @if (canRun(s) && s.status === 'Scheduled') {
                        <button mat-menu-item (click)="edit(s)">{{ 'common.edit' | translate }}</button>
                        <button mat-menu-item (click)="act(s, 'complete')">{{ 'sessions.complete' | translate }}</button>
                        <button mat-menu-item (click)="act(s, 'meeting-link')">{{ 'sessions.useTeacherRoom' | translate }}</button>
                        <button mat-menu-item (click)="act(s, 'cancel')">{{ 'sessions.cancel' | translate }}</button>
                      }
                    </mat-menu>
                  }
                </td>
              </tr>
            }
          </tbody>
        </table>
      </div>
    } @empty {
      <p class="empty">{{ 'sessions.none' | translate }}</p>
    }
  `,
})
export class SessionsPage implements OnInit {
  private readonly api = inject(ApiService);
  private readonly notify = inject(Notifier);
  private readonly router = inject(Router);
  private readonly auth = inject(AuthService);

  protected readonly isStaff = this.auth.hasPermission(Permissions.sessions.manage);
  protected readonly canSchedule = this.isStaff || this.auth.hasAnyRole(Roles.Teacher);
  /** Settle excuses and unexcused absences: staff, or supervisors for their teachers (the server checks which). */
  protected readonly canDecide = this.isStaff || this.auth.hasAnyRole(Roles.Admin, Roles.Manager, Roles.Supervisor);
  protected readonly canBeExcused = canBeExcused;
  protected readonly actions = inject(SessionActions);
  protected readonly students = signal<StudentDto[]>([]);
  protected readonly rtl = () => document.documentElement.dir === 'rtl';

  protected readonly sessions = signal<SessionDto[]>([]);
  protected readonly courses = signal<CourseDto[]>([]);
  protected readonly teachers = signal<StaffProfileDto[]>([]);
  protected readonly form = signal<SessionForm | null>(null);
  protected readonly weekStart = signal(startOfWeek(new Date()));
  protected readonly weekEnd = computed(() => addDays(this.weekStart(), 6));
  protected teacherFilter: number | null = null;

  protected readonly days = computed(() => {
    const byDay = new Map<string, SessionDto[]>();
    for (const s of this.sessions()) {
      const key = isoDate(parseUtc(s.startsAtUtc));
      byDay.set(key, [...(byDay.get(key) ?? []), s]);
    }
    return [...byDay.entries()].sort(([a], [b]) => a.localeCompare(b)).map(([date, sessions]) => ({ date, sessions }));
  });

  ngOnInit(): void {
    this.load();
    this.api.get<CourseDto[]>(`${Api.academic}/courses`).subscribe((c) => this.courses.set(c));
    this.api.get<StaffProfileDto[]>(`${Api.academic}/teachers`).subscribe((t) => this.teachers.set(t));
    if (this.canSchedule) {
      this.api.get<PagedResult<StudentDto>>(`${Api.academic}/students`, { pageSize: 100, status: 'Active' }).subscribe((r) => this.students.set(r.items));
    }
  }

  /** Teachers qualified for the subject (a teacher with no subjects listed teaches any). */
  protected teachersFor(courseId: number | null): StaffProfileDto[] {
    return this.teachers().filter((t) => !courseId || !t.courseIds?.length || t.courseIds.includes(courseId));
  }

  /** Picking a student fills in their teacher and usual session length. */
  protected onStudent(f: SessionForm): void {
    const student = this.students().find((s) => s.userId === f.studentUserId);
    if (!student) {
      return;
    }
    f.durationMinutes = student.sessionMinutes;
    f.title ||= student.fullName;
    if (this.isStaff && student.teachers.length) {
      f.teacherUserId = student.teachers[0].userId;
    }
  }

  protected excuse(s: SessionDto): void {
    this.actions.excuse(s).subscribe(() => this.load());
  }

  protected resolve(s: SessionDto): void {
    this.actions.resolve(s.excuse!, s).subscribe({ next: () => this.load(), error: (e) => this.notify.error(e) });
  }

  protected decideAbsence(s: SessionDto, counted: boolean): void {
    this.actions.decideAbsence(s, counted).subscribe({
      next: () => {
        this.notify.saved();
        this.load();
      },
      error: (e) => this.notify.error(e),
    });
  }

  protected shift(days: number): void {
    this.weekStart.set(addDays(this.weekStart(), days));
    this.load();
  }

  protected today(): void {
    this.weekStart.set(startOfWeek(new Date()));
    this.load();
  }

  protected load(): void {
    this.api
      .get<SessionDto[]>(`${Api.academic}/sessions`, {
        fromUtc: this.weekStart().toISOString(),
        toUtc: addDays(this.weekStart(), 7).toISOString(),
        teacherUserId: this.teacherFilter,
      })
      .subscribe((s) => this.sessions.set(s));
  }

  protected canRun(s: SessionDto): boolean {
    return this.isStaff || s.teacherUserId === this.auth.user()?.id;
  }

  protected edit(s: SessionDto | null): void {
    const me = this.auth.user()?.id ?? null;
    if (s) {
      const start = parseUtc(s.startsAtUtc);
      this.form.set({
        id: s.id, title: s.title, courseId: s.courseId, studentUserId: s.studentUserId, teacherUserId: s.teacherUserId, date: isoDate(start),
        time: start.toTimeString().slice(0, 5), durationMinutes: s.durationMinutes, meetingUrl: s.meetingUrl ?? '', notes: s.notes ?? '', repeatWeeks: 1,
      });
    } else {
      this.form.set({
        id: null, title: '', courseId: this.courses()[0]?.id ?? null, studentUserId: null, teacherUserId: this.isStaff ? null : me,
        date: isoDate(new Date()), time: '16:00', durationMinutes: 30, meetingUrl: '', notes: '', repeatWeeks: 1,
      });
    }
  }

  protected save(f: SessionForm): void {
    const body = {
      title: f.title, courseId: f.courseId, studentUserId: f.studentUserId, teacherUserId: f.teacherUserId,
      startsAtUtc: new Date(`${f.date}T${f.time}`).toISOString(), durationMinutes: f.durationMinutes,
      meetingUrl: f.meetingUrl || null, notes: f.notes || null, repeatWeeks: f.repeatWeeks,
    };
    const request = f.id ? this.api.put(`${Api.academic}/sessions/${f.id}`, body) : this.api.post(`${Api.academic}/sessions`, body);
    request.subscribe({
      next: () => {
        this.notify.saved();
        this.form.set(null);
        this.load();
      },
      error: (e) => this.notify.error(e),
    });
  }

  protected act(s: SessionDto, action: 'complete' | 'cancel' | 'meeting-link'): void {
    this.api.post(`${Api.academic}/sessions/${s.id}/${action}`).subscribe({ next: () => this.load(), error: (e) => this.notify.error(e) });
  }

  protected open(s: SessionDto): void {
    void this.router.navigate(['/sessions', s.id]);
  }
}

interface LogForm {
  rating: number | null;
  accomplished: string;
  homework: string;
  comment: string;
  memorization: string;
  revision: string;
  mistakes: number | null;
}

/**
 * One session: the student's attendance (US-026) and the teacher's session log (US-027): what was
 * covered, a rating, homework and notes, plus memorisation, revision and mistakes for Quran. Once the
 * session is completed and logged, a report goes to the guardian (or the adult student).
 */
@Component({
  selector: 'app-session-detail',
  imports: [PAGE_IMPORTS],
  template: `
    @if (session(); as s) {
      <div class="page-header">
        <div>
          <h1>{{ s.title }}</h1>
          <p class="muted">{{ s.startsAtUtc | utcDate: 'EEEE d MMM, HH:mm' }} · {{ s.courseName }} · {{ s.studentName ?? '—' }} · {{ s.teacherName }}</p>
        </div>
        <div class="head-actions">
          <span class="status" [class]="s.status">{{ 'status.' + s.status | translate }}</span>
          @if (s.reportSentOnUtc) {
            <span class="status ok" [matTooltip]="(s.reportSentOnUtc | utcDate: 'd MMM, HH:mm') ?? ''"><mat-icon inline>mark_email_read</mat-icon>{{ 'log.reportSent' | translate }}</span>
          }
          @if (s.status === 'Scheduled') { <button mat-stroked-button (click)="complete()">{{ 'sessions.complete' | translate }}</button> }
        </div>
      </div>

      @if (row(); as r) {
        <mat-card appearance="outlined" class="panel">
          <mat-card-header><mat-card-title>{{ 'sessions.attendance' | translate }} — {{ r.fullName }}</mat-card-title></mat-card-header>
          <mat-card-content>
            <div class="toolbar">
              <mat-button-toggle-group [(ngModel)]="attendance">
                <mat-button-toggle value="Present">{{ 'status.Present' | translate }}</mat-button-toggle>
                <mat-button-toggle value="Late">{{ 'status.Late' | translate }}</mat-button-toggle>
                <mat-button-toggle value="Absent">{{ 'status.Absent' | translate }}</mat-button-toggle>
              </mat-button-toggle-group>
              <mat-form-field class="grow"><mat-label>{{ 'common.notes' | translate }}</mat-label><input matInput [(ngModel)]="attendanceNote" /></mat-form-field>
              @if (canRecord) { <button mat-flat-button (click)="saveAttendance()" [disabled]="!attendance">{{ 'sessions.saveAttendance' | translate }}</button> }
            </div>
          </mat-card-content>
        </mat-card>

        <mat-card appearance="outlined" class="panel" style="margin-top: 16px">
          <mat-card-header>
            <mat-card-title>{{ 'log.title' | translate }}</mat-card-title>
            <mat-card-subtitle>{{ 'log.hint' | translate }}</mat-card-subtitle>
          </mat-card-header>
          <mat-card-content>
            <div class="form-grid">
              <mat-form-field class="span-2"><mat-label>{{ 'log.accomplished' | translate }}</mat-label><textarea matInput rows="2" [(ngModel)]="log.accomplished"></textarea></mat-form-field>
              @if (isQuran()) {
                <mat-form-field><mat-label>{{ 'log.memorization' | translate }}</mat-label><input matInput [(ngModel)]="log.memorization" [placeholder]="'log.memorizationHint' | translate" /></mat-form-field>
                <mat-form-field><mat-label>{{ 'log.revision' | translate }}</mat-label><input matInput [(ngModel)]="log.revision" /></mat-form-field>
                <mat-form-field><mat-label>{{ 'log.mistakes' | translate }}</mat-label><input matInput type="number" min="0" [(ngModel)]="log.mistakes" /></mat-form-field>
              }
              <mat-form-field>
                <mat-label>{{ 'sessions.rating' | translate }}</mat-label>
                <mat-select [(ngModel)]="log.rating">
                  <mat-option [value]="null">—</mat-option>
                  @for (n of [1, 2, 3, 4, 5]; track n) { <mat-option [value]="n">{{ n }} ★</mat-option> }
                </mat-select>
              </mat-form-field>
              <mat-form-field class="span-2"><mat-label>{{ 'log.homework' | translate }}</mat-label><input matInput [(ngModel)]="log.homework" /></mat-form-field>
              <mat-form-field class="span-2"><mat-label>{{ 'log.notes' | translate }}</mat-label><textarea matInput rows="2" [(ngModel)]="log.comment"></textarea></mat-form-field>
            </div>
          </mat-card-content>
        </mat-card>
      } @else {
        <p class="empty">{{ 'sessions.noRoster' | translate }}</p>
      }

      <div class="form-actions">
        <button mat-stroked-button routerLink="/sessions">{{ 'common.back' | translate }}</button>
        @if (canFeedback) { <button mat-flat-button (click)="saveLog()" [disabled]="!hasLog()">{{ 'log.save' | translate }}</button> }
      </div>
    }
  `,
  styles: `
    .head-actions { display: flex; gap: 8px; align-items: center; flex-wrap: wrap; }
    .grow { flex: 1; min-width: 200px; }
    .span-2 { grid-column: span 2; }
    @media (max-width: 640px) { .span-2 { grid-column: auto; } }
  `,
})
export class SessionDetailPage implements OnInit {
  private readonly api = inject(ApiService);
  private readonly notify = inject(Notifier);
  private readonly auth = inject(AuthService);

  /** Route param :id. */
  readonly id = input.required<string>();

  protected readonly canRecord = this.auth.hasPermission(Permissions.attendance.record);
  protected readonly canFeedback = this.auth.hasPermission(Permissions.feedback.write);
  protected readonly session = signal<SessionDto | null>(null);
  protected readonly row = signal<RosterItemDto | null>(null);
  protected readonly isQuran = computed(() => this.session()?.courseKind === 'Quran');
  protected attendance: string | null = null;
  protected attendanceNote = '';
  protected log: LogForm = emptyLog();

  ngOnInit(): void {
    this.loadSession();
    this.api.get<RosterItemDto[]>(`${Api.academic}/sessions/${this.id()}/roster`).subscribe((r) => this.setRow(r));
  }

  protected hasLog(): boolean {
    const l = this.log;
    return !!(l.rating || l.accomplished.trim() || l.homework.trim() || l.comment.trim() || l.memorization.trim() || l.revision.trim() || l.mistakes !== null);
  }

  protected saveAttendance(): void {
    const r = this.row();
    if (!r || !this.attendance) {
      return;
    }
    const items = [{ studentUserId: r.studentUserId, status: this.attendance, note: this.attendanceNote || null }];
    this.api.put<RosterItemDto[]>(`${Api.academic}/sessions/${this.id()}/attendance`, { items }).subscribe({
      next: (roster) => this.done(roster),
      error: (e) => this.notify.error(e),
    });
  }

  protected saveLog(): void {
    const r = this.row();
    if (!r) {
      return;
    }
    const l = this.log;
    const quran = this.isQuran();
    const item = {
      studentUserId: r.studentUserId, rating: l.rating, comment: l.comment || null, accomplished: l.accomplished || null, homework: l.homework || null,
      memorization: quran ? l.memorization || null : null, revision: quran ? l.revision || null : null, mistakes: quran ? l.mistakes : null,
    };
    this.api.put<RosterItemDto[]>(`${Api.academic}/sessions/${this.id()}/feedback`, { items: [item] }).subscribe({
      next: (roster) => {
        this.done(roster);
        this.loadSession();
      },
      error: (e) => this.notify.error(e),
    });
  }

  protected complete(): void {
    this.api.post<SessionDto>(`${Api.academic}/sessions/${this.id()}/complete`).subscribe({
      next: (s) => this.session.set(s),
      error: (e) => this.notify.error(e),
    });
  }

  private loadSession(): void {
    this.api.get<SessionDto>(`${Api.academic}/sessions/${this.id()}`).subscribe((s) => this.session.set(s));
  }

  private done(roster: RosterItemDto[]): void {
    this.notify.saved();
    this.setRow(roster);
  }

  private setRow(roster: RosterItemDto[]): void {
    const r = roster[0] ?? null;
    this.row.set(r);
    this.attendance = r?.attendanceStatus ?? null;
    this.attendanceNote = r?.attendanceNote ?? '';
    const l = r?.log;
    this.log = l
      ? {
          rating: l.rating, accomplished: l.accomplished ?? '', homework: l.homework ?? '', comment: l.comment ?? '',
          memorization: l.memorization ?? '', revision: l.revision ?? '', mistakes: l.mistakes,
        }
      : emptyLog();
  }
}

function emptyLog(): LogForm {
  return { rating: null, accomplished: '', homework: '', comment: '', memorization: '', revision: '', mistakes: null };
}
