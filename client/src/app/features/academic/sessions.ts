import { Component, computed, inject, input, OnInit, signal } from '@angular/core';
import { Router } from '@angular/router';
import { Api, ApiService } from '../../core/api/api.service';
import { CourseDto, GroupDto, RosterItemDto, SessionDto, StaffProfileDto } from '../../core/api/models';
import { AuthService } from '../../core/auth/auth.service';
import { Permissions, Roles } from '../../core/auth/permissions';
import { addDays, isoDate, Notifier } from '../../shared/notifier';
import { PAGE_IMPORTS } from '../../shared/page-imports';

interface SessionForm {
  id: number | null;
  title: string;
  courseId: number | null;
  groupId: number | null;
  teacherUserId: number | null;
  date: string;
  time: string;
  durationMinutes: number;
  type: 'Offline' | 'Online';
  location: string;
  meetingUrl: string;
  generateMeetingLink: boolean;
  notes: string;
  repeatWeeks: number;
}

function startOfWeek(date: Date): Date {
  // Academy weeks start on Saturday.
  const d = new Date(date.getFullYear(), date.getMonth(), date.getDate());
  return addDays(d, -((d.getDay() + 1) % 7));
}

/** Weekly timetable with scheduling, clash detection and online links (US-025, US-037). */
@Component({
  selector: 'app-sessions',
  imports: [PAGE_IMPORTS],
  template: `
    <div class="page-header">
      <h1>{{ 'nav.sessions' | translate }}</h1>
      @if (canSchedule) { <button mat-flat-button (click)="edit(null)"><mat-icon>event</mat-icon>{{ 'sessions.new' | translate }}</button> }
    </div>

    @if (form(); as f) {
      <mat-card appearance="outlined" class="panel">
        <mat-card-content>
          <div class="form-grid">
            <mat-form-field><mat-label>{{ 'common.title' | translate }}</mat-label><input matInput [(ngModel)]="f.title" /></mat-form-field>
            <mat-form-field>
              <mat-label>{{ 'nav.courses' | translate }}</mat-label>
              <mat-select [(ngModel)]="f.courseId">@for (c of courses(); track c.id) { <mat-option [value]="c.id">{{ c.name }}</mat-option> }</mat-select>
            </mat-form-field>
            <mat-form-field>
              <mat-label>{{ 'nav.groups' | translate }}</mat-label>
              <mat-select [(ngModel)]="f.groupId"><mat-option [value]="null">—</mat-option>@for (g of groups(); track g.id) { <mat-option [value]="g.id">{{ g.name }}</mat-option> }</mat-select>
            </mat-form-field>
            <mat-form-field>
              <mat-label>{{ 'roles.Teacher' | translate }}</mat-label>
              <mat-select [(ngModel)]="f.teacherUserId" [disabled]="!isStaff">@for (t of teachers(); track t.userId) { <mat-option [value]="t.userId">{{ t.fullName }}</mat-option> }</mat-select>
            </mat-form-field>
            <mat-form-field><mat-label>{{ 'common.date' | translate }}</mat-label><input matInput type="date" [(ngModel)]="f.date" /></mat-form-field>
            <mat-form-field><mat-label>{{ 'common.time' | translate }}</mat-label><input matInput type="time" [(ngModel)]="f.time" /></mat-form-field>
            <mat-form-field><mat-label>{{ 'sessions.duration' | translate }}</mat-label><input matInput type="number" [(ngModel)]="f.durationMinutes" /></mat-form-field>
            <mat-form-field>
              <mat-label>{{ 'common.type' | translate }}</mat-label>
              <mat-select [(ngModel)]="f.type"><mat-option value="Offline">{{ 'sessions.offline' | translate }}</mat-option><mat-option value="Online">{{ 'sessions.online' | translate }}</mat-option></mat-select>
            </mat-form-field>
            @if (f.type === 'Offline') {
              <mat-form-field><mat-label>{{ 'sessions.location' | translate }}</mat-label><input matInput [(ngModel)]="f.location" /></mat-form-field>
            } @else {
              <mat-form-field><mat-label>{{ 'sessions.meetingUrl' | translate }}</mat-label><input matInput [(ngModel)]="f.meetingUrl" dir="ltr" /></mat-form-field>
              <mat-checkbox [(ngModel)]="f.generateMeetingLink">{{ 'sessions.generateLink' | translate }}</mat-checkbox>
            }
            @if (!f.id) {
              <mat-form-field><mat-label>{{ 'sessions.repeatWeeks' | translate }}</mat-label><input matInput type="number" min="1" max="52" [(ngModel)]="f.repeatWeeks" /></mat-form-field>
            }
            <mat-form-field><mat-label>{{ 'common.notes' | translate }}</mat-label><input matInput [(ngModel)]="f.notes" /></mat-form-field>
          </div>
          <div class="form-actions">
            <button mat-button (click)="form.set(null)">{{ 'common.cancel' | translate }}</button>
            <button mat-flat-button (click)="save(f)" [disabled]="!f.title || !f.courseId || !f.teacherUserId">{{ 'common.save' | translate }}</button>
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
                <td class="ltr" style="width: 110px">{{ s.startsAtUtc | date: 'HH:mm' }} – {{ s.endsAtUtc | date: 'HH:mm' }}</td>
                <td><b>{{ s.title }}</b><div class="muted">{{ s.courseName }} · {{ s.groupName ?? '—' }} · {{ s.teacherName }}</div></td>
                <td>
                  @if (s.type === 'Online') {
                    @if (s.meetingUrl) { <a mat-button [href]="s.meetingUrl" target="_blank" rel="noopener"><mat-icon>videocam</mat-icon>{{ 'sessions.join' | translate }}</a> }
                  } @else { {{ s.location }} }
                </td>
                <td><span class="status" [class]="s.status">{{ 'status.' + s.status | translate }}</span></td>
                <td class="actions">
                  @if (canRun(s)) {
                    <button mat-button (click)="open(s)">{{ 'sessions.attendance' | translate }}</button>
                    <button mat-icon-button [matMenuTriggerFor]="menu"><mat-icon>more_vert</mat-icon></button>
                    <mat-menu #menu="matMenu">
                      @if (s.status === 'Scheduled') {
                        <button mat-menu-item (click)="edit(s)">{{ 'common.edit' | translate }}</button>
                        <button mat-menu-item (click)="act(s, 'complete')">{{ 'sessions.complete' | translate }}</button>
                        <button mat-menu-item (click)="act(s, 'meeting-link')">{{ 'sessions.generateLink' | translate }}</button>
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
  protected readonly rtl = () => document.documentElement.dir === 'rtl';

  protected readonly sessions = signal<SessionDto[]>([]);
  protected readonly courses = signal<CourseDto[]>([]);
  protected readonly groups = signal<GroupDto[]>([]);
  protected readonly teachers = signal<StaffProfileDto[]>([]);
  protected readonly form = signal<SessionForm | null>(null);
  protected readonly weekStart = signal(startOfWeek(new Date()));
  protected readonly weekEnd = computed(() => addDays(this.weekStart(), 6));
  protected teacherFilter: number | null = null;

  protected readonly days = computed(() => {
    const byDay = new Map<string, SessionDto[]>();
    for (const s of this.sessions()) {
      const key = isoDate(new Date(s.startsAtUtc));
      byDay.set(key, [...(byDay.get(key) ?? []), s]);
    }
    return [...byDay.entries()].sort(([a], [b]) => a.localeCompare(b)).map(([date, sessions]) => ({ date, sessions }));
  });

  ngOnInit(): void {
    this.load();
    this.api.get<CourseDto[]>(`${Api.academic}/courses`).subscribe((c) => this.courses.set(c));
    this.api.get<GroupDto[]>(`${Api.academic}/groups`).subscribe((g) => this.groups.set(g));
    this.api.get<StaffProfileDto[]>(`${Api.academic}/teachers`).subscribe((t) => this.teachers.set(t));
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
      const start = new Date(s.startsAtUtc);
      this.form.set({
        id: s.id, title: s.title, courseId: s.courseId, groupId: s.groupId, teacherUserId: s.teacherUserId, date: isoDate(start),
        time: start.toTimeString().slice(0, 5), durationMinutes: (new Date(s.endsAtUtc).getTime() - start.getTime()) / 60000, type: s.type,
        location: s.location ?? '', meetingUrl: s.meetingUrl ?? '', generateMeetingLink: false, notes: s.notes ?? '', repeatWeeks: 1,
      });
    } else {
      this.form.set({
        id: null, title: '', courseId: null, groupId: null, teacherUserId: this.isStaff ? null : me, date: isoDate(new Date()), time: '16:00',
        durationMinutes: 60, type: 'Offline', location: '', meetingUrl: '', generateMeetingLink: true, notes: '', repeatWeeks: 1,
      });
    }
  }

  protected save(f: SessionForm): void {
    const body = {
      title: f.title, courseId: f.courseId, groupId: f.groupId, teacherUserId: f.teacherUserId,
      startsAtUtc: new Date(`${f.date}T${f.time}`).toISOString(), durationMinutes: f.durationMinutes, type: f.type,
      location: f.location || null, meetingUrl: f.meetingUrl || null, generateMeetingLink: f.generateMeetingLink,
      notes: f.notes || null, repeatWeeks: f.repeatWeeks,
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

interface RosterRow extends RosterItemDto {
  status: string | null;
  note: string;
  ratingValue: number | null;
  commentValue: string;
}

/** Take attendance (US-026) and write per-student feedback (US-027) for one session. */
@Component({
  selector: 'app-session-detail',
  imports: [PAGE_IMPORTS],
  template: `
    @if (session(); as s) {
      <div class="page-header">
        <div>
          <h1>{{ s.title }}</h1>
          <p class="muted">{{ s.startsAtUtc | date: 'EEEE d MMM, HH:mm' }} · {{ s.courseName }} · {{ s.groupName ?? '—' }} · {{ s.teacherName }}</p>
        </div>
        <div>
          <span class="status" [class]="s.status">{{ 'status.' + s.status | translate }}</span>
          @if (s.status === 'Scheduled') { <button mat-stroked-button (click)="complete()">{{ 'sessions.complete' | translate }}</button> }
        </div>
      </div>

      <div class="table-wrap">
        <table class="data-table">
          <thead><tr><th>{{ 'common.fullName' | translate }}</th><th>{{ 'sessions.attendance' | translate }}</th><th>{{ 'common.notes' | translate }}</th><th>{{ 'sessions.rating' | translate }}</th><th>{{ 'sessions.comment' | translate }}</th></tr></thead>
          <tbody>
            @for (r of rows(); track r.studentUserId) {
              <tr>
                <td>{{ r.fullName }}</td>
                <td>
                  <mat-button-toggle-group [(ngModel)]="r.status">
                    <mat-button-toggle value="Present">{{ 'status.Present' | translate }}</mat-button-toggle>
                    <mat-button-toggle value="Late">{{ 'status.Late' | translate }}</mat-button-toggle>
                    <mat-button-toggle value="Absent">{{ 'status.Absent' | translate }}</mat-button-toggle>
                  </mat-button-toggle-group>
                </td>
                <td><input class="cell" [(ngModel)]="r.note" /></td>
                <td>
                  <select class="cell" [(ngModel)]="r.ratingValue">
                    <option [ngValue]="null">—</option>
                    @for (n of [1, 2, 3, 4, 5]; track n) { <option [ngValue]="n">{{ n }} ★</option> }
                  </select>
                </td>
                <td><input class="cell wide" [(ngModel)]="r.commentValue" /></td>
              </tr>
            } @empty {
              <tr><td colspan="5" class="empty">{{ 'sessions.noRoster' | translate }}</td></tr>
            }
          </tbody>
        </table>
      </div>
      <div class="form-actions">
        <button mat-stroked-button routerLink="/sessions">{{ 'common.back' | translate }}</button>
        @if (canRecord) { <button mat-flat-button (click)="saveAttendance()" [disabled]="!hasAttendance()">{{ 'sessions.saveAttendance' | translate }}</button> }
        @if (canFeedback) { <button mat-flat-button (click)="saveFeedback()" [disabled]="!hasFeedback()">{{ 'sessions.saveFeedback' | translate }}</button> }
      </div>
    }
  `,
  styles: `
    .cell { padding: 6px; border: 1px solid var(--mat-sys-outline-variant); border-radius: 6px; background: transparent; color: inherit; width: 120px; }
    .cell.wide { width: 240px; }
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
  protected readonly rows = signal<RosterRow[]>([]);

  ngOnInit(): void {
    this.api.get<SessionDto>(`${Api.academic}/sessions/${this.id()}`).subscribe((s) => this.session.set(s));
    this.loadRoster();
  }

  protected hasAttendance(): boolean {
    return this.rows().some((r) => r.status);
  }

  protected hasFeedback(): boolean {
    return this.rows().some((r) => r.ratingValue);
  }

  protected saveAttendance(): void {
    const items = this.rows().filter((r) => r.status).map((r) => ({ studentUserId: r.studentUserId, status: r.status, note: r.note || null }));
    this.api.put<RosterItemDto[]>(`${Api.academic}/sessions/${this.id()}/attendance`, { items }).subscribe({
      next: (r) => this.done(r),
      error: (e) => this.notify.error(e),
    });
  }

  protected saveFeedback(): void {
    const items = this.rows()
      .filter((r) => r.ratingValue)
      .map((r) => ({ studentUserId: r.studentUserId, rating: r.ratingValue, comment: r.commentValue || null }));
    this.api.put<RosterItemDto[]>(`${Api.academic}/sessions/${this.id()}/feedback`, { items }).subscribe({
      next: (r) => this.done(r),
      error: (e) => this.notify.error(e),
    });
  }

  protected complete(): void {
    this.api.post<SessionDto>(`${Api.academic}/sessions/${this.id()}/complete`).subscribe({
      next: (s) => this.session.set(s),
      error: (e) => this.notify.error(e),
    });
  }

  private done(roster: RosterItemDto[]): void {
    this.notify.saved();
    this.setRows(roster);
  }

  private loadRoster(): void {
    this.api.get<RosterItemDto[]>(`${Api.academic}/sessions/${this.id()}/roster`).subscribe((r) => this.setRows(r));
  }

  private setRows(roster: RosterItemDto[]): void {
    this.rows.set(
      roster.map((r) => ({ ...r, status: r.attendanceStatus, note: r.attendanceNote ?? '', ratingValue: r.rating, commentValue: r.comment ?? '' })),
    );
  }
}
