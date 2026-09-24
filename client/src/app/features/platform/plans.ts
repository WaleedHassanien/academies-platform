import { Component, inject, OnInit, signal } from '@angular/core';
import { Api, ApiService } from '../../core/api/api.service';
import { PlanDto } from '../../core/api/models';
import { Notifier } from '../../shared/notifier';
import { PAGE_IMPORTS } from '../../shared/page-imports';

interface PriceHistory {
  oldPrice: number;
  newPrice: number;
  changedOnUtc: string;
}

const LIMITS = ['students', 'teachers', 'users'];
const FEATURES = ['online_sessions', 'certificates', 'gamification', 'payment_gateway', 'analytics'];

/** Plans, prices, limits and features (US-014, US-016). Editing a plan changes it for every academy on it. */
@Component({
  selector: 'app-plans',
  imports: [PAGE_IMPORTS],
  template: `
    <div class="page-header">
      <h1>{{ 'platform.plans' | translate }}</h1>
      <button mat-flat-button (click)="edit(null)"><mat-icon>add</mat-icon>{{ 'platform.newPlan' | translate }}</button>
    </div>

    <div class="grid">
      @for (p of plans(); track p.id) {
        <mat-card appearance="outlined" [class.inactive]="!p.isActive">
          <mat-card-header>
            <mat-card-title>{{ p.name }} <span class="muted ltr">({{ p.code }})</span></mat-card-title>
            <mat-card-subtitle><span class="ltr">{{ p.monthlyPrice | number: '1.0-2' }}</span> / {{ 'platform.month' | translate }}
              @if (p.trialDays) { · {{ p.trialDays }} {{ 'platform.trialDays' | translate }} }</mat-card-subtitle>
          </mat-card-header>
          <mat-card-content>
            @for (k of limits; track k) {
              <div class="row"><span>{{ 'limits.' + k | translate }}</span><b>{{ p.limits[k] === -1 ? '∞' : p.limits[k] }}</b></div>
            }
            <mat-chip-set>
              @for (f of features; track f) {
                @if (p.features[f]) { <mat-chip>{{ 'features.' + f | translate }}</mat-chip> }
              }
            </mat-chip-set>
          </mat-card-content>
          <mat-card-actions><button mat-button (click)="edit(p)">{{ 'common.edit' | translate }}</button></mat-card-actions>
        </mat-card>
      }
    </div>

    @if (editing(); as e) {
      <mat-card appearance="outlined" class="panel">
        <mat-card-header><mat-card-title>{{ (e.id ? 'common.edit' : 'platform.newPlan') | translate }}</mat-card-title></mat-card-header>
        <mat-card-content>
          <div class="form-grid">
            <mat-form-field><mat-label>{{ 'platform.code' | translate }}</mat-label><input matInput [(ngModel)]="e.code" [disabled]="!!e.id" dir="ltr" /></mat-form-field>
            <mat-form-field><mat-label>{{ 'common.name' | translate }}</mat-label><input matInput [(ngModel)]="e.name" /></mat-form-field>
            <mat-form-field><mat-label>{{ 'platform.monthlyPrice' | translate }}</mat-label><input matInput type="number" [(ngModel)]="e.monthlyPrice" /></mat-form-field>
            <mat-form-field><mat-label>{{ 'platform.trialDays' | translate }}</mat-label><input matInput type="number" [(ngModel)]="e.trialDays" /></mat-form-field>
            <mat-form-field><mat-label>{{ 'platform.order' | translate }}</mat-label><input matInput type="number" [(ngModel)]="e.sortOrder" /></mat-form-field>
            <mat-slide-toggle [(ngModel)]="e.isActive">{{ 'common.active' | translate }}</mat-slide-toggle>
          </div>
          <h3 class="section-title">{{ 'platform.limits' | translate }} <span class="muted">({{ 'platform.unlimitedHint' | translate }})</span></h3>
          <div class="form-grid">
            @for (k of limits; track k) {
              <mat-form-field><mat-label>{{ 'limits.' + k | translate }}</mat-label><input matInput type="number" [(ngModel)]="e.limits[k]" /></mat-form-field>
            }
          </div>
          <h3 class="section-title">{{ 'platform.features' | translate }}</h3>
          <div class="toolbar">
            @for (f of features; track f) {
              <mat-checkbox [(ngModel)]="e.features[f]">{{ 'features.' + f | translate }}</mat-checkbox>
            }
          </div>
          @if (history().length) {
            <h3 class="section-title">{{ 'platform.priceHistory' | translate }}</h3>
            <table class="data-table">
              <tbody>
                @for (h of history(); track $index) {
                  <tr><td>{{ h.changedOnUtc | date: 'medium' }}</td><td class="num">{{ h.oldPrice | number }} → {{ h.newPrice | number }}</td></tr>
                }
              </tbody>
            </table>
          }
          <div class="form-actions">
            <button mat-button (click)="editing.set(null)">{{ 'common.cancel' | translate }}</button>
            <button mat-flat-button (click)="save(e)">{{ 'common.save' | translate }}</button>
          </div>
        </mat-card-content>
      </mat-card>
    }
  `,
  styles: `
    .row { display: flex; justify-content: space-between; padding: 2px 0; }
    .inactive { opacity: 0.6; }
  `,
})
export class PlansPage implements OnInit {
  private readonly api = inject(ApiService);
  private readonly notify = inject(Notifier);

  protected readonly limits = LIMITS;
  protected readonly features = FEATURES;
  protected readonly plans = signal<PlanDto[]>([]);
  protected readonly editing = signal<PlanDto | null>(null);
  protected readonly history = signal<PriceHistory[]>([]);

  ngOnInit(): void {
    this.load();
  }

  protected edit(plan: PlanDto | null): void {
    this.history.set([]);
    this.editing.set(
      plan
        ? structuredClone(plan)
        : {
            id: 0, code: '', name: '', monthlyPrice: 0, trialDays: 0, isActive: true, sortOrder: this.plans().length + 1,
            limits: Object.fromEntries(LIMITS.map((k) => [k, 0])), features: Object.fromEntries(FEATURES.map((f) => [f, false])),
          },
    );
    if (plan) {
      this.api.get<PriceHistory[]>(`${Api.subscriptions}/plans/${plan.id}/price-history`).subscribe((h) => this.history.set(h));
    }
  }

  protected save(plan: PlanDto): void {
    const body = { ...plan, limits: plan.limits, features: plan.features };
    const request = plan.id
      ? this.api.put(`${Api.subscriptions}/plans/${plan.id}`, body)
      : this.api.post(`${Api.subscriptions}/plans`, body);
    request.subscribe({
      next: () => {
        this.notify.saved();
        this.editing.set(null);
        this.load();
      },
      error: (e) => this.notify.error(e),
    });
  }

  private load(): void {
    this.api.get<PlanDto[]>(`${Api.subscriptions}/plans`).subscribe((p) => this.plans.set(p));
  }
}
