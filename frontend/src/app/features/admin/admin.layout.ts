import { ChangeDetectionStrategy, Component, ViewEncapsulation, inject } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { SeoService } from '../../core/services/seo.service';

/** Admin shell: sidebar (top strip on phones) + content. Also hosts the shared `.adm-*` styles used by every admin page. */
@Component({
  selector: 'app-admin-layout',
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  changeDetection: ChangeDetectionStrategy.OnPush,
  encapsulation: ViewEncapsulation.None,
  template: `
    <div class="adm-shell">
      <nav class="adm-nav" aria-label="Admin">
        <p class="adm-brand">Admin</p>
        <ul>
          <li><a routerLink="/admin" routerLinkActive="on" [routerLinkActiveOptions]="{ exact: true }" ariaCurrentWhenActive="page">Dashboard</a></li>
          <li><a routerLink="/admin/orders" routerLinkActive="on" ariaCurrentWhenActive="page">Orders</a></li>
          <li><a routerLink="/admin/products" routerLinkActive="on" ariaCurrentWhenActive="page">Products</a></li>
          <li><a routerLink="/admin/categories" routerLinkActive="on" ariaCurrentWhenActive="page">Categories</a></li>
          <li><a routerLink="/admin/brands" routerLinkActive="on" ariaCurrentWhenActive="page">Brands</a></li>
          <li><a routerLink="/admin/coupons" routerLinkActive="on" ariaCurrentWhenActive="page">Coupons</a></li>
          <li class="back"><a routerLink="/">Back to store</a></li>
        </ul>
      </nav>
      <main id="admin-main" class="adm-main" tabindex="-1"><router-outlet /></main>
    </div>
  `,
  styles: `
    .adm-shell { display: grid; gap: 0; max-width: 1500px; margin-inline: auto; min-height: 60vh; }
    .adm-nav { background: var(--ink); color: #e2e8f0; padding: .5rem 1rem; }
    .adm-brand { display: none; margin: 0 0 .5rem; font-weight: 800; letter-spacing: .06em; text-transform: uppercase; font-size: .8rem; color: #94a3b8; }
    .adm-nav ul { list-style: none; margin: 0; padding: 0; display: flex; gap: .25rem; overflow-x: auto; }
    .adm-nav a { display: block; white-space: nowrap; padding: .5rem .8rem; border-radius: var(--radius); color: #e2e8f0; font-weight: 500; }
    .adm-nav a:hover { background: rgb(255 255 255 / .1); text-decoration: none; }
    .adm-nav a.on { background: var(--primary); color: #fff; font-weight: 700; }
    .adm-nav .back { margin-left: auto; } .adm-nav .back a { color: #cbd5e1; }
    .adm-main { padding: 1rem; min-width: 0; }
    @media (min-width: 900px) {
      .adm-shell { grid-template-columns: 14rem minmax(0, 1fr); align-items: start; }
      .adm-nav { position: sticky; top: var(--header-h); min-height: calc(100vh - var(--header-h)); padding: 1rem .75rem; }
      .adm-brand { display: block; }
      .adm-nav ul { flex-direction: column; overflow: visible; }
      .adm-nav .back { margin: 1rem 0 0; border-top: 1px solid rgb(255 255 255 / .15); padding-top: 1rem; }
      .adm-main { padding: 1.5rem; }
    }

    .adm-head { display: flex; flex-wrap: wrap; align-items: center; justify-content: space-between; gap: .75rem; margin-bottom: 1rem; }
    .adm-head h1 { margin: 0; }
    .adm-toolbar { display: flex; flex-wrap: wrap; gap: .5rem; align-items: end; margin-bottom: 1rem; }
    .adm-toolbar .field { margin: 0; min-width: 10rem; }
    .adm-toolbar .grow { flex: 1 1 14rem; }
    .adm-scroll { overflow-x: auto; border: 1px solid var(--border); border-radius: var(--radius-lg); background: var(--surface); }
    .adm-table { min-width: 36rem; font-size: .92rem; }
    .adm-table caption { text-align: left; padding: .75rem 1rem; font-weight: 700; color: var(--ink); }
    .adm-table th, .adm-table td { padding: .6rem .8rem; text-align: left; border-top: 1px solid var(--border); vertical-align: middle; }
    .adm-table thead th { background: var(--surface-2); font-size: .8rem; text-transform: uppercase; letter-spacing: .03em; color: var(--muted); border-top: 0; }
    .adm-table td.num, .adm-table th.num { text-align: right; font-variant-numeric: tabular-nums; }
    .adm-table .actions { white-space: nowrap; text-align: right; }
    .adm-table .actions > * { margin-left: .25rem; }
    .adm-thumb { width: 44px; height: 44px; object-fit: contain; border: 1px solid var(--border); border-radius: 6px; background: #fff; }
    .adm-badge { display: inline-block; padding: .1rem .55rem; border-radius: 999px; font-size: .78rem; font-weight: 700; background: var(--surface-2); color: var(--muted); border: 1px solid var(--border); white-space: nowrap; }
    .adm-badge.ok { background: var(--success-bg); color: var(--success); border-color: transparent; }
    .adm-badge.warn { background: var(--warn-bg); color: var(--warn); border-color: transparent; }
    .adm-badge.bad { background: var(--danger-bg); color: var(--danger); border-color: transparent; }
    .adm-badge.info { background: var(--primary-weak); color: var(--primary-strong); border-color: transparent; }
    .adm-grid { display: grid; gap: 1rem; }
    .adm-grid.cols-2 { grid-template-columns: repeat(auto-fit, minmax(min(100%, 22rem), 1fr)); }
    .adm-form-grid { display: grid; gap: 1rem; grid-template-columns: repeat(auto-fit, minmax(min(100%, 15rem), 1fr)); }
    .adm-form-grid .wide { grid-column: 1 / -1; }
    textarea.input { min-height: 5rem; resize: vertical; }
    .adm-check { display: flex; align-items: center; gap: .5rem; font-weight: 600; min-height: 2.6rem; }
    .adm-check input { width: 1.1rem; height: 1.1rem; }
    .adm-pager { margin-top: 1rem; }
    .btn-danger-outline { border-color: var(--danger); color: var(--danger); background: var(--surface); }
    .btn-danger-outline:hover:not(:disabled) { background: var(--danger-bg); }
    .adm-kpis { display: grid; gap: .75rem; grid-template-columns: repeat(auto-fit, minmax(10rem, 1fr)); margin-bottom: 1rem; }
    .adm-kpi { background: var(--surface); border: 1px solid var(--border); border-radius: var(--radius-lg); padding: .9rem 1rem; }
    .adm-kpi dt { color: var(--muted); font-size: .82rem; font-weight: 600; }
    .adm-kpi dd { margin: .2rem 0 0; font-size: 1.45rem; font-weight: 800; color: var(--ink); font-variant-numeric: tabular-nums; }
    .adm-skel { height: 6rem; }
    .adm-subtle { font-size: .85rem; color: var(--muted); }
  `,
})
export class AdminLayout {
  constructor() {
    inject(SeoService).set({ title: 'Admin', noindex: true });
  }
}
