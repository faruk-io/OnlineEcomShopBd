import { DOCUMENT } from '@angular/common';
import { TestBed } from '@angular/core/testing';
import { Title } from '@angular/platform-browser';
import { provideRouter } from '@angular/router';
import { SeoService } from './seo.service';

describe('SeoService', () => {
  let seo: SeoService;
  let doc: Document;

  const meta = (sel: string) => doc.head.querySelector(sel)?.getAttribute('content');

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideRouter([])] });
    seo = TestBed.inject(SeoService);
    doc = TestBed.inject(DOCUMENT);
  });

  it('writes title, description, Open Graph, robots and canonical', () => {
    seo.set({ title: 'AMD Ryzen 5 5600 Price in Bangladesh', description: 'Buy it', image: '/images/x.svg', canonicalPath: '/product/ryzen', type: 'product' });

    expect(TestBed.inject(Title).getTitle()).toBe('AMD Ryzen 5 5600 Price in Bangladesh | TechBazar BD');
    expect(meta('meta[name="description"]')).toBe('Buy it');
    expect(meta('meta[name="robots"]')).toBe('index,follow');
    expect(meta('meta[property="og:type"]')).toBe('product');
    expect(meta('meta[property="og:title"]')).toContain('Ryzen');
    expect(meta('meta[property="og:image"]')).toMatch(/\/images\/x\.svg$/);
    expect(doc.head.querySelector('link[rel="canonical"]')?.getAttribute('href')).toMatch(/\/product\/ryzen$/);
  });

  it('does not duplicate the site name and supports noindex', () => {
    seo.set({ title: 'TechBazar BD — Home', noindex: true });
    expect(TestBed.inject(Title).getTitle()).toBe('TechBazar BD — Home');
    expect(meta('meta[name="robots"]')).toBe('noindex,follow');
  });

  it('reuses one canonical link and drops a stale og:image', () => {
    seo.set({ title: 'A', image: '/a.svg', canonicalPath: '/a' });
    seo.set({ title: 'B', canonicalPath: '/b' });
    expect(doc.head.querySelectorAll('link[rel="canonical"]')).toHaveLength(1);
    expect(doc.head.querySelector('meta[property="og:image"]')).toBeNull();
  });

  it('emits JSON-LD once per page and escapes "<" so product text cannot break out of the script tag', () => {
    seo.set({ title: 'P', jsonLd: { '@type': 'Product', name: '</script><script>alert(1)</script>' } });
    seo.set({ title: 'P2', jsonLd: { '@type': 'Product', name: 'x</script>y' } });
    const scripts = doc.head.querySelectorAll('script[type="application/ld+json"]');
    expect(scripts).toHaveLength(1);
    expect(scripts[0].textContent).not.toContain('</script>');
    expect(JSON.parse(scripts[0].textContent!).name).toBe('x</script>y');
    seo.set({ title: 'P3' });
    expect(doc.head.querySelector('script[type="application/ld+json"]')).toBeNull();
  });
});
