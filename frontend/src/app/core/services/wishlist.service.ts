import { HttpClient, HttpContext } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { toObservable } from '@angular/core/rxjs-interop';
import { Observable, catchError, distinctUntilChanged, map, of, pairwise, startWith, switchMap } from 'rxjs';
import { API_BASE, SILENT_ERRORS } from '../config';
import { ProductListItem } from '../models/api.models';
import { errorMessage } from '../interceptors/error.interceptor';
import { AuthService } from './auth.service';
import { StorageService } from './storage.service';
import { ToastService } from './toast.service';

const GUEST_KEY = 'tb.wishlist.v1';

/** Wishlist: localStorage snapshots for guests, server list when signed in (guest items are merged on login). */
@Injectable({ providedIn: 'root' })
export class WishlistService {
  private readonly http = inject(HttpClient);
  private readonly storage = inject(StorageService);
  private readonly auth = inject(AuthService);
  private readonly toast = inject(ToastService);

  private readonly _items = signal<ProductListItem[]>([]);
  readonly items = this._items.asReadonly();
  readonly count = computed(() => this._items().length);
  private readonly ids = computed(() => new Set(this._items().map((i) => i.id)));

  constructor() {
    toObservable(this.auth.user)
      .pipe(
        map((u) => u?.id ?? null),
        distinctUntilChanged(),
        startWith(null as string | null),
        pairwise(),
        switchMap(([prev, next]) => {
          if (next) return this.mergeGuestIntoServer();
          if (prev) this._items.set([]);
          return of(null);
        }),
      )
      .subscribe();
  }

  init(): void {
    if (!this.auth.isAuthenticated()) this._items.set(this.storage.get<ProductListItem[]>(GUEST_KEY, []));
  }

  has(productId: number): boolean {
    return this.ids().has(productId);
  }

  toggle(product: ProductListItem): void {
    const adding = !this.has(product.id);
    if (this.auth.isAuthenticated()) {
      const call$ = adding
        ? this.http.put<ProductListItem[]>(`${API_BASE}/wishlist/${product.id}`, null, this.ctx())
        : this.http.delete<ProductListItem[]>(`${API_BASE}/wishlist/${product.id}`, this.ctx());
      call$.subscribe({
        next: (items) => {
          this._items.set(items);
          this.toast.success(adding ? 'Added to wishlist' : 'Removed from wishlist');
        },
        error: (e) => this.toast.error(errorMessage(e, 'Could not update your wishlist.')),
      });
      return;
    }
    const next = adding ? [product, ...this._items()] : this._items().filter((i) => i.id !== product.id);
    this._items.set(next);
    this.storage.set(GUEST_KEY, next);
    this.toast.success(adding ? 'Added to wishlist' : 'Removed from wishlist');
  }

  private mergeGuestIntoServer(): Observable<unknown> {
    const guest = this.storage.get<ProductListItem[]>(GUEST_KEY, []);
    const request$ = guest.length
      ? this.http.post<ProductListItem[]>(`${API_BASE}/wishlist/merge`, { productIds: guest.map((i) => i.id) }, this.ctx())
      : this.http.get<ProductListItem[]>(`${API_BASE}/wishlist`, this.ctx());
    return request$.pipe(
      map((items) => {
        this._items.set(items);
        this.storage.remove(GUEST_KEY);
        return items;
      }),
      catchError(() => of(null)),
    );
  }

  private ctx(): { context: HttpContext } {
    return { context: new HttpContext().set(SILENT_ERRORS, true) };
  }
}
