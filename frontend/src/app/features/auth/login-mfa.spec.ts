import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { HttpTestingController } from '@angular/common/http/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { AuthService } from '../../core/services/auth.service';
import { authResponse, provideTestHttp } from '../../core/testing/test-helpers';
import { LoginPage } from './login.page';
import { normaliseRecoveryCode, totpDigits } from './auth-forms';

@Component({ template: '' })
class Blank {}

describe('LoginPage second step', () => {
  let http: HttpTestingController;
  let router: Router;
  let h: RouterTestingHarness;
  const el = () => h.routeNativeElement as HTMLElement;
  const q = <T extends HTMLElement>(sel: string) => el().querySelector<T>(sel);
  const type = (sel: string, value: string) => {
    const i = q<HTMLInputElement>(sel)!;
    i.value = value;
    i.dispatchEvent(new Event('input'));
  };
  const flushUi = async () => { h.detectChanges(); await h.fixture.whenStable(); h.detectChanges(); };
  const submit = async () => { q('form')!.dispatchEvent(new Event('submit', { cancelable: true })); await flushUi(); };
  const unauthorized = (detail: string) => ({ status: 401, title: 'Authentication failed.', detail });

  const toStepTwo = async (url = '/login') => {
    localStorage.clear();
    TestBed.configureTestingModule({ providers: [provideRouter([{ path: 'login', component: LoginPage }, { path: 'cart', component: Blank }, { path: '', component: Blank }]), provideTestHttp()] });
    http = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
    h = await RouterTestingHarness.create(url);
    type('#email', 'admin@example.com');
    type('#password', 'Passw0rdX');
    await submit();
    http.expectOne('/api/v1/auth/login').flush({ mfaRequired: true, mfaToken: 'tok', expiresAt: '2099-01-01T00:00:00Z' }, { status: 202, statusText: 'Accepted' });
    await flushUi();
  };
  afterEach(() => http.verify());

  it('shows the code step with the right input hints and focuses it', async () => {
    await toStepTwo();
    const input = q<HTMLInputElement>('#mfa-code')!;
    expect(input).toBeTruthy();
    expect(input.getAttribute('inputmode')).toBe('numeric');
    expect(input.getAttribute('autocomplete')).toBe('one-time-code');
    expect(Number(input.getAttribute('maxlength'))).toBeGreaterThanOrEqual(7);
    expect(q('label[for="mfa-code"]')).toBeTruthy();
    expect(q('#email')).toBeNull();
    expect(document.activeElement).toBe(input);
  });

  it('submits digits only and returns to the requested page', async () => {
    await toStepTwo('/login?returnUrl=%2Fcart');
    type('#mfa-code', '123 456');
    await submit();
    const req = http.expectOne('/api/v1/auth/mfa/verify');
    expect(req.request.body).toEqual({ mfaToken: 'tok', code: '123456' });
    req.flush(authResponse(2));
    await flushUi();
    expect(router.url).toBe('/cart');
  });

  it('does not call the API for an incomplete code', async () => {
    await toStepTwo();
    type('#mfa-code', '12');
    await submit();
    http.expectNone('/api/v1/auth/mfa/verify');
    expect(q('#mfa-error')!.textContent).toContain('6-digit');
    expect(q('[aria-live]')).toBeTruthy();
  });

  it('a wrong code shows the error and stays on the step', async () => {
    await toStepTwo();
    type('#mfa-code', '000000');
    await submit();
    http.expectOne('/api/v1/auth/mfa/verify').flush(unauthorized('That code is not valid.'), { status: 401, statusText: 'Unauthorized' });
    await flushUi();
    expect(q('#mfa-error')!.textContent).toContain('That code is not valid.');
    expect(q('#mfa-code')).toBeTruthy();
  });

  it('an expired challenge goes back to the password step with the reason', async () => {
    await toStepTwo();
    type('#mfa-code', '000000');
    await submit();
    http.expectOne('/api/v1/auth/mfa/verify').flush(unauthorized('This sign-in attempt is invalid or has expired. Please sign in again.'), { status: 401, statusText: 'Unauthorized' });
    await flushUi();
    expect(q('#email')).toBeTruthy();
    expect(q('[role="alert"]')!.textContent).toContain('expired');
  });

  it('a locked account shows the locked message on the password step', async () => {
    await toStepTwo();
    type('#mfa-code', '000000');
    await submit();
    http.expectOne('/api/v1/auth/mfa/verify').flush(unauthorized('Account temporarily locked. Try again later.'), { status: 401, statusText: 'Unauthorized' });
    await flushUi();
    expect(q('#email')).toBeTruthy();
    expect(q('[role="alert"]')!.textContent).toContain('locked');
  });

  it('toggles to a recovery code and sends it normalised', async () => {
    await toStepTwo();
    q<HTMLButtonElement>('#mfa-toggle')!.click();
    await flushUi();
    expect(q('#mfa-code')).toBeNull();
    type('#mfa-recovery', 'abcde fghjk');
    await submit();
    const req = http.expectOne('/api/v1/auth/mfa/verify');
    expect(req.request.body).toEqual({ mfaToken: 'tok', recoveryCode: 'ABCDE-FGHJK' });
    req.flush(authResponse(2));
    await flushUi();
  });

  it('Back forgets the challenge and shows the credentials again', async () => {
    await toStepTwo();
    q<HTMLButtonElement>('#mfa-back')!.click();
    await flushUi();
    expect(TestBed.inject(AuthService).mfaPending()).toBe(false);
    expect(q('#email')).toBeTruthy();
    expect(q('#mfa-code')).toBeNull();
  });

  it('input helpers', () => {
    expect(totpDigits('123 456')).toBe('123456');
    expect(totpDigits('12a3-4567')).toBe('123456');
    expect(normaliseRecoveryCode('abcde-fghjk')).toBe('ABCDE-FGHJK');
    expect(normaliseRecoveryCode('abcdefghjk')).toBe('ABCDE-FGHJK');
    expect(normaliseRecoveryCode('  ')).toBe('');
  });
});
