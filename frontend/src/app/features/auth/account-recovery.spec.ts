import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { HttpTestingController } from '@angular/common/http/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { AuthService } from '../../core/services/auth.service';
import { USER, authResponse, provideTestHttp } from '../../core/testing/test-helpers';
import { FORGOT_NEUTRAL_MESSAGE, ForgotPasswordPage } from './forgot-password.page';
import { LoginPage } from './login.page';
import { ResetPasswordPage } from './reset-password.page';
import { VerifyEmailPage } from './verify-email.page';

@Component({ template: '' })
class Blank {}

const TOKEN = 'Abcdefghijklmnopqrstuvwxyz0123456789_-ABCDE';
const NEW_PASSWORD = 'NewPassw0rd1';

describe('Account recovery pages', () => {
  let http: HttpTestingController;
  let router: Router;

  const setup = () => {
    localStorage.clear();
    TestBed.configureTestingModule({
      providers: [
        provideRouter([
          { path: 'login', component: LoginPage },
          { path: 'forgot-password', component: ForgotPasswordPage },
          { path: 'reset-password', component: ResetPasswordPage },
          { path: 'verify-email', component: VerifyEmailPage },
          { path: 'shop', component: Blank },
          { path: '', component: Blank },
        ]),
        provideTestHttp(),
      ],
    });
    http = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
  };
  afterEach(() => http.verify());

  const el = (h: RouterTestingHarness) => h.routeNativeElement as HTMLElement;
  const text = (h: RouterTestingHarness) => el(h).textContent!.replace(/\s+/g, ' ').trim();
  const fill = (root: HTMLElement, id: string, value: string) => {
    const input = root.querySelector<HTMLInputElement>(`#${id}`)!;
    input.value = value;
    input.dispatchEvent(new Event('input'));
    input.dispatchEvent(new Event('blur'));
  };
  const submit = async (h: RouterTestingHarness) => {
    el(h).querySelector('form')!.dispatchEvent(new Event('submit', { cancelable: true }));
    h.detectChanges();
    await h.fixture.whenStable();
  };
  const bad = (status: number, body: Record<string, unknown>) => [body, { status, statusText: 'x' }] as const;

  describe('ForgotPasswordPage', () => {
    const request = async (h: RouterTestingHarness, email: string, status = 202) => {
      fill(el(h), 'email', email);
      await submit(h);
      const req = http.expectOne('/api/v1/auth/forgot-password');
      expect(req.request.body).toEqual({ email });
      if (status === 202) req.flush({ message: 'server wording that must never be shown verbatim' }, { status: 202, statusText: 'Accepted' });
      else req.flush(...bad(status, { status, title: 'Too Many Requests' }));
      h.detectChanges();
      await h.fixture.whenStable();
    };

    it('validates the email before calling the API', async () => {
      setup();
      const h = await RouterTestingHarness.create('/forgot-password');
      await submit(h);
      expect(text(h)).toContain('Email is required.');
      http.expectNone('/api/v1/auth/forgot-password');
    });

    it('shows the identical neutral confirmation for any address, and moves focus to it', async () => {
      setup();
      const h = await RouterTestingHarness.create('/forgot-password');
      await request(h, 'known@example.com');
      const first = el(h).querySelector('[role=status]')!.textContent!.trim();
      expect(first).toBe(FORGOT_NEUTRAL_MESSAGE);
      expect(document.activeElement).toBe(el(h).querySelector('h1'));

      (el(h).querySelector('.linklike') as HTMLButtonElement).click();   // "try again"
      h.detectChanges();
      await request(h, 'nobody@example.com');
      expect(el(h).querySelector('[role=status]')!.textContent!.trim()).toBe(first);
      expect(text(h)).not.toContain('known@example.com');
    });

    it('answers a 429 with a friendly message and keeps the form usable', async () => {
      setup();
      const h = await RouterTestingHarness.create('/forgot-password');
      await request(h, 'a@example.com', 429);
      expect(el(h).querySelector('[role=alert]')!.textContent).toContain('Too many requests');
      expect(el(h).querySelector<HTMLButtonElement>('button[type=submit]')!.disabled).toBe(false);
      expect(el(h).querySelector('[role=status]')).toBeNull();
    });

    it('links back to sign in', async () => {
      setup();
      const h = await RouterTestingHarness.create('/forgot-password');
      expect(el(h).querySelector('a[href="/login"]')).not.toBeNull();
    });
  });

  describe('ResetPasswordPage', () => {
    const open = async (fragment = `token=${TOKEN}`) => {
      const h = await RouterTestingHarness.create(`/reset-password#${fragment}`);
      await h.fixture.whenStable();
      h.detectChanges();
      return h;
    };
    const fillValid = (h: RouterTestingHarness, pw = NEW_PASSWORD, confirm = pw) => {
      fill(el(h), 'newPassword', pw);
      fill(el(h), 'confirm', confirm);
    };

    it('removes the token from the address bar right after reading it', async () => {
      setup();
      await open();
      expect(router.url).toBe('/reset-password');
      expect(router.url).not.toContain(TOKEN);
    });

    it('sets the new password using the token read from the fragment, then goes to login with a notice (no auto-login)', async () => {
      setup();
      const h = await open();
      expect(el(h).querySelector('#newPassword')!.getAttribute('autocomplete')).toBe('new-password');
      fillValid(h);
      await submit(h);
      const req = http.expectOne('/api/v1/auth/reset-password');
      expect(req.request.body).toEqual({ token: TOKEN, newPassword: NEW_PASSWORD });
      req.flush(null, { status: 204, statusText: 'No Content' });
      await h.fixture.whenStable();
      expect(router.url).toBe('/login?reset=1');
      expect(TestBed.inject(AuthService).isAuthenticated()).toBe(false);
      http.expectNone('/api/v1/auth/login');
    });

    it('validates strength and confirmation client-side without a request', async () => {
      setup();
      const h = await open();
      fillValid(h, 'weakpass', 'different');
      await submit(h);
      expect(text(h)).toContain('Add an uppercase letter.');
      expect(text(h)).toContain('Passwords do not match.');
      http.expectNone('/api/v1/auth/reset-password');
    });

    it('a server-side weak-password error lands on the field and the same token can be retried', async () => {
      setup();
      const h = await open();
      fillValid(h, 'Passw0rdPassw0rd');
      await submit(h);
      http.expectOne('/api/v1/auth/reset-password').flush(
        ...bad(400, { status: 400, title: 'One or more validation errors occurred.', errors: { newPassword: ['Password is too common.'] } }),
      );
      h.detectChanges();
      expect(el(h).querySelector('#np-err')!.textContent).toContain('Password is too common.');
      expect(el(h).querySelector('#newPassword')!.getAttribute('aria-invalid')).toBe('true');
      expect(el(h).querySelector('form')).not.toBeNull();
      expect(el(h).querySelector<HTMLButtonElement>('button[type=submit]')!.disabled).toBe(false);

      fillValid(h);
      await submit(h);
      const retry = http.expectOne('/api/v1/auth/reset-password');
      expect(retry.request.body.token).toBe(TOKEN);   // the token was kept
      retry.flush(null, { status: 204, statusText: 'No Content' });
      await h.fixture.whenStable();
      expect(router.url).toBe('/login?reset=1');
    });

    it('shows the invalid state, with a way to request a new link, when the server rejects the token', async () => {
      setup();
      const h = await open();
      fillValid(h);
      await submit(h);
      http.expectOne('/api/v1/auth/reset-password').flush(...bad(400, { status: 400, title: 'Invalid or expired link.' }));
      h.detectChanges();
      await h.fixture.whenStable();
      expect(el(h).querySelector('h1')!.textContent).toContain('invalid or has expired');
      expect(el(h).querySelector('form')).toBeNull();
      expect(el(h).querySelector('a[href="/forgot-password"]')).not.toBeNull();
      expect(document.activeElement).toBe(el(h).querySelector('h1'));
    });

    it('shows the invalid state straight away (no request) without a usable token', async () => {
      for (const fragment of ['', 'token=', 'token=bad%20token', 'foo=bar']) {
        TestBed.resetTestingModule();
        setup();
        const h = await open(fragment);
        expect(el(h).querySelector('h1')!.textContent).toContain('invalid or has expired');
        expect(el(h).querySelector('a[href="/forgot-password"]')).not.toBeNull();
        http.verify();
      }
    });

    it('a newer link opened in the same tab (a hash-only change) replaces the token and clears the invalid state', async () => {
      setup();
      const h = await open('token=');
      expect(el(h).querySelector('h1')!.textContent).toContain('invalid or has expired');
      await router.navigateByUrl(`/reset-password#token=${TOKEN}`);
      await h.fixture.whenStable();
      h.detectChanges();
      expect(router.url).toBe('/reset-password');
      expect(el(h).querySelector('h1')!.textContent).toContain('Choose a new password');
      fillValid(h);
      await submit(h);
      const req = http.expectOne('/api/v1/auth/reset-password');
      expect(req.request.body.token).toBe(TOKEN);
      req.flush(null, { status: 204, statusText: 'No Content' });
      await h.fixture.whenStable();
    });

    it('keeps the form on a 429 and says so', async () => {
      setup();
      const h = await open();
      fillValid(h);
      await submit(h);
      http.expectOne('/api/v1/auth/reset-password').flush(...bad(429, { status: 429, title: 'Too Many Requests' }));
      h.detectChanges();
      expect(el(h).querySelector('[role=alert]')!.textContent).toContain('Too many attempts');
      expect(el(h).querySelector('form')).not.toBeNull();
    });
  });

  describe('VerifyEmailPage', () => {
    const open = async (fragment = `token=${TOKEN}`) => {
      const h = await RouterTestingHarness.create(`/verify-email#${fragment}`);
      await h.fixture.whenStable();
      return h;
    };
    const signIn = (confirmed: boolean) => {
      TestBed.inject(AuthService).login('rahim@example.com', 'x').subscribe();
      http.expectOne('/api/v1/auth/login').flush(authResponse(1, { ...USER, emailConfirmed: confirmed }));
    };

    it('posts the fragment token exactly once, strips it from the address bar and shows "verifying" until the answer', async () => {
      setup();
      const h = await open();
      const req = http.expectOne('/api/v1/auth/verify-email');
      expect(req.request.body).toEqual({ token: TOKEN });
      expect(router.url).toBe('/verify-email');
      h.detectChanges();
      expect(el(h).querySelector('h1')!.textContent).toContain('Verifying');
      req.flush(null, { status: 204, statusText: 'No Content' });
      h.detectChanges();
      await h.fixture.whenStable();
      http.expectNone('/api/v1/auth/verify-email');   // not re-posted by the later navigation / change detection
    });

    it('a second link opened in the same tab (a hash-only change) is verified too, exactly once', async () => {
      setup();
      const h = await open();
      http.expectOne('/api/v1/auth/verify-email').flush(...bad(400, { status: 400, title: 'Invalid or expired link.' }));
      h.detectChanges();
      await h.fixture.whenStable();
      await router.navigateByUrl('/verify-email#token=SecondToken_2');
      await h.fixture.whenStable();
      const req = http.expectOne('/api/v1/auth/verify-email');
      expect(req.request.body).toEqual({ token: 'SecondToken_2' });
      req.flush(null, { status: 204, statusText: 'No Content' });
      h.detectChanges();
      await h.fixture.whenStable();
      expect(router.url).toBe('/verify-email');
      expect(el(h).querySelector('h1')!.textContent).toContain('Email verified');
      http.expectNone('/api/v1/auth/verify-email');
    });

    it('verified (signed out): success state with continue shopping and sign in links, no profile call', async () => {
      setup();
      const h = await open();
      http.expectOne('/api/v1/auth/verify-email').flush(null, { status: 204, statusText: 'No Content' });
      h.detectChanges();
      await h.fixture.whenStable();
      expect(el(h).querySelector('h1')!.textContent).toContain('Email verified');
      expect(el(h).querySelector('[role=status]')).not.toBeNull();
      expect(el(h).querySelector('a[href="/shop"]')).not.toBeNull();
      expect(el(h).querySelector('a[href="/login"]')).not.toBeNull();
      expect(document.activeElement).toBe(el(h).querySelector('h1'));
      http.expectNone('/api/v1/auth/me');
    });

    it('verified while signed in: reloads the user so the unverified banner goes away', async () => {
      setup();
      signIn(false);
      const h = await open();
      http.expectOne('/api/v1/auth/verify-email').flush(null, { status: 204, statusText: 'No Content' });
      await h.fixture.whenStable();
      http.expectOne('/api/v1/auth/me').flush({ ...USER, emailConfirmed: true });
      h.detectChanges();
      expect(TestBed.inject(AuthService).user()?.emailConfirmed).toBe(true);
      expect(el(h).querySelector('a[href="/login"]')).toBeNull();
    });

    it('invalid (server says 400): explains, and a signed-in unverified user can ask for a new link', async () => {
      setup();
      signIn(false);
      const h = await open();
      http.expectOne('/api/v1/auth/verify-email').flush(...bad(400, { status: 400, title: 'Invalid or expired link.' }));
      h.detectChanges();
      await h.fixture.whenStable();
      h.detectChanges();
      expect(el(h).querySelector('h1')!.textContent).toContain('invalid or has expired');
      const btn = el(h).querySelector<HTMLButtonElement>('app-verify-email-banner button')!;
      expect(btn.textContent).toContain('Send me a new link');
      btn.click();
      http.expectOne('/api/v1/auth/resend-verification').flush({ message: 'sent' }, { status: 202, statusText: 'Accepted' });
      http.expectNone('/api/v1/auth/me');
    });

    it('invalid and signed out: no resend button, sign-in link instead', async () => {
      setup();
      const h = await open();
      http.expectOne('/api/v1/auth/verify-email').flush(...bad(400, { status: 400, title: 'Invalid or expired link.' }));
      h.detectChanges();
      await h.fixture.whenStable();
      expect(el(h).querySelector('app-verify-email-banner button')).toBeNull();
      expect(el(h).querySelector('a[href^="/login"]')).not.toBeNull();
    });

    it('a missing / malformed token is invalid without calling the API', async () => {
      for (const fragment of ['', 'token=a.b', 'a=b']) {
        TestBed.resetTestingModule();
        setup();
        const h = await open(fragment);
        expect(el(h).querySelector('h1')!.textContent).toContain('invalid or has expired');
        http.verify();
      }
    });

    it('a network / server failure is not reported as an invalid link and can be retried', async () => {
      setup();
      const h = await open();
      http.expectOne('/api/v1/auth/verify-email').flush(...bad(503, { status: 503, title: 'Unavailable' }));
      h.detectChanges();
      expect(el(h).querySelector('h1')!.textContent).toContain('could not verify');
      (el(h).querySelector('button.btn-primary') as HTMLButtonElement).click();
      http.expectOne('/api/v1/auth/verify-email').flush(null, { status: 204, statusText: 'No Content' });
      h.detectChanges();
      expect(el(h).querySelector('h1')!.textContent).toContain('Email verified');
    });
  });

  describe('LoginPage additions', () => {
    it('links to "Forgot your password?"', async () => {
      setup();
      const h = await RouterTestingHarness.create('/login');
      expect(el(h).querySelector('a[href="/forgot-password"]')!.textContent).toContain('Forgot your password?');
      expect(el(h).querySelector('.alert-success')).toBeNull();
    });

    it('shows the success notice after a password reset', async () => {
      setup();
      const h = await RouterTestingHarness.create('/login?reset=1');
      expect(el(h).querySelector('.alert-success[role=status]')!.textContent).toContain('password has been changed');
    });
  });
});
