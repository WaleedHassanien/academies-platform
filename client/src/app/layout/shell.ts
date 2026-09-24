import { BreakpointObserver, Breakpoints } from '@angular/cdk/layout';
import { Component, computed, DestroyRef, inject, OnInit, signal } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { MatBadgeModule } from '@angular/material/badge';
import { MatButtonModule } from '@angular/material/button';
import { MatDividerModule } from '@angular/material/divider';
import { MatIconModule } from '@angular/material/icon';
import { MatListModule } from '@angular/material/list';
import { MatMenuModule } from '@angular/material/menu';
import { MatSidenavModule } from '@angular/material/sidenav';
import { MatToolbarModule } from '@angular/material/toolbar';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { catchError, filter, interval, map, of, startWith, switchMap } from 'rxjs';
import { Api, ApiService } from '../core/api/api.service';
import { AuthService } from '../core/auth/auth.service';
import { Permissions, Roles } from '../core/auth/permissions';
import { LanguageService } from '../core/i18n/language.service';

interface NavItem {
  labelKey: string;
  icon: string;
  link: string;
  /** Shown only with this permission. */
  permission?: string;
  /** Shown only to one of these roles. */
  roles?: string[];
}

interface NavSection {
  titleKey: string;
  items: NavItem[];
}

const SECTIONS: NavSection[] = [
  {
    titleKey: 'navSection.platform',
    items: [
      { labelKey: 'platform.academies', icon: 'domain', link: '/platform', permission: Permissions.academies.manage },
      { labelKey: 'platform.plans', icon: 'workspace_premium', link: '/platform/plans', permission: Permissions.plans.manage },
    ],
  },
  {
    titleKey: 'navSection.academy',
    items: [
      { labelKey: 'nav.dashboard', icon: 'insights', link: '/dashboard', permission: Permissions.dashboards.view },
      { labelKey: 'nav.users', icon: 'group', link: '/users', permission: Permissions.users.view },
      { labelKey: 'nav.students', icon: 'school', link: '/students', permission: Permissions.profiles.view },
      { labelKey: 'nav.staff', icon: 'badge', link: '/staff', permission: Permissions.profiles.view },
      { labelKey: 'nav.groups', icon: 'groups', link: '/groups', permission: Permissions.profiles.view },
      { labelKey: 'nav.subscription', icon: 'card_membership', link: '/subscription', permission: Permissions.plans.view },
      { labelKey: 'nav.audit', icon: 'manage_search', link: '/audit', permission: Permissions.auditLogs.view },
    ],
  },
  {
    titleKey: 'navSection.learning',
    items: [
      { labelKey: 'nav.courses', icon: 'menu_book', link: '/courses', permission: Permissions.courses.view },
      { labelKey: 'nav.sessions', icon: 'event', link: '/sessions', permission: Permissions.sessions.view },
      { labelKey: 'nav.assignments', icon: 'assignment', link: '/assignments', permission: Permissions.courses.view },
      { labelKey: 'nav.certificates', icon: 'verified', link: '/certificates', permission: Permissions.courses.view },
    ],
  },
  {
    titleKey: 'navSection.finance',
    items: [
      { labelKey: 'nav.payments', icon: 'payments', link: '/payments', permission: Permissions.payments.manage },
      { labelKey: 'nav.paymentLogs', icon: 'receipt_long', link: '/payment-logs', permission: Permissions.payments.view },
      { labelKey: 'nav.pay', icon: 'price_change', link: '/pay', permission: Permissions.salaries.manage },
      { labelKey: 'nav.salaries', icon: 'account_balance_wallet', link: '/salaries', permission: Permissions.salaries.manage },
      { labelKey: 'nav.expenses', icon: 'shopping_cart', link: '/expenses', permission: Permissions.expenses.manage },
      { labelKey: 'nav.reports', icon: 'bar_chart', link: '/reports', permission: Permissions.reports.view },
    ],
  },
  {
    titleKey: 'navSection.me',
    items: [
      { labelKey: 'nav.parentPortal', icon: 'family_restroom', link: '/parent', roles: [Roles.Parent] },
      { labelKey: 'nav.me', icon: 'person', link: '/me', permission: Permissions.salaries.view },
      { labelKey: 'nav.notifications', icon: 'notifications', link: '/notifications' },
    ],
  },
];

@Component({
  selector: 'app-shell',
  imports: [
    RouterOutlet, RouterLink, RouterLinkActive, TranslatePipe, MatToolbarModule, MatSidenavModule, MatListModule,
    MatButtonModule, MatIconModule, MatMenuModule, MatBadgeModule, MatDividerModule,
  ],
  template: `
    <mat-sidenav-container class="shell">
      <mat-sidenav #nav [mode]="isHandset() ? 'over' : 'side'" [opened]="!isHandset()" class="shell-nav">
        <div class="brand">{{ 'app.name' | translate }}</div>
        <mat-nav-list>
          <a mat-list-item [routerLink]="auth.homeUrl()" routerLinkActive #home="routerLinkActive" [activated]="home.isActive" (click)="isHandset() && nav.close()">
            <mat-icon matListItemIcon>home</mat-icon><span matListItemTitle>{{ 'nav.home' | translate }}</span>
          </a>
          @for (section of sections(); track section.titleKey) {
            <div mat-subheader>{{ section.titleKey | translate }}</div>
            @for (item of section.items; track item.link) {
              <a mat-list-item [routerLink]="item.link" routerLinkActive #rla="routerLinkActive" [routerLinkActiveOptions]="{ exact: true }"
                 [activated]="rla.isActive" (click)="isHandset() && nav.close()">
                <mat-icon matListItemIcon>{{ item.icon }}</mat-icon>
                <span matListItemTitle>{{ item.labelKey | translate }}</span>
              </a>
            }
          }
        </mat-nav-list>
      </mat-sidenav>

      <mat-sidenav-content>
        <mat-toolbar class="shell-toolbar">
          @if (isHandset()) {
            <button mat-icon-button (click)="nav.toggle()" [attr.aria-label]="'app.menu' | translate"><mat-icon>menu</mat-icon></button>
          }
          <span class="spacer"></span>
          <button mat-icon-button routerLink="/notifications" [attr.aria-label]="'nav.notifications' | translate">
            <mat-icon [matBadge]="unread() || null" matBadgeColor="warn" matBadgeSize="small">notifications</mat-icon>
          </button>
          <button mat-button (click)="language.toggle()">{{ 'app.switchLanguage' | translate }}</button>
          <button mat-button [matMenuTriggerFor]="userMenu"><mat-icon>account_circle</mat-icon>{{ auth.user()?.fullName }}</button>
          <mat-menu #userMenu="matMenu">
            <div class="menu-roles">@for (r of auth.user()?.roles ?? []; track r) { <span>{{ 'roles.' + r | translate }}</span> }</div>
            <mat-divider />
            <button mat-menu-item (click)="auth.logout()"><mat-icon>logout</mat-icon><span>{{ 'auth.logout' | translate }}</span></button>
          </mat-menu>
        </mat-toolbar>
        <main class="shell-main"><router-outlet /></main>
      </mat-sidenav-content>
    </mat-sidenav-container>
  `,
  styles: `
    .shell { height: 100%; }
    .shell-nav { width: 256px; }
    .brand { font-weight: 700; font-size: 1.1rem; padding: 20px 16px 12px; }
    .shell-toolbar { position: sticky; top: 0; z-index: 2; gap: 4px; background: var(--mat-sys-surface); border-bottom: 1px solid var(--mat-sys-outline-variant); }
    .spacer { flex: 1; }
    .shell-main { padding: 24px; max-width: 1280px; }
    .menu-roles { padding: 8px 16px; display: flex; gap: 6px; flex-wrap: wrap; font-size: 0.8rem; color: var(--mat-sys-on-surface-variant); }
    @media (max-width: 599px) { .shell-main { padding: 16px; } }
  `,
})
export class Shell implements OnInit {
  protected readonly auth = inject(AuthService);
  protected readonly language = inject(LanguageService);
  private readonly api = inject(ApiService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly unread = signal(0);
  protected readonly isHandset = toSignal(
    inject(BreakpointObserver).observe(Breakpoints.Handset).pipe(map((r) => r.matches)),
    { initialValue: false },
  );

  /** Menu filtered by the caller's permissions and roles; empty sections are hidden. */
  protected readonly sections = computed(() => {
    this.auth.user();
    return SECTIONS.map((s) => ({
      ...s,
      items: s.items.filter(
        (i) => (!i.permission || this.auth.hasPermission(i.permission)) && (!i.roles || this.auth.hasAnyRole(...i.roles)),
      ),
    })).filter((s) => s.items.length > 0);
  });

  ngOnInit(): void {
    // Poll the unread count every minute, and refresh it after each navigation.
    const navigations = this.router.events.pipe(filter((e) => e instanceof NavigationEnd));
    interval(60_000)
      .pipe(
        startWith(0),
        switchMap(() => this.api.get<number>(`${Api.engagement}/notifications/me/unread-count`).pipe(catchError(() => of(0)))),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((n) => this.unread.set(n));
    navigations
      .pipe(
        switchMap(() => this.api.get<number>(`${Api.engagement}/notifications/me/unread-count`).pipe(catchError(() => of(0)))),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((n) => this.unread.set(n));
  }
}
