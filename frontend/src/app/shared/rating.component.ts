import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { IconComponent } from './icon.component';

@Component({
  selector: 'app-rating',
  imports: [IconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <span class="stars" role="img" [attr.aria-label]="label()">
      @for (i of stars; track i) {
        <span class="star" [class.on]="i <= filled()"><app-icon name="star" [size]="size()" /></span>
      }
    </span>
    @if (showCount()) {
      <span class="count">{{ count() > 0 ? '(' + count() + ')' : 'No reviews yet' }}</span>
    }
  `,
  styles: `
    :host { display: inline-flex; align-items: center; gap: 0.4rem; }
    .stars { display: inline-flex; }
    .star { color: var(--border-strong); }
    .star.on { color: var(--star); }
    .star.on ::ng-deep svg { fill: currentColor; }
    .count { font-size: 0.8rem; color: var(--muted); }
  `,
})
export class RatingComponent {
  readonly value = input(0);
  readonly count = input(0);
  readonly size = input(14);
  readonly showCount = input(true);
  protected readonly stars = [1, 2, 3, 4, 5];
  protected readonly filled = computed(() => Math.round(this.value()));
  protected readonly label = computed(() =>
    this.count() > 0 ? `Rated ${this.value().toFixed(1)} out of 5 from ${this.count()} reviews` : 'No reviews yet');
}
