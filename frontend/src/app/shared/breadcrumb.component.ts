import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { IconComponent } from './icon.component';

export interface Crumb {
  label: string;
  link?: string[];
}

@Component({
  selector: 'app-breadcrumb',
  imports: [RouterLink, IconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <nav aria-label="Breadcrumb">
      <ol>
        <li><a routerLink="/">Home</a></li>
        @for (c of items(); track $index; let last = $last) {
          <li>
            <app-icon name="chevron-right" [size]="14" />
            @if (c.link && !last) { <a [routerLink]="c.link">{{ c.label }}</a> }
            @else { <span [attr.aria-current]="last ? 'page' : null">{{ c.label }}</span> }
          </li>
        }
      </ol>
    </nav>
  `,
  styles: `
    ol { list-style: none; display: flex; flex-wrap: wrap; align-items: center; gap: 0.25rem; margin: 0; padding: 0; font-size: 0.85rem; color: var(--muted); }
    li { display: inline-flex; align-items: center; gap: 0.25rem; }
    a { color: var(--muted); } a:hover { color: var(--primary); }
    [aria-current] { color: var(--text); font-weight: 500; }
  `,
})
export class BreadcrumbComponent {
  readonly items = input.required<Crumb[]>();
}
