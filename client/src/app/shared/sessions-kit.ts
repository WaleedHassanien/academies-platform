import { Component, computed, effect, inject, Injectable, input, output, signal } from '@angular/core';
import { MAT_DIALOG_DATA, MatDialog, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatRadioModule } from '@angular/material/radio';
import { filter, map, Observable, switchMap } from 'rxjs';
import { Api, ApiService } from '../core/api/api.service';
import { ExcuseResolution, JoinLinkDto, SessionDto, SessionExcuseDto, SessionReportDto } from '../core/api/models';
import { LanguageService } from '../core/i18n/language.service';
import { addDays, isoDate, Notifier } from './notifier';
import { PAGE_IMPORTS } from './page-imports';
import { formatTime, parseUtc } from './time-zones';
import { Stat } from './ui';

// Shared pieces for one-to-one sessions: what happened to a session (outcome), the excuse and
// excuse-resolution dialogs, and the week/month "my sessions" panel on each role's dashboard.

/** Academy weeks start on Saturday. */
export function startOfWeek(date: Date): Date {
  const d = new Date(date.getFullYear(), date.getMonth(), date.getDate());
  return addDays(d, -((d.getDay() + 1) % 7));
}

export interface Outcome {
  /** Translation key under "outcome." */
  key: string;
  /** Status pill class. */
  tone: 'ok' | 'warn' | 'bad' | 'info' | '';
}

/** What happened to a session, from the student's attendance, the absence decision and any excuse. */
export function outcomeOf(s: SessionDto): Outcome {
  if (s.status === 'Excused') {
    return s.excuse?.status === 'Pending' || !s.excuse?.resolution
      ? { key: 'excusePending', tone: 'warn' }
      : { key: `excuse_${s.excuse.resolution}`, tone: 'info' };
  }
  if (s.status === 'Cancelled') {
    return { key: 'cancelled', tone: 'bad' };
  }
  if (s.attendanceStatus === 'Absent') {
    return s.absenceCounted === true ? { key: 'absentCounted', tone: 'bad' }
      : s.absenceCounted === false ? { key: 'absentNotCounted', tone: '' }
      : { key: 'absentPending', tone: 'warn' };
  }
  if (s.status === 'Completed') {
    return s.attendanceStatus === 'Late' ? { key: 'late', tone: 'ok' } : { key: 'held', tone: 'ok' };
  }
  return { key: 'scheduled', tone: 'info' };
}

/** A student may excuse themself from an upcoming one-to-one session. */
export function canBeExcused(s: SessionDto): boolean {
  return !!s.studentUserId && s.status === 'Scheduled' && parseUtc(s.startsAtUtc) > new Date();
}

@Component({
  selector: 'app-outcome',
  imports: [PAGE_IMPORTS],
  template: `@let o = outcome(); <span class="status" [class]="o.tone" [matTooltip]="tip()">{{ 'outcome.' + o.key | translate }}</span>`,
})
export class OutcomeChip {
  readonly session = input.required<SessionDto>();
  protected readonly outcome = computed(() => outcomeOf(this.session()));
  protected readonly tip = computed(() => this.session().excuse?.reason ?? '');
}

// ---------- Dialogs ----------

export interface ExcuseDialogResult {
  reason: string;
  preferredStartsAtUtc: string | null;
}

@Component({
  selector: 'app-excuse-dialog',
  imports: [PAGE_IMPORTS, MatDialogModule],
  template: `
    <h2 mat-dialog-title>{{ 'excuse.title' | translate }}</h2>
    <mat-dialog-content>
      <p class="muted">{{ data.title }} · {{ data.when }}</p>
      <mat-form-field class="full">
        <mat-label>{{ 'excuse.reason' | translate }}</mat-label>
        <textarea matInput rows="3" [(ngModel)]="reason"></textarea>
      </mat-form-field>
      <mat-form-field class="full">
        <mat-label>{{ 'excuse.preferredTime' | translate }}</mat-label>
        <input matInput type="datetime-local" [(ngModel)]="preferred" />
        <mat-hint>{{ 'excuse.preferredHint' | translate }}</mat-hint>
      </mat-form-field>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button mat-dialog-close>{{ 'common.cancel' | translate }}</button>
      <button mat-flat-button (click)="send()">{{ 'excuse.send' | translate }}</button>
    </mat-dialog-actions>
  `,
  styles: `.full { width: 100%; margin-top: 8px; } mat-dialog-content { min-width: min(440px, 80vw); }`,
})
export class ExcuseDialog {
  protected readonly data = inject<{ title: string; when: string }>(MAT_DIALOG_DATA);
  private readonly ref = inject(MatDialogRef<ExcuseDialog, ExcuseDialogResult>);
  protected reason = '';
  protected preferred = '';

  protected send(): void {
    this.ref.close({ reason: this.reason.trim(), preferredStartsAtUtc: this.preferred ? new Date(this.preferred).toISOString() : null });
  }
}

export interface ResolveDialogResult {
  resolution: ExcuseResolution | null;
  reject: boolean;
  makeupStartsAtUtc: string | null;
  note: string | null;
}

@Component({
  selector: 'app-resolve-excuse-dialog',
  imports: [PAGE_IMPORTS, MatDialogModule, MatRadioModule],
  template: `
    <h2 mat-dialog-title>{{ 'excuse.resolveTitle' | translate }}</h2>
    <mat-dialog-content>
      <p class="muted">{{ data.student }} · {{ data.title }} · {{ data.when }}</p>
      @if (data.reason) { <p class="reason"><mat-icon>format_quote</mat-icon>{{ data.reason }}</p> }
      <mat-radio-group class="options" [(ngModel)]="choice">
        @for (o of options; track o) {
          <mat-radio-button [value]="o">
            <b>{{ 'excuse.opt_' + o | translate }}</b>
            <div class="muted small">{{ 'excuse.hint_' + o | translate }}</div>
          </mat-radio-button>
        }
      </mat-radio-group>
      @if (choice === 'Rescheduled') {
        <mat-form-field class="full">
          <mat-label>{{ 'excuse.makeupTime' | translate }}</mat-label>
          <input matInput type="datetime-local" [(ngModel)]="makeup" />
        </mat-form-field>
      }
      <mat-form-field class="full">
        <mat-label>{{ 'common.notes' | translate }}</mat-label>
        <input matInput [(ngModel)]="note" />
      </mat-form-field>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button mat-dialog-close>{{ 'common.cancel' | translate }}</button>
      <button mat-flat-button (click)="save()" [disabled]="!choice || (choice === 'Rescheduled' && !makeup)">{{ 'common.save' | translate }}</button>
    </mat-dialog-actions>
  `,
  styles: `
    mat-dialog-content { min-width: min(480px, 84vw); }
    .options { display: flex; flex-direction: column; gap: 6px; margin: 8px 0 12px; }
    .options mat-radio-button { padding: 6px 8px; border-radius: 12px; border: 1px solid var(--app-border); }
    .small { font-size: 0.78rem; }
    .full { width: 100%; }
    .reason { display: flex; gap: 6px; align-items: flex-start; padding: 10px 12px; border-radius: 12px; background: var(--app-surface-2); }
    .reason mat-icon { color: var(--app-muted); }
  `,
})
export class ResolveExcuseDialog {
  protected readonly data = inject<{ student: string; title: string; when: string; reason: string | null; preferred: string | null }>(MAT_DIALOG_DATA);
  private readonly ref = inject(MatDialogRef<ResolveExcuseDialog, ResolveDialogResult>);
  protected readonly options = ['Rescheduled', 'CarriedOver', 'NotCounted', 'DeductedNextMonth', 'Reject'] as const;
  protected choice: (typeof this.options)[number] | null = null;
  protected makeup = this.data.preferred ? toLocalInput(parseUtc(this.data.preferred)) : '';
  protected note = '';

  protected save(): void {
    const reject = this.choice === 'Reject';
    this.ref.close({
      resolution: reject ? null : (this.choice as ExcuseResolution),
      reject,
      makeupStartsAtUtc: this.choice === 'Rescheduled' && this.makeup ? new Date(this.makeup).toISOString() : null,
      note: this.note.trim() || null,
    });
  }
}

/**
 * A teacher's permanent room link for their supervisor to hand over: pick how long it works,
 * then copy it or send it on WhatsApp. The teacher opens it as the room's moderator, no login.
 */
@Component({
  selector: 'app-share-room-dialog',
  imports: [PAGE_IMPORTS, MatDialogModule],
  template: `
    <h2 mat-dialog-title>{{ 'room.shareTitle' | translate: { name: data.fullName } }}</h2>
    <mat-dialog-content>
      <p class="muted">{{ 'room.shareHint' | translate }}</p>
      <mat-form-field class="full">
        <mat-label>{{ 'room.validFor' | translate }}</mat-label>
        <mat-select [(ngModel)]="days" (selectionChange)="load()">
          @for (d of [7, 30, 90]; track d) { <mat-option [value]="d">{{ 'room.days' | translate: { n: d } }}</mat-option> }
        </mat-select>
      </mat-form-field>
      @if (link(); as l) {
        <div class="link ltr">{{ l.url }}</div>
        <p class="muted small">{{ 'room.expires' | translate }} {{ l.expiresAtUtc | utcDate: 'd MMM y' }}</p>
      } @else {
        <span class="skeleton" style="height: 56px"></span>
      }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button mat-dialog-close>{{ 'common.close' | translate }}</button>
      <button mat-stroked-button (click)="copy()" [disabled]="!link()"><mat-icon>{{ copied() ? 'check' : 'content_copy' }}</mat-icon>{{ (copied() ? 'room.copied' : 'room.copy') | translate }}</button>
      <a mat-flat-button class="wa" [href]="whatsApp()" target="_blank" rel="noopener" [class.disabled]="!link()"><mat-icon>chat</mat-icon>{{ 'room.whatsapp' | translate }}</a>
    </mat-dialog-actions>
  `,
  styles: `
    mat-dialog-content { min-width: min(520px, 86vw); }
    .full { width: 100%; }
    .link { padding: 12px 14px; border-radius: 12px; background: var(--app-surface-2); border: 1px dashed var(--app-border-strong);
      font-size: 0.78rem; word-break: break-all; max-height: 110px; overflow: auto; user-select: all; }
    .small { font-size: 0.78rem; margin-top: 6px; }
    .wa { background-image: none !important; background-color: #25d366 !important; color: #fff !important; }
    .wa.disabled { pointer-events: none; opacity: 0.5; }
  `,
})
export class ShareRoomDialog {
  protected readonly data = inject<{ userId: number; fullName: string }>(MAT_DIALOG_DATA);
  private readonly api = inject(ApiService);
  private readonly notify = inject(Notifier);
  protected days = 30;
  protected readonly link = signal<JoinLinkDto | null>(null);
  protected readonly copied = signal(false);

  protected readonly whatsApp = computed(() => {
    const l = this.link();
    if (!l) {
      return '';
    }
    const text = `${this.notify.text('room.message', { name: this.data.fullName })}\n${l.url}`;
    return `https://wa.me/?text=${encodeURIComponent(text)}`;
  });

  constructor() {
    this.load();
  }

  protected load(): void {
    this.link.set(null);
    this.copied.set(false);
    this.api.get<JoinLinkDto>(`${Api.academic}/teachers/${this.data.userId}/meeting-room-link`, { days: this.days }).subscribe({
      next: (l) => this.link.set(l),
      error: (e) => this.notify.error(e),
    });
  }

  protected copy(): void {
    const url = this.link()?.url;
    if (!url) {
      return;
    }
    navigator.clipboard.writeText(url).then(
      () => {
        this.copied.set(true);
        setTimeout(() => this.copied.set(false), 2000);
      },
      () => this.notify.info('room.copyFailed'),
    );
  }
}

function toLocalInput(d: Date): string {
  return `${isoDate(d)}T${String(d.getHours()).padStart(2, '0')}:${String(d.getMinutes()).padStart(2, '0')}`;
}

/** Opens the dialogs and calls the API; each returns the updated session, or completes empty if cancelled. */
@Injectable({ providedIn: 'root' })
export class SessionActions {
  private readonly dialog = inject(MatDialog);
  private readonly api = inject(ApiService);
  private readonly notify = inject(Notifier);
  private readonly language = inject(LanguageService);

  excuse(s: SessionDto): Observable<SessionDto> {
    return this.dialog
      .open<ExcuseDialog, unknown, ExcuseDialogResult>(ExcuseDialog, { data: { title: s.title, when: this.when(s) }, autoFocus: 'textarea' })
      .afterClosed()
      .pipe(
        filter((r): r is ExcuseDialogResult => !!r),
        switchMap((r) => this.api.post<SessionDto>(`${Api.academic}/sessions/${s.id}/excuse`, r)),
        map((updated) => {
          this.notify.info('excuse.sent');
          return updated;
        }),
      );
  }

  resolve(excuse: SessionExcuseDto, s: SessionDto): Observable<SessionDto> {
    return this.dialog
      .open<ResolveExcuseDialog, unknown, ResolveDialogResult>(ResolveExcuseDialog, {
        data: { student: s.studentName ?? '', title: s.title, when: this.when(s), reason: excuse.reason, preferred: excuse.preferredStartsAtUtc },
      })
      .afterClosed()
      .pipe(
        filter((r): r is ResolveDialogResult => !!r),
        switchMap((r) => this.api.post<SessionDto>(`${Api.academic}/excuses/${excuse.id}/resolve`, r)),
        map((updated) => {
          this.notify.saved();
          return updated;
        }),
      );
  }

  decideAbsence(s: SessionDto, counted: boolean): Observable<SessionDto> {
    return this.api.put<SessionDto>(`${Api.academic}/sessions/${s.id}/absence-decision`, { counted });
  }

  /** Opens the caller's personal link into the session's room (signed with their name and email, no Jitsi login). */
  join(s: SessionDto): void {
    this.openLink(this.api.get<JoinLinkDto>(`${Api.academic}/sessions/${s.id}/join`));
  }

  /** A teacher's own room, any time. */
  openMyRoom(): void {
    this.openLink(this.api.get<JoinLinkDto>(`${Api.academic}/me/meeting-room`));
  }

  /** Supervisor/staff: get a teacher's room link to copy or send on WhatsApp. */
  shareTeacherRoom(teacher: { userId: number; fullName: string }): void {
    this.dialog.open(ShareRoomDialog, { data: teacher, autoFocus: false });
  }

  private openLink(request: Observable<JoinLinkDto>): void {
    // Open the tab now, while the click still counts as the user's; fill it in when the link arrives.
    const tab = window.open('', '_blank');
    request.subscribe({
      next: (link) => {
        if (tab) {
          tab.opener = null;
          tab.location.href = link.url;
        } else {
          window.location.href = link.url;
        }
      },
      error: (e) => {
        tab?.close();
        this.notify.error(e);
      },
    });
  }

  private when(s: SessionDto): string {
    const d = parseUtc(s.startsAtUtc);
    return `${d.toLocaleDateString(this.language.language() === 'ar' ? 'ar-EG' : 'en-GB', { weekday: 'short', day: 'numeric', month: 'short' })} ${formatTime(s.startsAtUtc, Intl.DateTimeFormat().resolvedOptions().timeZone, this.language.language())}`;
  }
}

// ---------- "My sessions" panel ----------

type Period = 'week' | 'month';

/**
 * Week (default) or month of the caller's sessions, as the server scopes them: a student sees
 * theirs, a teacher theirs, a supervisor their teachers'. With a session report for the same period.
 */
@Component({
  selector: 'app-my-sessions',
  imports: [PAGE_IMPORTS, Stat, OutcomeChip],
  template: `
    <div class="bar">
      <mat-button-toggle-group [value]="period()" (change)="setPeriod($event.value)" class="seg">
        <mat-button-toggle value="week">{{ 'mySessions.week' | translate }}</mat-button-toggle>
        <mat-button-toggle value="month">{{ 'mySessions.month' | translate }}</mat-button-toggle>
      </mat-button-toggle-group>
      <div class="nav">
        <button mat-icon-button (click)="move(-1)" [attr.aria-label]="'common.previous' | translate"><mat-icon class="flip">chevron_left</mat-icon></button>
        <b class="range">{{ rangeLabel() }}</b>
        <button mat-icon-button (click)="move(1)" [attr.aria-label]="'common.next' | translate"><mat-icon class="flip">chevron_right</mat-icon></button>
        <button mat-button (click)="reset()">{{ (period() === 'week' ? 'sessions.thisWeek' : 'mySessions.thisMonth') | translate }}</button>
      </div>
    </div>

    @if (report(); as r) {
      <div class="stats">
        <app-stat icon="event_upcoming" [label]="'outcome.scheduled' | translate" [value]="r.totals.scheduled" />
        <app-stat icon="task_alt" [label]="'outcome.held' | translate" [value]="r.totals.held"
                  [hint]="('mySessions.hours' | translate) + ': ' + hours(r.totals.heldMinutes)" />
        <app-stat icon="person_off" [label]="'mySessions.absences' | translate" [value]="r.totals.absentCounted + r.totals.absentNotCounted + r.totals.absentPending"
                  [hint]="('outcome.absentCounted' | translate) + ' ' + r.totals.absentCounted" />
        <app-stat icon="event_busy" [label]="'mySessions.excused' | translate" [value]="r.totals.excused" />
        @if (showPending() && (r.pendingExcuses || r.pendingAbsences)) {
          <app-stat icon="pending_actions" [label]="'mySessions.needsDecision' | translate" [value]="r.pendingExcuses + r.pendingAbsences"
                    class="clickable" routerLink="/requests" />
        }
      </div>
    }

    @for (day of days(); track day.key) {
      <h3 class="section-title">{{ day.date | date: 'EEEE d MMMM' }}</h3>
      <div class="list">
        @for (s of day.sessions; track s.id) {
          <div class="item" [class.muted-row]="s.status === 'Excused' || s.status === 'Cancelled'">
            <div class="time ltr">
              <b>{{ s.startsAtUtc | utcDate: 'HH:mm' }}</b>
              <small>{{ s.durationMinutes }} {{ 'mySessions.min' | translate }}</small>
            </div>
            <div class="what">
              <b>{{ s.title }}</b>
              <div class="muted">
                @if (showStudent() && s.studentName) { <span><mat-icon>person</mat-icon>{{ s.studentName }}</span> }
                @if (showTeacher()) { <span><mat-icon>co_present</mat-icon>{{ s.teacherName }}</span> }
                @if (s.makeupOfSessionId) { <span><mat-icon>update</mat-icon>{{ 'outcome.makeup' | translate }}</span> }
              </div>
            </div>
            <app-outcome [session]="s" />
            <div class="acts">
              @if (s.meetingUrl && s.status === 'Scheduled') {
                <button mat-icon-button (click)="join(s)" [matTooltip]="'sessions.join' | translate"><mat-icon>videocam</mat-icon></button>
              }
              @if (canExcuse() && canBeExcused(s)) {
                <button mat-stroked-button (click)="excuse(s)"><mat-icon>event_busy</mat-icon>{{ 'excuse.action' | translate }}</button>
              }
            </div>
          </div>
        }
      </div>
    } @empty {
      <p class="empty">{{ 'sessions.none' | translate }}</p>
    }

    @if (report(); as r) {
      @for (table of tables(); track table) {
        @let rows = table === 'teacher' ? r.byTeacher : r.byStudent;
        @if (rows.length) {
          <h3 class="section-title">{{ (table === 'teacher' ? 'mySessions.byTeacher' : 'mySessions.byStudent') | translate }}</h3>
          <div class="table-wrap">
            <table class="data-table">
              <thead><tr>
                <th>{{ 'common.name' | translate }}</th><th class="num">{{ 'outcome.held' | translate }}</th><th class="num">{{ 'outcome.absentCounted' | translate }}</th>
                <th class="num">{{ 'outcome.absentPending' | translate }}</th><th class="num">{{ 'mySessions.excused' | translate }}</th>
                <th class="num">{{ 'outcome.scheduled' | translate }}</th><th class="num">{{ 'mySessions.hours' | translate }}</th>
              </tr></thead>
              <tbody>
                @for (row of rows; track row.userId) {
                  <tr>
                    <td>@if (table === 'student') { <a [routerLink]="['/students', row.userId]">{{ row.fullName }}</a> } @else { {{ row.fullName }} }</td>
                    <td class="num">{{ row.counts.held }}</td><td class="num">{{ row.counts.absentCounted }}</td><td class="num">{{ row.counts.absentPending }}</td>
                    <td class="num">{{ row.counts.excused }}</td><td class="num">{{ row.counts.scheduled }}</td><td class="num">{{ hours(row.counts.heldMinutes) }}</td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        }
      }
    }
  `,
  styles: `
    .bar { display: flex; align-items: center; justify-content: space-between; flex-wrap: wrap; gap: 8px 16px; margin-bottom: 16px; }
    .nav { display: flex; align-items: center; gap: 4px; }
    .range { min-width: 150px; text-align: center; font-size: 0.95rem; }
    :host-context([dir='rtl']) .flip { transform: scaleX(-1); }
    .list { display: flex; flex-direction: column; gap: 8px; }
    .item { display: grid; grid-template-columns: 72px minmax(0, 1fr) auto auto; align-items: center; gap: 12px; padding: 12px 16px;
      border-radius: 16px; background: var(--app-surface); border: 1px solid var(--app-border); box-shadow: var(--app-shadow);
      animation: rise-in 0.35s cubic-bezier(0.2, 0.7, 0.2, 1) both; transition: transform 0.2s ease, box-shadow 0.2s ease; }
    .item:hover { transform: translateY(-2px); box-shadow: var(--app-shadow-lg); }
    .item.muted-row { opacity: 0.72; }
    .time { display: flex; flex-direction: column; align-items: center; padding: 6px 0; border-radius: 12px;
      background: color-mix(in srgb, var(--mat-sys-primary) 8%, transparent); color: var(--mat-sys-primary); }
    .time b { font-size: 1.05rem; font-variant-numeric: tabular-nums; }
    .time small { font-size: 0.7rem; opacity: 0.8; }
    .what { min-width: 0; }
    .what > b { display: block; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .what .muted { display: flex; flex-wrap: wrap; gap: 2px 12px; font-size: 0.8rem; margin-top: 2px; }
    .what .muted span { display: inline-flex; align-items: center; gap: 4px; }
    .what mat-icon { font-size: 15px; width: 15px; height: 15px; }
    .acts { display: flex; align-items: center; gap: 4px; }
    app-stat.clickable { cursor: pointer; }
    @media (max-width: 639px) {
      .item { grid-template-columns: 60px minmax(0, 1fr); }
      app-outcome, .acts { grid-column: 2; }
      .acts:empty { display: none; }
      .range { min-width: 0; font-size: 0.85rem; }
    }
  `,
})
export class MySessions {
  private readonly api = inject(ApiService);
  private readonly actions = inject(SessionActions);
  private readonly language = inject(LanguageService);

  /** Which report table to show under the list. */
  readonly breakdown = input<'teacher' | 'student' | 'both' | 'none'>('none');
  readonly showStudent = input(true);
  readonly showTeacher = input(true);
  /** Show "Excuse" on upcoming one-to-one sessions (students, parents). */
  readonly canExcuse = input(false);
  /** Show the "needs a decision" tile (supervisors). */
  readonly showPending = input(false);
  /** Fires after an excuse so the parent page can refresh its own figures. */
  readonly changed = output<void>();

  protected readonly period = signal<Period>('week');
  private readonly anchor = signal(new Date());
  protected readonly sessions = signal<SessionDto[]>([]);
  protected readonly report = signal<SessionReportDto | null>(null);
  protected readonly canBeExcused = canBeExcused;
  protected readonly tables = computed(() => {
    const b = this.breakdown();
    return b === 'both' ? (['teacher', 'student'] as const) : b === 'none' ? [] : [b];
  });

  private readonly range = computed(() => {
    const a = this.anchor();
    if (this.period() === 'week') {
      const from = startOfWeek(a);
      return { from, to: addDays(from, 7) };
    }
    const from = new Date(a.getFullYear(), a.getMonth(), 1);
    return { from, to: new Date(a.getFullYear(), a.getMonth() + 1, 1) };
  });

  protected readonly rangeLabel = computed(() => {
    const { from, to } = this.range();
    const locale = this.language.language() === 'ar' ? 'ar-EG' : 'en-GB';
    return this.period() === 'month'
      ? from.toLocaleDateString(locale, { month: 'long', year: 'numeric' })
      : `${from.toLocaleDateString(locale, { day: 'numeric', month: 'short' })} – ${addDays(to, -1).toLocaleDateString(locale, { day: 'numeric', month: 'short' })}`;
  });

  protected readonly days = computed(() => {
    const byDay = new Map<string, SessionDto[]>();
    for (const s of this.sessions()) {
      const key = isoDate(parseUtc(s.startsAtUtc));
      byDay.set(key, [...(byDay.get(key) ?? []), s]);
    }
    return [...byDay.entries()]
      .sort(([a], [b]) => a.localeCompare(b))
      .map(([key, sessions]) => ({ key, date: new Date(`${key}T12:00:00`), sessions }));
  });

  constructor() {
    effect(() => {
      const { from, to } = this.range();
      this.load(from, to);
    });
  }

  protected setPeriod(p: Period): void {
    this.period.set(p);
  }

  protected move(step: number): void {
    const a = this.anchor();
    this.anchor.set(this.period() === 'week' ? addDays(a, 7 * step) : new Date(a.getFullYear(), a.getMonth() + step, 1));
  }

  protected reset(): void {
    this.anchor.set(new Date());
  }

  protected hours(minutes: number): string {
    return (minutes / 60).toFixed(minutes % 60 ? 1 : 0);
  }

  protected join(s: SessionDto): void {
    this.actions.join(s);
  }

  protected excuse(s: SessionDto): void {
    this.actions.excuse(s).subscribe((updated) => {
      this.sessions.update((list) => list.map((x) => (x.id === updated.id ? updated : x)));
      const { from, to } = this.range();
      this.loadReport(from, to);
      this.changed.emit();
    });
  }

  private load(from: Date, to: Date): void {
    this.api
      .get<SessionDto[]>(`${Api.academic}/sessions`, { fromUtc: from.toISOString(), toUtc: to.toISOString() })
      .subscribe((s) => this.sessions.set(s));
    this.loadReport(from, to);
  }

  private loadReport(from: Date, to: Date): void {
    this.api
      .get<SessionReportDto>(`${Api.academic}/me/session-report`, { fromUtc: from.toISOString(), toUtc: to.toISOString() })
      .subscribe((r) => this.report.set(r));
  }
}
