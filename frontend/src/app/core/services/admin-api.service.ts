import { HttpClient, HttpContext, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE, SILENT_ERRORS } from '../config';
import {
  AdminBrand, AdminCategory, AdminCoupon, AdminOrderDetail, AdminOrderListItem, AdminProductDetail, AdminProductListItem, Dashboard, OrderStatus,
  Paged, SaveBrandRequest, SaveCategoryRequest, SaveCouponRequest, SaveProductRequest,
} from '../models/api.models';

const A = `${API_BASE}/admin`;

/** Thin typed wrapper over /api/v1/admin/*. Mutations render their own errors (SILENT_ERRORS) so forms can show field messages. */
@Injectable({ providedIn: 'root' })
export class AdminApiService {
  private readonly http = inject(HttpClient);
  private readonly inline = () => ({ context: new HttpContext().set(SILENT_ERRORS, true) });

  private query(o: Record<string, string | number | boolean | null | undefined>): HttpParams {
    let p = new HttpParams();
    for (const [k, v] of Object.entries(o)) if (v !== null && v !== undefined && v !== '') p = p.set(k, String(v));
    return p;
  }

  dashboard(days = 30, lowStock = 5): Observable<Dashboard> {
    return this.http.get<Dashboard>(`${A}/dashboard`, { params: this.query({ days, lowStock }) });
  }

  products(o: { search?: string; categoryId?: number | null; lowStock?: boolean; page?: number; pageSize?: number }): Observable<Paged<AdminProductListItem>> {
    return this.http.get<Paged<AdminProductListItem>>(`${A}/products`, { params: this.query(o) });
  }
  product(id: number): Observable<AdminProductDetail> {
    return this.http.get<AdminProductDetail>(`${A}/products/${id}`);
  }
  createProduct(body: SaveProductRequest): Observable<AdminProductDetail> {
    return this.http.post<AdminProductDetail>(`${A}/products`, body, this.inline());
  }
  updateProduct(id: number, body: SaveProductRequest): Observable<AdminProductDetail> {
    return this.http.put<AdminProductDetail>(`${A}/products/${id}`, body, this.inline());
  }
  deleteProduct(id: number): Observable<unknown> {
    return this.http.delete(`${A}/products/${id}`, this.inline());
  }
  uploadImage(file: File): Observable<{ url: string }> {
    const form = new FormData();
    form.append('file', file, file.name);
    return this.http.post<{ url: string }>(`${A}/uploads/images`, form, this.inline());
  }

  categories(): Observable<AdminCategory[]> {
    return this.http.get<AdminCategory[]>(`${A}/categories`);
  }
  createCategory(b: SaveCategoryRequest): Observable<AdminCategory> {
    return this.http.post<AdminCategory>(`${A}/categories`, b, this.inline());
  }
  updateCategory(id: number, b: SaveCategoryRequest): Observable<AdminCategory> {
    return this.http.put<AdminCategory>(`${A}/categories/${id}`, b, this.inline());
  }
  deleteCategory(id: number): Observable<unknown> {
    return this.http.delete(`${A}/categories/${id}`, this.inline());
  }

  brands(): Observable<AdminBrand[]> {
    return this.http.get<AdminBrand[]>(`${A}/brands`);
  }
  createBrand(b: SaveBrandRequest): Observable<AdminBrand> {
    return this.http.post<AdminBrand>(`${A}/brands`, b, this.inline());
  }
  updateBrand(id: number, b: SaveBrandRequest): Observable<AdminBrand> {
    return this.http.put<AdminBrand>(`${A}/brands/${id}`, b, this.inline());
  }
  deleteBrand(id: number): Observable<unknown> {
    return this.http.delete(`${A}/brands/${id}`, this.inline());
  }

  coupons(): Observable<AdminCoupon[]> {
    return this.http.get<AdminCoupon[]>(`${A}/coupons`);
  }
  createCoupon(b: SaveCouponRequest): Observable<AdminCoupon> {
    return this.http.post<AdminCoupon>(`${A}/coupons`, b, this.inline());
  }
  updateCoupon(id: number, b: SaveCouponRequest): Observable<AdminCoupon> {
    return this.http.put<AdminCoupon>(`${A}/coupons/${id}`, b, this.inline());
  }
  deleteCoupon(id: number): Observable<unknown> {
    return this.http.delete(`${A}/coupons/${id}`, this.inline());
  }

  orders(o: { status?: OrderStatus | null; search?: string; page?: number; pageSize?: number }): Observable<Paged<AdminOrderListItem>> {
    return this.http.get<Paged<AdminOrderListItem>>(`${A}/orders`, { params: this.query(o) });
  }
  order(orderNumber: string): Observable<AdminOrderDetail> {
    return this.http.get<AdminOrderDetail>(`${A}/orders/${encodeURIComponent(orderNumber)}`);
  }
  setOrderStatus(orderNumber: string, status: OrderStatus, note: string | null): Observable<AdminOrderDetail> {
    return this.http.put<AdminOrderDetail>(`${A}/orders/${encodeURIComponent(orderNumber)}/status`, { status, note }, this.inline());
  }
  markPaid(orderNumber: string): Observable<AdminOrderDetail> {
    return this.http.post<AdminOrderDetail>(`${A}/orders/${encodeURIComponent(orderNumber)}/mark-paid`, {}, this.inline());
  }
}
