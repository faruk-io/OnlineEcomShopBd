import { AbstractControl, FormGroup, ValidationErrors, ValidatorFn } from '@angular/forms';
import { ApiError } from '../../core/models/api.models';
import { isApiError } from '../../core/interceptors/error.interceptor';

export const BD_PHONE = /^(?:\+?88)?01[3-9]\d{8}$/;

/** Mirrors the API's password rules so users see problems before the round trip. */
export const passwordValidator: ValidatorFn = (c: AbstractControl): ValidationErrors | null => {
  const v = (c.value ?? '') as string;
  if (!v) return null;
  const errors: ValidationErrors = {};
  if (v.length < 8) errors['minlength'] = true;
  if (!/[A-Z]/.test(v)) errors['upper'] = true;
  if (!/[a-z]/.test(v)) errors['lower'] = true;
  if (!/[0-9]/.test(v)) errors['digit'] = true;
  return Object.keys(errors).length ? errors : null;
};

export const phoneValidator: ValidatorFn = (c) => (!c.value || BD_PHONE.test(c.value) ? null : { phone: true });

export const matchValidator = (a: string, b: string): ValidatorFn => (g: AbstractControl) => {
  const x = g.get(a)?.value, y = g.get(b)?.value;
  return x && y && x !== y ? { mismatch: true } : null;
};

/** Human message for the first error on a control (null when valid or untouched). */
export function fieldError(form: FormGroup, name: string, labels: Record<string, string> = {}): string | null {
  const c = form.get(name);
  if (!c || !c.errors || !(c.touched || c.dirty)) return null;
  const label = labels[name] ?? name;
  const e = c.errors;
  if (e['server']) return e['server'] as string;
  if (e['required']) return `${label} is required.`;
  if (e['email']) return 'Enter a valid email address.';
  if (e['phone']) return 'Enter a valid Bangladeshi mobile number, e.g. 01712345678.';
  if (e['minlength'] && (name === 'password' || name === 'newPassword')) return 'Use at least 8 characters.';
  if (e['upper']) return 'Add an uppercase letter.';
  if (e['lower']) return 'Add a lowercase letter.';
  if (e['digit']) return 'Add a number.';
  if (e['maxlength']) return `${label} is too long.`;
  return 'Please check this field.';
}

/** Copies server-side (ProblemDetails `errors`) messages onto the matching controls. Returns a form-level message. */
export function applyServerErrors(form: FormGroup, error: unknown): string {
  if (!isApiError(error)) return 'Something went wrong. Please try again.';
  const api: ApiError = error;
  let placed = false;
  for (const [field, messages] of Object.entries(api.errors ?? {})) {
    const c = form.get(field);
    if (c && messages[0]) {
      c.setErrors({ server: messages[0] });
      c.markAsTouched();
      placed = true;
    }
  }
  if (placed) return 'Please fix the highlighted fields.';
  if (api.status === 429) return 'Too many attempts. Please wait a minute and try again.';
  return api.detail ?? api.title;
}

/** Only allow same-site relative return URLs (blocks open redirects like //evil.com, https://… and backslash tricks). */
export function safeReturnUrl(url: string | null | undefined): string {
  return url && url.startsWith('/') && !url.startsWith('//') && !url.includes('://') && !url.includes('\\') ? url : '/';
}
