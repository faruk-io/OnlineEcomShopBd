import qrcode from 'qrcode-generator';

export interface QrMatrix {
  /** Modules per side, without the quiet zone. */
  size: number;
  rows: boolean[][];
}

/** Standard quiet zone (modules of white around the symbol) so phone scanners lock on reliably. */
export const QR_QUIET_ZONE = 4;

/** Encodes `text` locally (error correction M, smallest version that fits). Throws only when the text exceeds QR capacity. */
export function qrMatrix(text: string): QrMatrix {
  const qr = qrcode(0, 'M');
  qr.addData(text, 'Byte');
  qr.make();
  const size = qr.getModuleCount();
  const rows: boolean[][] = [];
  for (let r = 0; r < size; r++) {
    const row: boolean[] = [];
    for (let c = 0; c < size; c++) row.push(qr.isDark(r, c));
    rows.push(row);
  }
  return { size, rows };
}

/** One SVG path for all dark modules, merged into horizontal runs; coordinates include the quiet zone offset. */
export function qrPath(matrix: QrMatrix, quiet = QR_QUIET_ZONE): string {
  const parts: string[] = [];
  matrix.rows.forEach((row, y) => {
    let x = 0;
    while (x < row.length) {
      if (!row[x]) {
        x++;
        continue;
      }
      let end = x;
      while (end < row.length && row[end]) end++;
      parts.push(`M${x + quiet} ${y + quiet}h${end - x}v1h-${end - x}z`);
      x = end;
    }
  });
  return parts.join('');
}
