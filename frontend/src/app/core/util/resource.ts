import { ResourceRef } from '@angular/core';
import { ApiError } from '../models/api.models';

/**
 * Angular wraps whatever a resource's stream throws in an Error whose `.cause` is the original value.
 * This unwraps our normalised {@link ApiError} (or returns undefined for anything else).
 */
export function apiErrorOf(error: unknown): ApiError | undefined {
  const isApi = (v: unknown): v is ApiError => typeof v === 'object' && v !== null && typeof (v as ApiError).status === 'number';
  if (isApi(error)) return error;
  const cause = (error as { cause?: unknown } | null | undefined)?.cause;
  return isApi(cause) ? cause : undefined;
}

/** `resource.value()` throws while the resource is in the error state; this returns undefined instead. */
export function safeValue<T>(res: Pick<ResourceRef<T>, 'hasValue' | 'value'>): T | undefined {
  return res.hasValue() ? res.value() : undefined;
}
