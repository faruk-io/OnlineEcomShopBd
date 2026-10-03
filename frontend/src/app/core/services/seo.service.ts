import { DOCUMENT } from '@angular/common';
import { Injectable, RESPONSE_INIT, inject } from '@angular/core';
import { Meta, Title } from '@angular/platform-browser';
import { Router } from '@angular/router';
import { SITE_NAME } from '../config';

export interface PageMeta {
  title: string;
  description?: string;
  image?: string | null;
  /** Path (with optional query) used for the canonical URL; defaults to the current path without query. */
  canonicalPath?: string;
  type?: 'website' | 'product';
  /** Keep filtered / paginated duplicates out of the index. */
  noindex?: boolean;
  jsonLd?: object | null;
}

/** Title, description, Open Graph, canonical, robots and JSON-LD — all written during SSR so crawlers see them. */
@Injectable({ providedIn: 'root' })
export class SeoService {
  private readonly title = inject(Title);
  private readonly meta = inject(Meta);
  private readonly doc = inject(DOCUMENT);
  private readonly router = inject(Router);
  private readonly responseInit = inject(RESPONSE_INIT, { optional: true });

  set(page: PageMeta): void {
    const fullTitle = page.title.includes(SITE_NAME) ? page.title : `${page.title} | ${SITE_NAME}`;
    this.title.setTitle(fullTitle);

    const description = page.description ?? 'Buy computers, laptops, components and accessories online in Bangladesh.';
    this.tag('description', description);
    this.tag('robots', page.noindex ? 'noindex,follow' : 'index,follow');
    this.prop('og:title', fullTitle);
    this.prop('og:description', description);
    this.prop('og:type', page.type ?? 'website');
    this.prop('og:site_name', SITE_NAME);
    this.tag('twitter:card', page.image ? 'summary_large_image' : 'summary');

    const origin = this.origin();
    const path = page.canonicalPath ?? this.router.url.split('?')[0].split('#')[0];
    const canonical = `${origin}${path}`;
    this.setCanonical(canonical);
    this.prop('og:url', canonical);

    if (page.image) this.prop('og:image', page.image.startsWith('http') ? page.image : `${origin}${page.image}`);
    else this.meta.removeTag(`property='og:image'`);

    this.setJsonLd(page.jsonLd ?? null);
  }

  /** Sets the HTTP status of the server-rendered response (no-op in the browser). */
  setStatus(status: number): void {
    if (this.responseInit) (this.responseInit as { status?: number }).status = status;
  }

  /** Absolute URL for a site-relative path (for JSON-LD / Open Graph). */
  absoluteUrl(path: string): string {
    return path.startsWith('http') ? path : `${this.origin()}${path}`;
  }

  private origin(): string {
    try {
      return this.doc.location?.origin ?? '';
    } catch {
      return '';
    }
  }

  private tag(name: string, content: string): void {
    this.meta.updateTag({ name, content });
  }

  private prop(property: string, content: string): void {
    this.meta.updateTag({ property, content });
  }

  private setCanonical(href: string): void {
    let link = this.doc.head.querySelector<HTMLLinkElement>('link[rel="canonical"]');
    if (!link) {
      link = this.doc.createElement('link');
      link.setAttribute('rel', 'canonical');
      this.doc.head.appendChild(link);
    }
    link.setAttribute('href', href);
  }

  private setJsonLd(data: object | null): void {
    const id = 'ld-json-page';
    this.doc.getElementById(id)?.remove();
    if (!data) return;
    const script = this.doc.createElement('script');
    script.id = id;
    script.type = 'application/ld+json';
    // "<" is escaped so product text can never close the script element.
    script.text = JSON.stringify(data).replace(/</g, '\\u003c');
    this.doc.head.appendChild(script);
  }
}
