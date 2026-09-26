/** The academy's own clock: session times are always shown in Egypt time as well as the student's. */
export const ACADEMY_TIME_ZONE = 'Africa/Cairo';

/** Every IANA zone the browser knows, for pickers. */
export const TIME_ZONES: readonly string[] = (() => {
  try {
    return Intl.supportedValuesOf('timeZone');
  } catch {
    return [ACADEMY_TIME_ZONE, 'Asia/Riyadh', 'Asia/Dubai', 'Asia/Kuwait', 'Asia/Qatar', 'Europe/London', 'America/New_York', 'UTC'];
  }
})();

/**
 * The API sends *Utc fields without a zone suffix (EF reads them as unspecified), which
 * `new Date()` would treat as local time. Mark them as UTC before parsing.
 */
export function parseUtc(value: string): Date {
  return new Date(/[zZ]|[+-]\d\d:?\d\d$/.test(value) ? value : `${value}Z`);
}

function locale(lang: string): string {
  // Latin digits keep times easy to compare side by side in both languages.
  return lang === 'ar' ? 'ar-EG-u-nu-latn' : 'en-GB';
}

/** e.g. "Thu 25 Sep 2026". */
export function formatDate(utc: string, timeZone: string, lang: string): string {
  return new Intl.DateTimeFormat(locale(lang), { timeZone, weekday: 'short', day: 'numeric', month: 'short', year: 'numeric' }).format(parseUtc(utc));
}

/** e.g. "18:30". */
export function formatTime(utc: string, timeZone: string, lang: string): string {
  return new Intl.DateTimeFormat(locale(lang), { timeZone, hour: '2-digit', minute: '2-digit', hourCycle: 'h23' }).format(parseUtc(utc));
}

/** Live clock with seconds, e.g. "18:30:05". */
export function formatClock(at: Date, timeZone: string): string {
  return new Intl.DateTimeFormat('en-GB', { timeZone, hour: '2-digit', minute: '2-digit', second: '2-digit', hourCycle: 'h23' }).format(at);
}

/** Calendar day of an instant in a zone, as 'YYYY-MM-DD', to tell when a session falls on a different day. */
export function dayKey(utc: string, timeZone: string): string {
  return new Intl.DateTimeFormat('en-CA', { timeZone, year: 'numeric', month: '2-digit', day: '2-digit' }).format(parseUtc(utc));
}

/** e.g. "GMT+3". */
export function utcOffset(timeZone: string, at = new Date()): string {
  const part = new Intl.DateTimeFormat('en-US', { timeZone, timeZoneName: 'shortOffset' })
    .formatToParts(at)
    .find((p) => p.type === 'timeZoneName');
  return part?.value ?? '';
}

/** e.g. "Asia/Riyadh (GMT+3)". */
export function zoneLabel(timeZone: string): string {
  return `${timeZone.replace(/_/g, ' ')} (${utcOffset(timeZone)})`;
}
