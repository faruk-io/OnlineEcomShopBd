import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { QR_QUIET_ZONE, qrMatrix, qrPath } from './qr-code';

/**
 * Draws a QR code entirely in the browser (or during SSR) from `value`: nothing is sent anywhere and no image URL contains
 * the data. Always dark-on-white (independent of theme) so scanners work. The label must NOT include the encoded value.
 */
@Component({
  selector: 'app-qr-code',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (geometry(); as g) {
      <svg role="img" [attr.aria-label]="label()" [attr.viewBox]="'0 0 ' + g.total + ' ' + g.total" shape-rendering="crispEdges" xmlns="http://www.w3.org/2000/svg">
        <rect width="100%" height="100%" fill="#ffffff" />
        <path [attr.d]="g.path" fill="#000000" />
      </svg>
    }
  `,
  styles: `:host { display: inline-block; line-height: 0; background: #fff; border-radius: var(--radius); } svg { width: 100%; height: auto; display: block; }`,
})
export class QrCodeComponent {
  readonly value = input.required<string>();
  readonly label = input('QR code');

  protected readonly geometry = computed(() => {
    try {
      const m = qrMatrix(this.value());
      return { total: m.size + QR_QUIET_ZONE * 2, path: qrPath(m) };
    } catch {
      return null;
    }
  });
}
