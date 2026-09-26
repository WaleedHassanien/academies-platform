import { Component, computed, effect, input, model, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatAutocompleteModule } from '@angular/material/autocomplete';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { TIME_ZONES, zoneLabel } from './time-zones';

/** Searchable IANA time-zone picker ("riyadh", "dubai", "london"...). Two-way binds the zone id, or null. */
@Component({
  selector: 'app-time-zone-field',
  imports: [FormsModule, MatAutocompleteModule, MatButtonModule, MatFormFieldModule, MatIconModule, MatInputModule],
  template: `
    <mat-form-field class="full">
      <mat-label>{{ label() }}</mat-label>
      <mat-icon matPrefix>public</mat-icon>
      <input matInput dir="ltr" [ngModel]="query()" (ngModelChange)="query.set($event)" [matAutocomplete]="auto" (blur)="commit()" />
      @if (value()) {
        <button mat-icon-button matSuffix type="button" (click)="clear()" [attr.aria-label]="clearLabel()"><mat-icon>close</mat-icon></button>
      }
      <mat-autocomplete #auto="matAutocomplete" (optionSelected)="pick($event.option.value)">
        @for (z of matches(); track z) {
          <mat-option [value]="z"><span dir="ltr">{{ label_(z) }}</span></mat-option>
        }
      </mat-autocomplete>
      @if (hint()) { <mat-hint>{{ hint() }}</mat-hint> }
    </mat-form-field>
  `,
  styles: `
    :host { display: block; }
    .full { width: 100%; }
    mat-icon[matPrefix] { color: var(--app-muted); }
  `,
})
export class TimeZoneField {
  readonly value = model<string | null>(null);
  readonly label = input('');
  readonly hint = input('');
  readonly clearLabel = input('Clear');

  protected readonly query = signal('');
  protected readonly label_ = zoneLabel;

  protected readonly matches = computed(() => {
    const q = this.query().trim().toLowerCase().replace(/\s+/g, '_');
    const found = q ? TIME_ZONES.filter((z) => z.toLowerCase().includes(q)) : TIME_ZONES;
    return found.slice(0, 50);
  });

  constructor() {
    // Show the bound zone in the box whenever it changes (typing only changes the query).
    effect(() => {
      const zone = this.value();
      untracked(() => this.query.set(zone ?? ''));
    });
  }

  protected pick(zone: string): void {
    this.value.set(zone);
    this.query.set(zone);
  }

  /** Typing an exact zone id counts; anything else falls back to the last valid value. */
  protected commit(): void {
    const typed = this.query().trim();
    const exact = TIME_ZONES.find((z) => z.toLowerCase() === typed.toLowerCase());
    if (exact) {
      this.value.set(exact);
      this.query.set(exact);
    } else if (!typed) {
      this.value.set(null);
    } else {
      this.query.set(this.value() ?? '');
    }
  }

  protected clear(): void {
    this.value.set(null);
    this.query.set('');
  }
}
