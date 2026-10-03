import { expect, type APIRequestContext } from '@playwright/test';

/** The E2E API host (TEST ONLY) keeps every outgoing email in memory and serves it here. */
const API = `http://localhost:${process.env['E2E_API_PORT'] ?? '5180'}`;

export interface CapturedEmail {
  to: string;
  subject: string;
  textBody: string;
  htmlBody: string | null;
}

/** Emails sent to this address so far, oldest first. */
export async function emailsTo(request: APIRequestContext, to: string): Promise<CapturedEmail[]> {
  const res = await request.get(`${API}/__e2e/emails`, { params: { to } });
  expect(res.ok(), 'GET /__e2e/emails').toBe(true);
  return (await res.json()) as CapturedEmail[];
}

const LINK = /https?:\/\/[^\s"<]+#token=[A-Za-z0-9_-]+/;

/** First link in the mail whose path is `/<path>` (e.g. `verify-email`, `reset-password`), or null. */
function linkIn(mail: CapturedEmail, path: string): string | null {
  for (const body of [mail.textBody, mail.htmlBody ?? '']) {
    const m = LINK.exec(body.replace(/&amp;/g, '&'));
    if (m && new URL(m[0]).pathname === `/${path}`) return m[0];
  }
  return null;
}

/**
 * Waits (polling, no fixed sleeps) until an email to `to` contains a `/<path>#token=…` link and returns the newest such link.
 * `previous` lets a caller ask for a link that is different from one it already used (e.g. after a resend).
 */
export async function waitForLink(request: APIRequestContext, to: string, path: 'verify-email' | 'reset-password', previous: string[] = []): Promise<string> {
  let found = '';
  await expect
    .poll(
      async () => {
        const links = (await emailsTo(request, to)).map((m) => linkIn(m, path)).filter((l): l is string => !!l && !previous.includes(l));
        found = links.at(-1) ?? '';
        return found;
      },
      { message: `an email to ${to} with a ${path} link`, timeout: 20_000 },
    )
    .not.toBe('');
  return found;
}

/** The one-time token of an emailed link (what the page reads from the URL fragment). */
export const tokenOf = (link: string): string => new URL(link).hash.replace(/^#token=/, '');
