import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, Router, RouterStateSnapshot, UrlTree, provideRouter } from '@angular/router';
import { adminGuard } from '../../core/guards/admin.guard';
import { AuthService } from '../../core/services/auth.service';

describe('adminGuard', () => {
  const run = async (auth: { isAuthenticated: boolean; isAdmin: boolean }, url = '/admin/orders') => {
    const whenReady = vi.fn().mockResolvedValue(undefined);
    TestBed.configureTestingModule({
      providers: [provideRouter([]), { provide: AuthService, useValue: { whenReady, isAuthenticated: () => auth.isAuthenticated, isAdmin: () => auth.isAdmin } }],
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
});
