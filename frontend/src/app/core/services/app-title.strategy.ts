import { Injectable, inject } from '@angular/core';
import { Title } from '@angular/platform-browser';
import { RouterStateSnapshot, TitleStrategy } from '@angular/router';
import { SITE_NAME } from '../config';

/** Static routes declare `title: 'Cart'`; dynamic pages (product/category) set theirs through SeoService. */
@Injectable({ providedIn: 'root' })
export class AppTitleStrategy extends TitleStrategy {
  private readonly title = inject(Title);

  override updateTitle(snapshot: RouterStateSnapshot): void {
    const t = this.buildTitle(snapshot);
    this.title.setTitle(t ? `${t} | ${SITE_NAME}` : `${SITE_NAME} — Computers, Laptops & Electronics in Bangladesh`);
  }
}
