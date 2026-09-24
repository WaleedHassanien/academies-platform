import { Component, inject, OnInit, signal } from '@angular/core';
import { PagedResult } from '../../core/api/api.models';
import { Api, ApiService } from '../../core/api/api.service';
import { ParentDto, PersonRef, StaffProfileDto, StudentDto, WorkDayDto } from '../../core/api/models';
import { AuthService } from '../../core/auth/auth.service';
import { Permissions } from '../../core/auth/permissions';
import { Notifier } from '../../shared/notifier';
import { PAGE_IMPORTS } from '../../shared/page-imports';

/**
 * Teachers, supervisors and parents (US-020). Covers supervisor shifts (US-021),
 * supervisor→teacher links (US-022) and teacher→student links (US-023).
 */
@Component({
  selector: 'app-staff',
  imports: [PAGE_IMPORTS],
  template: `
    <div class="page-header"><h1>{{ 'nav.staff' | translate }}</h1></div>
    <mat-tab-group>
      <mat-tab [label]="'staff.teachers' | translate">
        <div class="table-wrap tab-body">
          <table class="data-table">
            <thead><tr><th>{{ 'common.fullName' | translate }}</th><th>{{ 'staff.specialization' | translate }}</th><th>{{ 'staff.students' | translate }}</th><th></th></tr></thead>
            <tbody>
              @for (t of teachers(); track t.userId) {
                <tr [class.selected]="teacher()?.userId === t.userId">
                  <td>{{ t.fullName }}</td>
                  <td>{{ t.specialization ?? '—' }}</td>
                  <td class="num">{{ t.linkedCount }}</td>
                  <td class="actions">@if (canManage) { <button mat-button (click)="openTeacher(t)">{{ 'common.edit' | translate }}</button> }</td>
                </tr>
              } @empty { <tr><td colspan="4" class="empty">{{ 'common.noData' | translate }}</td></tr> }
            </tbody>
          </table>
        </div>
        @if (teacher(); as t) {
          <mat-card appearance="outlined" class="panel">
            <mat-card-header><mat-card-title>{{ t.fullName }}</mat-card-title></mat-card-header>
            <mat-card-content>
              <div class="form-grid">
                <mat-form-field><mat-label>{{ 'staff.specialization' | translate }}</mat-label><input matInput [(ngModel)]="t.specialization" /></mat-form-field>
                <mat-form-field><mat-label>{{ 'staff.bio' | translate }}</mat-label><input matInput [(ngModel)]="t.notes" /></mat-form-field>
                <mat-form-field>
                  <mat-label>{{ 'staff.assignStudents' | translate }}</mat-label>
                  <mat-select [(ngModel)]="selectedStudents" multiple>
                    @for (s of allStudents(); track s.userId) { <mat-option [value]="s.userId">{{ s.fullName }}</mat-option> }
                  </mat-select>
                </mat-form-field>
              </div>
              <div class="form-actions">
                <button mat-button (click)="teacher.set(null)">{{ 'common.cancel' | translate }}</button>
                <button mat-flat-button (click)="saveTeacher(t)">{{ 'common.save' | translate }}</button>
              </div>
            </mat-card-content>
          </mat-card>
        }
      </mat-tab>

      <mat-tab [label]="'staff.supervisors' | translate">
        <div class="table-wrap tab-body">
          <table class="data-table">
            <thead><tr><th>{{ 'common.fullName' | translate }}</th><th>{{ 'staff.teachers' | translate }}</th><th></th></tr></thead>
            <tbody>
              @for (s of supervisors(); track s.userId) {
                <tr [class.selected]="supervisor()?.userId === s.userId">
                  <td>{{ s.fullName }}</td>
                  <td class="num">{{ s.linkedCount }}</td>
                  <td class="actions">@if (canManage) { <button mat-button (click)="openSupervisor(s)">{{ 'common.edit' | translate }}</button> }</td>
                </tr>
              } @empty { <tr><td colspan="3" class="empty">{{ 'common.noData' | translate }}</td></tr> }
            </tbody>
          </table>
        </div>
        @if (supervisor(); as s) {
          <mat-card appearance="outlined" class="panel">
            <mat-card-header><mat-card-title>{{ s.fullName }}</mat-card-title></mat-card-header>
            <mat-card-content>
              <mat-form-field class="wide">
                <mat-label>{{ 'staff.assignTeachers' | translate }}</mat-label>
                <mat-select [(ngModel)]="selectedTeachers" multiple>
                  @for (t of teachers(); track t.userId) { <mat-option [value]="t.userId">{{ t.fullName }}</mat-option> }
                </mat-select>
              </mat-form-field>
              <h3 class="section-title">{{ 'staff.workSchedule' | translate }}</h3>
              <table class="data-table">
                <thead><tr><th>{{ 'staff.day' | translate }}</th><th>{{ 'staff.workingDay' | translate }}</th><th>{{ 'staff.shiftStart' | translate }}</th><th>{{ 'staff.shiftEnd' | translate }}</th></tr></thead>
                <tbody>
                  @for (d of schedule(); track d.day) {
                    <tr>
                      <td>{{ 'days.' + d.day | translate }}</td>
                      <td><mat-checkbox [(ngModel)]="d.isWorkingDay" /></td>
                      <td><input type="time" [(ngModel)]="d.shiftStart" [disabled]="!d.isWorkingDay" /></td>
                      <td><input type="time" [(ngModel)]="d.shiftEnd" [disabled]="!d.isWorkingDay" /></td>
                    </tr>
                  }
                </tbody>
              </table>
              <div class="form-actions">
                <button mat-button (click)="supervisor.set(null)">{{ 'common.cancel' | translate }}</button>
                <button mat-flat-button (click)="saveSupervisor(s)">{{ 'common.save' | translate }}</button>
              </div>
            </mat-card-content>
          </mat-card>
        }
      </mat-tab>

      <mat-tab [label]="'staff.parents' | translate">
        <div class="table-wrap tab-body">
          <table class="data-table">
            <thead><tr><th>{{ 'common.fullName' | translate }}</th><th>{{ 'auth.email' | translate }}</th><th>{{ 'staff.children' | translate }}</th></tr></thead>
            <tbody>
              @for (p of parents(); track p.userId) {
                <tr><td>{{ p.fullName }}</td><td class="ltr">{{ p.email }}</td><td>{{ childNames(p) }}</td></tr>
              } @empty { <tr><td colspan="3" class="empty">{{ 'common.noData' | translate }}</td></tr> }
            </tbody>
          </table>
        </div>
      </mat-tab>
    </mat-tab-group>
  `,
  styles: `
    .tab-body { margin: 16px 0; }
    .wide { width: 100%; }
    input[type='time'] { padding: 4px; border: 1px solid var(--mat-sys-outline-variant); border-radius: 6px; background: transparent; color: inherit; }
  `,
})
export class StaffPage implements OnInit {
  private readonly api = inject(ApiService);
  private readonly notify = inject(Notifier);
  protected readonly canManage = inject(AuthService).hasPermission(Permissions.profiles.manage);

  protected readonly teachers = signal<StaffProfileDto[]>([]);
  protected readonly supervisors = signal<StaffProfileDto[]>([]);
  protected readonly parents = signal<ParentDto[]>([]);
  protected readonly allStudents = signal<StudentDto[]>([]);
  protected readonly teacher = signal<StaffProfileDto | null>(null);
  protected readonly supervisor = signal<StaffProfileDto | null>(null);
  protected readonly schedule = signal<WorkDayDto[]>([]);
  protected selectedStudents: number[] = [];
  protected selectedTeachers: number[] = [];

  ngOnInit(): void {
    this.load();
  }

  protected childNames(p: ParentDto): string {
    return p.children.map((c) => c.fullName).join('، ') || '—';
  }

  protected openTeacher(t: StaffProfileDto): void {
    this.teacher.set({ ...t });
    this.api.get<PersonRef[]>(`${Api.academic}/teachers/${t.userId}/students`).subscribe((s) => (this.selectedStudents = s.map((x) => x.userId)));
  }

  protected saveTeacher(t: StaffProfileDto): void {
    this.api.put(`${Api.academic}/teachers/${t.userId}`, { specialization: t.specialization || null, bio: t.notes || null }).subscribe({
      next: () =>
        this.api.put(`${Api.academic}/teachers/${t.userId}/students`, { userIds: this.selectedStudents }).subscribe({
          next: () => this.done(() => this.teacher.set(null)),
          error: (e) => this.notify.error(e),
        }),
      error: (e) => this.notify.error(e),
    });
  }

  protected openSupervisor(s: StaffProfileDto): void {
    this.supervisor.set({ ...s });
    this.api.get<PersonRef[]>(`${Api.academic}/supervisors/${s.userId}/teachers`).subscribe((t) => (this.selectedTeachers = t.map((x) => x.userId)));
    this.api.get<WorkDayDto[]>(`${Api.academic}/supervisors/${s.userId}/work-schedule`).subscribe((d) =>
      this.schedule.set(d.map((x) => ({ ...x, shiftStart: x.shiftStart?.slice(0, 5) ?? null, shiftEnd: x.shiftEnd?.slice(0, 5) ?? null }))),
    );
  }

  protected saveSupervisor(s: StaffProfileDto): void {
    const days = this.schedule().map((d) => ({
      day: d.day,
      isWorkingDay: d.isWorkingDay,
      shiftStart: d.isWorkingDay && d.shiftStart ? `${d.shiftStart.slice(0, 5)}:00` : null,
      shiftEnd: d.isWorkingDay && d.shiftEnd ? `${d.shiftEnd.slice(0, 5)}:00` : null,
    }));
    this.api.put(`${Api.academic}/supervisors/${s.userId}/teachers`, { userIds: this.selectedTeachers }).subscribe({
      next: () =>
        this.api.put(`${Api.academic}/supervisors/${s.userId}/work-schedule`, { days }).subscribe({
          next: () => this.done(() => this.supervisor.set(null)),
          error: (e) => this.notify.error(e),
        }),
      error: (e) => this.notify.error(e),
    });
  }

  private done(then: () => void): void {
    this.notify.saved();
    then();
    this.load();
  }

  private load(): void {
    this.api.get<StaffProfileDto[]>(`${Api.academic}/teachers`).subscribe((t) => this.teachers.set(t));
    this.api.get<StaffProfileDto[]>(`${Api.academic}/supervisors`).subscribe((s) => this.supervisors.set(s));
    this.api.get<ParentDto[]>(`${Api.academic}/parents`).subscribe((p) => this.parents.set(p));
    this.api.get<PagedResult<StudentDto>>(`${Api.academic}/students`, { pageSize: 100 }).subscribe((r) => this.allStudents.set(r.items));
  }
}
