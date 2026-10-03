import { TestBed } from '@angular/core/testing';
import { HttpTestingController } from '@angular/common/http/testing';
import { AuthService } from './auth.service';
import { authResponse, problem, provideTestHttp } from '../testing/test-helpers';

describe('AuthService', () => {
  let auth: AuthService;
  let http: HttpTestingController;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({ providers: [provideTestHttp()] });
    auth = TestBed.inject(AuthService);
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());

  it('login keeps the access token in memory and stores NO token at all (the refresh token is an HttpOnly cookie)', () => {
    auth.login('rahim@example.com', 'Passw0rdX').subscribe();
    const req = http.expectOne('/api/v1/auth/login');
    expect(req.request.body).toEqual({ email: 'rahim@example.com', password: 'Passw0rdX' });
    expect(req.request.headers.has('Authorization')).toBe(false);
    expect(req.request.headers.get('X-Refresh-Mode')).toBe('cookie');
    req.flush(authResponse(1));

    expect(auth.isAuthenticated()).toBe(true);
    expect(auth.user()?.fullName).toBe('Rahim Uddin');
    expect(auth.accessToken()).toBe('access-1');
    const stored = JSON.stringify({ ...localStorage });
    expect(stored).toBe('{"tb.session.v1":"1"}');                  // only a non-secret "session exists" hint
    expect(stored).not.toMatch(/access|refresh-/);
  });

  it('a failed login leaves the user signed out and surfaces a normalised error', () => {
    let error: unknown;
    auth.login('a@b.com', 'x').subscribe({ error: (e) => (error = e) });
    http.expectOne('/api/v1/auth/login').flush({ status: 401, title: 'Authentication failed.', detail: 'Invalid email or password.' }, { status: 401, statusText: 'Unauthorized' });
    expect(auth.isAuthenticated()).toBe(false);
    expect(error).toMatchObject({ status: 401, detail: 'Invalid email or password.' });
  });

  it('register stores the session like login', () => {
    auth.register({ fullName: 'Rahim Uddin', email: 'rahim@example.com', password: 'Passw0rdX' }).subscribe();
    http.expectOne('/api/v1/auth/register').flush(authResponse(1));
    expect(auth.isAuthenticated()).toBe(true);
  });

  it('refresh is single-flight: concurrent callers share one request', () => {
    localStorage.setItem('tb.session.v1', '1');
    const tokens: string[] = [];
    auth.refresh().subscribe((t) => tokens.push(t));
    auth.refresh().subscribe((t) => tokens.push(t));

    const req = http.expectOne('/api/v1/auth/refresh'); // exactly one
    expect(req.request.body).toEqual({});                       // the cookie carries the token, not the body
    expect(req.request.headers.get('X-Refresh-Mode')).toBe('cookie');
    req.flush(authResponse(2));

    expect(tokens).toEqual(['access-2', 'access-2']);
  });

  it('migrates a legacy localStorage refresh token once: sends it, then deletes it', () => {
    localStorage.setItem('tb.refresh.v1', 'legacy-token');
    auth.refresh().subscribe();
    const req = http.expectOne('/api/v1/auth/refresh');
    expect(req.request.body).toEqual({ refreshToken: 'legacy-token' });
    req.flush(authResponse(2));
    expect(localStorage.getItem('tb.refresh.v1')).toBeNull();
    expect(localStorage.getItem('tb.session.v1')).toBe('1');
  });

  it('a failed refresh clears the whole session', () => {
    localStorage.setItem('tb.session.v1', '1');
    let failed = false;
    auth.refresh().subscribe({ error: () => (failed = true) });
    http.expectOne('/api/v1/auth/refresh').flush(problem(401, 'Authentication failed.').error, { status: 401, statusText: 'Unauthorized' });
    expect(failed).toBe(true);
    expect(auth.isAuthenticated()).toBe(false);
    expect(localStorage.getItem('tb.session.v1')).toBeNull();
  });

  it('refresh without a session errors immediately (no HTTP call)', () => {
    let failed = false;
    auth.refresh().subscribe({ error: () => (failed = true) });
    expect(failed).toBe(true);
  });

  it('whenReady restores a stored session once, even if awaited repeatedly', async () => {
    localStorage.setItem('tb.session.v1', '1');
    const a = auth.whenReady();
    const b = auth.whenReady();
    http.expectOne('/api/v1/auth/refresh').flush(authResponse(3));
    await Promise.all([a, b]);
    expect(auth.user()?.email).toBe('rahim@example.com');
  });

  it('whenReady resolves (signed out) when the stored refresh token is rejected', async () => {
    localStorage.setItem('tb.session.v1', '1');
    const ready = auth.whenReady();
    http.expectOne('/api/v1/auth/refresh').flush({}, { status: 401, statusText: 'Unauthorized' });
    await ready;
    expect(auth.isAuthenticated()).toBe(false);
  });

  it('whenReady is immediate with no stored session', async () => {
    await auth.whenReady();
    http.expectNone('/api/v1/auth/refresh');
  });

  it('logout revokes the session server-side and clears local state', () => {
    auth.login('a@b.com', 'x').subscribe();
    http.expectOne('/api/v1/auth/login').flush(authResponse(1));

    auth.logout();
    const req = http.expectOne('/api/v1/auth/logout');
    expect(req.request.body).toEqual({});
    expect(req.request.headers.get('X-Refresh-Mode')).toBe('cookie');
    req.flush(null, { status: 204, statusText: 'No Content' });

    expect(auth.isAuthenticated()).toBe(false);
    expect(auth.accessToken()).toBeNull();
    expect(localStorage.getItem('tb.session.v1')).toBeNull();
  });

  it('logout still revokes server-side when the access token has already expired (no access token in memory)', () => {
    localStorage.setItem('tb.session.v1', '1');          // e.g. a reloaded tab whose silent refresh has not happened
    auth.logout();
    const req = http.expectOne('/api/v1/auth/logout');
    expect(req.request.headers.has('Authorization')).toBe(false);
    req.flush(null, { status: 204, statusText: 'No Content' });
    expect(localStorage.getItem('tb.session.v1')).toBeNull();
  });

  it('logout when never signed in makes no request', () => {
    auth.logout();
    http.expectNone('/api/v1/auth/logout');
  });

  it('logoutAll revokes every device and ends the local session', () => {
    auth.login('a@b.com', 'x').subscribe();
    http.expectOne('/api/v1/auth/login').flush(authResponse(1));
    let done = false;
    auth.logoutAll().subscribe(() => (done = true));
    const req = http.expectOne('/api/v1/auth/logout-all');
    expect(req.request.headers.get('Authorization')).toBe('Bearer access-1');
    req.flush(null, { status: 204, statusText: 'No Content' });
    expect(done).toBe(true);
    expect(auth.isAuthenticated()).toBe(false);
  });

  it('updateProfile replaces the user signal', () => {
    auth.login('a@b.com', 'x').subscribe();
    http.expectOne('/api/v1/auth/login').flush(authResponse(1));
    auth.updateProfile('Karim Hossain', '01812345678').subscribe();
    const req = http.expectOne('/api/v1/auth/me');
    expect(req.request.method).toBe('PUT');
    req.flush({ ...authResponse(1).user, fullName: 'Karim Hossain', phone: '01812345678' });
    expect(auth.user()?.fullName).toBe('Karim Hossain');
  });
});
