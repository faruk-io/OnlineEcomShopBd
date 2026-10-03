import { ApiError } from '../../core/models/api.models';
import { fromLocalInput, humanize, imageProblem, niceMax, normalizeFieldErrors, problemText, scaleBars, toLocalInput } from './admin.util';

describe('scaleBars (dashboard chart scaling)', () => {
  const pts = (...revenue: number[]) => revenue.map((r, i) => ({ date: `2026-10-0${i + 1}`, revenue: r, orders: r > 0 ? 1 : 0 }));

  it('rounds the axis up to a nice maximum', () => {
    expect(niceMax(0)).toBe(1);
    expect(niceMax(730)).toBe(1000);
    expect(niceMax(1000)).toBe(1000);
    expect(niceMax(1800)).toBe(2000);
    expect(niceMax(2300)).toBe(2500);
    expect(niceMax(4100)).toBe(5000);
    expect(niceMax(0.3)).toBeCloseTo(0.5);
  });

  it('maps revenue linearly onto the plot height, bottom-aligned', () => {
    const c = scaleBars(pts(500, 1000, 0), 300, 100);
    expect(c.max).toBe(1000);
    expect(c.bars.map((b) => b.height)).toEqual([50, 100, 0]);
    expect(c.bars.map((b) => b.y)).toEqual([50, 0, 100]);
    expect(c.bars.every((b) => b.y + b.height === 100)).toBe(true);
  });

  it('spreads bars evenly with gaps and keeps them inside the width', () => {
    const c = scaleBars(pts(1, 1, 1, 1), 400, 100, 0.2);
    expect(c.bars.map((b) => b.x)).toEqual([10, 110, 210, 310]);
    expect(c.bars[0].width).toBe(80);
    expect(c.bars[3].x + c.bars[3].width).toBeLessThanOrEqual(400);
  });

  it('keeps tiny non-zero days visible and never exceeds the plot', () => {
    const c = scaleBars(pts(1, 1_000_000), 100, 200);
    expect(c.bars[0].height).toBe(2);
    expect(c.bars[1].height).toBeLessThanOrEqual(200);
  });

  it('handles an empty or all-zero series', () => {
    expect(scaleBars([], 100, 100).bars).toEqual([]);
    const z = scaleBars(pts(0, 0), 100, 100);
    expect(z.bars.map((b) => b.height)).toEqual([0, 0]);
    expect(z.ticks.map((t) => t.y)).toEqual([100, 50, 0]);
  });

  it('labels bars MM-DD', () => {
    expect(scaleBars(pts(5), 100, 100).bars[0].label).toBe('10-01');
  });
});

describe('admin helpers', () => {
  it('normalises problem-details keys to camelCase paths and keeps the first message', () => {
    const e: ApiError = { status: 400, title: 'x', detail: null, traceId: null, errors: { Name: ['Required', 'Too long'], 'Specifications[0].Key': ['Bad'], DiscountPrice: ['Lower'] } };
    expect(normalizeFieldErrors(e)).toEqual({ name: 'Required', 'specifications[0].key': 'Bad', discountPrice: 'Lower' });
    expect(normalizeFieldErrors(undefined)).toEqual({});
  });

  it('prefers the API detail for conflicts', () => {
    const e: ApiError = { status: 409, title: 'Conflict', detail: 'Category still has products.', errors: null, traceId: null };
    expect(problemText(e)).toBe('Category still has products.');
    expect(problemText('nope', 'fallback')).toBe('fallback');
  });

  it('humanizes enum names', () => {
    expect(humanize('ReadyForPickup')).toBe('Ready for pickup');
    expect(humanize('FixedAmount')).toBe('Fixed amount');
  });

  it('round-trips datetime-local values', () => {
    const iso = '2026-12-31T10:30:00.000Z';
    expect(fromLocalInput(toLocalInput(iso))).toBe(iso);
    expect(fromLocalInput('')).toBeNull();
    expect(toLocalInput(null)).toBe('');
  });

  it('pre-validates image uploads', () => {
    expect(imageProblem({ name: 'a.png', type: 'image/png', size: 100 })).toBeNull();
    expect(imageProblem({ name: 'a.svg', type: 'image/svg+xml', size: 100 })).toContain('PNG, JPEG, GIF or WebP');
    expect(imageProblem({ name: 'big.jpg', type: 'image/jpeg', size: 5 * 1024 * 1024 + 1 })).toContain('5 MB');
  });
});
