/** What the API's emailed tokens look like: URL-safe base64 (43 chars today; the API accepts up to 128). */
const TOKEN = /^[A-Za-z0-9_-]{1,128}$/;

/**
 * Extracts `token` from a URL fragment such as `token=abc` or `#token=abc&x=1`. Returns null for anything that cannot be
 * a token (empty, no `token` key, garbage characters), so the caller can show the "link invalid" state without a server call.
 * The first `token` parameter wins. Pure: no DOM access, never logs or stores the value.
 */
export function readTokenFromFragment(fragment: string | null | undefined): string | null {
  if (!fragment) return null;
  const raw = fragment.startsWith('#') ? fragment.slice(1) : fragment;
  for (const part of raw.split('&')) {
    const eq = part.indexOf('=');
    if (eq < 0 || part.slice(0, eq) !== 'token') continue;
    let value: string;
    try {
      value = decodeURIComponent(part.slice(eq + 1));
    } catch {
      return null;
    }
    return TOKEN.test(value) ? value : null;
  }
  return null;
}
