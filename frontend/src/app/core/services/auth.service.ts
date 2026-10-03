import { HttpClient, HttpContext, HttpHeaders, HttpResponse } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { Observable, finalize, map, shareReplay, tap, throwError } from 'rxjs';
import { API_BASE, SILENT_ERRORS, SKIP_AUTH } from '../config';
import { ApiError, AuthResponse, MessageDto, MfaChallenge, MfaEnabled, MfaSetup, MfaStatus, RecoveryCodes, UserDto } from '../models/api.models';
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

/** Outcome of the password step: a finished session, or a second factor is still needed (see {@link AuthService.mfaPending}). */
export type LoginResult = { kind: 'session'; user: UserDto } | { kind: 'mfa'; expiresAt: string };

export type MfaProof = { code: string; recoveryCode?: never } | { recoveryCode: string; code?: never };

/** The pending challenge. Held in memory ONLY (never storage, never the URL). */
interface PendingMfa {
  token: string;
  expiresAt: string;
}

/** Problems that end the challenge (the user must sign in again) as opposed to a plain wrong code. */
const CHALLENGE_OVER = /expired|sign in again|locked/i;

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

  private readonly challenge = signal<PendingMfa | null>(null);
  /** True while the password was accepted but the second factor is outstanding. */
  readonly mfaPending = computed(() => this.challenge() !== null);
  readonly mfaExpiresAt = computed(() => this.challenge()?.expiresAt ?? null);

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

  /** 200 -> session stored; 202 -> only the challenge is kept (no tokens exist yet). */
  login(email: string, password: string): Observable<LoginResult> {
    this.challenge.set(null);
    return this.http
      .post<AuthResponse | MfaChallenge>(`${API_BASE}/auth/login`, { email, password }, { context: this.formContext(), headers: COOKIE_MODE, observe: 'response' })
      .pipe(map((res) => this.onLogin(res)));
  }

  /** Second step of a 202 login. A wrong code keeps the challenge; an expired / locked one ends it. */
  verifyMfa(proof: MfaProof): Observable<UserDto> {
    const pending = this.challenge();
    if (!pending) return throwError(() => this.localError(401, 'This sign-in attempt has expired. Please sign in again.'));
    if (Date.parse(pending.expiresAt) <= Date.now()) {
      this.challenge.set(null);
      return throwError(() => this.localError(401, 'This sign-in attempt has expired. Please sign in again.'));
    }
    const body = proof.recoveryCode !== undefined ? { mfaToken: pending.token, recoveryCode: proof.recoveryCode } : { mfaToken: pending.token, code: proof.code };
    return this.http.post<AuthResponse>(`${API_BASE}/auth/mfa/verify`, body, { context: this.formContext(), headers: COOKIE_MODE }).pipe(
      tap({
        next: (res) => {
          this.challenge.set(null);
          this.setSession(res);
        },
        error: (e: unknown) => {
          const detail = (e as Partial<ApiError>).detail ?? '';
          if ((e as Partial<ApiError>).status === 401 && CHALLENGE_OVER.test(detail)) this.challenge.set(null);
        },
      }),
      map((res) => res.user),
    );
  }

  /** "Back" on the second step: forget the challenge. */
  cancelMfa(): void {
    this.challenge.set(null);
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

  // ------------------------------------------------------------------ two-step verification
  mfaStatus(): Observable<MfaStatus> {
    return this.http.get<MfaStatus>(`${API_BASE}/auth/mfa`, { context: this.silent() });
  }

  mfaSetup(): Observable<MfaSetup> {
    return this.http.post<MfaSetup>(`${API_BASE}/auth/mfa/setup`, {}, { context: this.silent() });
  }

  /** The server ends every other session and answers with a fresh MFA-verified one, applied here like a login. */
  mfaEnable(code: string): Observable<string[]> {
    return this.http.post<MfaEnabled>(`${API_BASE}/auth/mfa/enable`, { code }, { context: this.silent(), headers: COOKIE_MODE }).pipe(
      tap((res) => this.setSession(res.auth)),
      map((res) => res.recoveryCodes),
    );
  }

  /** Every session is revoked server-side, so the local one is dropped as well (no logout call needed). */
  mfaDisable(password: string, proof: MfaProof): Observable<void> {
    return this.http.post<void>(`${API_BASE}/auth/mfa/disable`, { password, ...proof }, { context: this.silent() }).pipe(tap(() => this.clearSession()));
  }

  mfaRegenerateRecoveryCodes(code: string): Observable<string[]> {
    return this.http.post<RecoveryCodes>(`${API_BASE}/auth/mfa/recovery-codes`, { code }, { context: this.silent() }).pipe(map((r) => r.recoveryCodes));
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
    this.challenge.set(null);
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

  private onLogin(res: HttpResponse<AuthResponse | MfaChallenge>): LoginResult {
    const body = res.body;
    if (res.status === 202 && body && 'mfaRequired' in body) {
      this.challenge.set({ token: body.mfaToken, expiresAt: body.expiresAt });
      return { kind: 'mfa', expiresAt: body.expiresAt };
    }
    this.setSession(body as AuthResponse);
    return { kind: 'session', user: (body as AuthResponse).user };
  }

  private localError(status: number, detail: string): ApiError {
    return { status, title: 'Authentication failed.', detail, errors: null, traceId: null };
  }

  private silent(): HttpContext {
    return new HttpContext().set(SILENT_ERRORS, true);
  }

  private formContext(): HttpContext {
    return new HttpContext().set(SKIP_AUTH, true).set(SILENT_ERRORS, true);
  }
}
