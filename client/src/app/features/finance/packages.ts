import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { Api, ApiService } from '../../core/api/api.service';
import { FinanceSettingsDto, PackageDto } from '../../core/api/models';
import { AuthService } from '../../core/auth/auth.service';
import { Permissions } from '../../core/auth/permissions';
import { Notifier } from '../../shared/notifier';
import { PAGE_IMPORTS } from '../../shared/page-imports';

interface PackageForm {
  id: number | null;
  name: string;
  sessionsPerMonth: number;
  sessionMinutes: number;
  currency: string;
  monthlyPrice: number | null;
  isActive: boolean;
}

/**
 * Ready-made monthly packages (e.g. 8, 12, 16 or 20 sessions of 30, 45 or 60 minutes) with a price
 * per currency. Picking a package on a student's subject sets a prepaid billing that renews monthly.
 */
@Component({
  selector: 'app-packages',
  imports: [PAGE_IMPORTS],
  template: `
    <div class="page-header">
      <h1>{{ 'nav.packages' | translate }}</h1>
      @if (canManage) { <button mat-flat-button (click)="edit(null)"><mat-icon>add</mat-icon>{{ 'packages.new' | translate }}</button> }
    </div>
    <p class="muted">{{ 'packages.hint' | translate }}</p>

    @if (canManage) {
      <mat-card appearance="outlined" class="panel">
        <mat-card-header>
          <mat-card-title>{{ 'packages.standard' | translate }}</mat-card-title>
          <mat-card-subtitle>{{ 'packages.standardHint' | translate }}</mat-card-subtitle>
        </mat-card-header>
        <mat-card-content>
          <div class="toolbar">
            <mat-form-field>
              <mat-label>{{ 'billing.currency' | translate }}</mat-label>
              <mat-select [(ngModel)]="gridCurrency">@for (c of currencies(); track c) { <mat-option [value]="c">{{ c }}</mat-option> }</mat-select>
            </mat-form-field>
            <mat-form-field><mat-label>{{ 'packages.pricePerHour' | translate }}</mat-label><input matInput type="number" min="0" [(ngModel)]="pricePerHour" /></mat-form-field>
            <button mat-stroked-button (click)="createStandard()" [disabled]="!pricePerHour"><mat-icon>grid_view</mat-icon>{{ 'packages.createStandard' | translate }}</button>
          </div>
        </mat-card-content>
      </mat-card>
    }

    @if (form(); as f) {
      <mat-card appearance="outlined" class="panel">
        <mat-card-content>
          <div class="form-grid">
            <mat-form-field><mat-label>{{ 'common.name' | translate }}</mat-label><input matInput [(ngModel)]="f.name" /></mat-form-field>
            <mat-form-field><mat-label>{{ 'billing.sessionsPerMonth' | translate }}</mat-label><input matInput type="number" min="1" max="62" [(ngModel)]="f.sessionsPerMonth" /></mat-form-field>
            <mat-form-field>
              <mat-label>{{ 'packages.minutes' | translate }}</mat-label>
              <mat-select [(ngModel)]="f.sessionMinutes">@for (m of minuteOptions; track m) { <mat-option [value]="m">{{ m }}</mat-option> }</mat-select>
            </mat-form-field>
            <mat-form-field>
              <mat-label>{{ 'billing.currency' | translate }}</mat-label>
              <mat-select [(ngModel)]="f.currency">@for (c of currencies(); track c) { <mat-option [value]="c">{{ c }}</mat-option> }</mat-select>
            </mat-form-field>
            <mat-form-field><mat-label>{{ 'billing.monthlyPrice' | translate }}</mat-label><input matInput type="number" min="0" [(ngModel)]="f.monthlyPrice" /></mat-form-field>
            <mat-slide-toggle [(ngModel)]="f.isActive">{{ 'common.active' | translate }}</mat-slide-toggle>
          </div>
          <div class="form-actions">
            <button mat-button (click)="form.set(null)">{{ 'common.cancel' | translate }}</button>
            <button mat-flat-button (click)="save(f)" [disabled]="!f.name || !f.monthlyPrice || !f.sessionsPerMonth">{{ 'common.save' | translate }}</button>
          </div>
        </mat-card-content>
      </mat-card>
    }

    @for (group of byCurrency(); track group.currency) {
      <h3 class="section-title">{{ 'currency.' + group.currency | translate }}</h3>
      <div class="table-wrap">
        <table class="data-table">
          <thead>
            <tr>
              <th>{{ 'common.name' | translate }}</th><th>{{ 'billing.sessionsPerMonth' | translate }}</th><th>{{ 'packages.minutes' | translate }}</th>
              <th>{{ 'billing.monthlyPrice' | translate }}</th><th>{{ 'packages.perSession' | translate }}</th><th>{{ 'common.status' | translate }}</th><th></th>
            </tr>
          </thead>
          <tbody>
            @for (p of group.items; track p.id) {
              <tr [class.muted]="!p.isActive">
                <td>{{ p.name }}</td>
                <td class="num">{{ p.sessionsPerMonth }}</td>
                <td class="num">{{ p.sessionMinutes }}</td>
                <td class="num">{{ p.monthlyPrice | number: '1.0-2' }} {{ p.currency }}</td>
                <td class="num">{{ p.pricePerSession | number: '1.0-2' }}</td>
                <td><span class="status" [class.ok]="p.isActive">{{ (p.isActive ? 'common.active' : 'common.inactive') | translate }}</span></td>
                <td class="actions">@if (canManage) { <button mat-button (click)="edit(p)">{{ 'common.edit' | translate }}</button> }</td>
              </tr>
            }
          </tbody>
        </table>
      </div>
    } @empty {
      <p class="empty">{{ 'packages.none' | translate }}</p>
    }
  `,
})
export class PackagesPage implements OnInit {
  private readonly api = inject(ApiService);
  private readonly notify = inject(Notifier);
  protected readonly canManage = inject(AuthService).hasPermission(Permissions.payments.manage);

  protected readonly minuteOptions = [30, 45, 60, 90];
  protected readonly packages = signal<PackageDto[]>([]);
  protected readonly currencies = signal<string[]>(['USD', 'EUR', 'GBP', 'SAR', 'EGP']);
  protected readonly form = signal<PackageForm | null>(null);
  protected gridCurrency = 'USD';
  protected pricePerHour: number | null = null;

  protected readonly byCurrency = computed(() => {
    const map = new Map<string, PackageDto[]>();
    for (const p of this.packages()) {
      map.set(p.currency, [...(map.get(p.currency) ?? []), p]);
    }
    return [...map.entries()].map(([currency, items]) => ({ currency, items }));
  });

  ngOnInit(): void {
    this.load();
    this.api.get<FinanceSettingsDto>(`${Api.finance}/settings`).subscribe((s) => {
      this.currencies.set(s.available);
      this.gridCurrency = s.currency;
    });
  }

  protected edit(p: PackageDto | null): void {
    this.form.set(
      p
        ? { id: p.id, name: p.name, sessionsPerMonth: p.sessionsPerMonth, sessionMinutes: p.sessionMinutes, currency: p.currency, monthlyPrice: p.monthlyPrice, isActive: p.isActive }
        : { id: null, name: '', sessionsPerMonth: 8, sessionMinutes: 30, currency: this.gridCurrency, monthlyPrice: null, isActive: true },
    );
  }

  protected save(f: PackageForm): void {
    const body = { name: f.name, sessionsPerMonth: f.sessionsPerMonth, sessionMinutes: f.sessionMinutes, currency: f.currency, monthlyPrice: f.monthlyPrice, isActive: f.isActive };
    const request = f.id ? this.api.put(`${Api.finance}/packages/${f.id}`, body) : this.api.post(`${Api.finance}/packages`, body);
    request.subscribe({
      next: () => {
        this.notify.saved();
        this.form.set(null);
        this.load();
      },
      error: (e) => this.notify.error(e),
    });
  }

  protected createStandard(): void {
    this.api.post<PackageDto[]>(`${Api.finance}/packages/standard`, {}, { currency: this.gridCurrency, pricePerHour: this.pricePerHour }).subscribe({
      next: () => {
        this.notify.saved();
        this.load();
      },
      error: (e) => this.notify.error(e),
    });
  }

  private load(): void {
    this.api.get<PackageDto[]>(`${Api.finance}/packages`, { includeInactive: true }).subscribe((p) => this.packages.set(p));
  }
}
