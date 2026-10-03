import { HttpClient, HttpContext } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { toObservable } from '@angular/core/rxjs-interop';
import { distinctUntilChanged, map, pairwise, startWith, switchMap, of, catchError, Observable } from 'rxjs';
import { API_BASE, BACKGROUND, MAX_CART_QTY, SILENT_ERRORS } from '../config';
import { CartDto, CartItemDto, StockStatus } from '../models/api.models';
import { errorMessage } from '../interceptors/error.interceptor';
import { AuthService } from './auth.service';
import { StorageService } from './storage.service';
import { ToastService } from './toast.service';

const GUEST_KEY = 'tb.cart.v1';

/** One cart row as the UI needs it (totals are derived, never stored). */
export type CartLine = Omit<CartItemDto, 'lineTotal' | 'purchasable'>;

/** What a product card / detail page passes in to add something. */
export interface CartableProduct {
  id: number;
  slug: string;
  name: string;
  sku: string;
  imageUrl: string | null;
  price: number;
  effectivePrice: number;
  stockStatus: StockStatus;
}

export const isPurchasable = (s: StockStatus): boolean => s === 'InStock' || s === 'PreOrder';
const clampQty = (q: number): number => Math.min(MAX_CART_QTY, Math.max(1, Math.floor(q)));

/**
 * Guest cart in localStorage; signed-in cart on the server. On login the guest cart is merged into the server cart
 * (quantities add up, capped) and the local copy is dropped. On logout the cart falls back to an empty guest cart.
 */
@Injectable({ providedIn: 'root' })
export class CartService {
  private readonly http = inject(HttpClient);
  private readonly storage = inject(StorageService);
  private readonly auth = inject(AuthService);
  private readonly toast = inject(ToastService);

  private readonly _lines = signal<CartLine[]>([]);
  readonly lines = this._lines.asReadonly();
  readonly busy = signal(false);

  readonly count = computed(() => this._lines().reduce((n, l) => n + l.quantity, 0));
  /** Payable total: lines that became unavailable are shown but not charged (mirrors the API's CartDto). */
  readonly subtotal = computed(() => this._lines().filter((l) => isPurchasable(l.stockStatus)).reduce((s, l) => s + l.unitPrice * l.quantity, 0));
  readonly savings = computed(() => this._lines().filter((l) => isPurchasable(l.stockStatus)).reduce((s, l) => s + (l.listPrice - l.unitPrice) * l.quantity, 0));

  constructor() {
    // React to sign-in / sign-out. `pairwise` lets us tell "was guest, now user" (merge) from "was user, now guest".
    toObservable(this.auth.user)
      .pipe(
        map((u) => u?.id ?? null),
        distinctUntilChanged(),
        startWith(null as string | null),
        pairwise(),
        switchMap(([prev, next]) => {
          if (next) return this.mergeGuestIntoServer();
          if (prev) this._lines.set([]); // signed out: do not leak the previous user's cart
          return of(null);
        }),
      )
      .subscribe();
  }

  /** Load the guest cart from localStorage. Call once after hydration (keeps server and first client render identical). */
  init(): void {
    if (!this.auth.isAuthenticated()) this._lines.set(this.storage.get<CartLine[]>(GUEST_KEY, []));
  }

  quantityOf(productId: number): number {
    return this._lines().find((l) => l.productId === productId)?.quantity ?? 0;
  }

  add(product: CartableProduct, quantity = 1): void {
    if (!isPurchasable(product.stockStatus)) {
      this.toast.error(`${product.name} is currently unavailable.`);
      return;
    }
    const next = clampQty(this.quantityOf(product.id) + quantity);
    if (this.auth.isAuthenticated()) {
      this.serverCall(this.http.put<CartDto>(`${API_BASE}/cart/items/${product.id}`, { quantity: next }, this.ctx()), `${product.name} added to cart`);
      return;
    }
    this.writeGuest(this.upsert(this._lines(), {
      productId: product.id, slug: product.slug, name: product.name, sku: product.sku, imageUrl: product.imageUrl,
      listPrice: product.price, unitPrice: product.effectivePrice, quantity: next, stockStatus: product.stockStatus,
    }));
    this.toast.success(`${product.name} added to cart`);
  }

  setQuantity(productId: number, quantity: number): void {
    const q = clampQty(quantity);
    if (this.auth.isAuthenticated()) {
      this.serverCall(this.http.put<CartDto>(`${API_BASE}/cart/items/${productId}`, { quantity: q }, this.ctx()));
      return;
    }
    this.writeGuest(this._lines().map((l) => (l.productId === productId ? { ...l, quantity: q } : l)));
  }

  remove(productId: number): void {
    if (this.auth.isAuthenticated()) {
      this.serverCall(this.http.delete<CartDto>(`${API_BASE}/cart/items/${productId}`, this.ctx()));
      return;
    }
    this.writeGuest(this._lines().filter((l) => l.productId !== productId));
  }

  clear(): void {
    if (this.auth.isAuthenticated()) {
      this.serverCall(this.http.delete<CartDto>(`${API_BASE}/cart`, this.ctx()));
      return;
    }
    this.writeGuest([]);
  }

  /** Re-price the cart from the server (current prices / stock). Guests use the anonymous preview endpoint. */
  refresh(): void {
    if (this.auth.isAuthenticated()) {
      this.serverCall(this.http.get<CartDto>(`${API_BASE}/cart`, this.ctx(true)));
      return;
    }
    const lines = this._lines();
    if (!lines.length) return;
    this.http
      .post<CartDto>(`${API_BASE}/cart/preview`, { items: lines.map((l) => ({ productId: l.productId, quantity: l.quantity })) }, this.ctx(true))
      .pipe(catchError(() => of(null)))
      .subscribe((cart) => {
        if (!cart) return; // keep the local snapshot if the server is unreachable
        this.writeGuest(cart.items.map(toLine));
      });
  }

  private mergeGuestIntoServer(): Observable<unknown> {
    const guest = this.storage.get<CartLine[]>(GUEST_KEY, []);
    const request$ = guest.length
      ? this.http.post<CartDto>(`${API_BASE}/cart/merge`, { items: guest.map((l) => ({ productId: l.productId, quantity: l.quantity })) }, this.ctx(true))
      : this.http.get<CartDto>(`${API_BASE}/cart`, this.ctx(true));

    this.busy.set(true);
    return request$.pipe(
      map((cart) => {
        this._lines.set(cart.items.map(toLine));
        this.storage.remove(GUEST_KEY); // merged: the server cart is now the single source of truth
        if (guest.length) this.toast.success('Your cart was saved to your account.');
        this.busy.set(false);
        return cart;
      }),
      catchError(() => {
        this.busy.set(false);
        return of(null); // keep the guest copy so nothing is lost; the next login retries the merge
      }),
    );
  }

  private serverCall(request$: Observable<CartDto>, successMessage?: string): void {
    this.busy.set(true);
    request$.subscribe({
      next: (cart) => {
        this._lines.set(cart.items.map(toLine));
        this.busy.set(false);
        if (successMessage) this.toast.success(successMessage);
      },
      error: (e) => {
        this.busy.set(false);
        this.toast.error(errorMessage(e, 'Could not update your cart.'));
      },
    });
  }

  private upsert(lines: CartLine[], line: CartLine): CartLine[] {
    return lines.some((l) => l.productId === line.productId)
      ? lines.map((l) => (l.productId === line.productId ? line : l))
      : [...lines, line];
  }

  private writeGuest(lines: CartLine[]): void {
    this._lines.set(lines);
    this.storage.set(GUEST_KEY, lines);
  }

  private ctx(background = false): { context: HttpContext } {
    const context = new HttpContext().set(SILENT_ERRORS, true);
    return { context: background ? context.set(BACKGROUND, true) : context };
  }
}

function toLine(i: CartItemDto): CartLine {
  const { lineTotal: _l, purchasable: _p, ...line } = i;
  return line;
}
