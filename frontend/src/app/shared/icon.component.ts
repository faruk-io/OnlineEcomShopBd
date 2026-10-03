import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/**
 * Decorative icon drawn from the inline SVG sprite in `index.html` (`<symbol id="i-NAME">`).
 * A sprite + <use> is static markup, so it renders identically on the server and in the browser
 * (setting innerHTML on <svg> is not supported by the server DOM).
 * Hidden from assistive tech: put the accessible label on the surrounding control.
 */
@Component({
  selector: 'app-icon',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<svg [attr.width]="size()" [attr.height]="size()" viewBox="0 0 24 24" fill="none" stroke="currentColor"
    stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false"><use [attr.href]="'#i-' + name()" /></svg>`,
  styles: `:host { display: inline-flex; line-height: 0; } svg { flex: none; }`,
})
export class IconComponent {
  readonly name = input.required<string>();
  readonly size = input(20);
}
