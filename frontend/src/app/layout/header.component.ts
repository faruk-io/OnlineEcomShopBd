import { ChangeDetectionStrategy, Component, ElementRef, computed, effect, inject, signal, viewChild } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NavigationStart, Router, RouterLink, RouterLinkActive } from '@angular/router';
import { catchError, filter, of } from 'rxjs';
import { CategoryTreeNode } from '../core/models/api.models';
import { AuthService } from '../core/services/auth.service';
import { CartService } from '../core/services/cart.service';
import { CatalogService } from '../core/services/catalog.service';
import { CompareService } from '../core/services/compare.service';
import { WishlistService } from '../core/services/wishlist.service';
import { IconComponent } from '../shared/icon.component';
import { SearchBoxComponent } from './search-box.component';

@Component({
  selector: 'app-header',
  imports: [RouterLink, RouterLinkActive, IconComponent, SearchBoxComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './header.component.html',
  styleUrl: './header.component.scss',
  host: { '(document:keydown.escape)': 'closeAll()' },
})
export class HeaderComponent {
  private readonly router = inject(Router);
  private readonly catalog = inject(CatalogService);
  protected readonly auth = inject(AuthService);
  protected readonly cart = inject(CartService);
  protected readonly wishlist = inject(WishlistService);
  protected readonly compare = inject(CompareService);

  protected readonly tree = toSignal(this.catalog.categoryTree().pipe(catchError(() => of([] as CategoryTreeNode[]))), { initialValue: [] as CategoryTreeNode[] });
  protected readonly openMega = signal<string | null>(null);
  protected readonly drawerOpen = signal(false);
  protected readonly expandedMobile = signal<string | null>(null);
  protected readonly firstName = computed(() => this.auth.user()?.fullName.split(' ')[0] ?? '');

  private readonly closeBtn = viewChild<ElementRef<HTMLButtonElement>>('closeBtn');
  private closeTimer: ReturnType<typeof setTimeout> | undefined;

  constructor() {
    this.router.events.pipe(filter((e) => e instanceof NavigationStart)).subscribe(() => this.closeAll());
    effect(() => {
      if (this.drawerOpen()) queueMicrotask(() => this.closeBtn()?.nativeElement.focus());
    });
  }

  protected showMega(slug: string): void {
    clearTimeout(this.closeTimer);
    this.openMega.set(slug);
  }

  protected hideMegaSoon(): void {
    clearTimeout(this.closeTimer);
    this.closeTimer = setTimeout(() => this.openMega.set(null), 150);
  }

  protected toggleMega(slug: string): void {
    this.openMega.update((s) => (s === slug ? null : slug));
  }

  protected onMegaFocusOut(event: FocusEvent, host: HTMLElement): void {
    if (!host.contains(event.relatedTarget as Node | null)) this.openMega.set(null);
  }

  protected closeAll(): void {
    this.openMega.set(null);
    this.drawerOpen.set(false);
  }

  protected logout(): void {
    this.auth.logout();
    void this.router.navigateByUrl('/');
  }
}
