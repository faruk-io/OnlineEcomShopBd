import { HttpClient, HttpContext, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, catchError, shareReplay, throwError } from 'rxjs';
import { API_BASE, BACKGROUND } from '../config';
import {
  AutocompleteResult, BrandListItem, CategoryDetail, CategoryTreeNode, Paged, ProductDetail, ProductFacets, ProductListItem,
} from '../models/api.models';
import { ListingFilters, toApiParams } from '../util/listing-query';

@Injectable({ providedIn: 'root' })
export class CatalogService {
  private readonly http = inject(HttpClient);
  private tree$: Observable<CategoryTreeNode[]> | null = null;
  private brands$: Observable<BrandListItem[]> | null = null;

  products(filters: ListingFilters, category?: string | null, pageSize?: number): Observable<Paged<ProductListItem>> {
    return this.http.get<Paged<ProductListItem>>(`${API_BASE}/products`, { params: toApiParams(filters, category, pageSize) });
  }

  /** Full-text search page (q is mandatory server-side). */
  search(filters: ListingFilters, pageSize?: number): Observable<Paged<ProductListItem>> {
    return this.http.get<Paged<ProductListItem>>(`${API_BASE}/search`, { params: toApiParams(filters, null, pageSize) });
  }

  /** Quick lists for the home page / rails. */
  list(params: Record<string, string | number | boolean>): Observable<Paged<ProductListItem>> {
    let p = new HttpParams();
    for (const [k, v] of Object.entries(params)) p = p.set(k, v);
    return this.http.get<Paged<ProductListItem>>(`${API_BASE}/products`, { params: p });
  }

  facets(category?: string | null): Observable<ProductFacets> {
    let p = new HttpParams();
    if (category) p = p.set('category', category);
    return this.http.get<ProductFacets>(`${API_BASE}/products/facets`, { params: p });
  }

  product(slug: string): Observable<ProductDetail> {
    return this.http.get<ProductDetail>(`${API_BASE}/products/${encodeURIComponent(slug)}`);
  }

  related(slug: string, count = 8): Observable<ProductListItem[]> {
    return this.http.get<ProductListItem[]>(`${API_BASE}/products/${encodeURIComponent(slug)}/related`, {
      params: new HttpParams().set('count', count),
    });
  }

  /** Category tree is shared by header, home and footer: fetched once per app instance. */
  categoryTree(): Observable<CategoryTreeNode[]> {
    this.tree$ ??= this.http.get<CategoryTreeNode[]>(`${API_BASE}/categories`).pipe(
      shareReplay({ bufferSize: 1, refCount: false }),
      catchError((e) => {
        this.tree$ = null; // allow a retry on the next call
        return throwError(() => e);
      }),
    );
    return this.tree$;
  }

  category(slug: string): Observable<CategoryDetail> {
    return this.http.get<CategoryDetail>(`${API_BASE}/categories/${encodeURIComponent(slug)}`);
  }

  brands(): Observable<BrandListItem[]> {
    this.brands$ ??= this.http.get<BrandListItem[]>(`${API_BASE}/brands`).pipe(
      shareReplay({ bufferSize: 1, refCount: false }),
      catchError((e) => {
        this.brands$ = null;
        return throwError(() => e);
      }),
    );
    return this.brands$;
  }

  autocomplete(q: string, limit = 6): Observable<AutocompleteResult> {
    return this.http.get<AutocompleteResult>(`${API_BASE}/search/autocomplete`, {
      params: new HttpParams().set('q', q).set('limit', limit),
      context: new HttpContext().set(BACKGROUND, true),
    });
  }
}
