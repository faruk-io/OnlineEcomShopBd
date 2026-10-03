import { TestBed } from '@angular/core/testing';
import { CompareService } from './compare.service';
import { ToastService } from './toast.service';

describe('CompareService', () => {
  let svc: CompareService;
  let toast: ToastService;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({});
    svc = TestBed.inject(CompareService);
    toast = TestBed.inject(ToastService);
  });

  it('toggles products on and off', () => {
    expect(svc.toggle('a')).toBe(true);
    expect(svc.has('a')).toBe(true);
    svc.toggle('a');
    expect(svc.has('a')).toBe(false);
  });

  it('allows at most four products and explains why the fifth was refused', () => {
    ['a', 'b', 'c', 'd'].forEach((s) => svc.toggle(s));
    expect(svc.toggle('e')).toBe(false);
    expect(svc.slugs()).toEqual(['a', 'b', 'c', 'd']);
    expect(toast.toasts().at(-1)?.message).toMatch(/up to 4/);
  });

  it('persists and restores (truncating tampered storage to the limit)', () => {
    svc.toggle('a');
    expect(JSON.parse(localStorage.getItem('tb.compare.v1')!)).toEqual(['a']);
    localStorage.setItem('tb.compare.v1', JSON.stringify(['1', '2', '3', '4', '5', 7]));
    svc.init();
    expect(svc.slugs()).toEqual(['1', '2', '3', '4']);
    localStorage.setItem('tb.compare.v1', '{not json');
    svc.init();
    expect(svc.slugs()).toEqual([]);
  });

  it('remove and clear', () => {
    svc.toggle('a');
    svc.toggle('b');
    svc.remove('a');
    expect(svc.slugs()).toEqual(['b']);
    svc.clear();
    expect(svc.count()).toBe(0);
  });
});
