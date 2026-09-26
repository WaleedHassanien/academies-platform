import { Component, inject, input } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslatePipe } from '@ngx-translate/core';
import { LanguageService } from '../core/i18n/language.service';
import { ThemeService } from '../core/theme/theme.service';

/** Split-screen frame for the signed-out pages: brand panel on wide screens, the form card beside it. */
@Component({
  selector: 'app-auth-layout',
  imports: [TranslatePipe, MatButtonModule, MatIconModule, MatProgressBarModule, MatTooltipModule],
  template: `
    <div class="auth">
      <aside class="hero">
        <div class="hero-brand">
          <span class="logo"><mat-icon>school</mat-icon></span>
          <b>{{ 'app.name' | translate }}</b>
        </div>
        <div class="hero-body">
          <h2>{{ 'app.heroTitle' | translate }}</h2>
          <p>{{ 'app.heroText' | translate }}</p>
          <ul>
            <li><mat-icon>event_available</mat-icon>{{ 'app.feature1' | translate }}</li>
            <li><mat-icon>payments</mat-icon>{{ 'app.feature2' | translate }}</li>
            <li><mat-icon>devices</mat-icon>{{ 'app.feature3' | translate }}</li>
          </ul>
        </div>
        <span class="orb one"></span><span class="orb two"></span>
      </aside>

      <section class="main">
        <div class="top">
          <div class="mobile-brand">
            <span class="logo sm"><mat-icon>school</mat-icon></span>
            <b>{{ 'app.name' | translate }}</b>
          </div>
          <span class="spacer"></span>
          <button mat-button (click)="language.toggle()"><mat-icon>translate</mat-icon>{{ 'app.switchLanguage' | translate }}</button>
          <button mat-icon-button (click)="theme.toggle($event)" [matTooltip]="(theme.theme() === 'dark' ? 'app.lightMode' : 'app.darkMode') | translate"
                  [attr.aria-label]="(theme.theme() === 'dark' ? 'app.lightMode' : 'app.darkMode') | translate">
            <mat-icon>{{ theme.theme() === 'dark' ? 'light_mode' : 'dark_mode' }}</mat-icon>
          </button>
        </div>

        <div class="card">
          @if (busy()) {
            <mat-progress-bar mode="indeterminate" class="busy" />
          }
          <h1>{{ title() }}</h1>
          @if (subtitle()) {
            <p class="subtitle">{{ subtitle() }}</p>
          }
          <ng-content />
        </div>
      </section>
    </div>
  `,
  styles: `
    :host { display: block; min-height: 100%; }
    .auth { display: grid; grid-template-columns: minmax(0, 1.05fr) minmax(0, 1fr); min-height: 100dvh; }

    .hero { position: relative; overflow: hidden; display: flex; flex-direction: column; justify-content: space-between;
      padding: clamp(32px, 5vw, 64px); color: #fff; background: var(--app-gradient); }
    .hero-brand { position: relative; z-index: 1; display: flex; align-items: center; gap: 12px; font-size: 1.1rem; }
    .hero-brand .logo { background: rgb(255 255 255 / 18%); box-shadow: inset 0 0 0 1px rgb(255 255 255 / 30%); }
    .hero-body { position: relative; z-index: 1; max-width: 520px; }
    .hero h2 { font-size: clamp(1.8rem, 1.2rem + 2vw, 2.75rem); line-height: 1.2; letter-spacing: -0.02em; margin: 0 0 16px; font-weight: 700; }
    .hero p { font-size: 1.05rem; line-height: 1.7; opacity: 0.9; margin: 0 0 32px; }
    .hero ul { list-style: none; margin: 0; padding: 0; display: grid; gap: 14px; }
    .hero li { display: flex; align-items: center; gap: 12px; font-weight: 500; }
    .hero li mat-icon { display: grid; place-items: center; width: 36px; height: 36px; font-size: 20px; border-radius: 10px;
      background: rgb(255 255 255 / 16%); box-shadow: inset 0 0 0 1px rgb(255 255 255 / 25%); }
    .hero { background-size: 180% 180%; animation: hero-shift 14s ease-in-out infinite alternate; }
    @keyframes hero-shift { from { background-position: 0% 0%; } to { background-position: 100% 100%; } }
    .orb { position: absolute; border-radius: 50%; filter: blur(2px); background: rgb(255 255 255 / 10%); }
    .orb.one { width: 420px; height: 420px; inset-inline-end: -140px; top: -120px; animation: float 11s ease-in-out infinite alternate; }
    .orb.two { width: 280px; height: 280px; inset-inline-start: -90px; bottom: -110px; background: rgb(255 255 255 / 8%);
      animation: float 9s ease-in-out -3s infinite alternate-reverse; }
    @keyframes float { from { transform: translate(0, 0) scale(1); } to { transform: translate(-30px, 40px) scale(1.08); } }
    .hero-body > * { animation: rise 0.6s cubic-bezier(0.2, 0.7, 0.2, 1) both; }
    .hero-body > p { animation-delay: 80ms; }
    .hero li { animation: rise 0.6s cubic-bezier(0.2, 0.7, 0.2, 1) both; transition: transform 0.2s ease; }
    .hero li:nth-child(1) { animation-delay: 200ms; } .hero li:nth-child(2) { animation-delay: 280ms; } .hero li:nth-child(3) { animation-delay: 360ms; }
    .hero li:hover { transform: translateX(-4px); }
    :host-context([dir='ltr']) .hero li:hover { transform: translateX(4px); }
    @keyframes rise { from { opacity: 0; transform: translateY(14px); } }

    .main { display: flex; flex-direction: column; padding: 16px clamp(16px, 4vw, 48px) 32px; background: var(--app-bg); }
    .top { display: flex; align-items: center; gap: 4px; min-height: 48px; }
    .spacer { flex: 1; }
    .mobile-brand { display: none; align-items: center; gap: 10px; }
    .logo { display: grid; place-items: center; width: 44px; height: 44px; border-radius: 14px; color: #fff; background: var(--app-gradient); }
    .logo.sm { width: 36px; height: 36px; border-radius: 11px; }
    .logo mat-icon { font-variation-settings: 'FILL' 1; }

    .card { position: relative; width: 100%; max-width: 420px; margin: auto; padding: clamp(24px, 4vw, 40px); box-sizing: border-box;
      background: var(--app-surface); border: 1px solid var(--app-border); border-radius: 24px; box-shadow: var(--app-shadow-lg); overflow: hidden;
      animation: card-in 0.55s cubic-bezier(0.2, 0.7, 0.2, 1) both; }
    @keyframes card-in { from { opacity: 0; transform: translateY(18px) scale(0.98); } }
    .busy { position: absolute; inset: 0 0 auto; }
    h1 { margin: 0 0 6px; font-size: 1.6rem; font-weight: 700; letter-spacing: -0.02em; }
    .subtitle { margin: 0 0 28px; color: var(--app-muted); }

    @media (max-width: 899px) {
      .auth { grid-template-columns: 1fr; }
      .hero { display: none; }
      .mobile-brand { display: flex; }
      .main { background: radial-gradient(120% 60% at 50% 0%, color-mix(in srgb, var(--mat-sys-primary) 14%, transparent), transparent 70%), var(--app-bg); }
    }
    @media (max-width: 479px) {
      .main { padding-inline: 12px; }
      .card { padding: 24px 20px; border-radius: 20px; margin-top: 24px; }
      .top button[mat-button] { min-width: 0; }
    }
  `,
})
export class AuthLayout {
  protected readonly language = inject(LanguageService);
  protected readonly theme = inject(ThemeService);

  readonly title = input.required<string>();
  readonly subtitle = input<string>();
  readonly busy = input(false);
}
