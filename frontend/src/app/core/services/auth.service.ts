import { HttpClient, HttpContext, HttpHeaders } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { Observable, finalize, map, shareReplay, tap, throwError } from 'rxjs';
import { API_BASE, SILENT_ERRORS, SKIP_AUTH } from '../config';
import { AuthResponse, MessageDto, UserDto } from '../models/api.models';
import { StorageService } from './storage.service';

/**
 * The rotating refresh token lives ONLY in an HttpOnly, SameSite=Strict cookie set by the API, so injected script can never read it.
 * What we keep in localStorage is just this non-secret hint that a session may exist (so a reload knows to try a silent refresh).
 */
const SESSION_KEY = 'tb.session.v1';
/** Pre-cookie versions stored the refresh token itself here; it is sent once to be exchanged for a cookie, then deleted. */
const LEGACY_REFRESH_KEY = 'tb.refresh.v1';
/** Tells the API to deliver / read the refresh token via the cookie (and, as a custom header, makes cross-site forgery impossible). */
const COOKIE_MODE = new HttpHeaders({ 'X-Refresh-Mode': 'cookie' });

export interface RegisterPayload {
  fullName: string;
  email: string;
  phone?: string | null;
  password: string;
}

/**
 * Session handling. The short-lived access token lives in memory only; the rotating refresh token is an HttpOnly cookie that
 * JavaScript cannot see, so a page reload silently restores the session without any token ever being readable by page scripts.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly storage = inject(StorageService);

  private readonly _user = signal<UserDto | null>(null);
  readonly user = this._user.asReadonly();
  readonly isAuthenticated = computed(() => this._user() !== null);
  readonly isAdmin = computed(() => this._user()?.roles.includes('Admin') ?? false);

  private token: string | null = null;
  private inflightRefresh$: Observable<string> | null = null;
  private restore: Promise<void> | null = null;

  accessToken(): string | null {
    return this.token;
  }

  /** True when a session may be restorable (a hint only: the server decides). */
  hasSession(): boolean {
    return !!this.storage.getString(SESSION_KEY) || !!this.storage.getString(LEGACY_REFRESH_KEY);
  }

  /**
   * Resolves once the stored session (if any) has been restored. Safe to call many times; guards await it so a
   * hard reload on a protected page does not bounce a signed-in user to /login.
   */
  whenReady(): Promise<void> {
    if (!this.storage.isBrowser || !this.hasSession() || this.token) return Promise.resolve();
    this.restore ??= new Promise<void>((resolve) => {
      this.refresh().subscribe({ next: () => resolve(), error: () => resolve() });
    });
    return this.restore;
  }

  login(email: string, password: string): Observable<UserDto> {
    return this.http
      .post<AuthResponse>(`${API_BASE}/auth/login`, { email, password }, { context: this.formContext(), headers: COOKIE_MODE })
      .pipe(tap((res) => this.setSession(res)), map((res) => res.user));
  }

  register(payload: RegisterPayload): Observable<UserDto> {
    return this.http
      .post<AuthResponse>(`${API_BASE}/auth/register`, payload, { context: this.formContext(), headers: COOKIE_MODE })
      .pipe(tap((res) => this.setSession(res)), map((res) => res.user));
  }

  updateProfile(fullName: string, phone: string | null): Observable<UserDto> {
    return this.http
      .put<UserDto>(`${API_BASE}/auth/me`, { fullName, phone }, { context: new HttpContext().set(SILENT_ERRORS, true) })
      .pipe(tap((user) => this._user.set(user)));
  }

  /** Always answers 202 with the same message, whether or not the address has an account. */
  forgotPassword(email: string): Observable<MessageDto> {
    return this.http.post<MessageDto>(`${API_BASE}/auth/forgot-password`, { email }, { context: this.formContext() });
  }

  /** Spends the one-time link token. The server revokes every session of the account, so the local one is dropped too. */
  resetPassword(token: string, newPassword: string): Observable<void> {
    return this.http
      .post<void>(`${API_BASE}/auth/reset-password`, { token, newPassword }, { context: this.formContext() })
      .pipe(tap(() => this.clearSession()));
  }

  verifyEmail(token: string): Observable<void> {
    return this.http.post<void>(`${API_BASE}/auth/verify-email`, { token }, { context: this.formContext() });
  }

  /** Needs a signed-in user (the access token is attached). Throttled server-side. */
  resendVerification(): Observable<MessageDto> {
    return this.http.post<MessageDto>(`${API_BASE}/auth/resend-verification`, {}, { context: new HttpContext().set(SILENT_ERRORS, true) });
  }

  /** Re-reads the profile (e.g. after the email was verified in this browser). */
  reloadUser(): Observable<UserDto> {
    return this.http
      .get<UserDto>(`${API_BASE}/auth/me`, { context: new HttpContext().set(SILENT_ERRORS, true) })
      .pipe(tap((user) => this._user.set(user)));
  }

  logout(): void {
    if (this.hasSession()) {
      // Best effort, never block the UI on it. The API revokes by the cookie / token itself, so this works even when the access
      // token has already expired (a logout that silently leaves a live refresh token behind is not a logout).
      const legacy = this.storage.getString(LEGACY_REFRESH_KEY);
      this.http
        .post(`${API_BASE}/auth/logout`, legacy ? { refreshToken: legacy } : {}, { context: this.formContext(), headers: COOKIE_MODE })
        .subscribe({ error: () => undefined });
    }
    this.clearSession();
  }

  /** "Sign out of all devices": revokes every refresh token of the account. */
  logoutAll(): Observable<void> {
    return this.http
      .post<void>(`${API_BASE}/auth/logout-all`, {}, { context: new HttpContext().set(SILENT_ERRORS, true), headers: COOKIE_MODE })
      .pipe(tap(() => this.clearSession()));
  }

  /** Single-flight refresh: concurrent 401s share one request (the refresh token is single-use). */
  refresh(): Observable<string> {
    if (this.inflightRefresh$) return this.inflightRefresh$;
    if (!this.hasSession()) return throwError(() => new Error('No session'));
    const legacy = this.storage.getString(LEGACY_REFRESH_KEY);   // one-time migration from the pre-cookie storage

    this.inflightRefresh$ = this.http
      .post<AuthResponse>(`${API_BASE}/auth/refresh`, legacy ? { refreshToken: legacy } : {}, { context: this.formContext(), headers: COOKIE_MODE })
      .pipe(
        tap({ next: (res) => this.setSession(res), error: () => this.clearSession() }),
        map((res) => res.accessToken),
        finalize(() => (this.inflightRefresh$ = null)),
        shareReplay({ bufferSize: 1, refCount: true }),
      );
    return this.inflightRefresh$;
  }

  clearSession(): void {
    this.token = null;
    this._user.set(null);
    this.storage.remove(SESSION_KEY);
    this.storage.remove(LEGACY_REFRESH_KEY);
  }

  private setSession(res: AuthResponse): void {
    this.token = res.accessToken;
    this.storage.set(SESSION_KEY, '1');
    this.storage.remove(LEGACY_REFRESH_KEY);   // now held by the cookie
    // (res.refreshToken is null in cookie mode; it is never stored by script)
    this._user.set(res.user);
  }

  private formContext(): HttpContext {
    return new HttpContext().set(SKIP_AUTH, true).set(SILENT_ERRORS, true);
  }
}
