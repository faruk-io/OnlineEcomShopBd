import { ChangeDetectionStrategy, Component, ElementRef, afterNextRender, inject, viewChild } from '@angular/core';
import { NavigationEnd, Router, RouterOutlet } from '@angular/router';
import { filter } from 'rxjs';
import { AuthService } from './core/services/auth.service';
import { CartService } from './core/services/cart.service';
import { CompareService } from './core/services/compare.service';
import { WishlistService } from './core/services/wishlist.service';
import { FooterComponent } from './layout/footer.component';
import { HeaderComponent } from './layout/header.component';
import { LoadingBarComponent } from './layout/loading-bar.component';
import { ToastsComponent } from './layout/toasts.component';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, HeaderComponent, FooterComponent, ToastsComponent, LoadingBarComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <a class="skip-link" href="#main" (click)="focusMain($event)">Skip to main content</a>
    <app-loading-bar />
    <app-header />
    <main id="main" #main tabindex="-1"><router-outlet /></main>
    <app-footer />
    <app-toasts />
  `,
  styles: `
    :host { display: block; min-height: 100vh; }
    main { display: block; min-height: 60vh; }
    .skip-link { position: absolute; left: 0.5rem; top: -4rem; z-index: 400; background: var(--accent); color: #1f1300; padding: 0.6rem 1rem; border-radius: var(--radius); font-weight: 700; }
    .skip-link:focus { top: 0.5rem; }
  `,
})
export class App {
  private readonly router = inject(Router);
  private readonly main = viewChild.required<ElementRef<HTMLElement>>('main');

  constructor() {
    const auth = inject(AuthService);
    const cart = inject(CartService);
    const wishlist = inject(WishlistService);
    const compare = inject(CompareService);

    // Browser-only state is loaded after hydration so the first client render matches the server HTML exactly.
    afterNextRender(() => {
      cart.init();
      wishlist.init();
      compare.init();
      void auth.whenReady(); // silent session restore from the stored refresh token
    });

    // After each client-side navigation move focus to <main> so keyboard / screen-reader users start at the new content.
    let first = true;
    this.router.events.pipe(filter((e) => e instanceof NavigationEnd)).subscribe(() => {
      if (first) { first = false; return; }
      this.main().nativeElement.focus({ preventScroll: true });
    });
  }

  protected focusMain(event: Event): void {
    event.preventDefault();
    this.main().nativeElement.focus();
  }
}
