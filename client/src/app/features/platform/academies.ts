import { Component, inject, OnInit, signal } from '@angular/core';
import { NonNullableFormBuilder, Validators } from '@angular/forms';
import { Observable } from 'rxjs';
import { PagedResult } from '../../core/api/api.models';
import { Api, ApiService } from '../../core/api/api.service';
import { AcademyDto, AcademySubscriptionDto, AcademyUsageDto, PlanDto } from '../../core/api/models';
import { Notifier } from '../../shared/notifier';
import { PAGE_IMPORTS } from '../../shared/page-imports';
import { UsageBar } from '../../shared/ui';

/** SuperAdmin: academies (US-018), their plan, trial and limit overrides (US-015, US-016). */
@Component({
  selector: 'app-academies',
  imports: [PAGE_IMPORTS, UsageBar],
  template: `
    <div class="page-header">
      <h1>{{ 'platform.academies' | translate }}</h1>
      <button mat-flat-button (click)="showCreate.set(!showCreate())"><mat-icon>add</mat-icon>{{ 'platform.newAcademy' | translate }}</button>
    </div>

    @if (showCreate()) {
      <mat-card appearance="outlined" class="panel">
        <mat-card-content>
          <form [formGroup]="createForm" (ngSubmit)="create()" class="form-grid">
            <mat-form-field><mat-label>{{ 'platform.academyName' | translate }}</mat-label><input matInput formControlName="name" /></mat-form-field>
            <mat-form-field><mat-label>{{ 'common.address' | translate }}</mat-label><input matInput formControlName="address" /></mat-form-field>
            <mat-form-field><mat-label>{{ 'platform.adminName' | translate }}</mat-label><input matInput formControlName="adminFullName" /></mat-form-field>
            <mat-form-field><mat-label>{{ 'platform.adminEmail' | translate }}</mat-label><input matInput formControlName="adminEmail" dir="ltr" /></mat-form-field>
            <mat-form-field><mat-label>{{ 'platform.adminPassword' | translate }}</mat-label><input matInput type="password" formControlName="adminPassword" dir="ltr" /></mat-form-field>
            <div class="form-actions"><button mat-flat-button [disabled]="createForm.invalid">{{ 'common.create' | translate }}</button></div>
          </form>
        </mat-card-content>
      </mat-card>
    }

    <div class="toolbar">
      <mat-form-field><mat-label>{{ 'common.search' | translate }}</mat-label><input matInput [(ngModel)]="search" (keyup.enter)="load()" /></mat-form-field>
      <button mat-stroked-button (click)="load()">{{ 'common.search' | translate }}</button>
    </div>

    <div class="table-wrap">
      <table class="data-table">
        <thead><tr><th>#</th><th>{{ 'platform.academyName' | translate }}</th><th>{{ 'common.status' | translate }}</th><th>{{ 'platform.users' | translate }}</th><th>{{ 'common.created' | translate }}</th><th></th></tr></thead>
        <tbody>
          @for (a of academies(); track a.id) {
            <tr [class.selected]="selected()?.id === a.id">
              <td>{{ a.id }}</td>
              <td>{{ a.name }}</td>
              <td><span class="status" [class]="a.status">{{ 'status.' + a.status | translate }}</span></td>
              <td class="num">{{ a.userCount }}</td>
              <td>{{ a.createdOnUtc | date: 'mediumDate' }}</td>
              <td class="actions">
                <button mat-button (click)="select(a)">{{ 'platform.subscription' | translate }}</button>
                <button mat-button (click)="toggleStatus(a)">{{ (a.status === 'Active' ? 'platform.suspend' : 'platform.activate') | translate }}</button>
              </td>
            </tr>
          } @empty {
            <tr><td colspan="6" class="empty">{{ 'common.noData' | translate }}</td></tr>
          }
        </tbody>
      </table>
    </div>

    @if (selected(); as academy) {
      @if (subscription(); as sub) {
        <mat-card appearance="outlined" class="panel" style="margin-top: 16px">
          <mat-card-header>
            <mat-card-title>{{ academy.name }} — {{ sub.planName }} <span class="status" [class]="sub.status">{{ 'status.' + sub.status | translate }}</span></mat-card-title>
            <mat-card-subtitle>
              @if (sub.trialEndsOnUtc) { {{ 'platform.trialEnds' | translate }}: {{ sub.trialEndsOnUtc | date: 'mediumDate' }} }
            </mat-card-subtitle>
          </mat-card-header>
          <mat-card-content>
            <div class="grid">
              @for (u of usage()?.items ?? []; track u.key) {
                <app-usage-bar [label]="'limits.' + u.key | translate" [used]="u.used" [limit]="u.limit" />
              }
            </div>

            <h3 class="section-title">{{ 'platform.assignPlan' | translate }}</h3>
            <div class="toolbar">
              <mat-form-field>
                <mat-label>{{ 'platform.plan' | translate }}</mat-label>
                <mat-select [(ngModel)]="planId">
                  @for (p of plans(); track p.id) { <mat-option [value]="p.id">{{ p.name }} ({{ p.monthlyPrice | number }})</mat-option> }
                </mat-select>
              </mat-form-field>
              <mat-form-field><mat-label>{{ 'platform.endsOn' | translate }}</mat-label><input matInput type="date" [(ngModel)]="endsOn" /></mat-form-field>
              <mat-checkbox [(ngModel)]="asTrial">{{ 'platform.startAsTrial' | translate }}</mat-checkbox>
              <button mat-flat-button (click)="assignPlan()" [disabled]="!planId">{{ 'common.save' | translate }}</button>
            </div>

            <h3 class="section-title">{{ 'platform.extendTrial' | translate }}</h3>
            <div class="toolbar">
              <mat-form-field><mat-label>{{ 'platform.days' | translate }}</mat-label><input matInput type="number" [(ngModel)]="trialDays" /></mat-form-field>
              <button mat-stroked-button (click)="extendTrial()">{{ 'platform.extend' | translate }}</button>
            </div>

            <h3 class="section-title">{{ 'platform.overrides' | translate }}</h3>
            <table class="data-table">
              <thead><tr><th>{{ 'platform.limit' | translate }}</th><th>{{ 'platform.planValue' | translate }}</th><th>{{ 'platform.override' | translate }}</th><th>{{ 'common.reason' | translate }}</th><th></th></tr></thead>
              <tbody>
                @for (key of limitKeys; track key) {
                  <tr>
                    <td>{{ 'limits.' + key | translate }}</td>
                    <td class="num">{{ planLimit(key) }}</td>
                    <td><input class="cell-input" type="number" [(ngModel)]="overrideValues[key]" placeholder="—" /></td>
                    <td><input class="cell-input wide" [(ngModel)]="overrideReasons[key]" /></td>
                    <td class="actions">
                      <button mat-button (click)="setOverride(key)" [disabled]="overrideValues[key] == null">{{ 'common.save' | translate }}</button>
                      @if (hasOverride(key)) { <button mat-button (click)="removeOverride(key)">{{ 'common.remove' | translate }}</button> }
                    </td>
                  </tr>
                }
              </tbody>
            </table>
            <p class="muted">{{ 'platform.unlimitedHint' | translate }}</p>
          </mat-card-content>
        </mat-card>
      }
    }
  `,
  styles: `
    .cell-input { width: 90px; padding: 6px; border: 1px solid var(--mat-sys-outline-variant); border-radius: 6px; background: transparent; color: inherit; }
    .cell-input.wide { width: 200px; }
  `,
})
export class AcademiesPage implements OnInit {
  private readonly api = inject(ApiService);
  private readonly notify = inject(Notifier);

  protected readonly limitKeys = ['students', 'teachers', 'users'];
  protected readonly academies = signal<AcademyDto[]>([]);
  protected readonly plans = signal<PlanDto[]>([]);
  protected readonly selected = signal<AcademyDto | null>(null);
  protected readonly subscription = signal<AcademySubscriptionDto | null>(null);
  protected readonly usage = signal<AcademyUsageDto | null>(null);
  protected readonly showCreate = signal(false);

  protected search = '';
  protected planId: number | null = null;
  protected endsOn = '';
  protected asTrial = false;
  protected trialDays = 14;
  protected overrideValues: Record<string, number | null> = {};
  protected overrideReasons: Record<string, string> = {};

  protected readonly createForm = inject(NonNullableFormBuilder).group({
    name: ['', Validators.required],
    address: [''],
    adminFullName: [''],
    adminEmail: ['', Validators.email],
    adminPassword: [''],
  });

  ngOnInit(): void {
    this.load();
    this.api.get<PlanDto[]>(`${Api.subscriptions}/plans`).subscribe((p) => this.plans.set(p));
  }

  protected load(): void {
    this.api
      .get<PagedResult<AcademyDto>>(`${Api.identity}/academies`, { search: this.search, pageSize: 100 })
      .subscribe((r) => this.academies.set(r.items));
  }

  protected create(): void {
    const v = this.createForm.getRawValue();
    this.api.post<AcademyDto>(`${Api.identity}/academies`, { ...v, adminEmail: v.adminEmail || null, adminPassword: v.adminPassword || null }).subscribe({
      next: () => {
        this.notify.saved();
        this.createForm.reset();
        this.showCreate.set(false);
        this.load();
      },
      error: (e) => this.notify.error(e),
    });
  }

  protected toggleStatus(a: AcademyDto): void {
    const status = a.status === 'Active' ? 'Suspended' : 'Active';
    this.api.put(`${Api.identity}/academies/${a.id}/status`, { status }).subscribe({
      next: () => this.load(),
      error: (e) => this.notify.error(e),
    });
  }

  protected select(a: AcademyDto): void {
    this.selected.set(a);
    this.refreshSubscription();
  }

  protected planLimit(key: string): string {
    const plan = this.plans().find((p) => p.id === this.subscription()?.planId);
    const v = plan?.limits[key];
    return v === -1 ? '∞' : String(v ?? '—');
  }

  protected hasOverride(key: string): boolean {
    return !!this.subscription()?.overrides.some((o) => o.limitKey === key);
  }

  protected assignPlan(): void {
    this.subAction(this.api.put(this.subUrl(), { planId: this.planId, endsOnUtc: this.endsOn || null, startAsTrial: this.asTrial }));
  }

  protected extendTrial(): void {
    this.subAction(this.api.post(`${this.subUrl()}/extend-trial`, { days: this.trialDays }));
  }

  protected setOverride(key: string): void {
    this.subAction(
      this.api.put(`${Api.subscriptions}/academies/${this.selected()!.id}/overrides/${key}`, {
        value: this.overrideValues[key],
        reason: this.overrideReasons[key] || null,
      }),
    );
  }

  protected removeOverride(key: string): void {
    this.subAction(this.api.delete(`${Api.subscriptions}/academies/${this.selected()!.id}/overrides/${key}`));
  }

  private subUrl(): string {
    return `${Api.subscriptions}/academies/${this.selected()!.id}/subscription`;
  }

  private subAction(request: Observable<unknown>): void {
    request.subscribe({
      next: () => {
        this.notify.saved();
        this.refreshSubscription();
      },
      error: (e) => this.notify.error(e),
    });
  }

  private refreshSubscription(): void {
    const id = this.selected()!.id;
    this.api.get<AcademySubscriptionDto>(`${Api.subscriptions}/academies/${id}/subscription`).subscribe((s) => {
      this.subscription.set(s);
      this.planId = s.planId;
      this.overrideValues = Object.fromEntries(s.overrides.map((o) => [o.limitKey, o.value]));
      this.overrideReasons = Object.fromEntries(s.overrides.map((o) => [o.limitKey, o.reason ?? '']));
    });
    this.api.get<AcademyUsageDto>(`${Api.identity}/users/usage`, { academyId: id }).subscribe({
      next: (u) => this.usage.set(u),
      error: () => this.usage.set(null),
    });
  }
}
