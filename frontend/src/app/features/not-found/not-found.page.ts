import { ChangeDetectionStrategy, Component, inject, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { SeoService } from '../../core/services/seo.service';

@Component({
  selector: 'app-not-found',
  imports: [RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="container empty">
      <p class="code" aria-hidden="true">404</p>
      <h1>{{ heading() }}</h1>
      <p>{{ message() }}</p>
      <p class="actions">
        <a class="btn btn-primary" routerLink="/">Back to home</a>
        <a class="btn btn-outline" routerLink="/shop">Browse all products</a>
      </p>
    </div>
  `,
  styles: `
    .empty { padding-block: 4rem; }
    .code { font-size: 4rem; font-weight: 800; color: var(--primary); margin: 0; line-height: 1; }
    .actions { display: flex; gap: .75rem; justify-content: center; flex-wrap: wrap; margin-top: 1.5rem; }
  `,
})
export class NotFoundPage {
  readonly heading = input('Page not found');
  readonly message = input('The page you are looking for doesn’t exist or has moved.');

  constructor() {
    const seo = inject(SeoService);
    seo.setStatus(404);
    seo.set({ title: 'Page not found', noindex: true });
  }
}
