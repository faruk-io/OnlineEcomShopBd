import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { MFA_SETUP_URL } from '../interceptors/error.interceptor';
import { AuthService } from '../services/auth.service';

/**
 * Admin area gate (UX only: every /api/v1/admin endpoint independently enforces the Admin role on the server).
 * Anonymous users go to login; signed-in customers are sent home.
 */
export const adminGuard: CanActivateFn = async (_route, state) => {
  const auth = inject(AuthService);
  const router = inject(Router);
  await auth.whenReady();
  if (!auth.isAuthenticated()) return router.createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });
  if (!auth.isAdmin()) return router.createUrlTree(['/']);
  const user = auth.user();
  // Admins must have passed a second factor in THIS session (the API enforces it too); send them to enrolment / the 2FA login.
  if (user?.mfaRequired && !user.mfaSession) return router.parseUrl(MFA_SETUP_URL);
  return true;
};

/** Keeps the admin shell out of the public storefront's crawl and render path. */
export const ADMIN_BASE = '/admin';
