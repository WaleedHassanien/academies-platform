import { Dir } from '@angular/cdk/bidi';
import { Component, inject } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { LanguageService } from './core/i18n/language.service';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, Dir],
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App {
  // Material reads direction from the nearest [dir], so switching language flips it live.
  protected readonly language = inject(LanguageService);
}
