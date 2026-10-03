import { FormControl, FormGroup } from '@angular/forms';
import { applyServerErrors, fieldError, matchValidator, passwordValidator, phoneValidator, safeReturnUrl } from './auth-forms';

describe('safeReturnUrl (open-redirect protection)', () => {
  it.each([
    ['/cart', '/cart'],
    ['/account/profile?tab=1', '/account/profile?tab=1'],
    [null, '/'],
    [undefined, '/'],
    ['', '/'],
    ['//evil.example', '/'],
    ['https://evil.example', '/'],
    ['/\\evil', '/'],
    ['javascript:alert(1)', '/'],
    ['/redirect?to=http://evil.example', '/'],
  ])('%s -> %s', (input, expected) => expect(safeReturnUrl(input)).toBe(expected));
});

describe('validators', () => {
  it('password mirrors the API rules', () => {
    const v = (value: string) => passwordValidator(new FormControl(value));
    expect(v('')).toBeNull();
    expect(v('Passw0rdX')).toBeNull();
    expect(v('short1A')).toMatchObject({ minlength: true });
    expect(v('alllowercase1')).toMatchObject({ upper: true });
    expect(v('ALLUPPER123')).toMatchObject({ lower: true });
    expect(v('NoDigitsHere')).toMatchObject({ digit: true });
  });

  it.each([['01712345678', true], ['+8801712345678', true], ['8801912345678', true], ['01012345678', false], ['1234', false], ['', true]])(
    'BD mobile %s -> valid=%s', (value, valid) => expect(phoneValidator(new FormControl(value)) === null).toBe(valid));

  it('matchValidator flags different passwords only when both are filled', () => {
    const g = new FormGroup({ a: new FormControl(''), b: new FormControl('') }, { validators: [matchValidator('a', 'b')] });
    expect(g.errors).toBeNull();
    g.patchValue({ a: 'x', b: 'y' });
    expect(g.errors).toEqual({ mismatch: true });
    g.patchValue({ b: 'x' });
    expect(g.errors).toBeNull();
  });
});

describe('fieldError / applyServerErrors', () => {
  const form = () => new FormGroup({ email: new FormControl(''), phone: new FormControl('') });

  it('shows nothing until the control is touched', () => {
    const f = form();
    f.get('email')!.setErrors({ required: true });
    expect(fieldError(f, 'email')).toBeNull();
    f.get('email')!.markAsTouched();
    expect(fieldError(f, 'email', { email: 'Email' })).toBe('Email is required.');
  });

  it('copies ProblemDetails field errors onto controls and reports a summary', () => {
    const f = form();
    const summary = applyServerErrors(f, { status: 400, title: 'x', detail: null, traceId: null, errors: { email: ['An account with this email already exists.'], nope: ['ignored'] } });
    expect(summary).toBe('Please fix the highlighted fields.');
    expect(fieldError(f, 'email')).toBe('An account with this email already exists.');
  });

  it('falls back to rate-limit / detail / generic messages', () => {
    const f = form();
    expect(applyServerErrors(f, { status: 429, title: 'Too many requests.', detail: null, errors: null, traceId: null })).toMatch(/wait a minute/);
    expect(applyServerErrors(f, { status: 401, title: 'Authentication failed.', detail: 'Invalid email or password.', errors: null, traceId: null })).toBe('Invalid email or password.');
    expect(applyServerErrors(f, new Error('boom'))).toMatch(/Something went wrong/);
  });
});
