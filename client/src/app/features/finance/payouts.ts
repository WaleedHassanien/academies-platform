import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { PagedResult } from '../../core/api/api.models';
import { Api, ApiService } from '../../core/api/api.service';
import { MonthCloseResultDto, PayoutDto, StaffProfileDto, StudentDto, TeacherRateDto, UnpaidTeacherDto } from '../../core/api/models';
import { Notifier } from '../../shared/notifier';
import { PAGE_IMPORTS } from '../../shared/page-imports';
import { Stat } from '../../shared/ui';

interface TeacherCard extends UnpaidTeacherDto {
  open: boolean;
  selected: Set<number>;
  reference: string;
}

/**
 * Teacher pay per session: what each teacher is owed right now (send a transfer for chosen
 * sessions any day of the month), the month's payouts (month-end ones are built automatically
 * in the last hour of the month and wait for "transferred"), and each teacher's rate per student.
 */
@Component({
  selector: 'app-payouts',
  imports: [PAGE_IMPORTS, Stat],
  template: `
    <div class="page-header">
      <h1>{{ 'nav.payouts' | translate }}</h1>
      <div class="toolbar">
        <mat-form-field><mat-label>{{ 'payouts.month' | translate }}</mat-label><input matInput type="month" [(ngModel)]="month" (change)="loadPayouts()" /></mat-form-field>
        <button mat-stroked-button (click)="generateInvoices()"><mat-icon>receipt_long</mat-icon>{{ 'payouts.invoices' | translate }}</button>
        <button mat-flat-button (click)="closeMonth()"><mat-icon>event_available</mat-icon>{{ 'payouts.closeMonth' | translate }}</button>
      </div>
      <p class="muted">{{ 'payouts.hint' | translate }}</p>
    </div>

    <div class="stats">
      <app-stat icon="pending" [label]="'payouts.owedNow' | translate" [value]="(owedTotal() | number: '1.0-2') + ' ' + currency()" />
      <app-stat icon="event_note" [label]="'payouts.unpaidSessions' | translate" [value]="owedSessions()" />
      <app-stat icon="hourglass_top" [label]="'payouts.awaitingTransfer' | translate" [value]="(pendingTotal() | number: '1.0-2') + ' ' + currency()" />
      <app-stat icon="paid" [label]="'payouts.paidThisMonth' | translate" [value]="(paidTotal() | number: '1.0-2') + ' ' + currency()" />
    </div>

    <mat-tab-group mat-stretch-tabs="false" animationDuration="200ms">
      <mat-tab [label]="'payouts.owed' | translate">
        <div class="tab-body cards">
          @for (t of cards(); track t.teacherUserId) {
            <section class="teacher" [class.open]="t.open">
              <button class="head" (click)="t.open = !t.open">
                <span class="avatar">{{ initials(t.teacherName) }}</span>
                <span class="who"><b>{{ t.teacherName }}</b><small class="muted">{{ t.sessions.length }} {{ 'payouts.sessions' | translate }}</small></span>
                @if (t.missingRates) { <span class="status warn">{{ t.missingRates }} {{ 'payouts.noRate' | translate }}</span> }
                <b class="amount ltr">{{ t.total | number: '1.0-2' }} {{ t.currency }}</b>
                <mat-icon class="chev">expand_more</mat-icon>
              </button>
              @if (t.open) {
                <div class="body">
                  <div class="table-wrap flat">
                    <table class="data-table">
                      <thead><tr>
                        <th><mat-checkbox [checked]="allChecked(t)" [indeterminate]="t.selected.size > 0 && !allChecked(t)" (change)="toggleAll(t, $event.checked)" /></th>
                        <th>{{ 'common.date' | translate }}</th><th>{{ 'roles.Student' | translate }}</th><th>{{ 'sessions.duration' | translate }}</th>
                        <th>{{ 'common.status' | translate }}</th><th class="num">{{ 'payouts.rate' | translate }}</th>
                      </tr></thead>
                      <tbody>
                        @for (s of t.sessions; track s.sessionId) {
                          <tr>
                            <td><mat-checkbox [disabled]="s.rate === null" [checked]="t.selected.has(s.sessionId)" (change)="toggle(t, s.sessionId, $event.checked)" /></td>
                            <td class="ltr">{{ s.startsAtUtc | utcDate: 'EEE d MMM, HH:mm' }}</td>
                            <td>{{ s.studentName }}</td>
                            <td>{{ s.durationMinutes }} {{ 'mySessions.min' | translate }}</td>
                            <td><span class="status" [class.ok]="s.outcome === 'Held'" [class.bad]="s.outcome !== 'Held'">{{ 'outcome.' + outcomeKey(s.outcome) | translate }}</span></td>
                            <td class="num">@if (s.rate !== null) { {{ s.rate | number: '1.0-2' }} } @else { <a (click)="rateFor(t.teacherUserId, s.studentUserId)" class="link">{{ 'payouts.setRate' | translate }}</a> }</td>
                          </tr>
                        }
                      </tbody>
                    </table>
                  </div>
                  <div class="pay">
                    <mat-form-field><mat-label>{{ 'payouts.reference' | translate }}</mat-label><input matInput [(ngModel)]="t.reference" /></mat-form-field>
                    <span class="sum">{{ 'payouts.selected' | translate }}: <b class="ltr">{{ selectedTotal(t) | number: '1.0-2' }} {{ t.currency }}</b> ({{ t.selected.size }})</span>
                    <button mat-flat-button (click)="payNow(t)" [disabled]="!t.selected.size"><mat-icon>send</mat-icon>{{ 'payouts.payNow' | translate }}</button>
                  </div>
                </div>
              }
            </section>
          } @empty {
            <p class="empty">{{ 'payouts.nothingOwed' | translate }}</p>
          }
        </div>
      </mat-tab>

      <mat-tab [label]="'payouts.history' | translate">
        <div class="tab-body table-wrap">
          <table class="data-table">
            <thead><tr>
              <th>{{ 'roles.Teacher' | translate }}</th><th>{{ 'common.type' | translate }}</th><th class="num">{{ 'payouts.sessions' | translate }}</th>
              <th class="num">{{ 'common.amount' | translate }}</th><th>{{ 'common.status' | translate }}</th><th>{{ 'payouts.paidOn' | translate }}</th><th></th>
            </tr></thead>
            <tbody>
              @for (p of payouts(); track p.id) {
                <tr>
                  <td>{{ p.teacherName }}</td>
                  <td>{{ 'payouts.kind_' + p.kind | translate }}</td>
                  <td class="num">{{ p.sessionsCount }}</td>
                  <td class="num">{{ p.amount | number: '1.0-2' }} {{ p.currency }}</td>
                  <td><span class="status" [class.ok]="p.status === 'Paid'" [class.warn]="p.status === 'Pending'">{{ 'payouts.status_' + p.status | translate }}</span></td>
                  <td class="ltr">{{ (p.paidOnUtc | utcDate: 'd MMM, HH:mm') ?? '—' }}@if (p.reference) { <div class="muted">{{ p.reference }}</div> }</td>
                  <td class="actions">
                    <button mat-button (click)="view(p)">{{ 'students.view' | translate }}</button>
                    @if (p.status === 'Pending') { <button mat-flat-button (click)="markPaid(p)"><mat-icon>done_all</mat-icon>{{ 'payouts.markPaid' | translate }}</button> }
                  </td>
                </tr>
                @if (opened()?.id === p.id && opened()?.lines) {
                  <tr class="lines"><td colspan="7">
                    @for (l of opened()!.lines!; track l.sessionId) {
                      <div class="line"><span class="ltr">{{ l.sessionStartsAtUtc | utcDate: 'EEE d MMM, HH:mm' }}</span><span>{{ l.studentName }}</span><b class="ltr">{{ l.rate | number: '1.0-2' }}</b></div>
                    }
                  </td></tr>
                }
              } @empty {
                <tr><td colspan="7" class="empty">{{ 'common.noData' | translate }}</td></tr>
              }
            </tbody>
          </table>
        </div>
      </mat-tab>

      <mat-tab [label]="'payouts.rates' | translate">
        <div class="tab-body">
          <mat-card appearance="outlined" class="panel">
            <mat-card-content>
              <div class="form-grid">
                <mat-form-field><mat-label>{{ 'roles.Teacher' | translate }}</mat-label>
                  <mat-select [(ngModel)]="rateTeacher">@for (t of teachers(); track t.userId) { <mat-option [value]="t.userId">{{ t.fullName }}</mat-option> }</mat-select>
                </mat-form-field>
                <mat-form-field><mat-label>{{ 'roles.Student' | translate }}</mat-label>
                  <mat-select [(ngModel)]="rateStudent">@for (s of students(); track s.userId) { <mat-option [value]="s.userId">{{ s.fullName }}</mat-option> }</mat-select>
                </mat-form-field>
                <mat-form-field><mat-label>{{ 'payouts.rate' | translate }}</mat-label><input matInput type="number" min="0" [(ngModel)]="rateValue" /></mat-form-field>
              </div>
              <div class="form-actions"><button mat-flat-button (click)="saveRate()" [disabled]="!rateTeacher || !rateStudent || rateValue === null">{{ 'common.save' | translate }}</button></div>
            </mat-card-content>
          </mat-card>
          <div class="table-wrap">
            <table class="data-table">
              <thead><tr><th>{{ 'roles.Teacher' | translate }}</th><th>{{ 'roles.Student' | translate }}</th><th class="num">{{ 'payouts.rate' | translate }}</th><th></th></tr></thead>
              <tbody>
                @for (r of rates(); track r.teacherUserId + '-' + r.studentUserId) {
                  <tr>
                    <td>{{ r.teacherName }}</td><td>{{ r.studentName }}</td><td class="num">{{ r.ratePerSession | number: '1.0-2' }}</td>
                    <td class="actions"><button mat-button (click)="rateFor(r.teacherUserId, r.studentUserId, r.ratePerSession)">{{ 'common.edit' | translate }}</button></td>
                  </tr>
                } @empty {
                  <tr><td colspan="4" class="empty">{{ 'common.noData' | translate }}</td></tr>
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
    .cards { display: flex; flex-direction: column; gap: 12px; }
    .teacher { border-radius: var(--app-radius); background: var(--app-surface); border: 1px solid var(--app-border); box-shadow: var(--app-shadow);
      overflow: hidden; animation: rise-in 0.4s cubic-bezier(0.2, 0.7, 0.2, 1) both; }
    .teacher.open { border-color: color-mix(in srgb, var(--mat-sys-primary) 35%, var(--app-border)); }
    .head { display: flex; align-items: center; gap: 12px; width: 100%; padding: 14px 18px; border: 0; background: none; color: inherit; font: inherit;
      cursor: pointer; text-align: start; }
    .head:hover { background: var(--app-surface-2); }
    .avatar { display: grid; place-items: center; width: 40px; height: 40px; flex: none; border-radius: 50%; color: #fff; font-weight: 700; font-size: 0.85rem;
      background: var(--app-gradient); }
    .who { display: flex; flex-direction: column; flex: 1; min-width: 0; }
    .amount { font-size: 1.1rem; }
    .chev { transition: transform 0.2s ease; color: var(--app-muted); }
    .open .chev { transform: rotate(180deg); }
    .body { padding: 0 18px 16px; animation: fade-in 0.2s ease both; }
    .table-wrap.flat { box-shadow: none; }
    .pay { display: flex; align-items: center; justify-content: flex-end; flex-wrap: wrap; gap: 8px 16px; margin-top: 12px; }
    .pay mat-form-field { flex: 0 1 220px; }
    .link { cursor: pointer; font-weight: 600; }
    tr.lines td { background: var(--app-surface-2); }
    .line { display: grid; grid-template-columns: 160px 1fr auto; gap: 12px; padding: 4px 0; font-size: 0.85rem; }
    @media (max-width: 639px) { .head { flex-wrap: wrap; } .amount { margin-inline-start: 52px; } }
  `,
})
export class PayoutsPage implements OnInit {
  private readonly api = inject(ApiService);
  private readonly notify = inject(Notifier);

  protected month = new Date().toISOString().slice(0, 7);
  protected readonly cards = signal<TeacherCard[]>([]);
  protected readonly payouts = signal<PayoutDto[]>([]);
  protected readonly opened = signal<PayoutDto | null>(null);
  protected readonly rates = signal<TeacherRateDto[]>([]);
  protected readonly teachers = signal<StaffProfileDto[]>([]);
  protected readonly students = signal<StudentDto[]>([]);
  protected rateTeacher: number | null = null;
  protected rateStudent: number | null = null;
  protected rateValue: number | null = null;

  protected readonly currency = computed(() => this.cards()[0]?.currency ?? this.payouts()[0]?.currency ?? '');
  protected readonly owedTotal = computed(() => this.cards().reduce((a, t) => a + t.total, 0));
  protected readonly owedSessions = computed(() => this.cards().reduce((a, t) => a + t.sessions.length, 0));
  protected readonly pendingTotal = computed(() => this.payouts().filter((p) => p.status === 'Pending').reduce((a, p) => a + p.amount, 0));
  protected readonly paidTotal = computed(() => this.payouts().filter((p) => p.status === 'Paid').reduce((a, p) => a + p.amount, 0));

  ngOnInit(): void {
    this.loadUnpaid();
    this.loadPayouts();
    this.loadRates();
    this.api.get<StaffProfileDto[]>(`${Api.academic}/teachers`).subscribe((t) => this.teachers.set(t));
    this.api.get<PagedResult<StudentDto>>(`${Api.academic}/students`, { pageSize: 100 }).subscribe((r) => this.students.set(r.items));
  }

  protected initials(name: string | null): string {
    return (name ?? '?').trim().split(/\s+/).slice(0, 2).map((p) => p[0]).join('').toUpperCase();
  }

  protected outcomeKey(outcome: string): string {
    return outcome.charAt(0).toLowerCase() + outcome.slice(1);
  }

  protected allChecked(t: TeacherCard): boolean {
    const payable = t.sessions.filter((s) => s.rate !== null);
    return payable.length > 0 && payable.every((s) => t.selected.has(s.sessionId));
  }

  protected toggleAll(t: TeacherCard, on: boolean): void {
    t.selected = new Set(on ? t.sessions.filter((s) => s.rate !== null).map((s) => s.sessionId) : []);
    this.cards.update((c) => [...c]);
  }

  protected toggle(t: TeacherCard, id: number, on: boolean): void {
    const next = new Set(t.selected);
    if (on) {
      next.add(id);
    } else {
      next.delete(id);
    }
    t.selected = next;
    this.cards.update((c) => [...c]);
  }

  protected selectedTotal(t: TeacherCard): number {
    return t.sessions.filter((s) => t.selected.has(s.sessionId)).reduce((a, s) => a + (s.rate ?? 0), 0);
  }

  protected payNow(t: TeacherCard): void {
    this.api
      .post<PayoutDto>(`${Api.finance}/payouts/pay-now`, { teacherUserId: t.teacherUserId, sessionIds: [...t.selected], reference: t.reference || null, note: null })
      .subscribe({
        next: () => {
          this.notify.info('payouts.sent');
          this.loadUnpaid();
          this.loadPayouts();
        },
        error: (e) => this.notify.error(e),
      });
  }

  protected markPaid(p: PayoutDto): void {
    const reference = prompt(this.notify.text('payouts.referencePrompt')) ?? null;
    this.api.post<PayoutDto>(`${Api.finance}/payouts/${p.id}/mark-paid`, { reference }).subscribe({
      next: () => {
        this.notify.saved();
        this.loadPayouts();
      },
      error: (e) => this.notify.error(e),
    });
  }

  protected view(p: PayoutDto): void {
    if (this.opened()?.id === p.id) {
      this.opened.set(null);
      return;
    }
    this.api.get<PayoutDto>(`${Api.finance}/payouts/${p.id}`).subscribe((full) => this.opened.set(full));
  }

  protected closeMonth(): void {
    const [year, month] = this.month.split('-').map(Number);
    this.api.post<MonthCloseResultDto>(`${Api.finance}/month-close`, {}, { year, month }).subscribe({
      next: (r) => {
        this.notify.info('payouts.closed', { payouts: r.payouts, invoices: r.invoicesCreated + r.invoicesUpdated, missing: r.sessionsMissingRates });
        this.loadUnpaid();
        this.loadPayouts();
      },
      error: (e) => this.notify.error(e),
    });
  }

  protected generateInvoices(): void {
    const [year, month] = this.month.split('-').map(Number);
    this.api.post<{ created: number; updated: number }>(`${Api.finance}/invoices/generate`, {}, { year, month }).subscribe({
      next: (r) => this.notify.info('payouts.invoicesDone', { created: r.created, updated: r.updated }),
      error: (e) => this.notify.error(e),
    });
  }

  protected rateFor(teacherUserId: number, studentUserId: number, current: number | null = null): void {
    this.rateTeacher = teacherUserId;
    this.rateStudent = studentUserId;
    this.rateValue = current;
    this.notify.info('payouts.editRateHint');
  }

  protected saveRate(): void {
    this.api
      .put<TeacherRateDto>(`${Api.finance}/teachers/${this.rateTeacher}/rates/${this.rateStudent}`, { ratePerSession: this.rateValue })
      .subscribe({
        next: () => {
          this.notify.saved();
          this.rateValue = null;
          this.loadRates();
          this.loadUnpaid();
        },
        error: (e) => this.notify.error(e),
      });
  }

  protected loadPayouts(): void {
    const [year, month] = this.month.split('-').map(Number);
    this.api.get<PayoutDto[]>(`${Api.finance}/payouts`, { year, month }).subscribe((p) => this.payouts.set(p));
  }

  private loadUnpaid(): void {
    this.api.get<UnpaidTeacherDto[]>(`${Api.finance}/payouts/unpaid`).subscribe((list) =>
      this.cards.set(
        list.map((t) => ({
          ...t, open: false, reference: '',
          selected: new Set(t.sessions.filter((s) => s.rate !== null).map((s) => s.sessionId)),
        })),
      ),
    );
  }

  private loadRates(): void {
    this.api.get<TeacherRateDto[]>(`${Api.finance}/teacher-rates`).subscribe((r) => this.rates.set(r));
  }
}
