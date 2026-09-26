import { Component, inject, OnInit, signal } from '@angular/core';
import { Api, ApiService } from '../../core/api/api.service';
import { ExcuseItemDto, SessionDto } from '../../core/api/models';
import { Notifier } from '../../shared/notifier';
import { PAGE_IMPORTS } from '../../shared/page-imports';
import { OutcomeChip, SessionActions } from '../../shared/sessions-kit';

/**
 * What supervisors and admins have to decide on one-to-one sessions: students' excuses
 * (reschedule, carry over, don't count, deduct) and unexcused absences (count or not).
 */
@Component({
  selector: 'app-requests',
  imports: [PAGE_IMPORTS, OutcomeChip],
  template: `
    <div class="page-header">
      <h1>{{ 'nav.requests' | translate }}</h1>
      <p class="muted">{{ 'requests.hint' | translate }}</p>
    </div>

    <mat-tab-group mat-stretch-tabs="false" animationDuration="200ms">
      <mat-tab>
        <ng-template mat-tab-label>
          {{ 'requests.excuses' | translate }}
          @if (pendingExcuses()) { <span class="count">{{ pendingExcuses() }}</span> }
        </ng-template>
        <div class="tab-body">
          <mat-button-toggle-group [value]="excuseFilter()" (change)="setFilter($event.value)" class="filter">
            <mat-button-toggle value="Pending">{{ 'requests.pending' | translate }}</mat-button-toggle>
            <mat-button-toggle value="">{{ 'common.all' | translate }}</mat-button-toggle>
          </mat-button-toggle-group>
          <div class="table-wrap">
            <table class="data-table">
              <thead><tr>
                <th>{{ 'roles.Student' | translate }}</th><th>{{ 'students.session' | translate }}</th><th>{{ 'roles.Teacher' | translate }}</th>
                <th>{{ 'excuse.reason' | translate }}</th><th>{{ 'excuse.preferredTime' | translate }}</th><th>{{ 'common.status' | translate }}</th><th></th>
              </tr></thead>
              <tbody>
                @for (x of excuses(); track x.excuse.id) {
                  <tr>
                    <td><a [routerLink]="['/students', x.session.studentUserId]">{{ x.session.studentName }}</a>
                      @if (x.requestedByName && x.requestedByName !== x.session.studentName) { <div class="muted">{{ 'requests.by' | translate }} {{ x.requestedByName }}</div> }</td>
                    <td><b>{{ x.session.title }}</b><div class="muted ltr">{{ x.session.startsAtUtc | utcDate: 'EEE d MMM, HH:mm' }}</div></td>
                    <td>{{ x.session.teacherName }}</td>
                    <td>{{ x.excuse.reason || '—' }}</td>
                    <td class="ltr">{{ (x.excuse.preferredStartsAtUtc | utcDate: 'EEE d MMM, HH:mm') ?? '—' }}</td>
                    <td><app-outcome [session]="x.session" /></td>
                    <td class="actions">
                      @if (x.excuse.status === 'Pending') {
                        <button mat-flat-button (click)="resolve(x)"><mat-icon>gavel</mat-icon>{{ 'requests.decide' | translate }}</button>
                      }
                    </td>
                  </tr>
                } @empty {
                  <tr><td colspan="7" class="empty"><mat-icon>task_alt</mat-icon><div>{{ 'requests.noExcuses' | translate }}</div></td></tr>
                }
              </tbody>
            </table>
          </div>
        </div>
      </mat-tab>

      <mat-tab>
        <ng-template mat-tab-label>
          {{ 'requests.absences' | translate }}
          @if (absences().length) { <span class="count">{{ absences().length }}</span> }
        </ng-template>
        <div class="tab-body">
          <p class="muted">{{ 'requests.absencesHint' | translate }}</p>
          <div class="table-wrap">
            <table class="data-table">
              <thead><tr>
                <th>{{ 'roles.Student' | translate }}</th><th>{{ 'students.session' | translate }}</th><th>{{ 'roles.Teacher' | translate }}</th><th></th>
              </tr></thead>
              <tbody>
                @for (s of absences(); track s.id) {
                  <tr>
                    <td><a [routerLink]="['/students', s.studentUserId]">{{ s.studentName }}</a></td>
                    <td><b>{{ s.title }}</b><div class="muted ltr">{{ s.startsAtUtc | utcDate: 'EEE d MMM, HH:mm' }}</div></td>
                    <td>{{ s.teacherName }}</td>
                    <td class="actions">
                      <button mat-flat-button (click)="decide(s, true)"><mat-icon>check</mat-icon>{{ 'requests.counts' | translate }}</button>
                      <button mat-stroked-button (click)="decide(s, false)"><mat-icon>block</mat-icon>{{ 'requests.notCounted' | translate }}</button>
                    </td>
                  </tr>
                } @empty {
                  <tr><td colspan="4" class="empty"><mat-icon>task_alt</mat-icon><div>{{ 'requests.noAbsences' | translate }}</div></td></tr>
                }
              </tbody>
            </table>
          </div>
        </div>
      </mat-tab>
    </mat-tab-group>
  `,
  styles: `
    .tab-body { margin-top: 16px; }
    .filter { margin-bottom: 12px; }
    .count { display: inline-grid; place-items: center; min-width: 20px; height: 20px; padding: 0 6px; margin-inline-start: 8px; border-radius: 10px;
      font-size: 0.72rem; font-weight: 700; color: #fff; background: var(--tone-rose); }
    td.empty mat-icon { font-size: 36px; width: 36px; height: 36px; color: var(--ok-fg); }
  `,
})
export class RequestsPage implements OnInit {
  private readonly api = inject(ApiService);
  private readonly notify = inject(Notifier);
  private readonly actions = inject(SessionActions);

  protected readonly excuseFilter = signal<'Pending' | ''>('Pending');
  protected readonly excuses = signal<ExcuseItemDto[]>([]);
  protected readonly absences = signal<SessionDto[]>([]);
  protected readonly pendingExcuses = signal(0);

  ngOnInit(): void {
    this.loadExcuses();
    this.loadAbsences();
  }

  protected setFilter(value: 'Pending' | ''): void {
    this.excuseFilter.set(value);
    this.loadExcuses();
  }

  protected resolve(x: ExcuseItemDto): void {
    this.actions.resolve(x.excuse, x.session).subscribe({ next: () => this.loadExcuses(), error: (e) => this.notify.error(e) });
  }

  protected decide(s: SessionDto, counted: boolean): void {
    this.actions.decideAbsence(s, counted).subscribe({
      next: () => {
        this.notify.saved();
        this.absences.update((list) => list.filter((x) => x.id !== s.id));
      },
      error: (e) => this.notify.error(e),
    });
  }

  private loadExcuses(): void {
    this.api.get<ExcuseItemDto[]>(`${Api.academic}/excuses`, { status: this.excuseFilter() || null }).subscribe((list) => {
      this.excuses.set(list);
      if (this.excuseFilter() === 'Pending') {
        this.pendingExcuses.set(list.length);
      }
    });
  }

  private loadAbsences(): void {
    this.api.get<SessionDto[]>(`${Api.academic}/absences/pending`).subscribe((list) => this.absences.set(list));
  }
}
