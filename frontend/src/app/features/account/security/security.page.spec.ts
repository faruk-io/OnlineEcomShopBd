import { TestBed } from '@angular/core/testing';
import { HttpTestingController } from '@angular/common/http/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { Component } from '@angular/core';
import { MfaStatus } from '../../../core/models/api.models';
import { AuthService } from '../../../core/services/auth.service';
import { USER, authResponse, provideTestHttp } from '../../../core/testing/test-helpers';
import { SecurityPage, groupSecret } from './security.page';

@Component({ template: '' })
class Blank {}

const STATUS_OFF: MfaStatus = { enabled: false, required: false, setupPending: false, recoveryCodesRemaining: 0, enabledAt: null };
const STATUS_ON: MfaStatus = { enabled: true, required: false, setupPending: false, recoveryCodesRemaining: 8, enabledAt: '2026-09-01T10:00:00Z' };
const SETUP = { secret: 'JBSWY3DPEHPK3PXPJBSWY3DP', otpAuthUri: 'otpauth://totp/TechBazar%20BD:a%40b.c?secret=JBSWY3DPEHPK3PXPJBSWY3DP&issuer=TechBazar%20BD', issuer: 'TechBazar BD', account: 'a@b.c' };
const CODES = ['AAAAA-BBBBB', 'CCCCC-DDDDD', 'EEEEE-FFFFF'];

describe('SecurityPage', () => {
  let http: HttpTestingController;
  let h: RouterTestingHarness;
  let auth: AuthService;
  const el = () => h.routeNativeElement as HTMLElement;
  const q = <T extends HTMLElement>(sel: string) => el().querySelector<T>(sel);
  const flushUi = async () => { h.detectChanges(); await h.fixture.whenStable(); h.detectChanges(); };
  const type = (sel: string, value: string) => {
    const i = q<HTMLInputElement>(sel)!;
    i.value = value;
    i.dispatchEvent(new Event('input'));
  };
  const submit = async (sel = 'form') => { q(sel)!.dispatchEvent(new Event('submit', { cancelable: true })); await flushUi(); };

  const open = async (status: MfaStatus, url = '/account/security') => {
    localStorage.clear();
    TestBed.configureTestingModule({ providers: [provideRouter([{ path: 'account/security', component: SecurityPage }, { path: 'login', component: Blank }]), provideTestHttp()] });
    http = TestBed.inject(HttpTestingController);
    auth = TestBed.inject(AuthService);
    h = await RouterTestingHarness.create();
    const nav = h.navigateByUrl(url, SecurityPage);
    await Promise.resolve();
    await nav.then(() => undefined);
    http.expectOne('/api/v1/auth/mfa').flush(status);
    await flushUi();
  };
  const reloadAfterChange = async (status: MfaStatus) => { http.expectOne('/api/v1/auth/mfa').flush(status); await flushUi(); };
  const startSetup = async () => {
    q<HTMLButtonElement>('#mfa-setup')!.click();
    await flushUi();
    http.expectOne('/api/v1/auth/mfa/setup').flush(SETUP);
    await flushUi();
  };
  afterEach(() => http.verify());

  it('(a) off: explains and offers setup', async () => {
    await open(STATUS_OFF);
    expect(q('#mfa-setup')!.textContent).toContain('Set up two-step verification');
    expect(q('svg')).toBeNull();
    expect(q('[data-testid="admin-mfa-notice"]')).toBeNull();
  });

  it('shows the admin notice for ?reason=admin-mfa', async () => {
    await open(STATUS_OFF, '/account/security?reason=admin-mfa');
    expect(q('[data-testid="admin-mfa-notice"]')!.textContent).toContain('Administrators must use two-step verification');
  });

  it('(b) setup: QR (no img), grouped secret, code field', async () => {
    await open(STATUS_OFF);
    await startSetup();
    expect(q('svg[role="img"]')).toBeTruthy();
    expect(q('img')).toBeNull();
    expect(q('svg')!.getAttribute('aria-label')).not.toContain('JBSWY');
    expect(q('#mfa-secret')!.textContent!.trim()).toBe('JBSW Y3DP EHPK 3PXP JBSW Y3DP');
    expect(q('#enable-code')!.getAttribute('autocomplete')).toBe('one-time-code');
    expect(groupSecret('abcdefghij')).toBe('abcd efgh ij');
  });

  it('a wrong enable code shows the field error and keeps the setup', async () => {
    await open(STATUS_OFF);
    await startSetup();
    type('#enable-code', '111 111');
    await submit();
    const req = http.expectOne('/api/v1/auth/mfa/enable');
    expect(req.request.body).toEqual({ code: '111111' });
    req.flush({ status: 400, title: 'Validation', errors: { code: ['That code is not valid.'] } }, { status: 400, statusText: 'Bad Request' });
    await flushUi();
    expect(q('#enable-err')!.textContent).toContain('That code is not valid.');
    expect(q('#enable-code')!.getAttribute('aria-invalid')).toBe('true');
    expect(q('#mfa-secret')).toBeTruthy();
  });

  it('(d) enabling shows the recovery codes once, requires acknowledgement, then clears them', async () => {
    await open(STATUS_OFF);
    await startSetup();
    type('#enable-code', '123456');
    await submit();
    http.expectOne('/api/v1/auth/mfa/enable').flush({ recoveryCodes: CODES, auth: authResponse(7, { ...USER, mfaEnabled: true, mfaSession: true }) });
    await flushUi();
    await reloadAfterChange(STATUS_ON);

    expect(auth.accessToken()).toBe('access-7');
    const items = [...el().querySelectorAll('[data-testid="recovery-code"]')].map((x) => x.textContent!.trim());
    expect(items).toEqual(CODES);
    const done = q<HTMLButtonElement>('#dismiss-codes')!;
    expect(done.disabled).toBe(true);
    done.click();
    await flushUi();
    expect(el().querySelectorAll('[data-testid="recovery-code"]').length).toBe(3);   // cannot dismiss before ticking

    q<HTMLInputElement>('#ack-codes')!.click();
    await flushUi();
    expect(q<HTMLButtonElement>('#dismiss-codes')!.disabled).toBe(false);
    q<HTMLButtonElement>('#dismiss-codes')!.click();
    await flushUi();
    expect(el().querySelectorAll('[data-testid="recovery-code"]').length).toBe(0);
    expect(el().textContent).not.toContain('AAAAA-BBBBB');
    expect(JSON.stringify({ ...localStorage, ...sessionStorage })).not.toContain('AAAAA');
    expect(q('[data-testid="mfa-enabled"]')).toBeTruthy();
  });

  it('Copy uses the clipboard when available and degrades gracefully when not', async () => {
    await open(STATUS_OFF);
    await startSetup();
    type('#enable-code', '123456');
    await submit();
    http.expectOne('/api/v1/auth/mfa/enable').flush({ recoveryCodes: CODES, auth: authResponse(7) });
    await flushUi();
    await reloadAfterChange(STATUS_ON);

    q<HTMLButtonElement>('#copy-codes')!.click();
    await flushUi();
    expect(q('[role="status"]')!.textContent).toMatch(/not available|Copied/);

    const writeText = vi.fn().mockResolvedValue(undefined);
    Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true });
    q<HTMLButtonElement>('#copy-codes')!.click();
    await flushUi();
    expect(writeText).toHaveBeenCalledWith(CODES.join('\n'));
    Object.defineProperty(navigator, 'clipboard', { value: undefined, configurable: true });
  });

  it('(c) enabled: date, remaining count, warns when <= 3', async () => {
    await open({ ...STATUS_ON, recoveryCodesRemaining: 3 });
    expect(q('[data-testid="mfa-enabled"]')!.textContent).toContain('1 September 2026');
    expect(q('[data-testid="recovery-remaining"]')!.textContent).toContain('3');
    expect(q('[data-testid="recovery-remaining"] .alert-warn')).toBeTruthy();
    expect(q('#disable-open')).toBeTruthy();
  });

  it('no warning with plenty of recovery codes', async () => {
    await open(STATUS_ON);
    expect(q('[data-testid="recovery-remaining"] .alert-warn')).toBeNull();
  });

  it('required role: Turn off is hidden and explained', async () => {
    await open({ ...STATUS_ON, required: true });
    expect(q('#disable-open')).toBeNull();
    expect(q('[data-testid="mfa-required-note"]')!.textContent).toContain('mandatory');
  });

  it('regenerate asks for a current code and shows the new codes once', async () => {
    await open(STATUS_ON);
    q<HTMLButtonElement>('#regen-open')!.click();
    await flushUi();
    type('#regen-code', '654321');
    await submit();
    const req = http.expectOne('/api/v1/auth/mfa/recovery-codes');
    expect(req.request.body).toEqual({ code: '654321' });
    req.flush({ recoveryCodes: CODES });
    await flushUi();
    await reloadAfterChange({ ...STATUS_ON, recoveryCodesRemaining: 10 });
    expect(el().querySelectorAll('[data-testid="recovery-code"]').length).toBe(3);
  });

  it('turn off sends password + code, signs out and goes to /login', async () => {
    await open(STATUS_ON);
    auth.login('a@b.com', 'x').subscribe();
    http.expectOne('/api/v1/auth/login').flush(authResponse(1));
    q<HTMLButtonElement>('#disable-open')!.click();
    await flushUi();
    type('#disable-password', 'Passw0rdX');
    type('#disable-code', '123456');
    await submit();
    const req = http.expectOne('/api/v1/auth/mfa/disable');
    expect(req.request.body).toEqual({ password: 'Passw0rdX', code: '123456' });
    req.flush(null, { status: 204, statusText: 'No Content' });
    await flushUi();
    expect(auth.isAuthenticated()).toBe(false);
    expect(TestBed.inject(Router).url).toBe('/login');
  });

  it('turn off with a recovery code and a wrong password shows the field error', async () => {
    await open(STATUS_ON);
    q<HTMLButtonElement>('#disable-open')!.click();
    await flushUi();
    q<HTMLButtonElement>('button.btn-sm')!.click();   // use a recovery code instead
    await flushUi();
    type('#disable-password', 'nope');
    type('#disable-recovery', 'aaaaa-bbbbb');
    await submit();
    const req = http.expectOne('/api/v1/auth/mfa/disable');
    expect(req.request.body).toEqual({ password: 'nope', recoveryCode: 'AAAAA-BBBBB' });
    req.flush({ status: 400, title: 'Validation', errors: { password: ['That password is not correct.'] } }, { status: 400, statusText: 'Bad Request' });
    await flushUi();
    expect(q('#dp-err')!.textContent).toContain('not correct');
  });
});
