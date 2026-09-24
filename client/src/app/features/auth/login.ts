import { Component, inject, input, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { Router, RouterLink } from '@angular/router';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { apiErrorMessage } from '../../core/api/api.models';
import { AuthService } from '../../core/auth/auth.service';
import { LanguageService } from '../../core/i18n/language.service';

@Component({
  selector: 'app-login',
  imports: [
    ReactiveFormsModule,
    RouterLink,
    TranslatePipe,
    MatCardModule,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule,
    MatIconModule,
    MatProgressBarModule,
  ],
  template: `
    <div class="auth-page">
      <mat-card appearance="outlined">
        @if (busy()) {
          <mat-progress-bar mode="indeterminate" />
        }
        <mat-card-header>
          <mat-card-title>{{ 'auth.loginTitle' | translate }}</mat-card-title>
          <mat-card-subtitle>{{ 'app.name' | translate }}</mat-card-subtitle>
        </mat-card-header>
        <mat-card-content>
          <form [formGroup]="form" (ngSubmit)="submit()">
            <mat-form-field appearance="outline">
              <mat-label>{{ 'auth.email' | translate }}</mat-label>
              <input matInput type="email" formControlName="email" autocomplete="username" dir="ltr" />
            </mat-form-field>
            <mat-form-field appearance="outline">
              <mat-label>{{ 'auth.password' | translate }}</mat-label>
              <input matInput [type]="showPassword() ? 'text' : 'password'" formControlName="password" autocomplete="current-password" dir="ltr" />
              <button mat-icon-button matSuffix type="button" (click)="showPassword.set(!showPassword())"
                      [attr.aria-label]="'auth.togglePassword' | translate">
                <mat-icon>{{ showPassword() ? 'visibility_off' : 'visibility' }}</mat-icon>
              </button>
            </mat-form-field>
            @if (error()) {
              <p class="form-error" role="alert">{{ error() }}</p>
            }
            <button mat-flat-button type="submit" [disabled]="form.invalid || busy()">
              {{ 'auth.signIn' | translate }}
            </button>
          </form>
        </mat-card-content>
        <mat-card-actions align="end">
          <a mat-button routerLink="/forgot-password">{{ 'auth.forgotPassword' | translate }}</a>
          <button mat-button type="button" (click)="language.toggle()">{{ 'app.switchLanguage' | translate }}</button>
        </mat-card-actions>
      </mat-card>
    </div>
  `,
})
export class LoginPage {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly translate = inject(TranslateService);
  protected readonly language = inject(LanguageService);

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
