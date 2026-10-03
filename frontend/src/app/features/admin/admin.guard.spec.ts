import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, Router, RouterStateSnapshot, UrlTree, provideRouter } from '@angular/router';
import { adminGuard } from '../../core/guards/admin.guard';
import { AuthService } from '../../core/services/auth.service';

describe('adminGuard', () => {
  const run = async (auth: { isAuthenticated: boolean; isAdmin: boolean; mfaRequired?: boolean; mfaSession?: boolean }, url = '/admin/orders') => {
    const whenReady = vi.fn().mockResolvedValue(undefined);
    TestBed.configureTestingModule({
      providers: [provideRouter([]), { provide: AuthService, useValue: { whenReady, isAuthenticated: () => auth.isAuthenticated, isAdmin: () => auth.isAdmin, user: () => (auth.isAuthenticated ? { mfaRequired: auth.mfaRequired, mfaSession: auth.mfaSession } : null) } }],
    });
    const result = await TestBed.runInInjectionContext(() => adminGuard({} as ActivatedRouteSnapshot, { url } as RouterStateSnapshot));
    expect(whenReady).toHaveBeenCalled(); // waits for the silent session restore before deciding
    return result;
  };

  it('sends anonymous visitors to /login with the page they wanted as returnUrl', async () => {
    const r = await run({ isAuthenticated: false, isAdmin: false }, '/admin/orders?status=Pending');
    expect(r).toBeInstanceOf(UrlTree);
    const router = TestBed.inject(Router);
    expect(router.serializeUrl(r as UrlTree)).toBe('/login?returnUrl=%2Fadmin%2Forders%3Fstatus%3DPending');
  });

  it('sends signed-in customers to the storefront home', async () => {
    const r = await run({ isAuthenticated: true, isAdmin: false });
    expect(TestBed.inject(Router).serializeUrl(r as UrlTree)).toBe('/');
  });

  it('lets admins through', async () => {
    expect(await run({ isAuthenticated: true, isAdmin: true })).toBe(true);
  });

  it('sends an admin who has not passed MFA in this session to enrolment', async () => {
    const r = await run({ isAuthenticated: true, isAdmin: true, mfaRequired: true, mfaSession: false });
    expect(TestBed.inject(Router).serializeUrl(r as UrlTree)).toBe('/account/security?reason=admin-mfa');
  });

  it('lets an MFA-verified admin through', async () => {
    expect(await run({ isAuthenticated: true, isAdmin: true, mfaRequired: true, mfaSession: true })).toBe(true);
  });

  it('does not apply the MFA rule to non-admins', async () => {
    const r = await run({ isAuthenticated: true, isAdmin: false, mfaRequired: true, mfaSession: false });
    expect(TestBed.inject(Router).serializeUrl(r as UrlTree)).toBe('/');
  });
});
