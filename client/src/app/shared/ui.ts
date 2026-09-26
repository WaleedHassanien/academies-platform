import { DecimalPipe } from '@angular/common';
import { Component, computed, effect, input, OnDestroy, signal } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';

export interface ChartSeries {
  name: string;
  values: number[];
  color: string;
}

/**
 * Grouped bar chart in plain SVG (no chart library), used by the dashboards and reports
 * (US-034, US-038). Bars scale to the largest absolute value, and negatives draw below the axis.
 */
@Component({
  selector: 'app-bar-chart',
  imports: [DecimalPipe],
  template: `
    <div class="chart">
      <div class="legend">
        @for (s of series(); track s.name) {
          <button type="button" class="key" [class.off]="hidden().has(s.name)" [attr.aria-pressed]="!hidden().has(s.name)" (click)="toggle(s.name)">
            <i [style.background]="s.color"></i>{{ s.name }}
          </button>
        }
      </div>
      <div class="plot" (mouseleave)="hover.set(null)">
        <svg [attr.viewBox]="'0 0 ' + width + ' ' + height" preserveAspectRatio="none" role="img" [attr.aria-label]="title()">
          @for (y of gridLines(); track y) {
            <line x1="0" [attr.y1]="y" [attr.x2]="width" [attr.y2]="y" class="grid" vector-effect="non-scaling-stroke" />
          }
          @if (hover() !== null) {
            <rect class="band" [attr.x]="hover()! * groupWidth()" y="0" [attr.width]="groupWidth()" [attr.height]="height" rx="6" />
          }
          <line x1="0" [attr.y1]="zeroY()" [attr.x2]="width" [attr.y2]="zeroY()" class="axis" vector-effect="non-scaling-stroke" />
          @for (label of labels(); track $index; let i = $index) {
            @for (s of visible(); track s.name; let j = $index) {
              <rect
                class="bar"
                [class.neg]="s.values[i] < 0"
                [class.dim]="hover() !== null && hover() !== i"
                [style.animation-delay.ms]="i * 45 + j * 20"
                [attr.x]="barX(i, j)"
                [attr.y]="barY(s.values[i])"
                [attr.width]="barWidth()"
                [attr.height]="barHeight(s.values[i])"
                [attr.fill]="s.color"
                rx="3" />
            }
            <!-- Full-height hit area per month, so hovering anywhere in the column shows its values. -->
            <rect class="hit" [attr.x]="i * groupWidth()" y="0" [attr.width]="groupWidth()" [attr.height]="height"
                  (mouseenter)="hover.set(i)" (click)="hover.set(i)" />
          }
        </svg>
        @if (hover() !== null) {
          <div class="tip" [style.left.%]="tipLeft()" [class.edge-start]="tipLeft() < 18" [class.edge-end]="tipLeft() > 82">
            <b>{{ labels()[hover()!] }}</b>
            @for (s of visible(); track s.name) {
              <div class="row"><i [style.background]="s.color"></i><span>{{ s.name }}</span><strong>{{ s.values[hover()!] | number: '1.0-2' }}</strong></div>
            }
          </div>
        }
      </div>
      <div class="x-labels">
        @for (label of labels(); track $index; let i = $index) {
          <span [class.on]="hover() === i">{{ label }}</span>
        }
      </div>
    </div>
  `,
  styles: `
    .chart { width: 100%; }
    .plot { position: relative; direction: ltr; }
    svg { width: 100%; height: clamp(160px, 28vw, 220px); display: block; overflow: visible; }
    .grid { stroke: var(--app-border); stroke-width: 1; stroke-dasharray: 3 4; }
    .axis { stroke: var(--app-border-strong); stroke-width: 1; }
    .band { fill: color-mix(in srgb, var(--mat-sys-primary) 7%, transparent); }
    .bar { transform-box: fill-box; transform-origin: 50% 100%; animation: grow 0.7s cubic-bezier(0.2, 0.8, 0.2, 1) both;
      transition: opacity 0.2s ease; }
    .bar.neg { transform-origin: 50% 0; }
    .bar.dim { opacity: 0.35; }
    .hit { fill: transparent; cursor: crosshair; }
    @keyframes grow { from { transform: scaleY(0); } to { transform: scaleY(1); } }

    .tip { position: absolute; top: 4px; transform: translateX(-50%); z-index: 2; min-width: 150px; padding: 10px 12px; pointer-events: none;
      border-radius: 12px; background: color-mix(in srgb, var(--app-surface) 92%, transparent); backdrop-filter: blur(8px);
      border: 1px solid var(--app-border); box-shadow: var(--app-shadow-lg); font-size: 0.78rem; animation: tip-in 0.15s ease both;
      transition: left 0.15s ease; }
    .tip.edge-start { transform: none; }
    .tip.edge-end { transform: translateX(-100%); }
    .tip b { display: block; margin-bottom: 6px; font-size: 0.8rem; }
    .tip .row { display: flex; align-items: center; gap: 6px; padding: 2px 0; }
    .tip .row span { flex: 1; color: var(--app-muted); }
    .tip .row strong { font-variant-numeric: tabular-nums; }
    .tip i, .key i { display: inline-block; width: 10px; height: 10px; border-radius: 3px; flex: none; }
    @keyframes tip-in { from { opacity: 0; } }

    .x-labels { display: flex; justify-content: space-around; margin-top: 8px; font-size: 0.72rem; color: var(--app-muted); direction: ltr;
      font-variant-numeric: tabular-nums; }
    .x-labels span { flex: 1; text-align: center; min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; transition: color 0.15s; }
    .x-labels span.on { color: var(--mat-sys-primary); font-weight: 700; }
    .legend { display: flex; gap: 6px; flex-wrap: wrap; margin-bottom: 16px; }
    .key { display: inline-flex; align-items: center; gap: 6px; padding: 4px 10px; border-radius: 999px; font: inherit; font-size: 0.78rem;
      color: var(--mat-sys-on-surface); background: var(--app-surface-2); border: 1px solid var(--app-border); cursor: pointer;
      transition: opacity 0.15s, background-color 0.15s; }
    .key:hover { background: color-mix(in srgb, var(--mat-sys-primary) 8%, var(--app-surface-2)); }
    .key.off { opacity: 0.45; text-decoration: line-through; }
  `,
})
export class BarChart {
  readonly title = input('');
  readonly labels = input.required<string[]>();
  readonly series = input.required<ChartSeries[]>();

  protected readonly width = 600;
  protected readonly height = 180;

  /** Month column under the pointer (or last tapped on touch screens). */
  protected readonly hover = signal<number | null>(null);
  /** Series switched off from the legend. */
  protected readonly hidden = signal<ReadonlySet<string>>(new Set());
  protected readonly visible = computed(() => this.series().filter((s) => !this.hidden().has(s.name)));
  protected readonly tipLeft = computed(() => ((this.hover() ?? 0) + 0.5) * (100 / Math.max(1, this.labels().length)));

  private readonly max = computed(() => Math.max(1, ...this.visible().flatMap((s) => s.values.map((v) => Math.abs(v)))));
  private readonly hasNegative = computed(() => this.visible().some((s) => s.values.some((v) => v < 0)));

  protected toggle(name: string): void {
    const next = new Set(this.hidden());
    if (!next.delete(name)) {
      // Keep at least one series on screen.
      if (next.size + 1 >= this.series().length) {
        return;
      }
      next.add(name);
    }
    this.hidden.set(next);
  }

  protected readonly zeroY = computed(() => (this.hasNegative() ? this.height / 2 : this.height - 1));
  protected readonly gridLines = computed(() => [0.25, 0.5, 0.75].map((f) => Math.round(this.height * f)).filter((y) => y !== this.zeroY()));
  private readonly scale = computed(() => (this.hasNegative() ? this.height / 2 - 4 : this.height - 6) / this.max());
  protected readonly groupWidth = computed(() => this.width / Math.max(1, this.labels().length));
  protected readonly barWidth = computed(() => Math.max(2, (this.groupWidth() * 0.7) / Math.max(1, this.visible().length)));

  protected barX(i: number, j: number): number {
    return i * this.groupWidth() + this.groupWidth() * 0.15 + j * this.barWidth();
  }

  protected barY(value: number): number {
    return value >= 0 ? this.zeroY() - value * this.scale() : this.zeroY();
  }

  protected barHeight(value: number): number {
    return Math.abs(value) * this.scale();
  }
}

/** Big-number KPI tile. Its accent colour comes from `--tone` (set per position by `.stats` in styles.scss). */
@Component({
  selector: 'app-stat',
  imports: [MatIconModule],
  template: `
    <div class="stat">
      <div class="head">
        <span class="label">{{ label() }}</span>
        <span class="icon"><mat-icon>{{ icon() }}</mat-icon></span>
      </div>
      <span class="value">{{ shown() }}</span>
      @if (hint()) {
        <span class="hint">{{ hint() }}</span>
      }
    </div>
  `,
  styles: `
    :host { display: block; min-width: 0; --tone-c: var(--tone, var(--mat-sys-primary)); }
    :host(.clickable) { cursor: pointer; }
    :host(.clickable:focus-visible) { outline: none; }
    :host(.clickable:focus-visible) .stat { box-shadow: 0 0 0 3px color-mix(in srgb, var(--tone-c) 40%, transparent); }
    :host(.active) .stat { border-color: var(--tone-c); box-shadow: 0 0 0 1px var(--tone-c), var(--app-shadow-lg); }
    .stat { position: relative; overflow: hidden; display: flex; flex-direction: column; gap: 6px; height: 100%; box-sizing: border-box;
      padding: 18px 20px; border-radius: var(--app-radius); background: var(--app-surface); border: 1px solid var(--app-border);
      box-shadow: var(--app-shadow); transition: transform 0.25s cubic-bezier(0.2, 0.7, 0.2, 1), box-shadow 0.25s ease, border-color 0.25s ease; }
    .stat::after { content: ''; position: absolute; inset-inline-end: -30px; bottom: -30px; width: 96px; height: 96px; border-radius: 50%;
      background: color-mix(in srgb, var(--tone-c) 8%, transparent); pointer-events: none; transition: transform 0.4s cubic-bezier(0.2, 0.7, 0.2, 1); }
    .stat:hover { transform: translateY(-3px); box-shadow: var(--app-shadow-lg); border-color: color-mix(in srgb, var(--tone-c) 35%, var(--app-border)); }
    .stat:hover::after { transform: scale(1.6); }
    .stat:hover .icon { transform: rotate(-8deg) scale(1.08); }
    :host(.clickable) .stat:active { transform: scale(0.98); }
    .head { display: flex; align-items: flex-start; justify-content: space-between; gap: 8px; }
    .label { font-size: 0.8rem; font-weight: 500; color: var(--app-muted); line-height: 1.4; }
    .icon { flex: none; display: grid; place-items: center; width: 36px; height: 36px; border-radius: 11px;
      color: var(--tone-c); background: color-mix(in srgb, var(--tone-c) 12%, transparent); transition: transform 0.3s cubic-bezier(0.3, 1.5, 0.5, 1); }
    .icon mat-icon { font-size: 20px; width: 20px; height: 20px; font-variation-settings: 'FILL' 1; }
    .value { font-size: clamp(1.35rem, 1.1rem + 0.8vw, 1.75rem); font-weight: 700; letter-spacing: -0.02em; line-height: 1.2;
      font-variant-numeric: tabular-nums; overflow-wrap: anywhere; }
    .hint { font-size: 0.75rem; color: var(--app-muted); }
    @media (max-width: 639px) {
      .stat { padding: 14px; }
      .icon { width: 30px; height: 30px; border-radius: 9px; }
      .icon mat-icon { font-size: 17px; width: 17px; height: 17px; }
    }
  `,
})
export class Stat implements OnDestroy {
  readonly label = input.required<string>();
  readonly value = input.required<string | number | null>();
  readonly hint = input<string>();
  readonly icon = input('insights');

  /** What's on screen: numbers count up to the new value, anything else shows as is. */
  protected readonly shown = signal<string>('');
  private frame = 0;

  constructor() {
    effect(() => this.countUp(this.value()));
  }

  ngOnDestroy(): void {
    cancelAnimationFrame(this.frame);
  }

  private countUp(value: string | number | null): void {
    cancelAnimationFrame(this.frame);
    const text = value === null ? '' : String(value);
    // "1,234.5", "85%", "-40" → prefix, number, suffix.
    const match = /^(\D*?)(-?\d[\d,]*(?:\.\d+)?)(.*)$/.exec(text);
    const reduced = typeof matchMedia === 'function' && matchMedia('(prefers-reduced-motion: reduce)').matches;
    if (!match || reduced) {
      this.shown.set(text);
      return;
    }

    const [, prefix, digits, suffix] = match;
    const target = Number(digits.replace(/,/g, ''));
    const decimals = digits.split('.')[1]?.length ?? 0;
    const grouped = digits.includes(',');
    const format = (n: number) =>
      prefix + (grouped ? n.toLocaleString('en-US', { minimumFractionDigits: decimals, maximumFractionDigits: decimals }) : n.toFixed(decimals)) + suffix;

    const start = performance.now();
    const duration = 900;
    const step = (now: number) => {
      const t = Math.min(1, (now - start) / duration);
      const eased = 1 - Math.pow(1 - t, 3);
      this.shown.set(t < 1 ? format(target * eased) : text);
      if (t < 1) {
        this.frame = requestAnimationFrame(step);
      }
    };
    this.frame = requestAnimationFrame(step);
  }
}

/** Usage meter, e.g. students 140/150 (US-017). */
@Component({
  selector: 'app-usage-bar',
  template: `
    <div class="usage">
      <div class="row">
        <span>{{ label() }}</span>
        <span dir="ltr">{{ used() }} / {{ limit() ?? '∞' }}</span>
      </div>
      <div class="track"><div class="fill" [class.warn]="percent() >= 90" [style.width.%]="percent()"></div></div>
    </div>
  `,
  styles: `
    .usage { display: flex; flex-direction: column; gap: 6px; }
    .row { display: flex; justify-content: space-between; gap: 8px; font-size: 0.875rem; }
    .row span:last-child { font-weight: 600; font-variant-numeric: tabular-nums; }
    .track { height: 8px; border-radius: 999px; background: var(--app-surface-2); box-shadow: inset 0 0 0 1px var(--app-border); overflow: hidden; }
    .fill { height: 100%; border-radius: 999px; background: var(--app-gradient); transition: width 0.4s ease; }
    .fill.warn { background: linear-gradient(90deg, #f59e0b, #e11d48); }
  `,
})
export class UsageBar {
  readonly label = input.required<string>();
  readonly used = input.required<number>();
  readonly limit = input<number | null>(null);

  protected readonly percent = computed(() => {
    const limit = this.limit();
    return limit ? Math.min(100, Math.round((this.used() * 100) / limit)) : 0;
  });
}
