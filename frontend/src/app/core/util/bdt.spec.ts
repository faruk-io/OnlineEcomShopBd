import { BdtPipe } from './bdt.pipe';
import { formatBdt, groupIndian, warrantyText } from './bdt';

describe('formatBdt', () => {
  it.each([
    [0, '৳0'],
    [999, '৳999'],
    [1000, '৳1,000'],
    [12500, '৳12,500'],
    [100000, '৳1,00,000'],
    [125000, '৳1,25,000'],
    [1234567, '৳12,34,567'],
    [12345678, '৳1,23,45,678'],
  ])('groups %d as %s (Bangladeshi lakh/crore style)', (amount, expected) => {
    expect(formatBdt(amount)).toBe(expected);
  });

  it('keeps paisa only when present', () => {
    expect(formatBdt(1250.5)).toBe('৳1,250.50');
    expect(formatBdt(1250.0)).toBe('৳1,250');
  });

  it('can omit the symbol and handles negatives / empty values', () => {
    expect(formatBdt(125000, false)).toBe('1,25,000');
    expect(formatBdt(-5000)).toBe('-৳5,000');
    expect(formatBdt(null)).toBe('');
    expect(formatBdt(undefined)).toBe('');
    expect(formatBdt(Number.NaN)).toBe('');
  });

  it('groupIndian only touches digit strings', () => {
    expect(groupIndian('12')).toBe('12');
    expect(groupIndian('1234')).toBe('1,234');
  });

  it('is exposed as a pipe', () => {
    const pipe = new BdtPipe();
    expect(pipe.transform(125000)).toBe('৳1,25,000');
    expect(pipe.transform(125000, false)).toBe('1,25,000');
  });
});

describe('warrantyText', () => {
  it.each([
    [0, 'No warranty'],
    [12, '1 year'],
    [36, '3 years'],
    [18, '18 months'],
    [120, 'Lifetime (limited)'],
  ])('%d months -> %s', (months, text) => expect(warrantyText(months)).toBe(text));
});
