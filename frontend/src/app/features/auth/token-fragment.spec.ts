import { readTokenFromFragment } from './token-fragment';

const TOKEN = 'Abcdefghijklmnopqrstuvwxyz0123456789_-ABCDE';   // 43 chars of base64url

describe('readTokenFromFragment', () => {
  it('reads the token with or without the leading #', () => {
    expect(readTokenFromFragment(`token=${TOKEN}`)).toBe(TOKEN);
    expect(readTokenFromFragment(`#token=${TOKEN}`)).toBe(TOKEN);
  });

  it('returns null for empty / missing input', () => {
    expect(readTokenFromFragment(null)).toBeNull();
    expect(readTokenFromFragment(undefined)).toBeNull();
    expect(readTokenFromFragment('')).toBeNull();
    expect(readTokenFromFragment('#')).toBeNull();
    expect(readTokenFromFragment('token=')).toBeNull();
  });

  it('returns null for garbage', () => {
    expect(readTokenFromFragment('hello')).toBeNull();
    expect(readTokenFromFragment('tok=abc')).toBeNull();
    expect(readTokenFromFragment('xtoken=abc')).toBeNull();
    expect(readTokenFromFragment('token=<script>alert(1)</script>')).toBeNull();
    expect(readTokenFromFragment('token=has space')).toBeNull();
    expect(readTokenFromFragment('token=%E0%A4%A')).toBeNull();           // malformed escape
    expect(readTokenFromFragment(`token=${'a'.repeat(129)}`)).toBeNull();  // longer than the API accepts
  });

  it('finds the token among other parameters and takes the first one', () => {
    expect(readTokenFromFragment(`utm=x&token=${TOKEN}&y=1`)).toBe(TOKEN);
    expect(readTokenFromFragment('token=first&token=second')).toBe('first');
    expect(readTokenFromFragment('flag&token=abc')).toBe('abc');
  });

  it('decodes url-encoded values before validating', () => {
    expect(readTokenFromFragment('token=%61bc')).toBe('abc');
    expect(readTokenFromFragment('token=a%20b')).toBeNull();
  });
});
