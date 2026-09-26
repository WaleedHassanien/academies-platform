import { Component, computed, ElementRef, HostListener, inject, input, signal, viewChild } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { Router } from '@angular/router';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';

export interface PaletteItem {
  labelKey: string;
  sectionKey: string | null;
  icon: string;
  /** A route to open, or an action to run. */
  link?: string;
  run?: () => void;
}

/** Ctrl/⌘+K quick switcher: type to filter pages and actions, arrows to move, Enter to go. */
@Component({
  selector: 'app-command-palette',
  imports: [MatIconModule, TranslatePipe],
  template: `
    @if (open()) {
      <div class="backdrop" (click)="close()"></div>
      <div class="palette" role="dialog" aria-modal="true" [attr.aria-label]="'palette.title' | translate">
        <div class="search">
          <mat-icon>search</mat-icon>
          <input #box [value]="query()" (input)="setQuery(box.value)" (keydown)="onKey($event)"
                 [placeholder]="'palette.placeholder' | translate" role="combobox" aria-controls="palette-list" [attr.aria-activedescendant]="'pi-' + active()" />
          <kbd>Esc</kbd>
        </div>
        <ul id="palette-list" role="listbox">
          @for (r of results(); track r.labelKey + r.link; let i = $index) {
            <li [id]="'pi-' + i" role="option" [attr.aria-selected]="i === active()" [class.active]="i === active()"
                (mouseenter)="active.set(i)" (click)="choose(r)">
              <span class="icon"><mat-icon>{{ r.icon }}</mat-icon></span>
              <span class="text">
                <b>{{ r.labelKey | translate }}</b>
                @if (r.sectionKey) { <small>{{ r.sectionKey | translate }}</small> }
              </span>
              <mat-icon class="go flip">{{ r.link ? 'arrow_forward' : 'bolt' }}</mat-icon>
            </li>
          } @empty {
            <li class="none">{{ 'palette.none' | translate }}</li>
          }
        </ul>
        <div class="foot"><span><kbd>↑</kbd><kbd>↓</kbd> {{ 'palette.move' | translate }}</span><span><kbd>Enter</kbd> {{ 'palette.open' | translate }}</span></div>
      </div>
    }
  `,
  styles: `
    .backdrop { position: fixed; inset: 0; z-index: 1000; background: rgb(10 10 30 / 40%); backdrop-filter: blur(4px); animation: fade 0.15s ease both; }
    .palette { position: fixed; z-index: 1001; top: min(12vh, 120px); left: 50%; width: min(640px, calc(100vw - 24px)); transform: translateX(-50%);
      display: flex; flex-direction: column; max-height: min(70vh, 560px); overflow: hidden; border-radius: 20px;
      background: var(--app-surface); border: 1px solid var(--app-border); box-shadow: 0 30px 80px -20px rgb(20 10 60 / 45%);
      animation: drop 0.2s cubic-bezier(0.2, 0.7, 0.2, 1) both; }
    .search { display: flex; align-items: center; gap: 12px; padding: 16px 18px; border-bottom: 1px solid var(--app-border); }
    .search mat-icon { color: var(--mat-sys-primary); }
    .search input { flex: 1; min-width: 0; border: 0; outline: 0; background: none; color: inherit; font: inherit; font-size: 1.05rem; }
    ul { list-style: none; margin: 0; padding: 8px; overflow-y: auto; }
    li { display: flex; align-items: center; gap: 12px; padding: 10px 12px; border-radius: 12px; cursor: pointer; }
    li.active { background: color-mix(in srgb, var(--mat-sys-primary) 10%, transparent); }
    li.active .go { opacity: 1; transform: none; }
    .icon { display: grid; place-items: center; width: 36px; height: 36px; flex: none; border-radius: 10px;
      color: var(--mat-sys-primary); background: color-mix(in srgb, var(--mat-sys-primary) 10%, transparent); }
    .icon mat-icon { font-size: 20px; width: 20px; height: 20px; }
    .text { display: flex; flex-direction: column; flex: 1; min-width: 0; }
    .text b { font-weight: 600; font-size: 0.92rem; }
    .text small { color: var(--app-muted); font-size: 0.75rem; }
    .go { color: var(--mat-sys-primary); opacity: 0; transform: translateX(-4px); transition: opacity 0.15s, transform 0.15s; }
    :host-context([dir='rtl']) .flip { scale: -1 1; }
    .none { justify-content: center; color: var(--app-muted); cursor: default; padding: 24px; }
    .foot { display: flex; gap: 16px; padding: 10px 18px; border-top: 1px solid var(--app-border); font-size: 0.75rem; color: var(--app-muted); }
    kbd { display: inline-block; min-width: 18px; padding: 1px 6px; margin-inline-end: 3px; border-radius: 6px; text-align: center; font: 600 0.7rem/1.6 inherit;
      font-family: inherit; color: var(--app-muted); background: var(--app-surface-2); border: 1px solid var(--app-border); box-shadow: 0 1px 0 var(--app-border); }
    @keyframes fade { from { opacity: 0; } }
    @keyframes drop { from { opacity: 0; transform: translate(-50%, -8px) scale(0.98); } }
    @media (max-width: 639px) { .palette { top: 12px; max-height: calc(100dvh - 24px); } .foot { display: none; } }
  `,
})
export class CommandPalette {
  private readonly router = inject(Router);
  private readonly translate = inject(TranslateService);

  readonly items = input.required<PaletteItem[]>();

  protected readonly open = signal(false);
  protected readonly query = signal('');
  protected readonly active = signal(0);
  private readonly box = viewChild<ElementRef<HTMLInputElement>>('box');

  protected readonly results = computed(() => {
    const q = this.query().trim().toLowerCase();
    const all = this.items();
    if (!q) {
      return all;
    }
    // Match the translated label or section, in whichever language is active.
    return all.filter((i) =>
      [i.labelKey, i.sectionKey].some((k) => k && this.translate.instant(k).toLowerCase().includes(q)),
    );
  });

  show(): void {
    this.query.set('');
    this.active.set(0);
    this.open.set(true);
    setTimeout(() => this.box()?.nativeElement.focus());
  }

  protected close(): void {
    this.open.set(false);
  }

  @HostListener('document:keydown', ['$event'])
  protected onGlobalKey(e: KeyboardEvent): void {
    if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'k') {
      e.preventDefault();
      if (this.open()) {
        this.close();
      } else {
        this.show();
      }
    } else if (e.key === 'Escape' && this.open()) {
      this.close();
    }
  }

  protected setQuery(value: string): void {
    this.query.set(value);
    this.active.set(0);
  }

  protected onKey(e: KeyboardEvent): void {
    const count = this.results().length;
    if (e.key === 'ArrowDown' && count) {
      e.preventDefault();
      this.active.update((i) => (i + 1) % count);
      this.scrollActive();
    } else if (e.key === 'ArrowUp' && count) {
      e.preventDefault();
      this.active.update((i) => (i - 1 + count) % count);
      this.scrollActive();
    } else if (e.key === 'Enter' && count) {
      e.preventDefault();
      this.choose(this.results()[this.active()]);
    }
  }

  protected choose(item: PaletteItem): void {
    this.close();
    if (item.link) {
      void this.router.navigateByUrl(item.link);
    } else {
      item.run?.();
    }
  }

  private scrollActive(): void {
    setTimeout(() => document.getElementById(`pi-${this.active()}`)?.scrollIntoView({ block: 'nearest' }));
  }
}
