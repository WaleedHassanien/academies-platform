import { DOCUMENT } from '@angular/common';
import { inject, Injectable, signal } from '@angular/core';

export type Theme = 'light' | 'dark';

const STORAGE_KEY = 'academies.theme';

/** Light/dark mode. Defaults to the OS setting; an explicit choice is remembered. index.html applies it before first paint. */
@Injectable({ providedIn: 'root' })
export class ThemeService {
  private readonly document = inject(DOCUMENT);

  readonly theme = signal<Theme>(this.document.documentElement.classList.contains('dark-theme') ? 'dark' : 'light');

  // The <html> class is written only here and in toggle() (never from an effect), so a view
  // transition's "before" snapshot is always taken before the page changes.

  /**
   * Switches theme. Given the click, the new theme spreads as a circle from that point
   * (View Transitions API); browsers without it, or users who prefer less motion, get an instant swap.
   */
  toggle(event?: MouseEvent): void {
    const next: Theme = this.theme() === 'dark' ? 'light' : 'dark';
    try {
      localStorage.setItem(STORAGE_KEY, next);
    } catch {
      // Storage unavailable: the choice lasts for this page only.
    }

    this.theme.set(next);
    const root = this.document.documentElement;
    const reduced = this.document.defaultView?.matchMedia('(prefers-reduced-motion: reduce)').matches;
    if (!this.document.startViewTransition || reduced) {
      this.apply(next);
      return;
    }

    const view = this.document.defaultView!;
    const x = event?.clientX || (root.dir === 'rtl' ? 40 : view.innerWidth - 40);
    const y = event?.clientY || 32;
    const radius = Math.hypot(Math.max(x, view.innerWidth - x), Math.max(y, view.innerHeight - y));

    root.classList.add('theme-transition');
    // Callbacks run in order even when a quick second click skips this transition, so the last click wins.
    const transition = this.document.startViewTransition(() => this.apply(next));
    void transition.ready.then(() =>
      root.animate(
        { clipPath: [`circle(0px at ${x}px ${y}px)`, `circle(${radius}px at ${x}px ${y}px)`] },
        { duration: 550, easing: 'cubic-bezier(0.4, 0, 0.2, 1)', pseudoElement: '::view-transition-new(root)' },
      ),
    );
    void transition.finished.finally(() => root.classList.remove('theme-transition'));
  }

  private apply(theme: Theme): void {
    this.document.documentElement.classList.toggle('dark-theme', theme === 'dark');
  }
}
