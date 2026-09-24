import { DecimalPipe } from '@angular/common';
import { Component, computed, input } from '@angular/core';

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
      <svg [attr.viewBox]="'0 0 ' + width + ' ' + height" preserveAspectRatio="none" role="img" [attr.aria-label]="title()">
        <line x1="0" [attr.y1]="zeroY()" [attr.x2]="width" [attr.y2]="zeroY()" class="axis" />
        @for (label of labels(); track $index; let i = $index) {
          @for (s of series(); track s.name; let j = $index) {
            <rect
              [attr.x]="barX(i, j)"
              [attr.y]="barY(s.values[i])"
              [attr.width]="barWidth()"
              [attr.height]="barHeight(s.values[i])"
              [attr.fill]="s.color"
              rx="2">
              <title>{{ s.name }}: {{ s.values[i] | number: '1.0-2' }}</title>
            </rect>
          }
        }
      </svg>
      <div class="x-labels">
        @for (label of labels(); track $index) {
          <span>{{ label }}</span>
        }
      </div>
      <div class="legend">
        @for (s of series(); track s.name) {
          <span><i [style.background]="s.color"></i>{{ s.name }}</span>
        }
      </div>
    </div>
  `,
  styles: `
    .chart { width: 100%; }
    svg { width: 100%; height: 180px; display: block; }
    .axis { stroke: var(--mat-sys-outline-variant); stroke-width: 1; }
    .x-labels { display: flex; justify-content: space-around; font-size: 0.75rem; color: var(--mat-sys-on-surface-variant); direction: ltr; }
    .legend { display: flex; gap: 16px; flex-wrap: wrap; margin-top: 8px; font-size: 0.8rem; }
    .legend i { display: inline-block; width: 10px; height: 10px; border-radius: 2px; margin-inline-end: 6px; }
  `,
})
export class BarChart {
  readonly title = input('');
  readonly labels = input.required<string[]>();
  readonly series = input.required<ChartSeries[]>();

  protected readonly width = 600;
  protected readonly height = 180;

  private readonly max = computed(() => Math.max(1, ...this.series().flatMap((s) => s.values.map((v) => Math.abs(v)))));
  private readonly hasNegative = computed(() => this.series().some((s) => s.values.some((v) => v < 0)));

  protected readonly zeroY = computed(() => (this.hasNegative() ? this.height / 2 : this.height - 1));
  private readonly scale = computed(() => (this.hasNegative() ? this.height / 2 - 4 : this.height - 6) / this.max());
  private readonly groupWidth = computed(() => this.width / Math.max(1, this.labels().length));
  protected readonly barWidth = computed(() => Math.max(2, (this.groupWidth() * 0.7) / Math.max(1, this.series().length)));

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

/** Big-number KPI tile. */
@Component({
  selector: 'app-stat',
  template: `
    <div class="stat">
      <span class="label">{{ label() }}</span>
      <span class="value">{{ value() }}</span>
      @if (hint()) {
        <span class="hint">{{ hint() }}</span>
      }
    </div>
  `,
  styles: `
    .stat { display: flex; flex-direction: column; gap: 4px; padding: 16px; border-radius: 12px;
      background: var(--mat-sys-surface); border: 1px solid var(--mat-sys-outline-variant); min-width: 150px; }
    .label { font-size: 0.8rem; color: var(--mat-sys-on-surface-variant); }
    .value { font-size: 1.6rem; font-weight: 700; }
    .hint { font-size: 0.75rem; color: var(--mat-sys-on-surface-variant); }
  `,
})
export class Stat {
  readonly label = input.required<string>();
  readonly value = input.required<string | number | null>();
  readonly hint = input<string>();
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
    .usage { display: flex; flex-direction: column; gap: 4px; }
    .row { display: flex; justify-content: space-between; font-size: 0.9rem; }
    .track { height: 8px; border-radius: 4px; background: var(--mat-sys-surface-container-high); overflow: hidden; }
    .fill { height: 100%; background: var(--mat-sys-primary); }
    .fill.warn { background: var(--mat-sys-error); }
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
