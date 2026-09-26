import { DatePipe } from '@angular/common';
import { inject, LOCALE_ID, Pipe, PipeTransform } from '@angular/core';
import { parseUtc } from './time-zones';

/**
 * `date`, for the API's *Utc fields: they arrive without a zone suffix, so the plain date pipe
 * would read them as local time. Shows the instant in the viewer's own time zone.
 */
@Pipe({ name: 'utcDate' })
export class UtcDatePipe implements PipeTransform {
  private readonly date = new DatePipe(inject(LOCALE_ID));

  transform(value: string | null | undefined, format = 'mediumDate'): string | null {
    return value ? this.date.transform(parseUtc(value), format) : null;
  }
}
