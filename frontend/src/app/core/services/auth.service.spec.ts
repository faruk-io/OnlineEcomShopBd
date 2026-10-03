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

  it('login keeps the access token in memory and only persists the refresh token', () => {
    auth.login('rahim@example.com', 'Passw0rdX').subscribe();
    const req = http.expectOne('/api/v1/auth/login');
    expect(req.request.body).toEqual({ email: 'rahim@example.com', password: 'Passw0rdX' });
    expect(req.request.headers.has('Authorization')).toBe(false);
    req.flush(authResponse(1));

    expect(auth.isAuthenticated()).toBe(true);
    expect(auth.user()?.fullName).toBe('Rahim Uddin');
    expect(auth.accessToken()).toBe('access-1');
    expect(localStorage.getItem('tb.refresh.v1')).toBe('refresh-1');
    expect(JSON.stringify({ ...localStorage })).not.toContain('access-1');
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
    localStorage.setItem('tb.refresh.v1', 'refresh-0');
    const tokens: string[] = [];
    auth.refresh().subscribe((t) => tokens.push(t));
    auth.refresh().subscribe((t) => tokens.push(t));

    const req = http.expectOne('/api/v1/auth/refresh'); // exactly one
    expect(req.request.body).toEqual({ refreshToken: 'refresh-0' });
    req.flush(authResponse(2));

    expect(tokens).toEqual(['access-2', 'access-2']);
    expect(localStorage.getItem('tb.refresh.v1')).toBe('refresh-2'); // rotated
  });

  it('a failed refresh clears the whole session', () => {
    localStorage.setItem('tb.refresh.v1', 'stale');
    let failed = false;
    auth.refresh().subscribe({ error: () => (failed = true) });
    http.expectOne('/api/v1/auth/refresh').flush(problem(401, 'Authentication failed.').error, { status: 401, statusText: 'Unauthorized' });
    expect(failed).toBe(true);
    expect(auth.isAuthenticated()).toBe(false);
    expect(localStorage.getItem('tb.refresh.v1')).toBeNull();
  });

  it('refresh without a stored token errors immediately (no HTTP call)', () => {
    let failed = false;
    auth.refresh().subscribe({ error: () => (failed = true) });
    expect(failed).toBe(true);
  });

  it('whenReady restores a stored session once, even if awaited repeatedly', async () => {
    localStorage.setItem('tb.refresh.v1', 'refresh-0');
    const a = auth.whenReady();
    const b = auth.whenReady();
    http.expectOne('/api/v1/auth/refresh').flush(authResponse(3));
    await Promise.all([a, b]);
    expect(auth.user()?.email).toBe('rahim@example.com');
  });

  it('whenReady resolves (signed out) when the stored refresh token is rejected', async () => {
    localStorage.setItem('tb.refresh.v1', 'revoked');
    const ready = auth.whenReady();
    http.expectOne('/api/v1/auth/refresh').flush({}, { status: 401, statusText: 'Unauthorized' });
    await ready;
    expect(auth.isAuthenticated()).toBe(false);
  });

  it('whenReady is immediate with no stored session', async () => {
    await auth.whenReady();
    http.expectNone('/api/v1/auth/refresh');
  });

  it('logout revokes the refresh token server-side and clears local state', () => {
    auth.login('a@b.com', 'x').subscribe();
    http.expectOne('/api/v1/auth/login').flush(authResponse(1));

    auth.logout();
    const req = http.expectOne('/api/v1/auth/logout');
    expect(req.request.body).toEqual({ refreshToken: 'refresh-1' });
    expect(req.request.headers.get('Authorization')).toBe('Bearer access-1');
    req.flush(null, { status: 204, statusText: 'No Content' });

    expect(auth.isAuthenticated()).toBe(false);
    expect(auth.accessToken()).toBeNull();
    expect(localStorage.getItem('tb.refresh.v1')).toBeNull();
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
