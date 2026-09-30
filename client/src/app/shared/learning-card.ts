import { Component, computed, effect, inject, input, signal } from '@angular/core';
import { Api, ApiService } from '../core/api/api.service';
import { CourseDto, EnrollmentDto, LearningPlanDto, StaffProfileDto } from '../core/api/models';
import { AuthService } from '../core/auth/auth.service';
import { Permissions, Roles } from '../core/auth/permissions';
import { Notifier } from './notifier';
import { PAGE_IMPORTS } from './page-imports';

interface PlanForm {
  id: number | null;
  courseId: number | null;
  goal: string;
  reference: string;
  expectedAmount: string;
  targetDate: string;
  status: LearningPlanDto['status'];
  notes: string;
}

interface EnrollForm {
  id: number | null;
  courseId: number | null;
  teacherUserId: number | null;
  status: EnrollmentDto['status'];
}

/**
 * A student's subjects and, for each, the teacher's plan: the goal and reference agreed with the
 * family (there is no fixed curriculum), how much per session, and a target date. Staff enrol the
 * student in subjects; the student's teachers (and staff) write the plans; the family reads them.
 */
@Component({
  selector: 'app-learning-card',
  imports: [PAGE_IMPORTS],
  template: `
    <mat-card appearance="outlined">
      <mat-card-header>
        <mat-icon mat-card-avatar class="card-icon">flag</mat-icon>
        <mat-card-title>{{ 'plans.title' | translate }}</mat-card-title>
        <mat-card-subtitle>{{ 'plans.hint' | translate }}</mat-card-subtitle>
      </mat-card-header>
      <mat-card-content>
        @for (e of enrollments(); track e.id) {
          <div class="subject" [class.ended]="e.status === 'Ended'">
            <div class="subject-head">
              <b>{{ e.courseName }}</b>
              <span class="muted">{{ e.teacherName ?? ('plans.noTeacher' | translate) }}</span>
              @if (e.status !== 'Active') { <span class="status">{{ 'enrollment.' + e.status | translate }}</span> }
              <span class="spacer"></span>
              @if (canEnroll) { <button mat-icon-button (click)="editEnrollment(e)" [attr.aria-label]="'common.edit' | translate"><mat-icon>edit</mat-icon></button> }
            </div>
            @if (activePlan(e.courseId); as p) {
              <div class="plan">
                <div class="goal"><mat-icon>flag</mat-icon>{{ p.goal }}</div>
                <div class="plan-facts">
                  @if (p.reference) { <span><b>{{ 'plans.reference' | translate }}:</b> {{ p.reference }}</span> }
                  @if (p.expectedAmount) { <span><b>{{ 'plans.expected' | translate }}:</b> {{ p.expectedAmount }}</span> }
                  @if (p.targetDate) { <span><b>{{ 'plans.target' | translate }}:</b> {{ p.targetDate | date: 'mediumDate' }}</span> }
                </div>
                @if (p.notes) { <p class="muted notes">{{ p.notes }}</p> }
                @if (canWrite) {
                  <div class="plan-actions">
                    <button mat-button (click)="editPlan(p)">{{ 'common.edit' | translate }}</button>
                    <button mat-button (click)="setStatus(p, 'Achieved')"><mat-icon>emoji_events</mat-icon>{{ 'plans.achieved' | translate }}</button>
                  </div>
                }
              </div>
            } @else if (canWrite && e.status !== 'Ended') {
              <button mat-stroked-button class="add-plan" (click)="newPlan(e.courseId)"><mat-icon>add</mat-icon>{{ 'plans.new' | translate }}</button>
            } @else {
              <p class="muted small">{{ 'plans.none' | translate }}</p>
            }
            @if (pastPlans(e.courseId).length) {
              <details>
                <summary class="muted small">{{ 'plans.history' | translate }} ({{ pastPlans(e.courseId).length }})</summary>
                @for (p of pastPlans(e.courseId); track p.id) {
                  <div class="past"><span class="status" [class.ok]="p.status === 'Achieved'">{{ 'plans.status_' + p.status | translate }}</span> {{ p.goal }}</div>
                }
              </details>
            }
          </div>
        } @empty {
          <p class="muted">{{ 'plans.noSubjects' | translate }}</p>
        }

        @if (planForm(); as f) {
          <div class="form-grid edit">
            <mat-form-field class="span-2"><mat-label>{{ 'plans.goal' | translate }}</mat-label><input matInput [(ngModel)]="f.goal" [placeholder]="'plans.goalHint' | translate" /></mat-form-field>
            <mat-form-field><mat-label>{{ 'plans.reference' | translate }}</mat-label><input matInput [(ngModel)]="f.reference" [placeholder]="'plans.referenceHint' | translate" /></mat-form-field>
            <mat-form-field><mat-label>{{ 'plans.expected' | translate }}</mat-label><input matInput [(ngModel)]="f.expectedAmount" [placeholder]="'plans.expectedHint' | translate" /></mat-form-field>
            <mat-form-field><mat-label>{{ 'plans.target' | translate }}</mat-label><input matInput type="date" [(ngModel)]="f.targetDate" /></mat-form-field>
            <mat-form-field>
              <mat-label>{{ 'common.status' | translate }}</mat-label>
              <mat-select [(ngModel)]="f.status">@for (s of planStatuses; track s) { <mat-option [value]="s">{{ 'plans.status_' + s | translate }}</mat-option> }</mat-select>
            </mat-form-field>
            <mat-form-field class="span-2"><mat-label>{{ 'common.notes' | translate }}</mat-label><textarea matInput rows="2" [(ngModel)]="f.notes"></textarea></mat-form-field>
          </div>
          <div class="form-actions">
            <button mat-button (click)="planForm.set(null)">{{ 'common.cancel' | translate }}</button>
            <button mat-flat-button (click)="savePlan(f)" [disabled]="!f.goal.trim()">{{ 'common.save' | translate }}</button>
          </div>
        }

        @if (canEnroll) {
          @if (enrollForm(); as f) {
            <div class="form-grid edit">
              <mat-form-field>
                <mat-label>{{ 'students.subject' | translate }}</mat-label>
                <mat-select [(ngModel)]="f.courseId" [disabled]="!!f.id">@for (c of courses(); track c.id) { <mat-option [value]="c.id">{{ c.name }}</mat-option> }</mat-select>
              </mat-form-field>
              <mat-form-field>
                <mat-label>{{ 'roles.Teacher' | translate }}</mat-label>
                <mat-select [(ngModel)]="f.teacherUserId">
                  <mat-option [value]="null">—</mat-option>
                  @for (t of teachersFor(f.courseId); track t.userId) { <mat-option [value]="t.userId">{{ t.fullName }}</mat-option> }
                </mat-select>
              </mat-form-field>
              @if (f.id) {
                <mat-form-field>
                  <mat-label>{{ 'common.status' | translate }}</mat-label>
                  <mat-select [(ngModel)]="f.status">@for (s of enrollmentStatuses; track s) { <mat-option [value]="s">{{ 'enrollment.' + s | translate }}</mat-option> }</mat-select>
                </mat-form-field>
              }
            </div>
            <div class="form-actions">
              <button mat-button (click)="enrollForm.set(null)">{{ 'common.cancel' | translate }}</button>
              <button mat-flat-button (click)="saveEnrollment(f)" [disabled]="!f.courseId">{{ 'common.save' | translate }}</button>
            </div>
          } @else {
            <div class="form-actions"><button mat-stroked-button (click)="newEnrollment()"><mat-icon>add</mat-icon>{{ 'plans.addSubject' | translate }}</button></div>
          }
        }
      </mat-card-content>
    </mat-card>
  `,
  styles: `
    :host { display: block; }
    .card-icon { display: grid; place-items: center; border-radius: 12px; color: var(--tone-amber, #d97706);
      background: color-mix(in srgb, var(--tone-amber, #d97706) 12%, transparent); }
    .subject + .subject { margin-top: 14px; padding-top: 12px; border-top: 1px dashed var(--app-border); }
    .subject.ended { opacity: 0.6; }
    .subject-head { display: flex; align-items: center; flex-wrap: wrap; gap: 4px 10px; }
    .subject-head .spacer { flex: 1; }
    .subject-head button { width: 32px; height: 32px; padding: 4px; }
    .plan { margin-top: 8px; padding: 10px 12px; border-radius: 12px; background: var(--app-surface-2); border: 1px solid var(--app-border); }
    .goal { display: flex; align-items: center; gap: 6px; font-weight: 600; }
    .goal mat-icon { font-size: 18px; width: 18px; height: 18px; color: var(--mat-sys-primary); }
    .plan-facts { display: flex; flex-wrap: wrap; gap: 4px 16px; margin-top: 6px; font-size: 0.85rem; }
    .notes { margin: 6px 0 0; font-size: 0.85rem; }
    .plan-actions { display: flex; gap: 4px; margin-top: 6px; }
    .add-plan { margin-top: 8px; }
    .small { font-size: 0.8rem; }
    details { margin-top: 6px; }
    .past { font-size: 0.85rem; padding: 3px 0; }
    .edit { margin-top: 16px; }
    .span-2 { grid-column: span 2; }
    @media (max-width: 640px) { .span-2 { grid-column: auto; } }
  `,
})
export class LearningCard {
  private readonly api = inject(ApiService);
  private readonly notify = inject(Notifier);
  private readonly auth = inject(AuthService);

  readonly studentUserId = input.required<number>();

  protected readonly canEnroll = this.auth.hasPermission(Permissions.profiles.manage);
  /** Staff, or the student's teacher (the server checks the link). */
  protected readonly canWrite = this.canEnroll || this.auth.hasAnyRole(Roles.Teacher);
  protected readonly planStatuses: LearningPlanDto['status'][] = ['Active', 'Achieved', 'Closed'];
  protected readonly enrollmentStatuses: EnrollmentDto['status'][] = ['Active', 'Paused', 'Ended'];

  protected readonly enrollments = signal<EnrollmentDto[]>([]);
  protected readonly plans = signal<LearningPlanDto[]>([]);
  protected readonly courses = signal<CourseDto[]>([]);
  protected readonly teachers = signal<StaffProfileDto[]>([]);
  protected readonly planForm = signal<PlanForm | null>(null);
  protected readonly enrollForm = signal<EnrollForm | null>(null);

  private readonly active = computed(() => new Map(this.plans().filter((p) => p.status === 'Active').map((p) => [p.courseId, p])));

  constructor() {
    effect(() => {
      if (this.studentUserId()) {
        this.reload();
      }
    });
    if (this.canEnroll) {
      this.api.get<CourseDto[]>(`${Api.academic}/courses`).subscribe((c) => this.courses.set(c));
      this.api.get<StaffProfileDto[]>(`${Api.academic}/teachers`).subscribe((t) => this.teachers.set(t));
    }
  }

  reload(): void {
    const id = this.studentUserId();
    this.api.get<EnrollmentDto[]>(`${Api.academic}/students/${id}/enrollments`).subscribe({ next: (e) => this.enrollments.set(e), error: () => this.enrollments.set([]) });
    this.api.get<LearningPlanDto[]>(`${Api.academic}/students/${id}/plans`).subscribe({ next: (p) => this.plans.set(p), error: () => this.plans.set([]) });
  }

  protected activePlan(courseId: number): LearningPlanDto | null {
    return this.active().get(courseId) ?? null;
  }

  protected pastPlans(courseId: number): LearningPlanDto[] {
    return this.plans().filter((p) => p.courseId === courseId && p.status !== 'Active');
  }

  protected teachersFor(courseId: number | null): StaffProfileDto[] {
    return this.teachers().filter((t) => !courseId || !t.courseIds?.length || t.courseIds.includes(courseId));
  }

  protected newPlan(courseId: number): void {
    this.planForm.set({ id: null, courseId, goal: '', reference: '', expectedAmount: '', targetDate: '', status: 'Active', notes: '' });
  }

  protected editPlan(p: LearningPlanDto): void {
    this.planForm.set({
      id: p.id, courseId: p.courseId, goal: p.goal, reference: p.reference ?? '', expectedAmount: p.expectedAmount ?? '',
      targetDate: p.targetDate ?? '', status: p.status, notes: p.notes ?? '',
    });
  }

  protected setStatus(p: LearningPlanDto, status: LearningPlanDto['status']): void {
    this.editPlan(p);
    const f = this.planForm()!;
    f.status = status;
    this.savePlan(f);
  }

  protected savePlan(f: PlanForm): void {
    const body = {
      courseId: f.courseId, goal: f.goal.trim(), reference: f.reference || null, expectedAmount: f.expectedAmount || null,
      targetDate: f.targetDate || null, status: f.status, notes: f.notes || null,
    };
    const request = f.id
      ? this.api.put(`${Api.academic}/plans/${f.id}`, body)
      : this.api.post(`${Api.academic}/students/${this.studentUserId()}/plans`, body);
    request.subscribe({
      next: () => {
        this.notify.saved();
        this.planForm.set(null);
        this.reload();
      },
      error: (e) => this.notify.error(e),
    });
  }

  protected newEnrollment(): void {
    this.enrollForm.set({ id: null, courseId: null, teacherUserId: null, status: 'Active' });
  }

  protected editEnrollment(e: EnrollmentDto): void {
    this.enrollForm.set({ id: e.id, courseId: e.courseId, teacherUserId: e.teacherUserId, status: e.status });
  }

  protected saveEnrollment(f: EnrollForm): void {
    const body = { courseId: f.courseId, teacherUserId: f.teacherUserId, status: f.status };
    const request = f.id
      ? this.api.put(`${Api.academic}/enrollments/${f.id}`, body)
      : this.api.post(`${Api.academic}/students/${this.studentUserId()}/enrollments`, body);
    request.subscribe({
      next: () => {
        this.notify.saved();
        this.enrollForm.set(null);
        this.reload();
      },
      error: (e) => this.notify.error(e),
    });
  }
}
