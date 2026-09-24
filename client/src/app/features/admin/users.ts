import { Component, inject, OnInit, signal } from '@angular/core';
import { NonNullableFormBuilder, Validators } from '@angular/forms';
import { PagedResult } from '../../core/api/api.models';
import { Api, ApiService } from '../../core/api/api.service';
import { AcademyUsageDto, UserDto } from '../../core/api/models';
import { AuthService } from '../../core/auth/auth.service';
import { Permissions } from '../../core/auth/permissions';
import { Notifier } from '../../shared/notifier';
import { PAGE_IMPORTS } from '../../shared/page-imports';
import { UsageBar } from '../../shared/ui';

/** Academy users and roles (US-019), with plan usage and limit errors surfaced (US-017). */
@Component({
  selector: 'app-users',
  imports: [PAGE_IMPORTS, UsageBar],
  template: `
    <div class="page-header">
      <h1>{{ 'nav.users' | translate }}</h1>
      @if (canManage) {
        <button mat-flat-button (click)="startCreate()"><mat-icon>person_add</mat-icon>{{ 'users.new' | translate }}</button>
      }
    </div>

    @if (usage(); as u) {
      <mat-card appearance="outlined" class="panel">
        <mat-card-content>
          <p class="muted">{{ 'users.plan' | translate }}: <b>{{ u.planName }}</b> · <span class="status" [class]="u.status">{{ 'status.' + u.status | translate }}</span></p>
          <div class="grid">
            @for (i of u.items; track i.key) {
              <app-usage-bar [label]="'limits.' + i.key | translate" [used]="i.used" [limit]="i.limit" />
            }
          </div>
        </mat-card-content>
      </mat-card>
    }

    @if (formOpen()) {
      <mat-card appearance="outlined" class="panel">
        <mat-card-header><mat-card-title>{{ (editingId() ? 'users.edit' : 'users.new') | translate }}</mat-card-title></mat-card-header>
        <mat-card-content>
          <form [formGroup]="form" (ngSubmit)="save()" class="form-grid">
            <mat-form-field><mat-label>{{ 'common.fullName' | translate }}</mat-label><input matInput formControlName="fullName" /></mat-form-field>
            <mat-form-field><mat-label>{{ 'auth.email' | translate }}</mat-label><input matInput formControlName="email" dir="ltr" [readonly]="!!editingId()" /></mat-form-field>
            <mat-form-field><mat-label>{{ 'common.phone' | translate }}</mat-label><input matInput formControlName="phoneNumber" dir="ltr" /></mat-form-field>
            @if (!editingId()) {
              <mat-form-field>
                <mat-label>{{ 'auth.password' | translate }}</mat-label>
                <input matInput type="password" formControlName="password" dir="ltr" />
                <mat-hint>{{ 'auth.passwordRules' | translate }}</mat-hint>
              </mat-form-field>
            }
            <mat-form-field>
              <mat-label>{{ 'users.roles' | translate }}</mat-label>
              <mat-select formControlName="roles" multiple>
                @for (r of roles(); track r) { <mat-option [value]="r">{{ 'roles.' + r | translate }}</mat-option> }
              </mat-select>
            </mat-form-field>
            <div class="form-actions">
              <button mat-button type="button" (click)="formOpen.set(false)">{{ 'common.cancel' | translate }}</button>
              <button mat-flat-button [disabled]="form.invalid">{{ 'common.save' | translate }}</button>
            </div>
          </form>
        </mat-card-content>
      </mat-card>
    }

    <div class="toolbar">
      <mat-form-field><mat-label>{{ 'common.search' | translate }}</mat-label><input matInput [(ngModel)]="search" (keyup.enter)="load()" /></mat-form-field>
      <mat-form-field>
        <mat-label>{{ 'users.role' | translate }}</mat-label>
        <mat-select [(ngModel)]="role" (selectionChange)="load()">
          <mat-option [value]="''">{{ 'common.all' | translate }}</mat-option>
          @for (r of roles(); track r) { <mat-option [value]="r">{{ 'roles.' + r | translate }}</mat-option> }
        </mat-select>
      </mat-form-field>
    </div>

    <div class="table-wrap">
      <table class="data-table">
        <thead><tr><th>{{ 'common.fullName' | translate }}</th><th>{{ 'auth.email' | translate }}</th><th>{{ 'users.roles' | translate }}</th><th>{{ 'common.status' | translate }}</th><th>{{ 'users.lastLogin' | translate }}</th><th></th></tr></thead>
        <tbody>
          @for (u of users(); track u.id) {
            <tr>
              <td>{{ u.fullName }}</td>
              <td class="ltr">{{ u.email }}</td>
              <td>@for (r of u.roles; track r) { <span class="status">{{ 'roles.' + r | translate }}</span> }</td>
              <td><span class="status" [class.ok]="u.isActive" [class.bad]="!u.isActive">{{ (u.isActive ? 'common.active' : 'common.inactive') | translate }}</span></td>
              <td>{{ u.lastLoginOnUtc ? (u.lastLoginOnUtc | date: 'short') : '—' }}</td>
              <td class="actions">
                @if (canManage) {
                  <button mat-icon-button [matMenuTriggerFor]="menu"><mat-icon>more_vert</mat-icon></button>
                  <mat-menu #menu="matMenu">
                    <button mat-menu-item (click)="startEdit(u)">{{ 'common.edit' | translate }}</button>
                    <button mat-menu-item (click)="setActive(u, !u.isActive)">{{ (u.isActive ? 'users.deactivate' : 'users.activate') | translate }}</button>
                    <button mat-menu-item (click)="remove(u)">{{ 'common.delete' | translate }}</button>
                  </mat-menu>
                }
              </td>
            </tr>
          } @empty {
            <tr><td colspan="6" class="empty">{{ 'common.noData' | translate }}</td></tr>
          }
        </tbody>
      </table>
    </div>
  `,
})
export class UsersPage implements OnInit {
  private readonly api = inject(ApiService);
  private readonly notify = inject(Notifier);
  protected readonly canManage = inject(AuthService).hasPermission(Permissions.users.manage);

  protected readonly users = signal<UserDto[]>([]);
  protected readonly roles = signal<string[]>([]);
  protected readonly usage = signal<AcademyUsageDto | null>(null);
  protected readonly formOpen = signal(false);
  protected readonly editingId = signal<number | null>(null);
  protected search = '';
  protected role = '';

  protected readonly form = inject(NonNullableFormBuilder).group({
    fullName: ['', Validators.required],
    email: ['', [Validators.required, Validators.email]],
    phoneNumber: [''],
    password: [''],
    roles: [[] as string[], Validators.required],
  });

  ngOnInit(): void {
    this.api.get<string[]>(`${Api.identity}/users/roles`).subscribe((r) => this.roles.set(r));
    this.load();
  }

  protected load(): void {
    this.api
      .get<PagedResult<UserDto>>(`${Api.identity}/users`, { search: this.search, role: this.role, pageSize: 100 })
      .subscribe((r) => this.users.set(r.items));
    this.api.get<AcademyUsageDto>(`${Api.identity}/users/usage`).subscribe({ next: (u) => this.usage.set(u), error: () => this.usage.set(null) });
  }

  protected startCreate(): void {
    this.editingId.set(null);
    this.form.reset();
    this.formOpen.set(true);
  }

  protected startEdit(u: UserDto): void {
    this.editingId.set(u.id);
    this.form.reset({ fullName: u.fullName, email: u.email, phoneNumber: u.phoneNumber ?? '', password: '', roles: u.roles });
    this.formOpen.set(true);
  }

  protected save(): void {
    const v = this.form.getRawValue();
    const id = this.editingId();
    const request = id
      ? this.api.put(`${Api.identity}/users/${id}`, { fullName: v.fullName, phoneNumber: v.phoneNumber || null, roles: v.roles })
      : this.api.post(`${Api.identity}/users`, { ...v, phoneNumber: v.phoneNumber || null });
    request.subscribe({
      next: () => {
        this.notify.saved();
        this.formOpen.set(false);
        this.load();
      },
      error: (e) => this.notify.error(e),
    });
  }

  protected setActive(u: UserDto, isActive: boolean): void {
    this.api.put(`${Api.identity}/users/${u.id}/active`, { isActive }).subscribe({ next: () => this.load(), error: (e) => this.notify.error(e) });
  }

  protected remove(u: UserDto): void {
    if (confirm(`${u.fullName}?`)) {
      this.api.delete(`${Api.identity}/users/${u.id}`).subscribe({ next: () => this.load(), error: (e) => this.notify.error(e) });
    }
  }
}
