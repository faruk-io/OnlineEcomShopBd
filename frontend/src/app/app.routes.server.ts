import { RenderMode, ServerRoute } from '@angular/ssr';

/**
 * Catalog pages are server-rendered per request (fresh prices/stock, full meta tags for crawlers).
 * Anything that depends on the signed-in user is rendered in the browser only: tokens never touch the server.
 */
export const serverRoutes: ServerRoute[] = [
  { path: 'account', renderMode: RenderMode.Client },
  { path: 'account/**', renderMode: RenderMode.Client },
  { path: 'login', renderMode: RenderMode.Client },
  { path: 'register', renderMode: RenderMode.Client },
  { path: 'admin', renderMode: RenderMode.Client },
  { path: 'admin/**', renderMode: RenderMode.Client },
  { path: 'checkout', renderMode: RenderMode.Client },
  { path: 'cart', renderMode: RenderMode.Client },
  { path: 'wishlist', renderMode: RenderMode.Client },
  { path: '**', renderMode: RenderMode.Server },
];
