import { lang } from './i18n.ts';

const LOCALE_MAP: Record<string, string> = {
  en: 'en-US',
  de: 'de-DE',
  es: 'es-ES',
  fr: 'fr-FR',
  pt: 'pt-BR',
  it: 'it-IT',
  pl: 'pl-PL',
  ja: 'ja-JP',
};

/** BCP-47 tag derived from app language. */
export function localeTag(): string {
  return LOCALE_MAP[lang()] ?? 'en-US';
}

export function formatDate(d: Date | number, opts?: Intl.DateTimeFormatOptions): string {
  return new Date(d).toLocaleDateString(localeTag(), opts);
}

export function formatTime(d: Date | number, opts?: Intl.DateTimeFormatOptions): string {
  return new Date(d).toLocaleTimeString(localeTag(), opts);
}

export function formatDateTime(d: Date | number, opts?: Intl.DateTimeFormatOptions): string {
  return new Date(d).toLocaleString(localeTag(), opts);
}

export function formatNumber(n: number, opts?: Intl.NumberFormatOptions): string {
  return new Intl.NumberFormat(localeTag(), opts).format(n);
}

const REL = new Map<string, Intl.RelativeTimeFormat>();
function rel(): Intl.RelativeTimeFormat {
  const tag = localeTag();
  let r = REL.get(tag);
  if (!r) { r = new Intl.RelativeTimeFormat(tag, { numeric: 'auto' }); REL.set(tag, r); }
  return r;
}

/** "5 min ago" / "vor 5 Min." style relative formatting. Negative → past. */
export function formatRelative(value: number, unit: Intl.RelativeTimeFormatUnit): string {
  return rel().format(value, unit);
}
