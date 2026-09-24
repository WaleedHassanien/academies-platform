import { Component, inject, OnInit, signal } from '@angular/core';
import { PagedResult } from '../../core/api/api.models';
import { Api, ApiService } from '../../core/api/api.service';
import { AssignmentDto, CertificateDto, CourseDto, GroupDto, StudentDto, SubmissionDto } from '../../core/api/models';
import { AuthService } from '../../core/auth/auth.service';
import { Permissions, Roles } from '../../core/auth/permissions';
import { Notifier } from '../../shared/notifier';
import { PAGE_IMPORTS } from '../../shared/page-imports';

interface AssignmentForm {
  id: number | null;
  courseId: number | null;
  groupId: number | null;
  title: string;
  description: string;
  due: string;
  maxScore: number;
}

/** Assignments, submissions and grades (US-036). Teachers create and grade; students submit. */
@Component({
  selector: 'app-assignments',
  imports: [PAGE_IMPORTS],
  template: `
    <div class="page-header">
      <h1>{{ 'nav.assignments' | translate }}</h1>
      @if (isTeacher) { <button mat-flat-button (click)="edit(null)"><mat-icon>add</mat-icon>{{ 'assignments.new' | translate }}</button> }
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
              <mat-select [(ngModel)]="f.groupId"><mat-option [value]="null">{{ 'assignments.wholeCourse' | translate }}</mat-option>@for (g of groups(); track g.id) { <mat-option [value]="g.id">{{ g.name }}</mat-option> }</mat-select>
            </mat-form-field>
            <mat-form-field><mat-label>{{ 'assignments.due' | translate }}</mat-label><input matInput type="datetime-local" [(ngModel)]="f.due" /></mat-form-field>
            <mat-form-field><mat-label>{{ 'assignments.maxScore' | translate }}</mat-label><input matInput type="number" [(ngModel)]="f.maxScore" /></mat-form-field>
            <mat-form-field><mat-label>{{ 'common.description' | translate }}</mat-label><textarea matInput [(ngModel)]="f.description"></textarea></mat-form-field>
          </div>
          <div class="form-actions">
            <button mat-button (click)="form.set(null)">{{ 'common.cancel' | translate }}</button>
            <button mat-flat-button (click)="save(f)" [disabled]="!f.title || !f.courseId || !f.due">{{ 'common.save' | translate }}</button>
          </div>
        </mat-card-content>
      </mat-card>
    }

    <div class="table-wrap">
      <table class="data-table">
        <thead><tr><th>{{ 'common.title' | translate }}</th><th>{{ 'nav.courses' | translate }}</th><th>{{ 'assignments.due' | translate }}</th><th>{{ isStudent ? ('assignments.myResult' | translate) : ('assignments.submissions' | translate) }}</th><th></th></tr></thead>
        <tbody>
          @for (a of assignments(); track a.id) {
            <tr [class.selected]="selected()?.id === a.id">
              <td><b>{{ a.title }}</b><div class="muted">{{ a.description }}</div></td>
              <td>{{ a.courseName }} @if (a.groupName) { · {{ a.groupName }} }</td>
              <td>{{ a.dueAtUtc | date: 'short' }}</td>
              <td>
                @if (isStudent) {
                  @if (a.mySubmission; as m) {
                    @if (m.score != null) { <b class="ltr">{{ m.score }} / {{ a.maxScore }}</b> } @else { <span class="status warn">{{ 'assignments.submitted' | translate }}</span> }
                  } @else { <span class="status">{{ 'assignments.notSubmitted' | translate }}</span> }
                } @else { {{ a.submissionCount }} }
              </td>
              <td class="actions">
                @if (isStudent) {
                  @if (!a.mySubmission?.gradedAtUtc) { <button mat-button (click)="openSubmit(a)">{{ 'assignments.submit' | translate }}</button> }
                } @else {
                  <button mat-button (click)="openSubmissions(a)">{{ 'assignments.submissions' | translate }}</button>
                  @if (isTeacher) { <button mat-button (click)="edit(a)">{{ 'common.edit' | translate }}</button> }
                }
              </td>
            </tr>
          } @empty { <tr><td colspan="5" class="empty">{{ 'common.noData' | translate }}</td></tr> }
        </tbody>
      </table>
    </div>

    @if (selected(); as a) {
      <mat-card appearance="outlined" class="panel" style="margin-top: 16px">
        <mat-card-header><mat-card-title>{{ a.title }}</mat-card-title></mat-card-header>
        <mat-card-content>
          @if (isStudent) {
            @if (a.mySubmission?.teacherFeedback) { <p><b>{{ 'assignments.feedback' | translate }}:</b> {{ a.mySubmission?.teacherFeedback }}</p> }
            <mat-form-field class="wide"><mat-label>{{ 'assignments.answer' | translate }}</mat-label><textarea matInput rows="5" [(ngModel)]="answer"></textarea></mat-form-field>
            <mat-form-field class="wide"><mat-label>{{ 'assignments.attachment' | translate }}</mat-label><input matInput [(ngModel)]="attachment" dir="ltr" /></mat-form-field>
            <div class="form-actions"><button mat-flat-button (click)="submit(a)">{{ 'assignments.submit' | translate }}</button></div>
          } @else {
            <table class="data-table">
              <thead><tr><th>{{ 'roles.Student' | translate }}</th><th>{{ 'assignments.answer' | translate }}</th><th>{{ 'assignments.submittedAt' | translate }}</th><th>{{ 'assignments.score' | translate }}</th><th>{{ 'assignments.feedback' | translate }}</th><th></th></tr></thead>
              <tbody>
                @for (s of submissions(); track s.id) {
                  <tr>
                    <td>{{ s.studentName }}</td>
                    <td>{{ s.content }} @if (s.attachmentUrl) { <a [href]="s.attachmentUrl" target="_blank" rel="noopener">🔗</a> }</td>
                    <td>{{ s.submittedAtUtc | date: 'short' }} @if (s.isLate) { <span class="status bad">{{ 'assignments.late' | translate }}</span> }</td>
                    <td><input class="cell" type="number" [(ngModel)]="scores[s.id]" [max]="a.maxScore" /> / {{ a.maxScore }}</td>
                    <td><input class="cell wide" [(ngModel)]="feedbacks[s.id]" /></td>
                    <td class="actions"><button mat-button (click)="grade(a, s)" [disabled]="scores[s.id] == null">{{ 'assignments.grade' | translate }}</button></td>
                  </tr>
                } @empty { <tr><td colspan="6" class="empty">{{ 'common.noData' | translate }}</td></tr> }
              </tbody>
            </table>
          }
        </mat-card-content>
      </mat-card>
    }
  `,
  styles: `
    .wide { width: 100%; }
    .cell { padding: 6px; border: 1px solid var(--mat-sys-outline-variant); border-radius: 6px; background: transparent; color: inherit; width: 70px; }
    .cell.wide { width: 220px; }
  `,
})
export class AssignmentsPage implements OnInit {
  private readonly api = inject(ApiService);
  private readonly notify = inject(Notifier);
  private readonly auth = inject(AuthService);

  protected readonly isStudent = this.auth.hasAnyRole(Roles.Student) && !this.auth.hasPermission(Permissions.courses.manage);
  protected readonly isTeacher = this.auth.hasAnyRole(Roles.Teacher) || this.auth.hasPermission(Permissions.courses.manage);

  protected readonly assignments = signal<AssignmentDto[]>([]);
  protected readonly courses = signal<CourseDto[]>([]);
  protected readonly groups = signal<GroupDto[]>([]);
  protected readonly form = signal<AssignmentForm | null>(null);
  protected readonly selected = signal<AssignmentDto | null>(null);
  protected readonly submissions = signal<SubmissionDto[]>([]);
  protected answer = '';
  protected attachment = '';
  protected scores: Record<number, number | null> = {};
  protected feedbacks: Record<number, string> = {};

  ngOnInit(): void {
    this.load();
    if (this.isTeacher) {
      this.api.get<CourseDto[]>(`${Api.academic}/courses`).subscribe((c) => this.courses.set(c));
      this.api.get<GroupDto[]>(`${Api.academic}/groups`).subscribe((g) => this.groups.set(g));
    }
  }

  protected edit(a: AssignmentDto | null): void {
    this.form.set(
      a
        ? { id: a.id, courseId: a.courseId, groupId: a.groupId, title: a.title, description: a.description ?? '', due: a.dueAtUtc.slice(0, 16), maxScore: a.maxScore }
        : { id: null, courseId: null, groupId: null, title: '', description: '', due: '', maxScore: 100 },
    );
  }

  protected save(f: AssignmentForm): void {
    const body = { courseId: f.courseId, groupId: f.groupId, title: f.title, description: f.description || null, dueAtUtc: new Date(f.due).toISOString(), maxScore: f.maxScore };
    const request = f.id ? this.api.put(`${Api.academic}/assignments/${f.id}`, body) : this.api.post(`${Api.academic}/assignments`, body);
    request.subscribe({ next: () => this.after(() => this.form.set(null)), error: (e) => this.notify.error(e) });
  }

  protected openSubmit(a: AssignmentDto): void {
    this.selected.set(a);
    this.answer = a.mySubmission?.content ?? '';
    this.attachment = a.mySubmission?.attachmentUrl ?? '';
  }

  protected submit(a: AssignmentDto): void {
    this.api.post(`${Api.academic}/assignments/${a.id}/submissions`, { content: this.answer || null, attachmentUrl: this.attachment || null }).subscribe({
      next: () => this.after(() => this.selected.set(null)),
      error: (e) => this.notify.error(e),
    });
  }

  protected openSubmissions(a: AssignmentDto): void {
    this.selected.set(a);
    this.api.get<SubmissionDto[]>(`${Api.academic}/assignments/${a.id}/submissions`).subscribe((s) => {
      this.submissions.set(s);
      this.scores = Object.fromEntries(s.map((x) => [x.id, x.score]));
      this.feedbacks = Object.fromEntries(s.map((x) => [x.id, x.teacherFeedback ?? '']));
    });
  }

  protected grade(a: AssignmentDto, s: SubmissionDto): void {
    this.api.put(`${Api.academic}/submissions/${s.id}/grade`, { score: this.scores[s.id], feedback: this.feedbacks[s.id] || null }).subscribe({
      next: () => {
        this.notify.saved();
        this.openSubmissions(a);
      },
      error: (e) => this.notify.error(e),
    });
  }

  private after(then: () => void): void {
    this.notify.saved();
    then();
    this.load();
  }

  private load(): void {
    this.api.get<AssignmentDto[]>(`${Api.academic}/assignments`).subscribe((a) => this.assignments.set(a));
  }
}

/** Course certificates as PDF (US-041). */
@Component({
  selector: 'app-certificates',
  imports: [PAGE_IMPORTS],
  template: `
    <div class="page-header"><h1>{{ 'nav.certificates' | translate }}</h1></div>
    @if (canIssue) {
      <mat-card appearance="outlined" class="panel">
        <mat-card-content>
          <div class="toolbar">
            <mat-form-field>
              <mat-label>{{ 'roles.Student' | translate }}</mat-label>
              <mat-select [(ngModel)]="studentId">@for (s of students(); track s.userId) { <mat-option [value]="s.userId">{{ s.fullName }}</mat-option> }</mat-select>
            </mat-form-field>
            <mat-form-field>
              <mat-label>{{ 'nav.courses' | translate }}</mat-label>
              <mat-select [(ngModel)]="courseId">@for (c of courses(); track c.id) { <mat-option [value]="c.id">{{ c.name }}</mat-option> }</mat-select>
            </mat-form-field>
            <button mat-flat-button (click)="issue()" [disabled]="!studentId || !courseId">{{ 'certificates.issue' | translate }}</button>
          </div>
        </mat-card-content>
      </mat-card>
    }
    <div class="table-wrap">
      <table class="data-table">
        <thead><tr><th>{{ 'certificates.number' | translate }}</th><th>{{ 'roles.Student' | translate }}</th><th>{{ 'nav.courses' | translate }}</th><th>{{ 'certificates.issued' | translate }}</th><th></th></tr></thead>
        <tbody>
          @for (c of certificates(); track c.id) {
            <tr>
              <td class="ltr">{{ c.number }}</td><td>{{ c.studentName }}</td><td>{{ c.courseName }}</td><td>{{ c.issuedOnUtc | date: 'mediumDate' }}</td>
              <td class="actions"><button mat-button (click)="download(c)"><mat-icon>download</mat-icon>PDF</button></td>
            </tr>
          } @empty { <tr><td colspan="5" class="empty">{{ 'common.noData' | translate }}</td></tr> }
        </tbody>
      </table>
    </div>
  `,
})
export class CertificatesPage implements OnInit {
  private readonly api = inject(ApiService);
  private readonly notify = inject(Notifier);
  private readonly auth = inject(AuthService);

  protected readonly canIssue = this.auth.hasPermission(Permissions.courses.manage) || this.auth.hasAnyRole(Roles.Teacher);
  protected readonly certificates = signal<CertificateDto[]>([]);
  protected readonly students = signal<StudentDto[]>([]);
  protected readonly courses = signal<CourseDto[]>([]);
  protected studentId: number | null = null;
  protected courseId: number | null = null;

  ngOnInit(): void {
    this.load();
    if (this.canIssue) {
      this.api.get<PagedResult<StudentDto>>(`${Api.academic}/students`, { pageSize: 100 }).subscribe((r) => this.students.set(r.items));
      this.api.get<CourseDto[]>(`${Api.academic}/courses`).subscribe((c) => this.courses.set(c));
    }
  }

  protected issue(): void {
    this.api.post(`${Api.academic}/certificates`, { studentUserId: this.studentId, courseId: this.courseId }).subscribe({
      next: () => {
        this.notify.saved();
        this.load();
      },
      error: (e) => this.notify.error(e),
    });
  }

  protected download(c: CertificateDto): void {
    this.api.download(`${Api.academic}/certificates/${c.id}/pdf`, `${c.number}.pdf`);
  }

  private load(): void {
    this.api.get<CertificateDto[]>(`${Api.academic}/certificates`).subscribe((c) => this.certificates.set(c));
  }
}
