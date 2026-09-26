import { afterNextRender, DestroyRef, Directive, ElementRef, inject } from '@angular/core';

/**
 * Copies each column header onto its body cells as `data-label`, so that on phones (where
 * styles.scss turns every row into a card) each value shows its column name. Watches the table,
 * so rows added later and headers re-translated on a language switch stay labelled.
 */
@Directive({ selector: 'table.data-table' })
export class DataTable {
  private readonly table: HTMLTableElement = inject(ElementRef).nativeElement;

  constructor() {
    const destroyRef = inject(DestroyRef);
    afterNextRender(() => {
      this.label();
      // Only structure and text are observed, so writing attributes here never re-triggers it.
      const observer = new MutationObserver(() => this.label());
      observer.observe(this.table, { childList: true, characterData: true, subtree: true });
      destroyRef.onDestroy(() => observer.disconnect());
    });
  }

  private label(): void {
    const headerRow = this.table.tHead?.rows[this.table.tHead.rows.length - 1];
    if (!headerRow) {
      return;
    }

    // Expand colspans so header positions line up with cell positions.
    const headers: string[] = [];
    for (const th of Array.from(headerRow.cells)) {
      for (let i = 0; i < th.colSpan; i++) {
        headers.push(th.textContent?.trim() ?? '');
      }
    }

    for (const body of Array.from(this.table.tBodies)) {
      for (const row of Array.from(body.rows)) {
        let column = 0;
        for (const cell of Array.from(row.cells)) {
          const text = cell.colSpan === 1 ? headers[column] : '';
          if (text) {
            if (cell.getAttribute('data-label') !== text) {
              cell.setAttribute('data-label', text);
            }
          } else if (cell.hasAttribute('data-label')) {
            cell.removeAttribute('data-label');
          }
          column += cell.colSpan;
        }
      }
    }
  }
}
