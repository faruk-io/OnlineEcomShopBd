import { buildCompareGroups } from './compare.page';

const product = (groups: Record<string, Record<string, string>>) => ({
  specifications: Object.entries(groups).map(([group, items]) => ({ group, items: Object.entries(items).map(([key, value]) => ({ key, value })) })),
});

describe('buildCompareGroups', () => {
  it('aligns values by key and flags differing rows', () => {
    const groups = buildCompareGroups([
      product({ General: { Socket: 'AM5', Cores: '6' }, Power: { TDP: '65 W' } }),
      product({ General: { Socket: 'AM5', Cores: '8' }, Power: { TDP: '120 W' } }),
    ]);
    expect(groups.map((g) => g.group)).toEqual(['General', 'Power']);
    const general = groups[0].rows;
    expect(general.find((r) => r.key === 'Socket')).toEqual({ key: 'Socket', values: ['AM5', 'AM5'], differs: false });
    expect(general.find((r) => r.key === 'Cores')).toEqual({ key: 'Cores', values: ['6', '8'], differs: true });
  });

  it('fills missing specs with an em dash and treats that as a difference', () => {
    const groups = buildCompareGroups([product({ General: { Socket: 'AM5' } }), product({ General: { Socket: 'AM5', Cooler: 'Included' } })]);
    expect(groups[0].rows.find((r) => r.key === 'Cooler')).toEqual({ key: 'Cooler', values: ['—', 'Included'], differs: true });
  });

  it('includes groups that only some products have, in first-seen order', () => {
    const groups = buildCompareGroups([product({ A: { x: '1' } }), product({ B: { y: '2' } })]);
    expect(groups.map((g) => g.group)).toEqual(['A', 'B']);
    expect(groups[1].rows[0].values).toEqual(['—', '2']);
  });

  it('handles no products', () => {
    expect(buildCompareGroups([])).toEqual([]);
  });
});
