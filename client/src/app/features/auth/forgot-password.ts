import { Component, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { Router, RouterLink } from '@angular/router';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { apiErrorMessage } from '../../core/api/api.models';
import { AuthService } from '../../core/auth/auth.service';

/** Two steps: request a code by email, then set a new password with it (US-012). */
@Component({
  selector: 'app-forgot-password',
  imports: [
    ReactiveFormsModule,
    RouterLink,
    TranslatePipe,
    MatCardModule,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule,
    MatProgressBarModule,
  ],
  template: `
    <div class="auth-page">
      <mat-card appearance="outlined">
        @if (busy()) {
          <mat-progress-bar mode="indeterminate" />
        }
        <mat-card-header>
          <mat-card-title>{{ 'auth.resetTitle' | translate }}</mat-card-title>
        </mat-card-header>
        <mat-card-content>
          @if (step() === 'request') {
            <form [formGroup]="requestForm" (ngSubmit)="requestCode()">
              <p>{{ 'auth.resetHint' | translate }}</p>
              <mat-form-field appearance="outline">
                <mat-label>{{ 'auth.email' | translate }}</mat-label>
                <input matInput type="email" formControlName="email" dir="ltr" />
              </mat-form-field>
              <button mat-flat-button type="submit" [disabled]="requestForm.invalid || busy()">
                {{ 'auth.sendCode' | translate }}
              </button>
            </form>
          } @else {
            <form [formGroup]="resetForm" (ngSubmit)="reset()">
              <p>{{ 'auth.codeSent' | translate }}</p>
              <mat-form-field appearance="outline">
                <mat-label>{{ 'auth.code' | translate }}</mat-label>
                <input matInput formControlName="code" inputmode="numeric" maxlength="6" dir="ltr" />
              </mat-form-field>
              <mat-form-field appearance="outline">
                <mat-label>{{ 'auth.newPassword' | translate }}</mat-label>
                <input matInput type="password" formControlName="newPassword" autocomplete="new-password" dir="ltr" />
                <mat-hint>{{ 'auth.passwordRules' | translate }}</mat-hint>
              </mat-form-field>
              @if (error()) {
                <p class="form-error" role="alert">{{ error() }}</p>
              }
              <button mat-flat-button type="submit" [disabled]="resetForm.invalid || busy()">
                {{ 'auth.resetPassword' | translate }}
              </button>
            </form>
          }
        </mat-card-content>
        <mat-card-actions align="end">
          <a mat-button routerLink="/login">{{ 'auth.backToLogin' | translate }}</a>
        </mat-card-actions>
      </mat-card>
    </div>
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
