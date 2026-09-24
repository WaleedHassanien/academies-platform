import { Component, inject, OnInit, signal } from '@angular/core';
import { PagedResult } from '../../core/api/api.models';
import { Api, ApiService } from '../../core/api/api.service';
import { CourseDto, GroupDto, MaterialDto, StaffProfileDto, StudentDto } from '../../core/api/models';
import { AuthService } from '../../core/auth/auth.service';
import { Permissions } from '../../core/auth/permissions';
import { Notifier } from '../../shared/notifier';
import { PAGE_IMPORTS } from '../../shared/page-imports';

interface GroupForm {
  id: number | null;
  name: string;
  courseId: number | null;
  teacherUserId: number | null;
  students: number[];
}

/** Groups: name, course, teacher and members (US-023). */
@Component({
  selector: 'app-groups',
  imports: [PAGE_IMPORTS],
  template: `
    <div class="page-header">
      <h1>{{ 'nav.groups' | translate }}</h1>
      @if (canManage) { <button mat-flat-button (click)="edit(null)"><mat-icon>add</mat-icon>{{ 'groups.new' | translate }}</button> }
    </div>

    @if (form(); as f) {
      <mat-card appearance="outlined" class="panel">
        <mat-card-content>
          <div class="form-grid">
            <mat-form-field><mat-label>{{ 'common.name' | translate }}</mat-label><input matInput [(ngModel)]="f.name" /></mat-form-field>
            <mat-form-field>
              <mat-label>{{ 'nav.courses' | translate }}</mat-label>
              <mat-select [(ngModel)]="f.courseId"><mat-option [value]="null">—</mat-option>@for (c of courses(); track c.id) { <mat-option [value]="c.id">{{ c.name }}</mat-option> }</mat-select>
            </mat-form-field>
            <mat-form-field>
              <mat-label>{{ 'roles.Teacher' | translate }}</mat-label>
              <mat-select [(ngModel)]="f.teacherUserId"><mat-option [value]="null">—</mat-option>@for (t of teachers(); track t.userId) { <mat-option [value]="t.userId">{{ t.fullName }}</mat-option> }</mat-select>
            </mat-form-field>
            <mat-form-field>
              <mat-label>{{ 'nav.students' | translate }}</mat-label>
              <mat-select [(ngModel)]="f.students" multiple>@for (s of students(); track s.userId) { <mat-option [value]="s.userId">{{ s.fullName }}</mat-option> }</mat-select>
            </mat-form-field>
          </div>
          <div class="form-actions">
            <button mat-button (click)="form.set(null)">{{ 'common.cancel' | translate }}</button>
            <button mat-flat-button (click)="save(f)" [disabled]="!f.name">{{ 'common.save' | translate }}</button>
          </div>
        </mat-card-content>
      </mat-card>
    }

    <div class="grid">
      @for (g of groups(); track g.id) {
        <mat-card appearance="outlined">
          <mat-card-header>
            <mat-card-title>{{ g.name }}</mat-card-title>
            <mat-card-subtitle>{{ g.courseName ?? '—' }} · {{ g.teacherName ?? '—' }}</mat-card-subtitle>
          </mat-card-header>
          <mat-card-content>
            <mat-chip-set>@for (s of g.students; track s.userId) { <mat-chip>{{ s.fullName }}</mat-chip> }</mat-chip-set>
          </mat-card-content>
          @if (canManage) {
            <mat-card-actions>
              <button mat-button (click)="edit(g)">{{ 'common.edit' | translate }}</button>
              <button mat-button (click)="remove(g)">{{ 'common.delete' | translate }}</button>
            </mat-card-actions>
          }
        </mat-card>
      } @empty {
        <p class="empty">{{ 'common.noData' | translate }}</p>
      }
    </div>
  `,
})
export class GroupsPage implements OnInit {
  private readonly api = inject(ApiService);
  private readonly notify = inject(Notifier);
  protected readonly canManage = inject(AuthService).hasPermission(Permissions.profiles.manage);

  protected readonly groups = signal<GroupDto[]>([]);
  protected readonly courses = signal<CourseDto[]>([]);
  protected readonly teachers = signal<StaffProfileDto[]>([]);
  protected readonly students = signal<StudentDto[]>([]);
  protected readonly form = signal<GroupForm | null>(null);

  ngOnInit(): void {
    this.load();
    this.api.get<CourseDto[]>(`${Api.academic}/courses`).subscribe((c) => this.courses.set(c));
    this.api.get<StaffProfileDto[]>(`${Api.academic}/teachers`).subscribe((t) => this.teachers.set(t));
    this.api.get<PagedResult<StudentDto>>(`${Api.academic}/students`, { pageSize: 100 }).subscribe((r) => this.students.set(r.items));
  }

  protected edit(g: GroupDto | null): void {
    this.form.set(
      g
        ? { id: g.id, name: g.name, courseId: g.courseId, teacherUserId: g.teacherUserId, students: g.students.map((s) => s.userId) }
        : { id: null, name: '', courseId: null, teacherUserId: null, students: [] },
    );
  }

  protected save(f: GroupForm): void {
    const body = { name: f.name, courseId: f.courseId, teacherUserId: f.teacherUserId };
    const request = f.id ? this.api.put<GroupDto>(`${Api.academic}/groups/${f.id}`, body) : this.api.post<GroupDto>(`${Api.academic}/groups`, body);
    request.subscribe({
      next: (g) =>
        this.api.put(`${Api.academic}/groups/${g.id}/students`, { userIds: f.students }).subscribe({
          next: () => {
            this.notify.saved();
            this.form.set(null);
            this.load();
          },
          error: (e) => this.notify.error(e),
        }),
      error: (e) => this.notify.error(e),
    });
  }

  protected remove(g: GroupDto): void {
    if (confirm(`${g.name}?`)) {
      this.api.delete(`${Api.academic}/groups/${g.id}`).subscribe({ next: () => this.load(), error: (e) => this.notify.error(e) });
    }
  }

  private load(): void {
    this.api.get<GroupDto[]>(`${Api.academic}/groups`).subscribe((g) => this.groups.set(g));
  }
}

/** Courses and their materials (US-024, US-036). */
@Component({
  selector: 'app-courses',
  imports: [PAGE_IMPORTS],
  template: `
    <div class="page-header">
      <h1>{{ 'nav.courses' | translate }}</h1>
      @if (canManage) { <button mat-flat-button (click)="edit(null)"><mat-icon>add</mat-icon>{{ 'courses.new' | translate }}</button> }
    </div>

    @if (form(); as f) {
      <mat-card appearance="outlined" class="panel">
        <mat-card-content>
          <div class="form-grid">
            <mat-form-field><mat-label>{{ 'common.name' | translate }}</mat-label><input matInput [(ngModel)]="f.name" /></mat-form-field>
            <mat-form-field><mat-label>{{ 'students.level' | translate }}</mat-label><input matInput [(ngModel)]="f.level" /></mat-form-field>
            <mat-form-field><mat-label>{{ 'common.description' | translate }}</mat-label><textarea matInput [(ngModel)]="f.description"></textarea></mat-form-field>
            <mat-slide-toggle [(ngModel)]="f.isActive">{{ 'common.active' | translate }}</mat-slide-toggle>
          </div>
          <div class="form-actions">
            <button mat-button (click)="form.set(null)">{{ 'common.cancel' | translate }}</button>
            <button mat-flat-button (click)="save(f)" [disabled]="!f.name">{{ 'common.save' | translate }}</button>
          </div>
        </mat-card-content>
      </mat-card>
    }

    <div class="table-wrap">
      <table class="data-table">
        <thead><tr><th>{{ 'common.name' | translate }}</th><th>{{ 'students.level' | translate }}</th><th>{{ 'courses.materials' | translate }}</th><th>{{ 'common.status' | translate }}</th><th></th></tr></thead>
        <tbody>
          @for (c of courses(); track c.id) {
            <tr [class.selected]="selected()?.id === c.id">
              <td>{{ c.name }}<div class="muted">{{ c.description }}</div></td>
              <td>{{ c.level ?? '—' }}</td>
              <td class="num">{{ c.materialCount }}</td>
              <td><span class="status" [class.ok]="c.isActive">{{ (c.isActive ? 'common.active' : 'common.inactive') | translate }}</span></td>
              <td class="actions">
                <button mat-button (click)="openMaterials(c)">{{ 'courses.materials' | translate }}</button>
                @if (canManage) { <button mat-button (click)="edit(c)">{{ 'common.edit' | translate }}</button> }
              </td>
            </tr>
          } @empty { <tr><td colspan="5" class="empty">{{ 'common.noData' | translate }}</td></tr> }
        </tbody>
      </table>
    </div>

    @if (selected(); as c) {
      <mat-card appearance="outlined" class="panel" style="margin-top: 16px">
        <mat-card-header><mat-card-title>{{ c.name }} — {{ 'courses.materials' | translate }}</mat-card-title></mat-card-header>
        <mat-card-content>
          <table class="data-table">
            <tbody>
              @for (m of materials(); track m.id) {
                <tr>
                  <td><a [href]="m.url" target="_blank" rel="noopener">{{ m.title }}</a></td>
                  <td>{{ 'materialType.' + m.type | translate }}</td>
                  <td class="actions">@if (canManage) { <button mat-button (click)="removeMaterial(c, m)">{{ 'common.delete' | translate }}</button> }</td>
                </tr>
              } @empty { <tr><td class="empty">{{ 'common.noData' | translate }}</td></tr> }
            </tbody>
          </table>
          @if (canManage) {
            <div class="toolbar" style="margin-top: 12px">
              <mat-form-field><mat-label>{{ 'common.title' | translate }}</mat-label><input matInput [(ngModel)]="materialTitle" /></mat-form-field>
              <mat-form-field><mat-label>URL</mat-label><input matInput [(ngModel)]="materialUrl" dir="ltr" /></mat-form-field>
              <mat-form-field>
                <mat-label>{{ 'common.type' | translate }}</mat-label>
                <mat-select [(ngModel)]="materialType">@for (t of ['Link', 'Document', 'Video']; track t) { <mat-option [value]="t">{{ 'materialType.' + t | translate }}</mat-option> }</mat-select>
              </mat-form-field>
              <button mat-flat-button (click)="addMaterial(c)" [disabled]="!materialTitle || !materialUrl">{{ 'common.add' | translate }}</button>
            </div>
          }
        </mat-card-content>
      </mat-card>
    }
  `,
})
export class CoursesPage implements OnInit {
  private readonly api = inject(ApiService);
  private readonly notify = inject(Notifier);
  protected readonly canManage = inject(AuthService).hasPermission(Permissions.courses.manage);

  protected readonly courses = signal<CourseDto[]>([]);
  protected readonly form = signal<CourseDto | null>(null);
  protected readonly selected = signal<CourseDto | null>(null);
  protected readonly materials = signal<MaterialDto[]>([]);
  protected materialTitle = '';
  protected materialUrl = '';
  protected materialType = 'Link';

  ngOnInit(): void {
    this.load();
  }

  protected edit(c: CourseDto | null): void {
    this.form.set(c ? { ...c } : { id: 0, name: '', description: '', level: '', isActive: true, materialCount: 0 });
  }

  protected save(f: CourseDto): void {
    const body = { name: f.name, description: f.description || null, level: f.level || null, isActive: f.isActive };
    const request = f.id ? this.api.put(`${Api.academic}/courses/${f.id}`, body) : this.api.post(`${Api.academic}/courses`, body);
    request.subscribe({
      next: () => {
        this.notify.saved();
        this.form.set(null);
        this.load();
      },
      error: (e) => this.notify.error(e),
    });
  }

  protected openMaterials(c: CourseDto): void {
    this.selected.set(c);
    this.api.get<MaterialDto[]>(`${Api.academic}/courses/${c.id}/materials`).subscribe((m) => this.materials.set(m));
  }

  protected addMaterial(c: CourseDto): void {
    this.api.post(`${Api.academic}/courses/${c.id}/materials`, { title: this.materialTitle, url: this.materialUrl, type: this.materialType }).subscribe({
      next: () => {
        this.materialTitle = '';
        this.materialUrl = '';
        this.openMaterials(c);
        this.load();
      },
      error: (e) => this.notify.error(e),
    });
  }

  protected removeMaterial(c: CourseDto, m: MaterialDto): void {
    this.api.delete(`${Api.academic}/courses/${c.id}/materials/${m.id}`).subscribe({ next: () => this.openMaterials(c), error: (e) => this.notify.error(e) });
  }

  private load(): void {
    this.api.get<CourseDto[]>(`${Api.academic}/courses`, { includeInactive: true }).subscribe((c) => this.courses.set(c));
  }
}
