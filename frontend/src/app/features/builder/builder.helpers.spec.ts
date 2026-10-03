import { BuildLine, BuildReport, BuilderSlot } from '../../core/models/api.models';
import { productItem } from '../../core/testing/test-helpers';
import {
  addPart, buildStatus, issuesBySeverity, itemsFromReport, partitionLines, pickerFilters, previewLine, psuMeter, removePart, sanitizeItems,
  setQuantity, shareUrl, slotState, specsFromFilters, toRequestItems,
} from './builder.helpers';

const single: BuilderSlot = { slot: 'Cpu', label: 'Processor', categorySlug: 'processor', required: true, allowMultiple: false };
const multi: BuilderSlot = { slot: 'Ram', label: 'Memory (RAM)', categorySlug: 'ram', required: true, allowMultiple: true };

const line = (over: Partial<BuildLine> = {}): BuildLine => ({
  slot: 'Cpu', productId: 1, name: 'CPU', slug: 'cpu', sku: 'S', imageUrl: null, brandName: 'AMD', listPrice: 100, unitPrice: 90, quantity: 1, lineTotal: 90,
  stockStatus: 'InStock', purchasable: true, ...over,
});
const report = (over: Partial<BuildReport> = {}): BuildReport => ({
  lines: [], total: 0, savings: 0, allPurchasable: true,
  compatibility: { isComplete: false, isCompatible: true, estimatedWatts: 0, recommendedPsuWatts: 0, psuWatts: null, issues: [], slotFilters: {} }, ...over,
});

describe('builder helpers', () => {
  it('maps items to explicit request bodies', () => {
    expect(toRequestItems([{ slot: 'Cpu', productId: 5 }, { slot: 'Ram', productId: 6, quantity: 2 }])).toEqual([
      { slot: 'Cpu', productId: 5, quantity: 1 }, { slot: 'Ram', productId: 6, quantity: 2 },
    ]);
  });

  it('replaces single-slot parts and merges multi-slot parts', () => {
    let items = addPart([], single, 1);
    items = addPart(items, single, 2);
    expect(items).toEqual([{ slot: 'Cpu', productId: 2, quantity: 1 }]);
    items = addPart(items, multi, 10);
    items = addPart(items, multi, 10);
    items = addPart(items, multi, 11);
    expect(items.filter((i) => i.slot === 'Ram')).toEqual([{ slot: 'Ram', productId: 10, quantity: 2 }, { slot: 'Ram', productId: 11, quantity: 1 }]);
  });

  it('removes parts and clamps quantities to 1..10', () => {
    const items = [{ slot: 'Ram' as const, productId: 10, quantity: 2 }, { slot: 'Cpu' as const, productId: 1, quantity: 1 }];
    expect(setQuantity(items, 'Ram', 10, 99)[0].quantity).toBe(10);
    expect(setQuantity(items, 'Ram', 10, 0)[0].quantity).toBe(1);
    expect(removePart(items, 'Ram', 10)).toEqual([{ slot: 'Cpu', productId: 1, quantity: 1 }]);
  });

  it('sanitises untrusted stored data', () => {
    expect(sanitizeItems('nope')).toEqual([]);
    expect(
      sanitizeItems([{ slot: 'Cpu', productId: 3, quantity: 50 }, { slot: 'Cpu', productId: 3 }, { slot: 'Bogus', productId: 4 }, { slot: 'Ram', productId: -1 }, null, { slot: 'Gpu', productId: 7 }], [single, { ...single, slot: 'Gpu' }]),
    ).toEqual([{ slot: 'Cpu', productId: 3, quantity: 10 }, { slot: 'Gpu', productId: 7, quantity: 1 }]);
  });

  it('turns report lines into items', () => {
    expect(itemsFromReport(report({ lines: [line({ productId: 4, quantity: 2 })] }))).toEqual([{ slot: 'Cpu', productId: 4, quantity: 2 }]);
  });

  it('parses slot filters into catalog spec filters, grouping values per key', () => {
    expect(specsFromFilters(['Socket:AM4', 'RAM Type:DDR4', 'Socket:AM5', 'Socket:AM4', 'broken', ':x', 'k:'])).toEqual({ Socket: ['AM4', 'AM5'], 'RAM Type': ['DDR4'] });
    expect(specsFromFilters(undefined)).toEqual({});
  });

  it('builds picker filters with or without the compatibility specs', () => {
    const q = { q: ' ryzen ', compatibleOnly: true, inStock: true, sort: 'price_asc' as const, page: 2 };
    const on = pickerFilters(q, ['Socket:AM4']);
    expect(on).toMatchObject({ q: 'ryzen', inStock: true, sort: 'price_asc', page: 2, specs: { Socket: ['AM4'] } });
    expect(pickerFilters({ ...q, compatibleOnly: false }, ['Socket:AM4']).specs).toEqual({});
  });

  it('derives the build status', () => {
    expect(buildStatus(0, null)).toBe('empty');
    expect(buildStatus(1, null)).toBe('incomplete');
    expect(buildStatus(1, report())).toBe('incomplete');
    const ok = report(); ok.compatibility.isComplete = true;
    expect(buildStatus(3, ok)).toBe('compatible');
    const bad = report(); bad.compatibility.isCompatible = false;
    expect(buildStatus(3, bad)).toBe('attention');
  });

  it('groups issues and flags slot rows (errors beat warnings, info is ignored)', () => {
    const issues = [
      { severity: 'Warning' as const, code: 'W', message: 'w', slots: ['Cpu' as const, 'Cooler' as const] },
      { severity: 'Error' as const, code: 'E', message: 'e', slots: ['Cpu' as const] },
      { severity: 'Info' as const, code: 'I', message: 'i', slots: ['Gpu' as const] },
    ];
    expect(issuesBySeverity(issues, 'Error').map((i) => i.code)).toEqual(['E']);
    expect(slotState(issues, 'Cpu')).toBe('error');
    expect(slotState(issues, 'Cooler')).toBe('warn');
    expect(slotState(issues, 'Gpu')).toBeNull();
    expect(slotState(undefined, 'Cpu')).toBeNull();
  });

  it('partitions purchasable lines and builds cartable products from server prices', () => {
    const { buy, skipped } = partitionLines([line(), line({ productId: 2, purchasable: false, stockStatus: 'OutOfStock' })]);
    expect(buy.map((l) => l.productId)).toEqual([1]);
    expect(skipped.map((l) => l.productId)).toEqual([2]);
    expect(previewLine('Cpu', productItem({ id: 9, effectivePrice: 100, stockStatus: 'OutOfStock' }), 2)).toMatchObject({ productId: 9, lineTotal: 200, purchasable: false });
  });

  it('computes share links and the PSU meter', () => {
    expect(shareUrl('https://x.test', 'AB 1')).toBe('https://x.test/builder?b=AB%201');
    expect(psuMeter(300, 650, 450)).toEqual({ max: 650, pct: 46, level: 'ok' });
    expect(psuMeter(560, 650, 450).level).toBe('tight');
    expect(psuMeter(500, 400, 600).level).toBe('low');
    expect(psuMeter(300, null, 450)).toEqual({ max: 450, pct: 67, level: 'none' });
  });
});
