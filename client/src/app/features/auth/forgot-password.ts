import { Component, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { Router, RouterLink } from '@angular/router';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { apiErrorMessage } from '../../core/api/api.models';
import { AuthService } from '../../core/auth/auth.service';
import { AuthLayout } from '../../shared/auth-layout';

/** Two steps: request a code by email, then set a new password with it (US-012). */
@Component({
  selector: 'app-forgot-password',
  imports: [ReactiveFormsModule, RouterLink, TranslatePipe, MatFormFieldModule, MatInputModule, MatButtonModule, MatIconModule, AuthLayout],
  template: `
    <app-auth-layout [title]="'auth.resetTitle' | translate"
                     [subtitle]="(step() === 'request' ? 'auth.resetHint' : 'auth.codeSent') | translate" [busy]="busy()">
      @if (step() === 'request') {
        <form class="auth-form" [formGroup]="requestForm" (ngSubmit)="requestCode()">
          <mat-form-field>
            <mat-label>{{ 'auth.email' | translate }}</mat-label>
            <mat-icon matPrefix>mail</mat-icon>
            <input matInput type="email" formControlName="email" dir="ltr" />
          </mat-form-field>
          <button mat-flat-button class="submit" type="submit" [disabled]="requestForm.invalid || busy()">
            {{ 'auth.sendCode' | translate }}
          </button>
        </form>
      } @else {
        <form class="auth-form" [formGroup]="resetForm" (ngSubmit)="reset()">
          <mat-form-field>
            <mat-label>{{ 'auth.code' | translate }}</mat-label>
            <mat-icon matPrefix>pin</mat-icon>
            <input matInput formControlName="code" inputmode="numeric" maxlength="6" dir="ltr" />
          </mat-form-field>
          <mat-form-field>
            <mat-label>{{ 'auth.newPassword' | translate }}</mat-label>
            <mat-icon matPrefix>lock</mat-icon>
            <input matInput type="password" formControlName="newPassword" autocomplete="new-password" dir="ltr" />
            <mat-hint>{{ 'auth.passwordRules' | translate }}</mat-hint>
          </mat-form-field>
          @if (error()) {
            <p class="form-error" role="alert"><mat-icon>error</mat-icon>{{ error() }}</p>
          }
          <button mat-flat-button class="submit" type="submit" [disabled]="resetForm.invalid || busy()">
            {{ 'auth.resetPassword' | translate }}
          </button>
        </form>
      }
      <a class="back" routerLink="/login"><mat-icon class="flip">arrow_back</mat-icon>{{ 'auth.backToLogin' | translate }}</a>
    </app-auth-layout>
  `,
  styles: `
    .auth-form { display: flex; flex-direction: column; gap: 16px; }
    .auth-form mat-icon[matPrefix] { color: var(--app-muted); }
    .submit { height: 48px; font-size: 1rem; }
    .back { display: inline-flex; align-items: center; gap: 6px; margin-top: 20px; text-decoration: none; font-weight: 500; font-size: 0.9rem; }
    .back mat-icon { font-size: 18px; width: 18px; height: 18px; }
    :host-context([dir='rtl']) .flip { transform: scaleX(-1); }
  `,
})
export class ForgotPasswordPage {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly translate = inject(TranslateService);
  private readonly fb = inject(NonNullableFormBuilder);

  protected readonly step = signal<'request' | 'reset'>('request');
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly requestForm = this.fb.group({ email: ['', [Validators.required, Validators.email]] });
  protected readonly resetForm = this.fb.group({
    code: ['', [Validators.required, Validators.pattern(/^\d{6}$/)]],
    newPassword: ['', [Validators.required, Validators.minLength(8)]],
  });

  protected requestCode(): void {
    this.busy.set(true);
    this.auth.forgotPassword(this.requestForm.getRawValue().email).subscribe({
      // The API answers the same whether or not the email exists, so always move on.
      next: () => {
        this.step.set('reset');
        this.busy.set(false);
      },
      error: () => {
        this.step.set('reset');
        this.busy.set(false);
      },
    });
  }

  protected reset(): void {
    this.busy.set(true);
    this.error.set(null);
    const { code, newPassword } = this.resetForm.getRawValue();

    this.auth.resetPassword(this.requestForm.getRawValue().email, code, newPassword).subscribe({
      next: () => void this.router.navigateByUrl('/login'),
      error: (err: unknown) => {
        this.error.set(apiErrorMessage(err, this.translate.instant('auth.resetFailed')));
        this.busy.set(false);
      },
    });
  }
}
