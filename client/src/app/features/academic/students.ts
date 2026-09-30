import { Component, inject, OnInit, signal } from '@angular/core';
import { PagedResult } from '../../core/api/api.models';
import { Api, ApiService } from '../../core/api/api.service';
import { CourseDto, ParentDto, StudentDto } from '../../core/api/models';
import { AuthService } from '../../core/auth/auth.service';
import { Permissions } from '../../core/auth/permissions';
import { Notifier } from '../../shared/notifier';
import { PAGE_IMPORTS } from '../../shared/page-imports';
import { TimeZoneField } from '../../shared/time-zone-field';

/** Student profiles: level, enrollment, status, the optional guardian and who pays (US-020). */
@Component({
  selector: 'app-students',
  imports: [PAGE_IMPORTS, TimeZoneField],
  template: `
    <div class="page-header"><h1>{{ 'nav.students' | translate }}</h1></div>
    <p class="muted">{{ 'students.hint' | translate }}</p>

    <div class="toolbar">
      <mat-form-field><mat-label>{{ 'common.search' | translate }}</mat-label><input matInput [(ngModel)]="search" (keyup.enter)="load()" /></mat-form-field>
      <mat-form-field>
        <mat-label>{{ 'students.subject' | translate }}</mat-label>
        <mat-select [(ngModel)]="courseId" (selectionChange)="load()">
          <mat-option [value]="null">{{ 'common.all' | translate }}</mat-option>
          @for (c of courses(); track c.id) { <mat-option [value]="c.id">{{ c.name }}</mat-option> }
        </mat-select>
      </mat-form-field>
      <mat-form-field>
        <mat-label>{{ 'common.status' | translate }}</mat-label>
        <mat-select [(ngModel)]="status" (selectionChange)="load()">
          <mat-option [value]="''">{{ 'common.all' | translate }}</mat-option>
          @for (s of statuses; track s) { <mat-option [value]="s">{{ 'status.' + s | translate }}</mat-option> }
        </mat-select>
      </mat-form-field>
    </div>

    <div class="table-wrap">
      <table class="data-table">
        <thead><tr><th>{{ 'common.fullName' | translate }}</th><th>{{ 'students.level' | translate }}</th><th>{{ 'students.subjects' | translate }}</th><th>{{ 'students.teachers' | translate }}</th><th>{{ 'students.parent' | translate }}</th><th>{{ 'students.payer' | translate }}</th><th>{{ 'students.enrolled' | translate }}</th><th>{{ 'common.status' | translate }}</th><th></th></tr></thead>
        <tbody>
          @for (s of students(); track s.userId) {
            <tr [class.selected]="editing()?.userId === s.userId">
              <td><a class="name" [routerLink]="['/students', s.userId]">{{ s.fullName }}</a><div class="muted ltr small">{{ s.email }}</div></td>
              <td>{{ s.level ?? '—' }}</td>
              <td>{{ s.subjects.join('، ') || '—' }}</td>
              <td>{{ teacherNames(s) || '—' }}</td>
              <td>{{ s.parentName ?? ('students.noGuardian' | translate) }}</td>
              <td>{{ s.effectivePayerUserId === s.userId ? ('students.payerSelf' | translate) : (s.payerName ?? '—') }}</td>
              <td>{{ s.enrollmentDate | date: 'mediumDate' }}</td>
              <td><span class="status" [class]="s.status">{{ 'status.' + s.status | translate }}</span></td>
              <td class="actions">
                <a mat-button [routerLink]="['/students', s.userId]"><mat-icon>visibility</mat-icon>{{ 'students.view' | translate }}</a>
                @if (canManage) { <button mat-button (click)="edit(s)">{{ 'common.edit' | translate }}</button> }
              </td>
            </tr>
          } @empty {
            <tr><td colspan="9" class="empty">{{ 'common.noData' | translate }}</td></tr>
          }
        </tbody>
      </table>
    </div>

    @if (editing(); as e) {
      <mat-card appearance="outlined" class="panel" style="margin-top: 16px">
        <mat-card-header><mat-card-title>{{ e.fullName }}</mat-card-title></mat-card-header>
        <mat-card-content>
          <div class="form-grid">
            <mat-form-field><mat-label>{{ 'students.level' | translate }}</mat-label><input matInput [(ngModel)]="e.level" /></mat-form-field>
            <mat-form-field><mat-label>{{ 'students.enrolled' | translate }}</mat-label><input matInput type="date" [(ngModel)]="e.enrollmentDate" /></mat-form-field>
            <mat-form-field>
              <mat-label>{{ 'common.status' | translate }}</mat-label>
              <mat-select [(ngModel)]="e.status">@for (s of statuses; track s) { <mat-option [value]="s">{{ 'status.' + s | translate }}</mat-option> }</mat-select>
            </mat-form-field>
            <mat-form-field>
              <mat-label>{{ 'students.parent' | translate }}</mat-label>
              <mat-select [(ngModel)]="e.parentUserId">
                <mat-option [value]="null">{{ 'students.noGuardian' | translate }}</mat-option>
                @for (p of parents(); track p.userId) { <mat-option [value]="p.userId">{{ p.fullName }}</mat-option> }
              </mat-select>
              <mat-hint>{{ 'students.guardianHint' | translate }}</mat-hint>
            </mat-form-field>
            <mat-form-field>
              <mat-label>{{ 'students.payer' | translate }}</mat-label>
              <mat-select [(ngModel)]="e.payerUserId">
                <mat-option [value]="null">{{ 'students.payerDefault' | translate }}</mat-option>
                <mat-option [value]="e.userId">{{ 'students.payerSelf' | translate }}</mat-option>
                @for (p of parents(); track p.userId) { <mat-option [value]="p.userId">{{ p.fullName }}</mat-option> }
              </mat-select>
              <mat-hint>{{ 'students.payerHint' | translate }}</mat-hint>
            </mat-form-field>
            <app-time-zone-field [(value)]="e.timeZone" [label]="'students.timeZone' | translate" [clearLabel]="'common.remove' | translate" />
          </div>
          <div class="form-actions">
            <button mat-button (click)="editing.set(null)">{{ 'common.cancel' | translate }}</button>
            <button mat-flat-button (click)="save(e)">{{ 'common.save' | translate }}</button>
          </div>
        </mat-card-content>
      </mat-card>
    }
  `,
  styles: `
    .small { font-size: 0.78rem; }
    .name { font-weight: 600; text-decoration: none; }
    .name:hover { text-decoration: underline; }
  `,
})
export class StudentsPage implements OnInit {
  private readonly api = inject(ApiService);
  private readonly notify = inject(Notifier);
  protected readonly canManage = inject(AuthService).hasPermission(Permissions.profiles.manage);

  protected readonly statuses = ['Active', 'Suspended', 'Graduated', 'Withdrawn'];
  protected readonly students = signal<StudentDto[]>([]);
  protected readonly courses = signal<CourseDto[]>([]);
  protected readonly parents = signal<ParentDto[]>([]);
  protected readonly editing = signal<StudentDto | null>(null);
  protected search = '';
  protected courseId: number | null = null;
  protected status = '';

  ngOnInit(): void {
    this.load();
    this.api.get<CourseDto[]>(`${Api.academic}/courses`).subscribe((c) => this.courses.set(c));
    if (this.canManage) {
      this.api.get<ParentDto[]>(`${Api.academic}/parents`).subscribe((p) => this.parents.set(p));
    }
  }

  protected load(): void {
    this.api
      .get<PagedResult<StudentDto>>(`${Api.academic}/students`, { search: this.search, courseId: this.courseId, status: this.status, pageSize: 100 })
      .subscribe((r) => this.students.set(r.items));
  }

  protected edit(s: StudentDto): void {
    this.editing.set({ ...s });
  }

  protected teacherNames(s: StudentDto): string {
    return s.teachers.map((t) => t.fullName).join('، ');
  }

  protected save(e: StudentDto): void {
    const body = { level: e.level || null, enrollmentDate: e.enrollmentDate, status: e.status, timeZone: e.timeZone };
    this.api.put(`${Api.academic}/students/${e.userId}`, body).subscribe({
      next: () =>
        this.api.put(`${Api.academic}/students/${e.userId}/parent`, { parentUserId: e.parentUserId }).subscribe({
          next: () =>
            this.api.put(`${Api.academic}/students/${e.userId}/payer`, { payerUserId: e.payerUserId }).subscribe({
              next: () => {
                this.notify.saved();
                this.editing.set(null);
                this.load();
              },
              error: (err) => this.notify.error(err),
            }),
          error: (err) => this.notify.error(err),
        }),
      error: (err) => this.notify.error(err),
    });
  }
}
