import { TestBed } from '@angular/core/testing';
import { Component } from '@angular/core';
import { QR_QUIET_ZONE, qrMatrix, qrPath } from './qr-code';
import { QrCodeComponent } from './qr-code.component';

const URI = 'otpauth://totp/TechBazar%20BD:admin%40techbazar.example?secret=JBSWY3DPEHPK3PXPJBSWY3DPEHPK3PXP&issuer=TechBazar%20BD&algorithm=SHA1&digits=6&period=30';

describe('qr code', () => {
  it('is deterministic and square', () => {
    const a = qrMatrix('hello');
    const b = qrMatrix('hello');
    expect(a).toEqual(b);
    expect(a.rows.length).toBe(a.size);
    expect(a.rows.every((r) => r.length === a.size)).toBe(true);
  });

  it('uses version 1 (21 modules) for a very short string and grows for longer data', () => {
    expect(qrMatrix('hi').size).toBe(21);
    expect(qrMatrix(URI).size).toBeGreaterThan(21);
    expect((qrMatrix(URI).size - 17) % 4).toBe(0);   // valid QR sizes are 21, 25, 29 ...
  });

  it('draws the three finder patterns', () => {
    const m = qrMatrix(URI);
    for (const [r, c] of [[0, 0], [0, m.size - 7], [m.size - 7, 0]]) {
      expect(m.rows[r][c]).toBe(true);
      expect(m.rows[r + 3][c + 3]).toBe(true);
      expect(m.rows[r + 1][c + 1]).toBe(false);
    }
  });

  it('offsets the path by the quiet zone and never paints inside it', () => {
    const m = qrMatrix('hi');
    const d = qrPath(m);
    expect(d.startsWith(`M${QR_QUIET_ZONE} ${QR_QUIET_ZONE}h7`)).toBe(true);   // top-left finder row: 7 dark modules
    const coords = [...d.matchAll(/M(\d+) (\d+)h(\d+)/g)].map((x) => [+x[1], +x[2], +x[3]]);
    expect(coords.every(([x, y, w]) => x >= QR_QUIET_ZONE && y >= QR_QUIET_ZONE && x + w <= m.size + QR_QUIET_ZONE && y < m.size + QR_QUIET_ZONE)).toBe(true);
  });

  it('encodes a ~120 char otpauth URI without throwing', () => {
    expect(URI.length).toBeGreaterThan(120);
    expect(() => qrMatrix(URI)).not.toThrow();
  });

  it('renders an svg with a path, an accessible label without the secret, and no <img>', () => {
    @Component({ imports: [QrCodeComponent], template: `<app-qr-code [value]="v" label="Authenticator QR code" />` })
    class Host { v = URI; }
    const f = TestBed.createComponent(Host);
    f.detectChanges();
    const root = f.nativeElement as HTMLElement;
    const svg = root.querySelector('svg')!;
    expect(svg.getAttribute('role')).toBe('img');
    expect(svg.getAttribute('aria-label')).toBe('Authenticator QR code');
    expect(svg.getAttribute('aria-label')).not.toContain('JBSWY');
    expect(svg.querySelector('path')!.getAttribute('d')!.length).toBeGreaterThan(50);
    expect(root.querySelector('img')).toBeNull();
    expect(root.innerHTML).not.toContain('JBSWY3DPEHPK3PXP');
  });
});
