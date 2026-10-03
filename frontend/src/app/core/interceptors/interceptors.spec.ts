import { HttpClient, HttpContext } from '@angular/common/http';
import { HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { BACKGROUND, SILENT_ERRORS, SKIP_AUTH } from '../config';
import { ApiError } from '../models/api.models';
import { AuthService } from '../services/auth.service';
import { LoadingService } from '../services/loading.service';
import { ToastService } from '../services/toast.service';
import { authResponse, provideTestHttp } from '../testing/test-helpers';
import { errorMessage, isApiError } from './error.interceptor';

describe('HTTP interceptors', () => {
  let client: HttpClient;
  let http: HttpTestingController;
  let auth: AuthService;
  let toast: ToastService;
  let loading: LoadingService;

  const signIn = () => {
    auth.login('a@b.com', 'x').subscribe();
    http.expectOne('/api/v1/auth/login').flush(authResponse(1));
  };

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({ providers: [provideTestHttp()] });
    client = TestBed.inject(HttpClient);
    http = TestBed.inject(HttpTestingController);
    auth = TestBed.inject(AuthService);
    toast = TestBed.inject(ToastService);
    loading = TestBed.inject(LoadingService);
  });
  afterEach(() => http.verify());

  describe('auth', () => {
    it('attaches the bearer token to API calls only', () => {
      signIn();
      client.get('/api/v1/cart').subscribe();
      expect(http.expectOne('/api/v1/cart').request.headers.get('Authorization')).toBe('Bearer access-1');
      client.get('https://cdn.example.com/x.js').subscribe();
      expect(http.expectOne('https://cdn.example.com/x.js').request.headers.has('Authorization')).toBe(false);
    });

    it('does not attach a token when signed out or when SKIP_AUTH is set', () => {
      client.get('/api/v1/products').subscribe();
      expect(http.expectOne('/api/v1/products').request.headers.has('Authorization')).toBe(false);
      signIn();
      client.get('/api/v1/products', { context: new HttpContext().set(SKIP_AUTH, true) }).subscribe();
      expect(http.expectOne('/api/v1/products').request.headers.has('Authorization')).toBe(false);
    });

    it('on 401 silently refreshes once and replays the original request with the new token', () => {
      signIn();
      let body: unknown;
      client.get('/api/v1/cart').subscribe((b) => (body = b));

      http.expectOne('/api/v1/cart').flush({}, { status: 401, statusText: 'Unauthorized' });
      const refresh = http.expectOne('/api/v1/auth/refresh');
      expect(refresh.request.body).toEqual({});   // cookie mode: the HttpOnly cookie carries the token
      refresh.flush(authResponse(2));

      const replay = http.expectOne('/api/v1/cart');
      expect(replay.request.headers.get('Authorization')).toBe('Bearer access-2');
      replay.flush({ items: [] });
      expect(body).toEqual({ items: [] });
    });

    it('parallel 401s share a single refresh request', () => {
      signIn();
      client.get('/api/v1/cart').subscribe();
      client.get('/api/v1/wishlist').subscribe();
      http.expectOne('/api/v1/cart').flush({}, { status: 401, statusText: 'Unauthorized' });
      http.expectOne('/api/v1/wishlist').flush({}, { status: 401, statusText: 'Unauthorized' });

      http.expectOne('/api/v1/auth/refresh').flush(authResponse(2)); // exactly one
      http.expectOne('/api/v1/cart').flush({});
      http.expectOne('/api/v1/wishlist').flush([]);
    });

    it('surfaces the original 401 and ends the session when the refresh fails', () => {
      signIn();
      let error: ApiError | undefined;
      client.get('/api/v1/cart').subscribe({ error: (e) => (error = e) });
      http.expectOne('/api/v1/cart').flush({ status: 401, title: 'Unauthorized' }, { status: 401, statusText: 'Unauthorized' });
      http.expectOne('/api/v1/auth/refresh').flush({}, { status: 401, statusText: 'Unauthorized' });

      expect(error?.status).toBe(401);
      expect(auth.isAuthenticated()).toBe(false);
    });

    it('never loops: a replayed request that 401s again is not refreshed twice', () => {
      signIn();
      let error: ApiError | undefined;
      client.get('/api/v1/cart').subscribe({ error: (e) => (error = e) });
      http.expectOne('/api/v1/cart').flush({}, { status: 401, statusText: 'Unauthorized' });
      http.expectOne('/api/v1/auth/refresh').flush(authResponse(2));
      http.expectOne('/api/v1/cart').flush({}, { status: 401, statusText: 'Unauthorized' });
      http.expectNone('/api/v1/auth/refresh');
      expect(error?.status).toBe(401);
    });

    it('an anonymous 401 is passed through without trying to refresh', () => {
      let error: ApiError | undefined;
      client.get('/api/v1/cart').subscribe({ error: (e) => (error = e) });
      http.expectOne('/api/v1/cart').flush({}, { status: 401, statusText: 'Unauthorized' });
      http.expectNone('/api/v1/auth/refresh');
      expect(error?.status).toBe(401);
    });
  });

  describe('mfa_required', () => {
    const mfaRequired = () => client.get('/api/v1/admin/orders').subscribe({ error: () => undefined });
    const flush = () => http.expectOne('/api/v1/admin/orders').flush(
      { status: 403, title: 'Forbidden', code: 'mfa_required' }, { status: 403, statusText: 'Forbidden' });

    it('sends the admin to enrolment without a generic error toast', () => {
      const nav = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
      mfaRequired();
      flush();
      expect(nav).toHaveBeenCalledTimes(1);
      expect(nav).toHaveBeenCalledWith('/account/security?reason=admin-mfa');
      expect(toast.toasts()).toEqual([]);
    });

    it('does not navigate again when already on the security page (no loop)', () => {
      const router = TestBed.inject(Router);
      Object.defineProperty(router, 'url', { get: () => '/account/security?reason=admin-mfa' });
      const nav = vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);
      mfaRequired();
      flush();
      expect(nav).not.toHaveBeenCalled();
    });

    it('still toasts an ordinary 403', () => {
      const nav = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
      client.get('/api/v1/admin/orders').subscribe({ error: () => undefined });
      http.expectOne('/api/v1/admin/orders').flush({ status: 403, title: 'Forbidden' }, { status: 403, statusText: 'Forbidden' });
      expect(nav).not.toHaveBeenCalled();
      expect(toast.toasts()[0].message).toContain('permission');
    });
  });

  describe('error normalisation', () => {
    it('turns ProblemDetails into an ApiError (validation map, trace id)', () => {
      let error: unknown;
      client.post('/api/v1/auth/register', {}).subscribe({ error: (e) => (error = e) });
      http.expectOne('/api/v1/auth/register').flush(
        { status: 400, title: 'One or more validation errors occurred.', errors: { email: ['Enter a valid email.'] }, traceId: 'abc' },
        { status: 400, statusText: 'Bad Request' },
      );
      expect(isApiError(error)).toBe(true);
      expect(error).toMatchObject({ status: 400, errors: { email: ['Enter a valid email.'] }, traceId: 'abc' });
      expect(errorMessage(error)).toBe('Enter a valid email.');
    });

    it('survives non-JSON error bodies', () => {
      let error: ApiError | undefined;
      client.get('/api/v1/products').subscribe({ error: (e) => (error = e) });
      http.expectOne('/api/v1/products').flush('<html>Bad gateway</html>', { status: 502, statusText: 'Bad Gateway' });
      expect(error).toMatchObject({ status: 502, title: 'Bad Gateway', errors: null });
    });

    it('toasts server and network failures but not validation errors', () => {
      client.get('/api/v1/a').subscribe({ error: () => undefined });
      http.expectOne('/api/v1/a').flush({}, { status: 500, statusText: 'Server Error' });
      client.get('/api/v1/b').subscribe({ error: () => undefined });
      http.expectOne('/api/v1/b').error(new ProgressEvent('error'), { status: 0 });
      client.get('/api/v1/c').subscribe({ error: () => undefined });
      http.expectOne('/api/v1/c').flush({}, { status: 400, statusText: 'Bad Request' });
      client.get('/api/v1/d').subscribe({ error: () => undefined });
      http.expectOne('/api/v1/d').flush({}, { status: 404, statusText: 'Not Found' });

      const messages = toast.toasts().map((t) => t.message);
      expect(messages).toHaveLength(2);
      expect(messages[0]).toMatch(/our side/);
      expect(messages[1]).toMatch(/Cannot reach the server/);
    });

    it('SILENT_ERRORS suppresses the toast', () => {
      client.get('/api/v1/a', { context: new HttpContext().set(SILENT_ERRORS, true) }).subscribe({ error: () => undefined });
      http.expectOne('/api/v1/a').flush({}, { status: 500, statusText: 'Server Error' });
      expect(toast.toasts()).toHaveLength(0);
    });

    it('errorMessage falls back for unknown values', () => {
      expect(errorMessage(new Error('x'))).toMatch(/Something went wrong/);
      expect(errorMessage({ status: 500, title: 'T', detail: 'D', errors: null, traceId: null })).toBe('D');
    });
  });

  describe('loading', () => {
    it('is true while a foreground API request is in flight and false after (success or failure)', () => {
      expect(loading.isLoading()).toBe(false);
      client.get('/api/v1/products').subscribe();
      client.get('/api/v1/brands').subscribe({ error: () => undefined });
      expect(loading.isLoading()).toBe(true);
      http.expectOne('/api/v1/products').flush({});
      expect(loading.isLoading()).toBe(true);
      http.expectOne('/api/v1/brands').flush({}, { status: 500, statusText: 'x' });
      expect(loading.isLoading()).toBe(false);
    });

    it('ignores background requests such as autocomplete', () => {
      client.get('/api/v1/search/autocomplete?q=ab', { context: new HttpContext().set(BACKGROUND, true) }).subscribe();
      expect(loading.isLoading()).toBe(false);
      http.expectOne('/api/v1/search/autocomplete?q=ab').flush({});
    });
  });
});
