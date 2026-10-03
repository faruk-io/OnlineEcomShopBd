import { Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpTestingController } from '@angular/common/http/testing';
import { AuthService } from '../core/services/auth.service';
import { USER, authResponse, provideTestHttp } from '../core/testing/test-helpers';
import { RESEND_COOLDOWN_SECONDS, VerifyEmailBannerComponent } from './verify-email-banner.component';

@Component({ imports: [VerifyEmailBannerComponent], template: '<app-verify-email-banner />' })
class Host {}

describe('VerifyEmailBannerComponent', () => {
  let http: HttpTestingController;
  let f: ComponentFixture<Host>;
  const el = () => f.nativeElement as HTMLElement;
  const btn = () => el().querySelector<HTMLButtonElement>('button');
  const tick = async () => {
    f.detectChanges();
    await f.whenStable();
    f.detectChanges();
  };

  const render = async (user: 'none' | 'unverified' | 'verified') => {
    localStorage.clear();
    TestBed.configureTestingModule({ providers: [provideTestHttp()] });
    http = TestBed.inject(HttpTestingController);
    if (user !== 'none') {
      TestBed.inject(AuthService).login('rahim@example.com', 'x').subscribe();
      http.expectOne('/api/v1/auth/login').flush(authResponse(1, { ...USER, emailConfirmed: user === 'verified' }));
    }
    f = TestBed.createComponent(Host);
    await tick();
  };

  beforeEach(() => vi.useFakeTimers({ toFake: ['setInterval', 'clearInterval'] }));   // never fake setTimeout: it freezes the zoneless scheduler
  afterEach(() => {
    http.verify();
    vi.useRealTimers();
  });

  it('renders nothing for signed-out and for verified users', async () => {
    await render('none');
    expect(el().querySelector('.banner')).toBeNull();
    TestBed.resetTestingModule();
    await render('verified');
    expect(el().querySelector('.banner')).toBeNull();
  });

  it('is shown to an unverified user', async () => {
    await render('unverified');
    expect(el().querySelector('.banner')!.textContent).toContain('rahim@example.com');
    expect(btn()!.textContent).toContain('Resend verification email');
  });

  it('resends, shows the neutral server message, then counts a 60 s cooldown down and re-enables the button', async () => {
    await render('unverified');
    btn()!.click();
    http.expectOne('/api/v1/auth/resend-verification').flush({ message: 'If that address needs verifying, a link is on its way.' }, { status: 202, statusText: 'Accepted' });
    await tick();
    expect(el().querySelector('[role=status]')!.textContent).toContain('a link is on its way');
    expect(btn()!.disabled).toBe(true);
    expect(btn()!.textContent).toContain(`(${RESEND_COOLDOWN_SECONDS}s)`);

    btn()!.click();                                  // ignored while cooling down
    http.expectNone('/api/v1/auth/resend-verification');

    vi.advanceTimersByTime(10_000);
    await tick();
    expect(btn()!.textContent).toContain('(50s)');
    vi.advanceTimersByTime(50_000);
    await tick();
    expect(btn()!.disabled).toBe(false);
    expect(btn()!.textContent).not.toContain('(');
  });

  it('answers 429 with friendly text (no toast) and also cools down', async () => {
    await render('unverified');
    btn()!.click();
    http.expectOne('/api/v1/auth/resend-verification').flush({ status: 429, title: 'Too Many Requests' }, { status: 429, statusText: 'Too Many Requests' });
    await tick();
    expect(el().querySelector('[role=alert]')!.textContent).toContain('several emails');
    expect(btn()!.disabled).toBe(true);
    expect(TestBed.inject(AuthService).isAuthenticated()).toBe(true);
  });

  it('another failure shows a retryable message without a cooldown', async () => {
    await render('unverified');
    btn()!.click();
    http.expectOne('/api/v1/auth/resend-verification').flush({ status: 503, title: 'x' }, { status: 503, statusText: 'x' });
    await tick();
    expect(el().querySelector('[role=alert]')!.textContent).toContain('could not send');
    expect(btn()!.disabled).toBe(false);
  });

  it('hides itself as soon as the profile says the email is confirmed', async () => {
    await render('unverified');
    TestBed.inject(AuthService).reloadUser().subscribe();
    http.expectOne('/api/v1/auth/me').flush({ ...USER, emailConfirmed: true });
    await tick();
    expect(el().querySelector('.banner')).toBeNull();
  });
});
