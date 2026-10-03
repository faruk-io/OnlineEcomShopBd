import { HttpErrorResponse, HttpInterceptorFn, HttpRequest } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, switchMap, throwError } from 'rxjs';
import { API_BASE, IS_RETRY, SKIP_AUTH } from '../config';
import { AuthService } from '../services/auth.service';

const withToken = (req: HttpRequest<unknown>, token: string) =>
  req.clone({ setHeaders: { Authorization: `Bearer ${token}` } });

/**
 * Attaches the JWT to API calls and, on a 401, silently refreshes once and replays the request.
 * If the refresh fails the session is cleared and the original 401 is surfaced to the caller.
 */
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  if (!req.url.startsWith(API_BASE) || req.context.get(SKIP_AUTH)) return next(req);

  const auth = inject(AuthService);
  const token = auth.accessToken();
  const outgoing = token ? withToken(req, token) : req;

  return next(outgoing).pipe(
    catchError((error: unknown) => {
      const canRefresh =
        error instanceof HttpErrorResponse &&
        error.status === 401 &&
        !req.context.get(IS_RETRY) &&
        auth.hasSession() &&
        !!token; // only a rejected *authenticated* call warrants a refresh

      if (!canRefresh) return throwError(() => error);

      return auth.refresh().pipe(
        switchMap((fresh) => next(withToken(req.clone({ context: req.context.set(IS_RETRY, true) }), fresh))),
        catchError(() => throwError(() => error)),
      );
    }),
  );
};
