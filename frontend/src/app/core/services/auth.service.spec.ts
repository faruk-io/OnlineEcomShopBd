import { TestBed } from '@angular/core/testing';
import { HttpTestingController } from '@angular/common/http/testing';
import { AuthService } from './auth.service';
import { SILENT_ERRORS, SKIP_AUTH } from '../config';
import { USER, authResponse, problem, provideTestHttp } from '../testing/test-helpers';

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

  describe('account recovery', () => {
    const isAnonymousSilent = (req: { context: { get: <T>(t: never) => T } }) =>
      req.context.get(SKIP_AUTH as never) === true && req.context.get(SILENT_ERRORS as never) === true;

    it('forgotPassword posts only the email, anonymously, with errors left to the page', () => {
      let msg = '';
      auth.forgotPassword('rahim@example.com').subscribe((r) => (msg = r.message));
      const req = http.expectOne('/api/v1/auth/forgot-password');
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual({ email: 'rahim@example.com' });
      expect(isAnonymousSilent(req.request)).toBe(true);
      req.flush({ message: 'ok' }, { status: 202, statusText: 'Accepted' });
      expect(msg).toBe('ok');
    });

    it('resetPassword posts token + new password anonymously and drops the local session on success', () => {
      auth.login('a@b.com', 'x').subscribe();
      http.expectOne('/api/v1/auth/login').flush(authResponse(1));
      expect(auth.isAuthenticated()).toBe(true);

      auth.resetPassword('tok_123-abc', 'NewPassw0rd').subscribe();
      const req = http.expectOne('/api/v1/auth/reset-password');
      expect(req.request.body).toEqual({ token: 'tok_123-abc', newPassword: 'NewPassw0rd' });
      expect(isAnonymousSilent(req.request)).toBe(true);
      req.flush(null, { status: 204, statusText: 'No Content' });

      expect(auth.isAuthenticated()).toBe(false);
      expect(auth.accessToken()).toBeNull();
      expect(localStorage.getItem('tb.session.v1')).toBeNull();
    });

    it('a rejected reset keeps the local session and surfaces the normalised error (the token is not spent for weak passwords)', () => {
      auth.login('a@b.com', 'x').subscribe();
      http.expectOne('/api/v1/auth/login').flush(authResponse(1));
      let error: unknown;
      auth.resetPassword('tok', 'weak').subscribe({ error: (e) => (error = e) });
      http.expectOne('/api/v1/auth/reset-password').flush(
        { status: 400, title: 'Validation', errors: { newPassword: ['Too weak.'] } }, { status: 400, statusText: 'Bad Request' },
      );
      expect(error).toMatchObject({ status: 400, errors: { newPassword: ['Too weak.'] } });
      expect(auth.isAuthenticated()).toBe(true);
    });

    it('verifyEmail posts the token anonymously', () => {
      auth.verifyEmail('tok').subscribe();
      const req = http.expectOne('/api/v1/auth/verify-email');
      expect(req.request.body).toEqual({ token: 'tok' });
      expect(isAnonymousSilent(req.request)).toBe(true);
      req.flush(null, { status: 204, statusText: 'No Content' });
    });

    it('resendVerification is authenticated (access token attached) and silent', () => {
      auth.login('a@b.com', 'x').subscribe();
      http.expectOne('/api/v1/auth/login').flush(authResponse(1));
      auth.resendVerification().subscribe();
      const req = http.expectOne('/api/v1/auth/resend-verification');
      expect(req.request.method).toBe('POST');
      expect(req.request.context.get(SKIP_AUTH)).toBe(false);
      expect(req.request.context.get(SILENT_ERRORS)).toBe(true);
      expect(req.request.headers.get('Authorization')).toBe('Bearer access-1');
      req.flush({ message: 'sent' }, { status: 202, statusText: 'Accepted' });
    });

    it('reloadUser re-reads /auth/me and updates the user signal', () => {
      auth.login('a@b.com', 'x').subscribe();
      http.expectOne('/api/v1/auth/login').flush(authResponse(1, { ...authResponse(1).user, emailConfirmed: false }));
      expect(auth.user()?.emailConfirmed).toBe(false);
      auth.reloadUser().subscribe();
      const req = http.expectOne('/api/v1/auth/me');
      expect(req.request.method).toBe('GET');
      req.flush({ ...authResponse(1).user, emailConfirmed: true });
      expect(auth.user()?.emailConfirmed).toBe(true);
    });
  });

  describe('two-step verification', () => {
    const challenge = (expiresAt = '2099-01-01T00:00:00Z') => ({ mfaRequired: true, mfaToken: 'chal-123', expiresAt });
    const unauthorized = (detail: string) => ({ status: 401, title: 'Authentication failed.', detail });
    const startChallenge = () => {
      auth.login('rahim@example.com', 'Passw0rdX').subscribe();
      http.expectOne('/api/v1/auth/login').flush(challenge(), { status: 202, statusText: 'Accepted' });
    };
    const storageDump = () => JSON.stringify({ ...localStorage, ...sessionStorage });

    it('a 200 login still yields a session', () => {
      let kind = '';
      auth.login('a@b.com', 'x').subscribe((r) => (kind = r.kind));
      http.expectOne('/api/v1/auth/login').flush(authResponse(1));
      expect(kind).toBe('session');
      expect(auth.isAuthenticated()).toBe(true);
      expect(auth.mfaPending()).toBe(false);
    });

    it('a 202 keeps only the in-memory challenge: no session, no token, nothing in storage', () => {
      let result: unknown;
      auth.login('rahim@example.com', 'Passw0rdX').subscribe((r) => (result = r));
      http.expectOne('/api/v1/auth/login').flush(challenge(), { status: 202, statusText: 'Accepted' });
      expect(result).toEqual({ kind: 'mfa', expiresAt: '2099-01-01T00:00:00Z' });
      expect(auth.mfaPending()).toBe(true);
      expect(auth.isAuthenticated()).toBe(false);
      expect(auth.accessToken()).toBeNull();
      expect(storageDump()).toBe('{}');
      expect(storageDump()).not.toContain('chal-123');
    });

    it('verify sends the challenge + code with the cookie header and applies the session', () => {
      startChallenge();
      auth.verifyMfa({ code: '123456' }).subscribe();
      const req = http.expectOne('/api/v1/auth/mfa/verify');
      expect(req.request.body).toEqual({ mfaToken: 'chal-123', code: '123456' });
      expect(req.request.headers.get('X-Refresh-Mode')).toBe('cookie');
      expect(req.request.headers.has('Authorization')).toBe(false);
      req.flush(authResponse(2));
      expect(auth.isAuthenticated()).toBe(true);
      expect(auth.accessToken()).toBe('access-2');
      expect(auth.mfaPending()).toBe(false);
      expect(storageDump()).toBe('{"tb.session.v1":"1"}');
    });

    it('verify can use a recovery code instead', () => {
      startChallenge();
      auth.verifyMfa({ recoveryCode: 'ABCDE-FGHJK' }).subscribe();
      expect(http.expectOne('/api/v1/auth/mfa/verify').request.body).toEqual({ mfaToken: 'chal-123', recoveryCode: 'ABCDE-FGHJK' });
    });

    it('a wrong code keeps the challenge so the user can retry', () => {
      startChallenge();
      let error: unknown;
      auth.verifyMfa({ code: '000000' }).subscribe({ error: (e) => (error = e) });
      http.expectOne('/api/v1/auth/mfa/verify').flush(unauthorized('That code is not valid.'), { status: 401, statusText: 'Unauthorized' });
      expect(error).toMatchObject({ status: 401 });
      expect(auth.mfaPending()).toBe(true);
      expect(auth.isAuthenticated()).toBe(false);
    });

    it.each(['This sign-in attempt is invalid or has expired. Please sign in again.', 'Account temporarily locked. Try again later.'])(
      'an expired / locked challenge is dropped (%s)', (detail) => {
        startChallenge();
        auth.verifyMfa({ code: '000000' }).subscribe({ error: () => undefined });
        http.expectOne('/api/v1/auth/mfa/verify').flush(unauthorized(detail), { status: 401, statusText: 'Unauthorized' });
        expect(auth.mfaPending()).toBe(false);
      });

    it('a challenge past its expiry is dropped locally without calling the API', () => {
      auth.login('a@b.com', 'x').subscribe();
      http.expectOne('/api/v1/auth/login').flush(challenge('2000-01-01T00:00:00Z'), { status: 202, statusText: 'Accepted' });
      let error: unknown;
      auth.verifyMfa({ code: '123456' }).subscribe({ error: (e) => (error = e) });
      expect(error).toMatchObject({ status: 401 });
      expect(auth.mfaPending()).toBe(false);
    });

    it('cancelMfa forgets the challenge', () => {
      startChallenge();
      auth.cancelMfa();
      expect(auth.mfaPending()).toBe(false);
    });

    it('enable applies the fresh MFA session from the response and returns the recovery codes', () => {
      localStorage.setItem('tb.session.v1', '1');
      let codes: string[] = [];
      auth.mfaEnable('123456').subscribe((c) => (codes = c));
      const req = http.expectOne('/api/v1/auth/mfa/enable');
      expect(req.request.body).toEqual({ code: '123456' });
      expect(req.request.headers.get('X-Refresh-Mode')).toBe('cookie');
      req.flush({ recoveryCodes: ['ABCDE-FGHJK'], auth: authResponse(5, { ...USER, mfaEnabled: true, mfaSession: true }) });
      expect(codes).toEqual(['ABCDE-FGHJK']);
      expect(auth.accessToken()).toBe('access-5');
      expect(auth.user()?.mfaSession).toBe(true);
      expect(storageDump()).not.toContain('ABCDE');
    });

    it('disable signs out locally without another server call', () => {
      auth.login('a@b.com', 'x').subscribe();
      http.expectOne('/api/v1/auth/login').flush(authResponse(1));
      auth.mfaDisable('Passw0rdX', { recoveryCode: 'ABCDE-FGHJK' }).subscribe();
      const req = http.expectOne('/api/v1/auth/mfa/disable');
      expect(req.request.body).toEqual({ password: 'Passw0rdX', recoveryCode: 'ABCDE-FGHJK' });
      req.flush(null, { status: 204, statusText: 'No Content' });
      expect(auth.isAuthenticated()).toBe(false);
      expect(auth.accessToken()).toBeNull();
      expect(localStorage.getItem('tb.session.v1')).toBeNull();
    });

    it('status / setup / regenerate use the documented endpoints', () => {
      auth.mfaStatus().subscribe();
      http.expectOne({ method: 'GET', url: '/api/v1/auth/mfa' }).flush({ enabled: false, required: false, setupPending: false, recoveryCodesRemaining: 0, enabledAt: null });
      auth.mfaSetup().subscribe();
      http.expectOne({ method: 'POST', url: '/api/v1/auth/mfa/setup' }).flush({ secret: 'ABCD', otpAuthUri: 'otpauth://x', issuer: 'i', account: 'a' });
      let codes: string[] = [];
      auth.mfaRegenerateRecoveryCodes('123456').subscribe((c) => (codes = c));
      const req = http.expectOne('/api/v1/auth/mfa/recovery-codes');
      expect(req.request.body).toEqual({ code: '123456' });
      req.flush({ recoveryCodes: ['AAAAA-BBBBB'] });
      expect(codes).toEqual(['AAAAA-BBBBB']);
    });
  });
});
