import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
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
  return auth.isAdmin() ? true : router.createUrlTree(['/']);
};

/** Keeps the admin shell out of the public storefront's crawl and render path. */
export const ADMIN_BASE = '/admin';
