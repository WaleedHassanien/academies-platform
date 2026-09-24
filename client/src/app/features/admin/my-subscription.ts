import { Component, inject, OnInit, signal } from '@angular/core';
import { Api, ApiService } from '../../core/api/api.service';
import { AcademySubscriptionDto, AcademyUsageDto, PlanDto } from '../../core/api/models';
import { PAGE_IMPORTS } from '../../shared/page-imports';
import { UsageBar } from '../../shared/ui';

/** The academy's plan, trial, consumption and the upgrade options (US-015, US-017). */
@Component({
  selector: 'app-my-subscription',
  imports: [PAGE_IMPORTS, UsageBar],
  template: `
    <div class="page-header"><h1>{{ 'nav.subscription' | translate }}</h1></div>
    @if (sub(); as s) {
      <mat-card appearance="outlined" class="panel">
        <mat-card-header>
          <mat-card-title>{{ s.planName }} <span class="status" [class]="s.status">{{ 'status.' + s.status | translate }}</span></mat-card-title>
          <mat-card-subtitle>
            @if (s.trialEndsOnUtc) { {{ 'platform.trialEnds' | translate }}: {{ s.trialEndsOnUtc | date: 'mediumDate' }} }
            @if (s.endsOnUtc) { · {{ 'platform.endsOn' | translate }}: {{ s.endsOnUtc | date: 'mediumDate' }} }
          </mat-card-subtitle>
        </mat-card-header>
        <mat-card-content>
          <div class="grid">
            @for (u of usage()?.items ?? []; track u.key) {
              <app-usage-bar [label]="'limits.' + u.key | translate" [used]="u.used" [limit]="u.limit" />
            }
          </div>
          <mat-chip-set>
            @for (f of features(s); track f) { <mat-chip>{{ 'features.' + f | translate }}</mat-chip> }
          </mat-chip-set>
        </mat-card-content>
      </mat-card>
    }
    <h2 class="section-title">{{ 'subscription.available' | translate }}</h2>
    <div class="grid">
      @for (p of plans(); track p.id) {
        <mat-card appearance="outlined" [class.current]="p.id === sub()?.planId">
          <mat-card-header><mat-card-title>{{ p.name }}</mat-card-title><mat-card-subtitle class="ltr">{{ p.monthlyPrice | number }} / {{ 'platform.month' | translate }}</mat-card-subtitle></mat-card-header>
          <mat-card-content>
            @for (k of ['students', 'teachers', 'users']; track k) {
              <div>{{ 'limits.' + k | translate }}: <b>{{ p.limits[k] === -1 ? '∞' : p.limits[k] }}</b></div>
            }
          </mat-card-content>
        </mat-card>
      }
    </div>
    <p class="muted">{{ 'subscription.upgradeHint' | translate }}</p>
  `,
  styles: `.current { border-color: var(--mat-sys-primary); border-width: 2px; }`,
})
export class MySubscriptionPage implements OnInit {
  private readonly api = inject(ApiService);
  protected readonly sub = signal<AcademySubscriptionDto | null>(null);
  protected readonly usage = signal<AcademyUsageDto | null>(null);
  protected readonly plans = signal<PlanDto[]>([]);

  ngOnInit(): void {
    this.api.get<AcademySubscriptionDto>(`${Api.subscriptions}/academies/current/subscription`).subscribe((s) => this.sub.set(s));
    this.api.get<AcademyUsageDto>(`${Api.identity}/users/usage`).subscribe((u) => this.usage.set(u));
    this.api.get<PlanDto[]>(`${Api.subscriptions}/plans`).subscribe((p) => this.plans.set(p));
  }

  protected features(s: AcademySubscriptionDto): string[] {
    return Object.entries(s.effective.features).filter(([, on]) => on).map(([k]) => k);
  }
}
