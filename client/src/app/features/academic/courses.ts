import { Component, inject, OnInit, signal } from '@angular/core';
import { Api, ApiService } from '../../core/api/api.service';
import { CourseDto, CourseKind, MaterialDto } from '../../core/api/models';
import { AuthService } from '../../core/auth/auth.service';
import { Permissions } from '../../core/auth/permissions';
import { Notifier } from '../../shared/notifier';
import { PAGE_IMPORTS } from '../../shared/page-imports';

export const COURSE_KINDS: CourseKind[] = ['Quran', 'Arabic', 'IslamicStudies', 'Other'];

/**
 * Subjects (Quran, Arabic, Islamic studies...) and their materials (US-024, US-036). There is no
 * fixed curriculum: each student's plan is written by their teacher.
 */
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
            <mat-form-field>
              <mat-label>{{ 'courses.kind' | translate }}</mat-label>
              <mat-select [(ngModel)]="f.kind">@for (k of kinds; track k) { <mat-option [value]="k">{{ 'courseKind.' + k | translate }}</mat-option> }</mat-select>
              <mat-hint>{{ 'courses.kindHint' | translate }}</mat-hint>
            </mat-form-field>
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
        <thead><tr><th>{{ 'common.name' | translate }}</th><th>{{ 'courses.kind' | translate }}</th><th>{{ 'students.level' | translate }}</th><th>{{ 'courses.materials' | translate }}</th><th>{{ 'common.status' | translate }}</th><th></th></tr></thead>
        <tbody>
          @for (c of courses(); track c.id) {
            <tr [class.selected]="selected()?.id === c.id">
              <td>{{ c.name }}<div class="muted">{{ c.description }}</div></td>
              <td>{{ 'courseKind.' + c.kind | translate }}</td>
              <td>{{ c.level ?? '—' }}</td>
              <td class="num">{{ c.materialCount }}</td>
              <td><span class="status" [class.ok]="c.isActive">{{ (c.isActive ? 'common.active' : 'common.inactive') | translate }}</span></td>
              <td class="actions">
                <button mat-button (click)="openMaterials(c)">{{ 'courses.materials' | translate }}</button>
                @if (canManage) { <button mat-button (click)="edit(c)">{{ 'common.edit' | translate }}</button> }
              </td>
            </tr>
          } @empty { <tr><td colspan="6" class="empty">{{ 'common.noData' | translate }}</td></tr> }
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

  protected readonly kinds = COURSE_KINDS;
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
    this.form.set(c ? { ...c } : { id: 0, name: '', description: '', level: '', kind: 'Quran', isActive: true, materialCount: 0 });
  }

  protected save(f: CourseDto): void {
    const body = { name: f.name, description: f.description || null, level: f.level || null, isActive: f.isActive, kind: f.kind };
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
