import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';
import { API_BASE, SILENT_ERRORS } from '../config';
import { ApiError } from '../models/api.models';
import { ToastService } from '../services/toast.service';

export const MFA_SETUP_PATH = '/account/security';
export const MFA_SETUP_URL = '/account/security?reason=admin-mfa';

/** Converts any failed API response into a normalised {@link ApiError} and toasts the ones users cannot act on inline. */
export const errorInterceptor: HttpInterceptorFn = (req, next) => {
  if (!req.url.startsWith(API_BASE)) return next(req);
  const toast = inject(ToastService);
  const router = inject(Router);

  return next(req).pipe(
    catchError((error: unknown) => {
      if (!(error instanceof HttpErrorResponse)) return throwError(() => error);
      const apiError = toApiError(error);

      if (apiError.status === 403 && apiError.code === 'mfa_required') {
        // An admin session that has not passed a second factor: send them to enrolment once (never loop on the page itself).
        if (!router.url.startsWith(MFA_SETUP_PATH) && !router.getCurrentNavigation()) void router.navigateByUrl(MFA_SETUP_URL);
      } else if (!req.context.get(SILENT_ERRORS)) {
        if (apiError.status === 0) toast.error('Cannot reach the server. Check your connection and try again.');
        else if (apiError.status === 429) toast.error('Too many requests. Please wait a moment and try again.');
        else if (apiError.status >= 500) toast.error('Something went wrong on our side. Please try again shortly.');
        else if (apiError.status === 403) toast.error('You do not have permission to do that.');
      }
      return throwError(() => apiError);
    }),
  );
};

export function toApiError(error: HttpErrorResponse): ApiError {
  const body = (typeof error.error === 'object' && error.error !== null ? error.error : {}) as Record<string, unknown>;
  const errors = body['errors'] && typeof body['errors'] === 'object' ? (body['errors'] as Record<string, string[]>) : null;
  return {
    status: error.status,
    title: typeof body['title'] === 'string' ? body['title'] : error.statusText || 'Request failed',
    detail: typeof body['detail'] === 'string' ? body['detail'] : null,
    errors,
    traceId: typeof body['traceId'] === 'string' ? body['traceId'] : null,
    ...(typeof body['code'] === 'string' ? { code: body['code'] } : {}),
  };
}

export function isApiError(value: unknown): value is ApiError {
  return typeof value === 'object' && value !== null && 'status' in value && 'title' in value;
}

/** Best user-facing message for an unknown thrown value. */
export function errorMessage(value: unknown, fallback = 'Something went wrong. Please try again.'): string {
  if (!isApiError(value)) return fallback;
  if (value.errors) {
    const first = Object.values(value.errors).flat()[0];
    if (first) return first;
  }
  return value.detail ?? value.title ?? fallback;
}
