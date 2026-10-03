import { HttpClient, HttpContext } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { Observable, finalize, map, shareReplay, tap, throwError } from 'rxjs';
import { API_BASE, SILENT_ERRORS, SKIP_AUTH } from '../config';
import { AuthResponse, UserDto } from '../models/api.models';
import { StorageService } from './storage.service';

const REFRESH_KEY = 'tb.refresh.v1';

export interface RegisterPayload {
  fullName: string;
  email: string;
  phone?: string | null;
  password: string;
}

/**
 * Session handling. The short-lived access token lives in memory only; the rotating refresh token is persisted
 * so a page reload can silently restore the session (the API takes it in the JSON body).
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

  hasRefreshToken(): boolean {
    return !!this.storage.getString(REFRESH_KEY);
  }

  /**
   * Resolves once the stored session (if any) has been restored. Safe to call many times; guards await it so a
   * hard reload on a protected page does not bounce a signed-in user to /login.
   */
  whenReady(): Promise<void> {
    if (!this.storage.isBrowser || !this.hasRefreshToken() || this.token) return Promise.resolve();
    this.restore ??= new Promise<void>((resolve) => {
      this.refresh().subscribe({ next: () => resolve(), error: () => resolve() });
    });
    return this.restore;
  }

  login(email: string, password: string): Observable<UserDto> {
    return this.http
      .post<AuthResponse>(`${API_BASE}/auth/login`, { email, password }, { context: this.formContext() })
      .pipe(tap((res) => this.setSession(res)), map((res) => res.user));
  }

  register(payload: RegisterPayload): Observable<UserDto> {
    return this.http
      .post<AuthResponse>(`${API_BASE}/auth/register`, payload, { context: this.formContext() })
      .pipe(tap((res) => this.setSession(res)), map((res) => res.user));
  }

  updateProfile(fullName: string, phone: string | null): Observable<UserDto> {
    return this.http
      .put<UserDto>(`${API_BASE}/auth/me`, { fullName, phone }, { context: new HttpContext().set(SILENT_ERRORS, true) })
      .pipe(tap((user) => this._user.set(user)));
  }

  logout(): void {
    const refreshToken = this.storage.getString(REFRESH_KEY);
    if (refreshToken && this.token) {
      // Best effort: revoke server-side, never block the UI on it.
      this.http
        .post(`${API_BASE}/auth/logout`, { refreshToken }, { context: new HttpContext().set(SILENT_ERRORS, true).set(SKIP_AUTH, false) })
        .subscribe({ error: () => undefined });
    }
    this.clearSession();
  }

  /** Single-flight refresh: concurrent 401s share one request (the refresh token is single-use). */
  refresh(): Observable<string> {
    if (this.inflightRefresh$) return this.inflightRefresh$;
    const refreshToken = this.storage.getString(REFRESH_KEY);
    if (!refreshToken) return throwError(() => new Error('No refresh token'));

    this.inflightRefresh$ = this.http
      .post<AuthResponse>(`${API_BASE}/auth/refresh`, { refreshToken }, { context: this.formContext() })
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
    this.storage.remove(REFRESH_KEY);
  }

  private setSession(res: AuthResponse): void {
    this.token = res.accessToken;
    this.storage.set(REFRESH_KEY, res.refreshToken);
    this._user.set(res.user);
  }

  private formContext(): HttpContext {
    return new HttpContext().set(SKIP_AUTH, true).set(SILENT_ERRORS, true);
  }
}
