import { HttpClient, HttpContext, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE, SILENT_ERRORS } from '../config';
import {
  Address, CheckoutOptions, CheckoutQuote, CheckoutQuoteRequest, OrderDetail, OrderSummary, Paged, PaymentRedirect, PlaceOrderRequest,
  PlaceOrderResult, SaveAddressRequest,
} from '../models/api.models';

/** Addresses, checkout quote, order placement and tracking. Totals always come from the server; nothing is calculated client-side. */
@Injectable({ providedIn: 'root' })
export class CheckoutService {
  private readonly http = inject(HttpClient);
  /** Forms show server validation / conflict messages inline. */
  private readonly inline = () => new HttpContext().set(SILENT_ERRORS, true);

  addresses(): Observable<Address[]> {
    return this.http.get<Address[]>(`${API_BASE}/addresses`);
  }
  createAddress(body: SaveAddressRequest): Observable<Address> {
    return this.http.post<Address>(`${API_BASE}/addresses`, body, { context: this.inline() });
  }
  updateAddress(id: number, body: SaveAddressRequest): Observable<Address> {
    return this.http.put<Address>(`${API_BASE}/addresses/${id}`, body, { context: this.inline() });
  }
  setDefaultAddress(id: number): Observable<unknown> {
    return this.http.put(`${API_BASE}/addresses/${id}/default`, {});
  }
  deleteAddress(id: number): Observable<unknown> {
    return this.http.delete(`${API_BASE}/addresses/${id}`);
  }

  options(): Observable<CheckoutOptions> {
    return this.http.get<CheckoutOptions>(`${API_BASE}/checkout/options`);
  }
  quote(body: CheckoutQuoteRequest): Observable<CheckoutQuote> {
    return this.http.post<CheckoutQuote>(`${API_BASE}/checkout/quote`, body, { context: this.inline() });
  }
  place(body: PlaceOrderRequest): Observable<PlaceOrderResult> {
    return this.http.post<PlaceOrderResult>(`${API_BASE}/orders`, body, { context: this.inline() });
  }

  orders(page = 1, pageSize = 10): Observable<Paged<OrderSummary>> {
    return this.http.get<Paged<OrderSummary>>(`${API_BASE}/orders`, { params: new HttpParams().set('page', page).set('pageSize', pageSize) });
  }
  order(orderNumber: string): Observable<OrderDetail> {
    return this.http.get<OrderDetail>(`${API_BASE}/orders/${encodeURIComponent(orderNumber)}`, { context: this.inline() });
  }
  cancel(orderNumber: string): Observable<OrderDetail> {
    return this.http.post<OrderDetail>(`${API_BASE}/orders/${encodeURIComponent(orderNumber)}/cancel`, {}, { context: this.inline() });
  }
  pay(orderNumber: string): Observable<PaymentRedirect> {
    return this.http.post<PaymentRedirect>(`${API_BASE}/orders/${encodeURIComponent(orderNumber)}/pay`, {}, { context: this.inline() });
  }
}
