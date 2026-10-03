import { createHmac } from 'node:crypto';

const ALPHABET = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567';

function base32Decode(text: string): Buffer {
  const clean = text.replace(/[\s=-]/g, '').toUpperCase();
  let bits = 0;
  let value = 0;
  const out: number[] = [];
  for (const ch of clean) {
    const i = ALPHABET.indexOf(ch);
    if (i < 0) throw new Error(`Not a base32 secret: "${text}"`);
    value = (value << 5) | i;
    bits += 5;
    if (bits >= 8) {
      out.push((value >>> (bits - 8)) & 0xff);
      bits -= 8;
    }
  }
  return Buffer.from(out);
}

/**
 * RFC 6238 TOTP (SHA-1, 6 digits, 30 s) for the given base32 secret, `offset` steps away from now.
 * The server accepts steps -1..+1 and each step only ONCE, so a test that needs several codes in a row must use ascending offsets
 * (-1 to turn MFA on, 0 to sign in, +1 for a later action).
 */
export function totp(secretBase32: string, offset = 0, nowMs: number = Date.now()): string {
  const step = Math.floor(nowMs / 1000 / 30) + offset;
  const counter = Buffer.alloc(8);
  counter.writeBigUInt64BE(BigInt(step));
  const hash = createHmac('sha1', base32Decode(secretBase32)).update(counter).digest();
  const o = hash[hash.length - 1]! & 0x0f;
  const bin = ((hash[o]! & 0x7f) << 24) | (hash[o + 1]! << 16) | (hash[o + 2]! << 8) | hash[o + 3]!;
  return (bin % 1_000_000).toString().padStart(6, '0');
}
