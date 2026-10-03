import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { HttpTestingController } from '@angular/common/http/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { ToastService } from '../../core/services/toast.service';
import { authResponse, provideTestHttp } from '../../core/testing/test-helpers';
import { LoginPage } from './login.page';
import { RegisterPage } from './register.page';

@Component({ template: '' })
class Blank {}

describe('Auth pages', () => {
  let http: HttpTestingController;
  let router: Router;

  const setup = () => {
    localStorage.clear();
    TestBed.configureTestingModule({
      providers: [provideRouter([{ path: 'login', component: LoginPage }, { path: 'register', component: RegisterPage }, { path: 'cart', component: Blank }, { path: '', component: Blank }]), provideTestHttp()],
    });
    http = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
  };
  afterEach(() => http.verify());

  const el = (h: RouterTestingHarness) => h.routeNativeElement as HTMLElement;
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

  describe('LoginPage', () => {
    it('validates before calling the API and announces errors next to the fields', async () => {
      setup();
      const h = await RouterTestingHarness.create('/login');
      await submit(h);
      expect(el(h).textContent).toContain('Email is required.');
      expect(el(h).textContent).toContain('Password is required.');
      expect(el(h).querySelector('#email')!.getAttribute('aria-invalid')).toBe('true');
      http.expectNone('/api/v1/auth/login');

      fill(el(h), 'email', 'not-an-email');
      h.detectChanges();
      expect(el(h).textContent).toContain('Enter a valid email address.');
    });

    it('signs in and returns to the requested page', async () => {
      setup();
      const h = await RouterTestingHarness.create('/login?returnUrl=%2Fcart');
      fill(el(h), 'email', 'rahim@example.com');
      fill(el(h), 'password', 'Passw0rdX');
      await submit(h);
      http.expectOne('/api/v1/auth/login').flush(authResponse(1));
      await h.fixture.whenStable();
      expect(router.url).toBe('/cart');
      expect(TestBed.inject(ToastService).toasts()[0].message).toBe('Welcome back, Rahim!');
    });

    it('ignores a hostile returnUrl', async () => {
      setup();
      const h = await RouterTestingHarness.create('/login?returnUrl=https%3A%2F%2Fevil.example');
      fill(el(h), 'email', 'rahim@example.com');
      fill(el(h), 'password', 'Passw0rdX');
      await submit(h);
      http.expectOne('/api/v1/auth/login').flush(authResponse(1));
      await h.fixture.whenStable();
      expect(router.url).toBe('/');
    });

    it('shows the server message on bad credentials and lets the user retry', async () => {
      setup();
      const h = await RouterTestingHarness.create('/login');
      fill(el(h), 'email', 'rahim@example.com');
      fill(el(h), 'password', 'wrong');
      await submit(h);
      http.expectOne('/api/v1/auth/login').flush({ status: 401, title: 'Authentication failed.', detail: 'Invalid email or password.' }, { status: 401, statusText: 'Unauthorized' });
      h.detectChanges();
      const alert = el(h).querySelector('[role=alert]')!;
      expect(alert.textContent).toContain('Invalid email or password.');
      expect(el(h).querySelector<HTMLButtonElement>('button[type=submit]')!.disabled).toBe(false);
    });
  });

  describe('RegisterPage', () => {
    const fillValid = (h: RouterTestingHarness) => {
      fill(el(h), 'fullName', 'Rahim Uddin');
      fill(el(h), 'email', 'rahim@example.com');
      fill(el(h), 'phone', '01712345678');
      fill(el(h), 'password', 'Passw0rdX');
      fill(el(h), 'confirm', 'Passw0rdX');
    };

    it('enforces password strength, matching confirmation and phone format client-side', async () => {
      setup();
      const h = await RouterTestingHarness.create('/register');
      fill(el(h), 'fullName', 'Rahim');
      fill(el(h), 'email', 'rahim@example.com');
      fill(el(h), 'phone', '123');
      fill(el(h), 'password', 'weakpass');
      fill(el(h), 'confirm', 'different');
      await submit(h);
      const text = el(h).textContent!;
      expect(text).toContain('Add an uppercase letter.');
      expect(text).toContain('valid Bangladeshi mobile number');
      expect(text).toContain('Passwords do not match.');
      http.expectNone('/api/v1/auth/register');
    });

    it('registers (phone optional -> null), then goes to the return URL', async () => {
      setup();
      const h = await RouterTestingHarness.create('/register?returnUrl=%2Fcart');
      fillValid(h);
      fill(el(h), 'phone', '');
      await submit(h);
      const req = http.expectOne('/api/v1/auth/register');
      expect(req.request.body).toEqual({ fullName: 'Rahim Uddin', email: 'rahim@example.com', phone: null, password: 'Passw0rdX' });
      req.flush(authResponse(1));
      await h.fixture.whenStable();
      expect(router.url).toBe('/cart');
    });

    it('maps a 409 / validation response onto the right field', async () => {
      setup();
      const h = await RouterTestingHarness.create('/register');
      fillValid(h);
      await submit(h);
      http.expectOne('/api/v1/auth/register').flush(
        { status: 400, title: 'One or more validation errors occurred.', errors: { email: ['Email is already taken.'] } },
        { status: 400, statusText: 'Bad Request' },
      );
      h.detectChanges();
      expect(el(h).querySelector('#email-err')!.textContent).toContain('Email is already taken.');
      expect(el(h).querySelector('[role=alert]')!.textContent).toContain('highlighted');
    });
  });
});
