import { Component, inject, OnInit, signal } from '@angular/core';
import { PagedResult } from '../../core/api/api.models';
import { Api, ApiService } from '../../core/api/api.service';
import { AuditLogDto } from '../../core/api/models';
import { PAGE_IMPORTS } from '../../shared/page-imports';

/** Who did what and when: finance, permissions, subscriptions (US-043). */
@Component({
  selector: 'app-audit',
  imports: [PAGE_IMPORTS],
  template: `
    <div class="page-header"><h1>{{ 'nav.audit' | translate }}</h1></div>
    <div class="toolbar">
      <mat-form-field>
        <mat-label>{{ 'audit.service' | translate }}</mat-label>
        <mat-select [(ngModel)]="service" (selectionChange)="load(1)">
          <mat-option [value]="''">{{ 'common.all' | translate }}</mat-option>
          @for (s of services; track s) { <mat-option [value]="s">{{ s }}</mat-option> }
        </mat-select>
      </mat-form-field>
      <mat-form-field><mat-label>{{ 'audit.action' | translate }}</mat-label><input matInput [(ngModel)]="action" (keyup.enter)="load(1)" dir="ltr" placeholder="payments." /></mat-form-field>
      <mat-form-field><mat-label>{{ 'common.from' | translate }}</mat-label><input matInput type="date" [(ngModel)]="from" /></mat-form-field>
      <mat-form-field><mat-label>{{ 'common.to' | translate }}</mat-label><input matInput type="date" [(ngModel)]="to" /></mat-form-field>
      <button mat-stroked-button (click)="load(1)">{{ 'common.apply' | translate }}</button>
    </div>
    <div class="table-wrap">
      <table class="data-table">
        <thead><tr><th>{{ 'audit.when' | translate }}</th><th>{{ 'audit.who' | translate }}</th><th>{{ 'audit.service' | translate }}</th><th>{{ 'audit.action' | translate }}</th><th>{{ 'audit.entity' | translate }}</th><th>{{ 'audit.data' | translate }}</th></tr></thead>
        <tbody>
          @for (a of page()?.items ?? []; track a.id) {
            <tr>
              <td>{{ a.occurredOnUtc | date: 'short' }}</td>
              <td>{{ a.userName ?? (a.userId ? '#' + a.userId : ('audit.system' | translate)) }}</td>
              <td>{{ a.service }}</td>
              <td class="ltr">{{ a.action }}</td>
              <td class="ltr">{{ a.entityName }} {{ a.entityId }}</td>
              <td class="ltr data">{{ a.dataJson }}</td>
            </tr>
          } @empty {
            <tr><td colspan="6" class="empty">{{ 'common.noData' | translate }}</td></tr>
          }
        </tbody>
      </table>
    </div>
    @if (page(); as p) {
      <div class="form-actions">
        <button mat-button [disabled]="p.page <= 1" (click)="load(p.page - 1)">{{ 'common.previous' | translate }}</button>
        <span class="muted">{{ p.page }} / {{ p.totalPages || 1 }}</span>
        <button mat-button [disabled]="p.page >= p.totalPages" (click)="load(p.page + 1)">{{ 'common.next' | translate }}</button>
      </div>
    }
  `,
  styles: `.data { max-width: 420px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; font-size: 0.8rem; }`,
})
export class AuditPage implements OnInit {
  private readonly api = inject(ApiService);
  protected readonly services = ['identity', 'subscription', 'academic', 'finance', 'engagement'];
  protected readonly page = signal<PagedResult<AuditLogDto> | null>(null);
  protected service = '';
  protected action = '';
  protected from = '';
  protected to = '';

  ngOnInit(): void {
    this.load(1);
  }

  protected load(page: number): void {
    this.api
      .get<PagedResult<AuditLogDto>>(`${Api.engagement}/audit-logs`, {
        service: this.service, action: this.action, from: this.from, to: this.to, page, pageSize: 50,
      })
      .subscribe((p) => this.page.set(p));
  }
}
