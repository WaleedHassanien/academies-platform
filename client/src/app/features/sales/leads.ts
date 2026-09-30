import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { TranslateService } from '@ngx-translate/core';
import { Observable, of, switchMap } from 'rxjs';
import { PagedResult } from '../../core/api/api.models';
import { Api, ApiService } from '../../core/api/api.service';
import { CourseDto, JoinLinkDto, LeadActivityDto, LeadDto, LeadStatus, LeadSummaryDto, StaffProfileDto, UserDto } from '../../core/api/models';
import { AuthService } from '../../core/auth/auth.service';
import { Permissions } from '../../core/auth/permissions';
import { isoDate, Notifier } from '../../shared/notifier';
import { PAGE_IMPORTS } from '../../shared/page-imports';
import { TimeZoneField } from '../../shared/time-zone-field';
import { parseUtc } from '../../shared/time-zones';
import { Stat } from '../../shared/ui';

interface LeadForm {
  id: number | null;
  fullName: string;
  phone: string;
  email: string;
  country: string;
  timeZone: string | null;
  isAdult: boolean;
  guardianName: string;
  courseId: number | null;
  source: string;
  nextFollowUpOn: string;
  notes: string;
}

interface TrialForm {
  teacherUserId: number | null;
  courseId: number | null;
  date: string;
  time: string;
  durationMinutes: number;
}

/** New accounts (or an existing parent) for converting a lead into a student. */
interface ConvertForm {
  studentName: string;
  studentEmail: string;
  studentPhone: string;
  password: string;
  parentMode: 'none' | 'new' | 'existing';
  parentName: string;
  parentEmail: string;
  parentPhone: string;
  parentSearch: string;
  existingParentId: number | null;
  /** Who pays: the guardian (default) or the student. */
  studentPays: boolean;
}

const STATUSES: LeadStatus[] = ['New', 'Contacted', 'TrialScheduled', 'TrialDone', 'Converted', 'Lost'];
const SOURCES = ['Website', 'WhatsApp', 'Facebook', 'Instagram', 'Referral', 'Returning', 'Other'];

/**
 * Sales and customer service: prospective students (leads), their follow-up notes, a free trial
 * session with a teacher, and conversion into a student account (with a guardian when the student
 * is a child), or closing the lead with the reason it was lost.
 */
@Component({
  selector: 'app-leads',
  imports: [PAGE_IMPORTS, Stat, TimeZoneField],
  template: `
    <div class="page-header">
      <h1>{{ 'nav.leads' | translate }}</h1>
      @if (canManage) { <button mat-flat-button (click)="edit(null)"><mat-icon>person_add</mat-icon>{{ 'leads.new' | translate }}</button> }
    </div>

    @if (summary(); as s) {
      <div class="stats">
        <app-stat icon="groups" [label]="'leads.total' | translate" [value]="s.total" />
        <app-stat icon="fiber_new" [label]="'leadStatus.New' | translate" [value]="count(s, 'New')" class="clickable" (click)="filterBy('New')" />
        <app-stat icon="event" [label]="'leadStatus.TrialScheduled' | translate" [value]="count(s, 'TrialScheduled')" class="clickable" (click)="filterBy('TrialScheduled')" />
        <app-stat icon="how_to_reg" [label]="'leadStatus.Converted' | translate" [value]="count(s, 'Converted')" class="clickable" (click)="filterBy('Converted')" />
        <app-stat icon="percent" [label]="'leads.conversion' | translate" [value]="s.conversionRate + '%'" [hint]="'leads.conversionHint' | translate" />
      </div>
    }

    @if (form(); as f) {
      <mat-card appearance="outlined" class="panel">
        <mat-card-header><mat-card-title>{{ (f.id ? 'leads.edit' : 'leads.new') | translate }}</mat-card-title></mat-card-header>
        <mat-card-content>
          <div class="form-grid">
            <mat-form-field><mat-label>{{ 'leads.studentName' | translate }}</mat-label><input matInput [(ngModel)]="f.fullName" /></mat-form-field>
            <mat-slide-toggle [(ngModel)]="f.isAdult">{{ 'leads.isAdult' | translate }}</mat-slide-toggle>
            @if (!f.isAdult) {
              <mat-form-field><mat-label>{{ 'leads.guardianName' | translate }}</mat-label><input matInput [(ngModel)]="f.guardianName" /></mat-form-field>
            }
            <mat-form-field><mat-label>{{ 'common.phone' | translate }}</mat-label><input matInput [(ngModel)]="f.phone" dir="ltr" /></mat-form-field>
            <mat-form-field><mat-label>{{ 'common.email' | translate }}</mat-label><input matInput type="email" [(ngModel)]="f.email" dir="ltr" /></mat-form-field>
            <mat-form-field><mat-label>{{ 'leads.country' | translate }}</mat-label><input matInput [(ngModel)]="f.country" /></mat-form-field>
            <app-time-zone-field [(value)]="f.timeZone" [label]="'students.timeZone' | translate" [clearLabel]="'common.remove' | translate" />
            <mat-form-field>
              <mat-label>{{ 'students.subject' | translate }}</mat-label>
              <mat-select [(ngModel)]="f.courseId"><mat-option [value]="null">—</mat-option>@for (c of courses(); track c.id) { <mat-option [value]="c.id">{{ c.name }}</mat-option> }</mat-select>
            </mat-form-field>
            <mat-form-field>
              <mat-label>{{ 'leads.source' | translate }}</mat-label>
              <mat-select [(ngModel)]="f.source"><mat-option value="">—</mat-option>@for (s of sources; track s) { <mat-option [value]="s">{{ 'leadSource.' + s | translate }}</mat-option> }</mat-select>
            </mat-form-field>
            <mat-form-field><mat-label>{{ 'leads.followUp' | translate }}</mat-label><input matInput type="date" [(ngModel)]="f.nextFollowUpOn" /></mat-form-field>
            <mat-form-field class="span-2"><mat-label>{{ 'common.notes' | translate }}</mat-label><textarea matInput rows="2" [(ngModel)]="f.notes"></textarea></mat-form-field>
          </div>
          <div class="form-actions">
            <button mat-button (click)="form.set(null)">{{ 'common.cancel' | translate }}</button>
            <button mat-flat-button (click)="save(f)" [disabled]="!f.fullName.trim() || (!f.phone.trim() && !f.email.trim())">{{ 'common.save' | translate }}</button>
          </div>
        </mat-card-content>
      </mat-card>
    }

    <div class="toolbar">
      <mat-form-field><mat-label>{{ 'common.search' | translate }}</mat-label><input matInput [(ngModel)]="search" (keyup.enter)="load()" /></mat-form-field>
      <mat-form-field>
        <mat-label>{{ 'common.status' | translate }}</mat-label>
        <mat-select [(ngModel)]="status" (selectionChange)="load()">
          <mat-option value="">{{ 'common.all' | translate }}</mat-option>
          @for (s of statuses; track s) { <mat-option [value]="s">{{ 'leadStatus.' + s | translate }}</mat-option> }
        </mat-select>
      </mat-form-field>
    </div>

    <div class="table-wrap">
      <table class="data-table">
        <thead>
          <tr>
            <th>{{ 'common.fullName' | translate }}</th><th>{{ 'leads.contact' | translate }}</th><th>{{ 'students.subject' | translate }}</th>
            <th>{{ 'common.status' | translate }}</th><th>{{ 'leads.trial' | translate }}</th><th>{{ 'leads.followUp' | translate }}</th><th></th>
          </tr>
        </thead>
        <tbody>
          @for (l of leads(); track l.id) {
            <tr [class.selected]="selected()?.id === l.id">
              <td><b>{{ l.fullName }}</b>@if (!l.isAdult && l.guardianName) { <div class="muted">{{ 'leads.guardian' | translate }}: {{ l.guardianName }}</div> }</td>
              <td class="ltr"><div>{{ l.phone }}</div><div class="muted">{{ l.email }}</div></td>
              <td>{{ l.courseName ?? '—' }}</td>
              <td><span class="status" [class]="'lead-' + l.status">{{ 'leadStatus.' + l.status | translate }}</span>@if (l.lostReason) { <div class="muted small">{{ l.lostReason }}</div> }</td>
              <td>
                @if (l.trial; as t) {
                  <span class="ltr">{{ t.startsAtUtc | utcDate: 'd MMM, HH:mm' }}</span>
                  <div class="muted small">{{ t.teacherName }} · {{ 'trials.status_' + t.status | translate }}</div>
                } @else { — }
              </td>
              <td [class.overdue]="isDue(l)">{{ l.nextFollowUpOn ? (l.nextFollowUpOn | date: 'mediumDate') : '—' }}</td>
              <td class="actions"><button mat-button (click)="open(l)">{{ 'leads.open' | translate }}</button></td>
            </tr>
          } @empty { <tr><td colspan="7" class="empty">{{ 'common.noData' | translate }}</td></tr> }
        </tbody>
      </table>
    </div>

    @if (selected(); as l) {
      <div class="grid detail">
        <mat-card appearance="outlined">
          <mat-card-header>
            <mat-card-title>{{ l.fullName }}</mat-card-title>
            <mat-card-subtitle>{{ 'leadStatus.' + l.status | translate }} · {{ l.assignedToName ?? '—' }}</mat-card-subtitle>
          </mat-card-header>
          <mat-card-content>
            <div class="facts">
              @if (l.phone) { <a class="fact ltr" [href]="'https://wa.me/' + digits(l.phone)" target="_blank" rel="noopener"><mat-icon>chat</mat-icon>{{ l.phone }}</a> }
              @if (l.email) { <a class="fact ltr" [href]="'mailto:' + l.email"><mat-icon>mail</mat-icon>{{ l.email }}</a> }
              @if (l.country) { <span class="fact"><mat-icon>public</mat-icon>{{ l.country }}</span> }
              @if (l.timeZone) { <span class="fact ltr"><mat-icon>schedule</mat-icon>{{ l.timeZone }}</span> }
              @if (l.source) { <span class="fact"><mat-icon>campaign</mat-icon>{{ 'leadSource.' + l.source | translate }}</span> }
            </div>
            @if (l.notes) { <p>{{ l.notes }}</p> }
            @if (l.convertedStudentUserId) {
              <a mat-stroked-button [routerLink]="['/students', l.convertedStudentUserId]"><mat-icon>school</mat-icon>{{ l.convertedStudentName ?? ('leads.studentPage' | translate) }}</a>
            }
            @if (canManage && l.status !== 'Converted') {
              <div class="toolbar actions-row">
                <button mat-button (click)="edit(l)"><mat-icon>edit</mat-icon>{{ 'common.edit' | translate }}</button>
                @if (l.status !== 'Lost') {
                  <button mat-button (click)="startTrial(l)"><mat-icon>event</mat-icon>{{ 'leads.scheduleTrial' | translate }}</button>
                  <button mat-flat-button (click)="startConvert(l)"><mat-icon>how_to_reg</mat-icon>{{ 'leads.convert' | translate }}</button>
                  <button mat-button (click)="lose(l)"><mat-icon>block</mat-icon>{{ 'leads.markLost' | translate }}</button>
                } @else {
                  <button mat-button (click)="setStatus(l, 'Contacted')"><mat-icon>replay</mat-icon>{{ 'leads.reopen' | translate }}</button>
                }
              </div>
            }

            @if (l.trial; as t) {
              <div class="trial-box">
                <b>{{ 'leads.trial' | translate }}</b>
                <div class="ltr">{{ t.startsAtUtc | utcDate: 'EEEE d MMM, HH:mm' }} ({{ 'students.egyptTime' | translate }})</div>
                <div class="muted">{{ t.teacherName }} · {{ t.courseName ?? '—' }} · {{ 'trials.status_' + t.status | translate }}</div>
                @if (t.notes) { <p class="muted">{{ 'trials.assessment' | translate }}: {{ t.notes }}</p> }
                @if (t.status === 'Scheduled' && canManage) {
                  <div class="toolbar">
                    <button mat-stroked-button (click)="copyTrialLink(l)"><mat-icon>link</mat-icon>{{ 'leads.copyTrialLink' | translate }}</button>
                    <button mat-button (click)="trialOutcome(l, 'Cancelled')">{{ 'leads.cancelTrial' | translate }}</button>
                  </div>
                }
              </div>
            }

            @if (trialForm(); as t) {
              <div class="form-grid sub-form">
                <mat-form-field>
                  <mat-label>{{ 'students.subject' | translate }}</mat-label>
                  <mat-select [(ngModel)]="t.courseId"><mat-option [value]="null">—</mat-option>@for (c of courses(); track c.id) { <mat-option [value]="c.id">{{ c.name }}</mat-option> }</mat-select>
                </mat-form-field>
                <mat-form-field>
                  <mat-label>{{ 'roles.Teacher' | translate }}</mat-label>
                  <mat-select [(ngModel)]="t.teacherUserId">@for (x of teachersFor(t.courseId); track x.userId) { <mat-option [value]="x.userId">{{ x.fullName }}</mat-option> }</mat-select>
                </mat-form-field>
                <mat-form-field><mat-label>{{ 'common.date' | translate }}</mat-label><input matInput type="date" [(ngModel)]="t.date" /></mat-form-field>
                <mat-form-field><mat-label>{{ 'common.time' | translate }}</mat-label><input matInput type="time" [(ngModel)]="t.time" /><mat-hint>{{ 'leads.yourTime' | translate }}</mat-hint></mat-form-field>
                <mat-form-field><mat-label>{{ 'sessions.duration' | translate }}</mat-label><input matInput type="number" min="15" max="120" [(ngModel)]="t.durationMinutes" /></mat-form-field>
              </div>
              <div class="form-actions">
                <button mat-button (click)="trialForm.set(null)">{{ 'common.cancel' | translate }}</button>
                <button mat-flat-button (click)="scheduleTrial(l, t)" [disabled]="!t.teacherUserId || !t.date || !t.time">{{ 'leads.scheduleTrial' | translate }}</button>
              </div>
            }

            @if (convertForm(); as c) {
              <div class="sub-form">
                <h4>{{ 'leads.convertTitle' | translate }}</h4>
                <p class="muted">{{ 'leads.convertHint' | translate }}</p>
                <div class="form-grid">
                  <mat-form-field><mat-label>{{ 'leads.studentName' | translate }}</mat-label><input matInput [(ngModel)]="c.studentName" /></mat-form-field>
                  <mat-form-field><mat-label>{{ 'leads.studentEmail' | translate }}</mat-label><input matInput type="email" [(ngModel)]="c.studentEmail" dir="ltr" /></mat-form-field>
                  <mat-form-field><mat-label>{{ 'common.phone' | translate }}</mat-label><input matInput [(ngModel)]="c.studentPhone" dir="ltr" /></mat-form-field>
                  <mat-form-field><mat-label>{{ 'leads.password' | translate }}</mat-label><input matInput [(ngModel)]="c.password" dir="ltr" /><mat-hint>{{ 'leads.passwordHint' | translate }}</mat-hint></mat-form-field>
                </div>
                <mat-button-toggle-group [(ngModel)]="c.parentMode" class="parent-mode">
                  <mat-button-toggle value="none">{{ 'leads.noGuardian' | translate }}</mat-button-toggle>
                  <mat-button-toggle value="new">{{ 'leads.newGuardian' | translate }}</mat-button-toggle>
                  <mat-button-toggle value="existing">{{ 'leads.existingGuardian' | translate }}</mat-button-toggle>
                </mat-button-toggle-group>
                @if (c.parentMode === 'new') {
                  <div class="form-grid">
                    <mat-form-field><mat-label>{{ 'leads.guardianName' | translate }}</mat-label><input matInput [(ngModel)]="c.parentName" /></mat-form-field>
                    <mat-form-field><mat-label>{{ 'leads.guardianEmail' | translate }}</mat-label><input matInput type="email" [(ngModel)]="c.parentEmail" dir="ltr" /></mat-form-field>
                    <mat-form-field><mat-label>{{ 'common.phone' | translate }}</mat-label><input matInput [(ngModel)]="c.parentPhone" dir="ltr" /></mat-form-field>
                  </div>
                } @else if (c.parentMode === 'existing') {
                  <div class="toolbar">
                    <mat-form-field><mat-label>{{ 'common.search' | translate }}</mat-label><input matInput [(ngModel)]="c.parentSearch" (keyup.enter)="findParents(c.parentSearch)" /></mat-form-field>
                    <button mat-stroked-button (click)="findParents(c.parentSearch)"><mat-icon>search</mat-icon></button>
                    <mat-form-field>
                      <mat-label>{{ 'students.parent' | translate }}</mat-label>
                      <mat-select [(ngModel)]="c.existingParentId">@for (p of parentMatches(); track p.id) { <mat-option [value]="p.id">{{ p.fullName }} · {{ p.email }}</mat-option> }</mat-select>
                    </mat-form-field>
                  </div>
                }
                @if (c.parentMode !== 'none') {
                  <mat-checkbox [(ngModel)]="c.studentPays">{{ 'leads.studentPays' | translate }}</mat-checkbox>
                }
                <div class="form-actions">
                  <button mat-button (click)="convertForm.set(null)">{{ 'common.cancel' | translate }}</button>
                  <button mat-flat-button (click)="convert(l, c)" [disabled]="busy() || !convertValid(c)">{{ 'leads.convert' | translate }}</button>
                </div>
              </div>
            }
          </mat-card-content>
        </mat-card>

        <mat-card appearance="outlined">
          <mat-card-header><mat-card-title>{{ 'leads.activity' | translate }}</mat-card-title></mat-card-header>
          <mat-card-content>
            @if (canManage) {
              <div class="toolbar">
                <mat-form-field class="grow"><mat-label>{{ 'leads.addNote' | translate }}</mat-label><input matInput [(ngModel)]="note" (keyup.enter)="addNote(l)" /></mat-form-field>
                <button mat-flat-button (click)="addNote(l)" [disabled]="!note.trim()">{{ 'common.add' | translate }}</button>
              </div>
            }
            @for (a of activity(); track a.id) {
              <div class="note">
                <div>{{ a.note }}</div>
                <small class="muted">{{ a.byName ?? '—' }} · {{ a.createdOnUtc | utcDate: 'd MMM, HH:mm' }}</small>
              </div>
            } @empty { <p class="muted">{{ 'common.noData' | translate }}</p> }
          </mat-card-content>
        </mat-card>
      </div>
    }
  `,
  styles: `
    .clickable { cursor: pointer; }
    .small { font-size: 0.78rem; }
    .overdue { color: var(--warn-fg); font-weight: 600; }
    .detail { margin-top: 16px; align-items: start; }
    .facts { display: flex; flex-wrap: wrap; gap: 8px; margin-bottom: 12px; }
    .fact { display: inline-flex; align-items: center; gap: 6px; padding: 3px 10px; border-radius: 999px; font-size: 0.8rem; text-decoration: none;
      color: inherit; background: var(--app-surface-2); border: 1px solid var(--app-border); }
    .fact mat-icon { font-size: 16px; width: 16px; height: 16px; color: var(--app-muted); }
    .actions-row { margin-top: 12px; flex-wrap: wrap; }
    .trial-box { margin-top: 12px; padding: 12px; border-radius: 12px; background: var(--app-surface-2); border: 1px solid var(--app-border); }
    .sub-form { margin-top: 16px; padding-top: 12px; border-top: 1px dashed var(--app-border); }
    .sub-form h4 { margin: 0 0 4px; }
    .parent-mode { margin: 4px 0 12px; }
    .note { padding: 8px 0; border-bottom: 1px dashed var(--app-border); }
    .grow { flex: 1; }
    .span-2 { grid-column: span 2; }
    .status.lead-Converted { background: var(--ok-bg); color: var(--ok-fg); }
    .status.lead-Lost { background: var(--bad-bg, #fee2e2); color: var(--bad-fg, #b91c1c); }
    .status.lead-TrialScheduled, .status.lead-TrialDone { background: var(--info-bg, #e0f2fe); color: var(--info-fg, #0369a1); }
    @media (max-width: 640px) { .span-2 { grid-column: auto; } }
  `,
})
export class LeadsPage implements OnInit {
  private readonly api = inject(ApiService);
  private readonly notify = inject(Notifier);
  private readonly translate = inject(TranslateService);
  protected readonly canManage = inject(AuthService).hasPermission(Permissions.leads.manage);

  protected readonly statuses = STATUSES;
  protected readonly sources = SOURCES;
  protected readonly leads = signal<LeadDto[]>([]);
  protected readonly summary = signal<LeadSummaryDto | null>(null);
  protected readonly courses = signal<CourseDto[]>([]);
  protected readonly teachers = signal<StaffProfileDto[]>([]);
  protected readonly selected = signal<LeadDto | null>(null);
  protected readonly activity = signal<LeadActivityDto[]>([]);
  protected readonly form = signal<LeadForm | null>(null);
  protected readonly trialForm = signal<TrialForm | null>(null);
  protected readonly convertForm = signal<ConvertForm | null>(null);
  protected readonly parentMatches = signal<UserDto[]>([]);
  protected readonly busy = signal(false);
  protected readonly today = computed(() => isoDate(new Date()));
  protected search = '';
  protected status = '';
  protected note = '';

  ngOnInit(): void {
    this.load();
    this.api.get<CourseDto[]>(`${Api.academic}/courses`).subscribe((c) => this.courses.set(c));
    this.api.get<StaffProfileDto[]>(`${Api.academic}/teachers`).subscribe({ next: (t) => this.teachers.set(t), error: () => this.teachers.set([]) });
  }

  protected load(): void {
    this.api
      .get<PagedResult<LeadDto>>(`${Api.academic}/leads`, { search: this.search, status: this.status, pageSize: 100 })
      .subscribe((r) => this.leads.set(r.items));
    this.api.get<LeadSummaryDto>(`${Api.academic}/leads/summary`).subscribe((s) => this.summary.set(s));
  }

  protected count(s: LeadSummaryDto, status: LeadStatus): number {
    return s.byStatus[status] ?? 0;
  }

  protected filterBy(status: LeadStatus): void {
    this.status = status;
    this.load();
  }

  protected isDue(l: LeadDto): boolean {
    return !!l.nextFollowUpOn && l.nextFollowUpOn <= this.today() && l.status !== 'Converted' && l.status !== 'Lost';
  }

  protected digits(phone: string): string {
    return phone.replace(/\D/g, '');
  }

  protected teachersFor(courseId: number | null): StaffProfileDto[] {
    return this.teachers().filter((t) => !courseId || !t.courseIds?.length || t.courseIds.includes(courseId));
  }

  protected open(l: LeadDto): void {
    this.selected.set(l);
    this.trialForm.set(null);
    this.convertForm.set(null);
    this.api.get<LeadActivityDto[]>(`${Api.academic}/leads/${l.id}/activity`).subscribe((a) => this.activity.set(a));
  }

  protected edit(l: LeadDto | null): void {
    this.form.set(
      l
        ? {
            id: l.id, fullName: l.fullName, phone: l.phone ?? '', email: l.email ?? '', country: l.country ?? '', timeZone: l.timeZone, isAdult: l.isAdult,
            guardianName: l.guardianName ?? '', courseId: l.courseId, source: l.source ?? '', nextFollowUpOn: l.nextFollowUpOn ?? '', notes: l.notes ?? '',
          }
        : {
            id: null, fullName: '', phone: '', email: '', country: '', timeZone: null, isAdult: true, guardianName: '', courseId: null, source: '',
            nextFollowUpOn: '', notes: '',
          },
    );
  }

  protected save(f: LeadForm): void {
    const body = {
      fullName: f.fullName, phone: f.phone || null, email: f.email || null, country: f.country || null, timeZone: f.timeZone, isAdult: f.isAdult,
      guardianName: f.isAdult ? null : f.guardianName || null, courseId: f.courseId, source: f.source || null, assignedToUserId: null,
      nextFollowUpOn: f.nextFollowUpOn || null, notes: f.notes || null,
    };
    const request = f.id ? this.api.put<LeadDto>(`${Api.academic}/leads/${f.id}`, body) : this.api.post<LeadDto>(`${Api.academic}/leads`, body);
    request.subscribe({
      next: (l) => {
        this.notify.saved();
        this.form.set(null);
        this.refresh(l);
      },
      error: (e) => this.notify.error(e),
    });
  }

  protected setStatus(l: LeadDto, status: LeadStatus, lostReason: string | null = null): void {
    this.api.put<LeadDto>(`${Api.academic}/leads/${l.id}/status`, { status, lostReason }).subscribe({
      next: (updated) => this.refresh(updated),
      error: (e) => this.notify.error(e),
    });
  }

  protected lose(l: LeadDto): void {
    const reason = prompt(this.translate.instant('leads.lostReason'));
    if (reason?.trim()) {
      this.setStatus(l, 'Lost', reason.trim());
    }
  }

  protected addNote(l: LeadDto): void {
    if (!this.note.trim()) {
      return;
    }
    this.api.post<LeadActivityDto>(`${Api.academic}/leads/${l.id}/activity`, { note: this.note.trim() }).subscribe({
      next: () => {
        this.note = '';
        this.api.get<LeadDto>(`${Api.academic}/leads/${l.id}`).subscribe((updated) => this.refresh(updated));
      },
      error: (e) => this.notify.error(e),
    });
  }

  protected startTrial(l: LeadDto): void {
    this.convertForm.set(null);
    this.trialForm.set({ teacherUserId: l.trial?.teacherUserId ?? null, courseId: l.courseId, date: isoDate(new Date()), time: '16:00', durationMinutes: 30 });
  }

  protected scheduleTrial(l: LeadDto, t: TrialForm): void {
    const body = { teacherUserId: t.teacherUserId, courseId: t.courseId, startsAtUtc: new Date(`${t.date}T${t.time}`).toISOString(), durationMinutes: t.durationMinutes };
    this.api.post<LeadDto>(`${Api.academic}/leads/${l.id}/trial`, body).subscribe({
      next: (updated) => {
        this.notify.saved();
        this.trialForm.set(null);
        this.refresh(updated);
      },
      error: (e) => this.notify.error(e),
    });
  }

  protected trialOutcome(l: LeadDto, status: 'Attended' | 'NoShow' | 'Cancelled'): void {
    this.api.put<LeadDto>(`${Api.academic}/leads/${l.id}/trial/outcome`, { status, notes: null }).subscribe({
      next: (updated) => this.refresh(updated),
      error: (e) => this.notify.error(e),
    });
  }

  /** A guest link into the teacher's room for the family, to send on WhatsApp. */
  protected copyTrialLink(l: LeadDto): void {
    this.api.get<JoinLinkDto>(`${Api.academic}/leads/${l.id}/trial/join-link`).subscribe({
      next: (link) =>
        navigator.clipboard.writeText(link.url).then(
          () => this.notify.info('leads.linkCopied', { until: parseUtc(link.expiresAtUtc).toLocaleString() }),
          () => prompt('', link.url),
        ),
      error: (e) => this.notify.error(e),
    });
  }

  protected startConvert(l: LeadDto): void {
    this.trialForm.set(null);
    this.convertForm.set({
      studentName: l.fullName, studentEmail: l.isAdult ? (l.email ?? '') : '', studentPhone: l.isAdult ? (l.phone ?? '') : '', password: tempPassword(),
      parentMode: l.isAdult ? 'none' : 'new', parentName: l.guardianName ?? '', parentEmail: l.isAdult ? '' : (l.email ?? ''),
      parentPhone: l.isAdult ? '' : (l.phone ?? ''), parentSearch: '', existingParentId: null, studentPays: false,
    });
  }

  protected findParents(search: string): void {
    this.api
      .get<PagedResult<UserDto>>(`${Api.identity}/users/accounts`, { search, role: 'Parent' })
      .subscribe({ next: (r) => this.parentMatches.set(r.items), error: (e) => this.notify.error(e) });
  }

  protected convertValid(c: ConvertForm): boolean {
    const student = !!c.studentName.trim() && !!c.studentEmail.trim() && c.password.length >= 8;
    const parent = c.parentMode === 'none' || (c.parentMode === 'new' ? !!c.parentName.trim() && !!c.parentEmail.trim() : !!c.existingParentId);
    return student && parent;
  }

  /** Creates the accounts in Identity (guardian first), then links them to the lead in Academic. */
  protected convert(l: LeadDto, c: ConvertForm): void {
    this.busy.set(true);
    const account = (fullName: string, email: string, phone: string, role: string) =>
      this.api.post<UserDto>(`${Api.identity}/users/accounts`, { fullName, email, phoneNumber: phone || null, password: c.password, roles: [role] });

    const parent$: Observable<number | null> =
      c.parentMode === 'new'
        ? account(c.parentName, c.parentEmail, c.parentPhone, 'Parent').pipe(switchMap((p) => of(p.id)))
        : of(c.parentMode === 'existing' ? c.existingParentId : null);

    parent$
      .pipe(
        switchMap((parentId) =>
          account(c.studentName, c.studentEmail, c.studentPhone, 'Student').pipe(
            switchMap((student) =>
              this.api.post<LeadDto>(`${Api.academic}/leads/${l.id}/convert`, {
                studentUserId: student.id, parentUserId: parentId, payerUserId: c.studentPays && parentId ? student.id : null,
              }),
            ),
          ),
        ),
      )
      .subscribe({
        next: (updated) => {
          this.busy.set(false);
          this.convertForm.set(null);
          alert(this.notify.text('leads.converted', { email: c.studentEmail, password: c.password }));
          this.refresh(updated);
        },
        error: (e) => {
          this.busy.set(false);
          this.notify.error(e);
        },
      });
  }

  private refresh(l: LeadDto): void {
    this.load();
    this.open(l);
  }
}

/** A first password the family changes later: upper, lower and digits, as Identity requires. */
function tempPassword(): string {
  const pick = (chars: string, n: number) => Array.from(crypto.getRandomValues(new Uint32Array(n)), (x) => chars[x % chars.length]).join('');
  return pick('ABCDEFGHJKLMNPQRSTUVWXYZ', 2) + pick('abcdefghijkmnopqrstuvwxyz', 4) + pick('23456789', 4);
}
