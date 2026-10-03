import { HttpContextToken } from '@angular/common/http';

/**
 * All API calls use this relative base. In development `proxy.conf.json` forwards it to the .NET API,
 * in production the Node SSR server (`src/server.ts`) or your reverse proxy does. Keeping the URL relative means
 * server-rendered and browser requests share one HTTP transfer-cache key (no duplicate fetch after hydration).
 */
export const API_BASE = '/api/v1';

/** Do not attach the access token / run silent refresh (login, register, refresh). */
export const SKIP_AUTH = new HttpContextToken<boolean>(() => false);
/** Marks a request already retried after a silent token refresh. */
export const IS_RETRY = new HttpContextToken<boolean>(() => false);
/** Do not toast errors for this request (caller renders the error itself). */
export const SILENT_ERRORS = new HttpContextToken<boolean>(() => false);
/** Do not drive the global loading bar (type-ahead, background syncs). */
export const BACKGROUND = new HttpContextToken<boolean>(() => false);

export const SITE_NAME = 'TechBazar BD';
export const MAX_COMPARE = 4;
export const MAX_CART_QTY = 10;
