import { BreakpointObserver } from '@angular/cdk/layout';
import { afterNextRender, Component, computed, DestroyRef, inject, OnInit, signal, viewChild } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { MatBadgeModule } from '@angular/material/badge';
import { MatButtonModule } from '@angular/material/button';
import { MatDividerModule } from '@angular/material/divider';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatSidenav, MatSidenavContent, MatSidenavModule } from '@angular/material/sidenav';
import { MatTooltipModule } from '@angular/material/tooltip';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { catchError, distinctUntilChanged, filter, interval, map, of, startWith, switchMap } from 'rxjs';
import { Api, ApiService } from '../core/api/api.service';
import { AuthService } from '../core/auth/auth.service';
import { Permissions, Roles } from '../core/auth/permissions';
import { LanguageService } from '../core/i18n/language.service';
import { ThemeService } from '../core/theme/theme.service';
import { CommandPalette, PaletteItem } from './command-palette';

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
      {
        labelKey: 'nav.requests', icon: 'pending_actions', link: '/requests', permission: Permissions.sessions.view,
        roles: [Roles.Admin, Roles.Manager, Roles.Supervisor],
      },
      { labelKey: 'nav.assignments', icon: 'assignment', link: '/assignments', permission: Permissions.courses.view },
      { labelKey: 'nav.certificates', icon: 'verified', link: '/certificates', permission: Permissions.courses.view },
    ],
  },
  {
    titleKey: 'navSection.finance',
    items: [
      { labelKey: 'nav.payments', icon: 'payments', link: '/payments', permission: Permissions.payments.manage },
      { labelKey: 'nav.paymentLogs', icon: 'receipt_long', link: '/payment-logs', permission: Permissions.payments.view },
      { labelKey: 'nav.payouts', icon: 'send_money', link: '/payouts', permission: Permissions.salaries.manage },
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

const COLLAPSED_KEY = 'academies.navCollapsed';

@Component({
  selector: 'app-shell',
  imports: [
    RouterOutlet, RouterLink, RouterLinkActive, TranslatePipe, MatSidenavModule, MatButtonModule, MatIconModule, MatMenuModule,
    MatBadgeModule, MatDividerModule, MatTooltipModule, CommandPalette,
  ],
  template: `
    <mat-sidenav-container class="shell" [class.collapsed]="railed()" autosize>
      <mat-sidenav #nav [mode]="isDesktop() ? 'side' : 'over'" [opened]="isDesktop()" class="shell-nav">
        <div class="nav-inner">
          <a class="brand" [routerLink]="auth.homeUrl()" (click)="closeIfOverlay()">
            <span class="logo"><mat-icon>school</mat-icon></span>
            <span class="brand-text">
              <b>{{ 'app.name' | translate }}</b>
              <small>{{ 'app.tagline' | translate }}</small>
            </span>
          </a>

          <nav class="nav-scroll">
            <a class="nav-link" [routerLink]="auth.homeUrl()" routerLinkActive="active" [routerLinkActiveOptions]="{ exact: true }"
               [matTooltip]="railed() ? ('nav.home' | translate) : ''" matTooltipPosition="after" (click)="closeIfOverlay()">
              <mat-icon>home</mat-icon><span class="label">{{ 'nav.home' | translate }}</span>
            </a>
            @for (section of sections(); track section.titleKey) {
              <div class="nav-section"><span>{{ section.titleKey | translate }}</span></div>
              @for (item of section.items; track item.link) {
                <a class="nav-link" [routerLink]="item.link" routerLinkActive="active" [routerLinkActiveOptions]="{ exact: true }"
                   [matTooltip]="railed() ? (item.labelKey | translate) : ''" matTooltipPosition="after" (click)="closeIfOverlay()">
                  <mat-icon>{{ item.icon }}</mat-icon>
                  <span class="label">{{ item.labelKey | translate }}</span>
                  @if (item.link === '/notifications' && unread()) { <span class="count">{{ unread() }}</span> }
                </a>
              }
            }
          </nav>

          <div class="nav-footer">
            <div class="user-card">
              <span class="avatar">{{ initials() }}</span>
              <span class="user-text">
                <b>{{ auth.user()?.fullName }}</b>
                <small>@for (r of auth.user()?.roles ?? []; track r; let last = $last) { {{ 'roles.' + r | translate }}@if (!last) {, } }</small>
              </span>
              <button mat-icon-button class="logout" (click)="auth.logout()" [matTooltip]="'auth.logout' | translate" [attr.aria-label]="'auth.logout' | translate">
                <mat-icon>logout</mat-icon>
              </button>
            </div>
          </div>
        </div>
      </mat-sidenav>

      <mat-sidenav-content #content class="shell-content">
        <header class="topbar">
          @if (isDesktop()) {
            <button mat-icon-button (click)="toggleRail()" [attr.aria-label]="(railed() ? 'app.expandMenu' : 'app.collapseMenu') | translate"
                    [matTooltip]="(railed() ? 'app.expandMenu' : 'app.collapseMenu') | translate">
              <mat-icon class="flip">{{ railed() ? 'left_panel_open' : 'left_panel_close' }}</mat-icon>
            </button>
          } @else {
            <button mat-icon-button (click)="nav.toggle()" [attr.aria-label]="'app.menu' | translate"><mat-icon>menu</mat-icon></button>
            <a class="mini-brand" [routerLink]="auth.homeUrl()"><span class="logo sm"><mat-icon>school</mat-icon></span></a>
          }

          @if (current(); as c) {
            <div class="crumbs">
              @if (c.sectionKey) { <span class="muted">{{ c.sectionKey | translate }}</span><mat-icon class="sep flip">chevron_right</mat-icon> }
              <b>{{ c.labelKey | translate }}</b>
            </div>
          }

          <span class="spacer"></span>

          <button class="search-pill" (click)="palette.show()" [attr.aria-label]="'palette.title' | translate">
            <mat-icon>search</mat-icon>
            <span class="search-text">{{ 'palette.placeholder' | translate }}</span>
            <kbd>Ctrl K</kbd>
          </button>
          <button mat-button class="lang" (click)="language.toggle()" [attr.aria-label]="'app.switchLanguage' | translate">
            <mat-icon>translate</mat-icon><span class="lang-label">{{ 'app.switchLanguage' | translate }}</span>
          </button>
          <button mat-icon-button class="theme-btn" (click)="theme.toggle($event)" [matTooltip]="(theme.theme() === 'dark' ? 'app.lightMode' : 'app.darkMode') | translate"
                  [attr.aria-label]="(theme.theme() === 'dark' ? 'app.lightMode' : 'app.darkMode') | translate">
            <mat-icon>{{ theme.theme() === 'dark' ? 'light_mode' : 'dark_mode' }}</mat-icon>
          </button>
          <button mat-icon-button routerLink="/notifications" [matTooltip]="'nav.notifications' | translate" [attr.aria-label]="'nav.notifications' | translate">
            <mat-icon [class.ring]="unread()" [matBadge]="unread() || null" matBadgeColor="warn" matBadgeSize="small" aria-hidden="false">notifications</mat-icon>
          </button>
          <button class="avatar-btn" [matMenuTriggerFor]="userMenu" [attr.aria-label]="'app.account' | translate">
            <span class="avatar">{{ initials() }}</span>
          </button>
          <mat-menu #userMenu="matMenu" xPosition="before">
            <div class="menu-head">
              <span class="avatar lg">{{ initials() }}</span>
              <div>
                <b>{{ auth.user()?.fullName }}</b>
                <div class="muted ltr-text">{{ auth.user()?.email }}</div>
                <div class="roles">@for (r of auth.user()?.roles ?? []; track r) { <span>{{ 'roles.' + r | translate }}</span> }</div>
              </div>
            </div>
            <mat-divider />
            <button mat-menu-item (click)="theme.toggle($event)">
              <mat-icon>{{ theme.theme() === 'dark' ? 'light_mode' : 'dark_mode' }}</mat-icon>
              <span>{{ (theme.theme() === 'dark' ? 'app.lightMode' : 'app.darkMode') | translate }}</span>
            </button>
            <button mat-menu-item (click)="language.toggle()"><mat-icon>translate</mat-icon><span>{{ 'app.switchLanguage' | translate }}</span></button>
            <mat-divider />
            <button mat-menu-item (click)="auth.logout()"><mat-icon>logout</mat-icon><span>{{ 'auth.logout' | translate }}</span></button>
          </mat-menu>
        </header>
        <main class="shell-main"><router-outlet /></main>

        @if (scrolled()) {
          <button mat-fab class="to-top" (click)="toTop()" [attr.aria-label]="'app.backToTop' | translate" [matTooltip]="'app.backToTop' | translate">
            <mat-icon>arrow_upward</mat-icon>
          </button>
        }
      </mat-sidenav-content>
    </mat-sidenav-container>

    <app-command-palette #palette [items]="paletteItems()" />
  `,
  styles: `
    :host { display: block; height: 100%; }
    .shell { height: 100%; background: transparent; }

    /* ---------- Sidebar ---------- */
    .shell-nav { width: var(--app-nav-width); border-inline-end: 1px solid var(--app-border) !important; transition: width 0.2s ease; }
    .collapsed .shell-nav { width: var(--app-rail-width); }
    .nav-inner { display: flex; flex-direction: column; height: 100%; }

    .brand { display: flex; align-items: center; gap: 12px; padding: 20px 20px 16px; text-decoration: none; color: inherit; min-height: 44px; }
    .logo { flex: none; display: grid; place-items: center; width: 40px; height: 40px; border-radius: 12px; color: #fff;
      background: var(--app-gradient); box-shadow: 0 6px 16px -6px rgb(99 70 229 / 60%); }
    .logo.sm { width: 34px; height: 34px; border-radius: 10px; }
    .logo mat-icon { font-variation-settings: 'FILL' 1; }
    .brand-text { display: flex; flex-direction: column; min-width: 0; line-height: 1.3; }
    .brand-text b { font-size: 1rem; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
    .brand-text small { font-size: 0.75rem; color: var(--app-muted); white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }

    .nav-scroll { flex: 1; overflow-y: auto; overflow-x: hidden; padding: 4px 12px 16px; }
    .nav-section { padding: 18px 12px 6px; font-size: 0.7rem; font-weight: 600; letter-spacing: 0.06em; text-transform: uppercase; color: var(--app-muted); white-space: nowrap; }
    .nav-link { position: relative; display: flex; align-items: center; gap: 12px; height: 42px; padding: 0 12px; margin: 2px 0; border-radius: 12px;
      color: var(--mat-sys-on-surface-variant); text-decoration: none; font-size: 0.9rem; font-weight: 500; white-space: nowrap;
      transition: background-color 0.15s ease, color 0.15s ease; }
    .nav-link:hover { background: var(--app-surface-2); color: var(--mat-sys-on-surface); }
    .nav-link:focus-visible { outline: 2px solid var(--mat-sys-primary); outline-offset: -2px; }
    .nav-link.active { color: var(--mat-sys-primary); background: color-mix(in srgb, var(--mat-sys-primary) 10%, transparent); font-weight: 600; }
    .nav-link.active mat-icon { font-variation-settings: 'FILL' 1; }
    .nav-link.active::before { content: ''; position: absolute; inset-inline-start: -12px; top: 10px; bottom: 10px; width: 4px;
      border-start-end-radius: 4px; border-end-end-radius: 4px; background: var(--mat-sys-primary); }
    .nav-link mat-icon { flex: none; font-size: 22px; width: 22px; height: 22px; }
    .label { overflow: hidden; text-overflow: ellipsis; }
    .count { margin-inline-start: auto; min-width: 20px; height: 20px; padding: 0 6px; box-sizing: border-box; border-radius: 10px;
      display: grid; place-items: center; font-size: 0.7rem; font-weight: 700; color: #fff; background: var(--tone-rose); }

    .nav-footer { padding: 12px; border-top: 1px solid var(--app-border); }
    .user-card { display: flex; align-items: center; gap: 10px; padding: 8px; border-radius: 14px; background: var(--app-surface-2); }
    .user-text { display: flex; flex-direction: column; min-width: 0; flex: 1; line-height: 1.3; }
    .user-text b { font-size: 0.85rem; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
    .user-text small { font-size: 0.72rem; color: var(--app-muted); white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }

    /* Rail (collapsed sidebar on desktop). */
    .collapsed .brand { justify-content: center; padding-inline: 0; }
    .collapsed .brand-text, .collapsed .label, .collapsed .user-text, .collapsed .logout { display: none; }
    .collapsed .nav-section { padding: 12px 0 4px; }
    .collapsed .nav-section span { display: none; }
    .collapsed .nav-section::after { content: ''; display: block; height: 1px; margin: 0 14px; background: var(--app-border); }
    .collapsed .nav-link { justify-content: center; padding: 0; }
    .collapsed .count { position: absolute; top: 4px; inset-inline-end: 6px; min-width: 16px; height: 16px; font-size: 0.6rem; }
    .collapsed .user-card { justify-content: center; background: transparent; padding: 0; }

    /* ---------- Top bar ---------- */
    .topbar { position: sticky; top: 0; z-index: 3; display: flex; align-items: center; gap: 4px; height: var(--app-topbar-height);
      padding: 0 clamp(8px, 2vw, 24px); box-sizing: border-box;
      background: color-mix(in srgb, var(--app-bg) 80%, transparent); backdrop-filter: saturate(180%) blur(14px);
      -webkit-backdrop-filter: saturate(180%) blur(14px); border-bottom: 1px solid var(--app-border); }
    .mini-brand { display: flex; text-decoration: none; margin-inline-end: 4px; }
    .crumbs { display: flex; align-items: center; gap: 4px; min-width: 0; margin-inline-start: 8px; font-size: 0.9rem; white-space: nowrap; }
    .crumbs b { overflow: hidden; text-overflow: ellipsis; font-weight: 600; }
    .crumbs .sep { font-size: 18px; width: 18px; height: 18px; color: var(--app-muted); }
    .spacer { flex: 1; }
    :host-context([dir='rtl']) .flip { transform: scaleX(-1); }
    .lang { border-radius: 12px; }
    .avatar-btn { border: 0; padding: 0; margin-inline-start: 6px; background: none; cursor: pointer; border-radius: 50%; }
    .avatar-btn:focus-visible { outline: 2px solid var(--mat-sys-primary); outline-offset: 2px; }
    .avatar { flex: none; display: grid; place-items: center; width: 36px; height: 36px; border-radius: 50%; color: #fff; font-size: 0.8rem;
      font-weight: 700; letter-spacing: 0.02em; background: var(--app-gradient); }
    .avatar.lg { width: 44px; height: 44px; font-size: 0.95rem; }

    .menu-head { display: flex; gap: 12px; align-items: center; padding: 12px 16px 14px; min-width: 240px; }
    .menu-head b { font-size: 0.95rem; }
    .menu-head .muted { font-size: 0.78rem; }
    .ltr-text { direction: ltr; unicode-bidi: plaintext; text-align: start; }
    .roles { display: flex; gap: 4px; flex-wrap: wrap; margin-top: 6px; }
    .roles span { font-size: 0.7rem; font-weight: 600; padding: 2px 8px; border-radius: 999px; color: var(--mat-sys-primary);
      background: color-mix(in srgb, var(--mat-sys-primary) 12%, transparent); }

    /* ---------- Content ---------- */
    .shell-main { padding: clamp(16px, 3vw, 32px); padding-bottom: calc(clamp(16px, 3vw, 32px) + env(safe-area-inset-bottom) + 56px);
      max-width: 1440px; margin-inline: auto; box-sizing: border-box; view-transition-name: page; }

    /* ---------- Interaction ---------- */
    .nav-link mat-icon { transition: transform 0.2s cubic-bezier(0.2, 0.7, 0.2, 1); }
    .nav-link:hover mat-icon { transform: scale(1.12); }
    .nav-link:active { transform: scale(0.98); }
    .logo { transition: transform 0.3s cubic-bezier(0.3, 1.6, 0.5, 1); }
    .brand:hover .logo { transform: rotate(-8deg) scale(1.06); }

    .search-pill { display: flex; align-items: center; gap: 8px; height: 40px; min-width: 240px; padding: 0 8px 0 12px; margin-inline-end: 4px;
      border-radius: 12px; border: 1px solid var(--app-border); background: var(--app-surface); color: var(--app-muted); font: inherit;
      font-size: 0.85rem; cursor: pointer; transition: border-color 0.15s, box-shadow 0.15s, color 0.15s; }
    .search-pill:hover { border-color: color-mix(in srgb, var(--mat-sys-primary) 45%, var(--app-border)); color: var(--mat-sys-on-surface);
      box-shadow: 0 0 0 4px color-mix(in srgb, var(--mat-sys-primary) 10%, transparent); }
    .search-pill mat-icon { font-size: 20px; width: 20px; height: 20px; }
    .search-text { flex: 1; text-align: start; white-space: nowrap; }
    .search-pill kbd { font: 600 0.7rem/1.6 inherit; font-family: inherit; padding: 1px 6px; border-radius: 6px; background: var(--app-surface-2);
      border: 1px solid var(--app-border); direction: ltr; }

    .theme-btn mat-icon { transition: transform 0.5s cubic-bezier(0.3, 1.4, 0.5, 1); }
    .theme-btn:hover mat-icon { transform: rotate(-30deg); }
    .ring { animation: ring 1.2s ease 0.6s 2; transform-origin: 50% 4px; }
    @keyframes ring {
      0%, 100% { transform: rotate(0); } 15% { transform: rotate(14deg); } 30% { transform: rotate(-12deg); }
      45% { transform: rotate(8deg); } 60% { transform: rotate(-5deg); } 75% { transform: rotate(2deg); }
    }

    .to-top { position: fixed; bottom: calc(20px + env(safe-area-inset-bottom)); inset-inline-end: 20px; z-index: 5;
      background: var(--app-gradient) !important; color: #fff !important; animation: fab-in 0.25s cubic-bezier(0.3, 1.4, 0.5, 1) both; }
    @keyframes fab-in { from { opacity: 0; transform: scale(0.6) translateY(12px); } }

    @media (max-width: 1199px) { .search-pill { min-width: 0; } .search-text, .search-pill kbd { display: none; } .search-pill { width: 40px; justify-content: center; padding: 0; border-color: transparent; background: none; } }

    @media (max-width: 1023px) {
      .shell-nav { width: min(86vw, var(--app-nav-width)); }
    }
    @media (max-width: 639px) {
      .crumbs .muted, .crumbs .sep, .lang-label { display: none; }
      .lang { min-width: 0; padding: 0 8px; }
      .lang mat-icon { margin: 0; }
    }
    @media (max-width: 380px) {
      .crumbs { display: none; }
    }
  `,
})
export class Shell implements OnInit {
  protected readonly auth = inject(AuthService);
  protected readonly language = inject(LanguageService);
  protected readonly theme = inject(ThemeService);
  private readonly api = inject(ApiService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  private readonly nav = viewChild.required<MatSidenav>('nav');
  private readonly content = viewChild.required<MatSidenavContent>('content');

  protected readonly unread = signal(0);
  /** Wide screens keep the sidebar docked; tablets and phones open it as a drawer. */
  protected readonly isDesktop = toSignal(
    inject(BreakpointObserver).observe('(min-width: 1024px)').pipe(map((r) => r.matches)),
    { initialValue: true },
  );
  private readonly collapsed = signal(readCollapsed());
  protected readonly railed = computed(() => this.isDesktop() && this.collapsed());

  private readonly url = toSignal(
    this.router.events.pipe(
      filter((e) => e instanceof NavigationEnd),
      map(() => this.router.url),
    ),
    { initialValue: this.router.url },
  );

  /** The menu entry for the current page, for the breadcrumb. */
  protected readonly current = computed(() => {
    const path = this.url().split(/[?#]/)[0];
    if (path === this.auth.homeUrl()) {
      return { sectionKey: null, labelKey: 'nav.home' };
    }
    // Exact match first; otherwise the parent page (e.g. /students/12 → Students).
    for (const match of [(link: string) => link === path, (link: string) => path.startsWith(`${link}/`)]) {
      for (const s of this.sections()) {
        const item = s.items.find((i) => match(i.link));
        if (item) {
          return { sectionKey: s.titleKey, labelKey: item.labelKey };
        }
      }
    }
    return null;
  });

  protected readonly initials = computed(() => {
    const parts = (this.auth.user()?.fullName ?? '').trim().split(/\s+/).filter(Boolean);
    return parts.slice(0, 2).map((p) => p[0]).join('').toUpperCase() || '?';
  });

  /** Pages and quick actions for the Ctrl+K palette. */
  protected readonly paletteItems = computed<PaletteItem[]>(() => [
    { labelKey: 'nav.home', sectionKey: null, icon: 'home', link: this.auth.homeUrl() },
    ...this.sections().flatMap((s) => s.items.map((i) => ({ labelKey: i.labelKey, sectionKey: s.titleKey, icon: i.icon, link: i.link }))),
    {
      labelKey: this.theme.theme() === 'dark' ? 'app.lightMode' : 'app.darkMode', sectionKey: 'palette.actions',
      icon: this.theme.theme() === 'dark' ? 'light_mode' : 'dark_mode', run: () => this.theme.toggle(),
    },
    { labelKey: 'app.switchLanguage', sectionKey: 'palette.actions', icon: 'translate', run: () => this.language.toggle() },
    { labelKey: 'auth.logout', sectionKey: 'palette.actions', icon: 'logout', run: () => this.auth.logout() },
  ]);

  /** Shows the back-to-top button once the page has scrolled a screen or so. */
  protected readonly scrolled = signal(false);

  constructor() {
    afterNextRender(() => {
      const content = this.content();
      content
        .elementScrolled()
        .pipe(
          map(() => content.measureScrollOffset('top') > 480),
          distinctUntilChanged(),
          takeUntilDestroyed(this.destroyRef),
        )
        .subscribe((v) => this.scrolled.set(v));
    });
  }

  protected toTop(): void {
    this.content().scrollTo({ top: 0, behavior: 'smooth' });
  }

  protected toggleRail(): void {
    this.collapsed.update((c) => !c);
    try {
      localStorage.setItem(COLLAPSED_KEY, String(this.collapsed()));
    } catch {
      // Storage unavailable: the choice lasts for this page only.
    }
  }

  protected closeIfOverlay(): void {
    if (!this.isDesktop()) {
      void this.nav().close();
    }
  }

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
    // The sidenav content is the scroll container, so the router's own scroll reset doesn't reach it.
    navigations.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => this.content().scrollTo({ top: 0 }));
  }
}

function readCollapsed(): boolean {
  try {
    return localStorage.getItem(COLLAPSED_KEY) === 'true';
  } catch {
    return false;
  }
}
