import { DOCUMENT } from '@angular/common';
import { computed, effect, inject, Injectable, signal } from '@angular/core';
import { TranslateService } from '@ngx-translate/core';

export type Language = 'ar' | 'en';

const STORAGE_KEY = 'academies.lang';

/** Current UI language and text direction (US-005). Arabic is the default and is RTL. */
@Injectable({ providedIn: 'root' })
export class LanguageService {
  private readonly translate = inject(TranslateService);
  private readonly document = inject(DOCUMENT);

  readonly language = signal<Language>(readStoredLanguage());
  readonly dir = computed(() => (this.language() === 'ar' ? 'rtl' : 'ltr'));

  constructor() {
    this.translate.addLangs(['ar', 'en']);
    this.translate.setFallbackLang('ar');

    effect(() => {
      const lang = this.language();
      this.translate.use(lang);
      this.document.documentElement.lang = lang;
      this.document.documentElement.dir = this.dir();
      try {
        localStorage.setItem(STORAGE_KEY, lang);
      } catch {
        // Storage unavailable: language resets on reload, which is acceptable.
      }
    });
  }

  toggle(): void {
    this.language.update((l) => (l === 'ar' ? 'en' : 'ar'));
  }
}

function readStoredLanguage(): Language {
  try {
    return localStorage.getItem(STORAGE_KEY) === 'en' ? 'en' : 'ar';
  } catch {
    return 'ar';
  }
}
