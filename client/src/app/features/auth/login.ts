import { Component, inject, input, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { Router, RouterLink } from '@angular/router';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { apiErrorMessage } from '../../core/api/api.models';
import { AuthService } from '../../core/auth/auth.service';
import { AuthLayout } from '../../shared/auth-layout';

@Component({
  selector: 'app-login',
  imports: [
    ReactiveFormsModule, RouterLink, TranslatePipe, MatFormFieldModule, MatInputModule, MatButtonModule, MatIconModule, MatProgressSpinnerModule,
    AuthLayout,
  ],
  template: `
    <app-auth-layout [title]="'auth.loginTitle' | translate" [subtitle]="'auth.loginSubtitle' | translate" [busy]="busy()">
      <form class="auth-form" [formGroup]="form" (ngSubmit)="submit()">
        <mat-form-field>
          <mat-label>{{ 'auth.email' | translate }}</mat-label>
          <mat-icon matPrefix>mail</mat-icon>
          <input matInput type="email" formControlName="email" autocomplete="username" dir="ltr" />
        </mat-form-field>
        <mat-form-field>
          <mat-label>{{ 'auth.password' | translate }}</mat-label>
          <mat-icon matPrefix>lock</mat-icon>
          <input matInput [type]="showPassword() ? 'text' : 'password'" formControlName="password" autocomplete="current-password" dir="ltr" />
          <button mat-icon-button matSuffix type="button" (click)="showPassword.set(!showPassword())"
                  [attr.aria-label]="'auth.togglePassword' | translate">
            <mat-icon>{{ showPassword() ? 'visibility_off' : 'visibility' }}</mat-icon>
          </button>
        </mat-form-field>
        <div class="row-end"><a routerLink="/forgot-password">{{ 'auth.forgotPassword' | translate }}</a></div>
        @if (error()) {
          <p class="form-error" role="alert"><mat-icon>error</mat-icon>{{ error() }}</p>
        }
        <button mat-flat-button class="submit" type="submit" [disabled]="form.invalid || busy()">
          @if (busy()) { <mat-progress-spinner mode="indeterminate" diameter="20" strokeWidth="2.5" /> }
          @else { <span class="label">{{ 'auth.signIn' | translate }}<mat-icon class="arrow flip">arrow_forward</mat-icon></span> }
        </button>
      </form>
    </app-auth-layout>
  `,
  styles: `
    .auth-form { display: flex; flex-direction: column; gap: 16px; }
    .auth-form mat-icon[matPrefix] { color: var(--app-muted); }
    .row-end { display: flex; justify-content: flex-end; margin-top: -6px; font-size: 0.875rem; }
    .row-end a { text-decoration: none; font-weight: 500; }
    .submit { height: 48px; font-size: 1rem; }
    .submit mat-progress-spinner { margin: 0 auto; }
    .label { display: inline-flex; align-items: center; gap: 8px; }
    .arrow { font-size: 20px; width: 20px; height: 20px; transition: transform 0.2s ease; }
    .submit:hover .arrow { transform: translateX(4px); }
    :host-context([dir='rtl']) .flip { scale: -1 1; }
    :host-context([dir='rtl']) .submit:hover .arrow { transform: translateX(-4px); }
  `,
})
export class LoginPage {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly translate = inject(TranslateService);

  /** Bound from ?returnUrl= by withComponentInputBinding(). */
  readonly returnUrl = input<string>();

  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly showPassword = signal(false);

  protected readonly form = inject(NonNullableFormBuilder).group({
    email: ['', [Validators.required, Validators.email]],
    password: ['', Validators.required],
  });

  protected submit(): void {
    if (this.form.invalid) {
      return;
    }

    this.busy.set(true);
    this.error.set(null);
    const { email, password } = this.form.getRawValue();

    this.auth.login(email, password).subscribe({
      next: () => void this.router.navigateByUrl(this.safeReturnUrl() ?? this.auth.homeUrl()),
      error: (err: unknown) => {
        this.error.set(apiErrorMessage(err, this.translate.instant('auth.loginFailed')));
        this.busy.set(false);
      },
    });
  }

  /** Only same-app paths; never an absolute URL from the query string. */
  private safeReturnUrl(): string | null {
    const url = this.returnUrl();
    return url && url.startsWith('/') && !url.startsWith('//') ? url : null;
  }
}
