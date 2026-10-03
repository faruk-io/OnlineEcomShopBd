import { ChangeDetectionStrategy, Component, DestroyRef, afterNextRender, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { IconComponent } from '../../shared/icon.component';

interface Slide {
  eyebrow: string;
  title: string;
  text: string;
  cta: string;
  link: string[];
  query?: Record<string, string>;
  art: string;
  theme: 'indigo' | 'violet' | 'teal';
}

/** WAI-ARIA carousel: auto-rotates only when motion is allowed, pauses on hover/focus, fully keyboard operable. */
@Component({
  selector: 'app-hero-slider',
  imports: [RouterLink, IconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="hero" role="region" aria-roledescription="carousel" aria-label="Featured offers"
      (mouseenter)="hover.set(true)" (mouseleave)="hover.set(false)" (focusin)="focused.set(true)" (focusout)="focused.set(false)">
      <div class="track" [attr.aria-live]="playing() ? 'off' : 'polite'">
        @for (s of slides; track s.title; let i = $index) {
          <div class="slide" [class]="'theme-' + s.theme" [class.active]="i === index()" role="group" aria-roledescription="slide"
            [attr.aria-label]="i + 1 + ' of ' + slides.length" [attr.inert]="i === index() ? null : ''">
            <div class="copy">
              <p class="eyebrow">{{ s.eyebrow }}</p>
              <h2>{{ s.title }}</h2>
              <p class="text">{{ s.text }}</p>
              <a class="btn btn-accent" [routerLink]="s.link" [queryParams]="s.query">{{ s.cta }} <app-icon name="chevron-right" [size]="16" /></a>
            </div>
            <img [src]="s.art" alt="" width="360" height="360" [attr.loading]="i === 0 ? 'eager' : 'lazy'" [attr.fetchpriority]="i === 0 ? 'high' : null" />
          </div>
        }
      </div>
      <div class="controls">
        <button type="button" class="nav" (click)="go(index() - 1)" aria-label="Previous slide"><app-icon name="chevron-left" [size]="20" /></button>
        <div class="dots">
          @for (s of slides; track s.title; let i = $index) {
            <button type="button" class="dot" [class.on]="i === index()" (click)="go(i)" [attr.aria-label]="'Go to slide ' + (i + 1)" [attr.aria-current]="i === index() ? 'true' : null"></button>
          }
        </div>
        <button type="button" class="nav" (click)="go(index() + 1)" aria-label="Next slide"><app-icon name="chevron-right" [size]="20" /></button>
        @if (canAutoplay()) {
          <button type="button" class="nav pause" (click)="userPaused.set(!userPaused())" [attr.aria-label]="userPaused() ? 'Start automatic slide show' : 'Pause automatic slide show'">{{ userPaused() ? '▶' : '❚❚' }}</button>
        }
      </div>
    </section>
  `,
  styleUrl: './hero-slider.component.scss',
})
export class HeroSliderComponent {
  protected readonly slides: Slide[] = [
    { eyebrow: 'Level up your rig', title: 'Graphics cards for every budget', text: 'From 1080p esports to 1440p ultra: compare GeForce and Radeon cards with real power and size specs.', cta: 'Shop graphics cards', link: ['/category', 'graphics-card'], art: '/images/placeholders/graphics-card.svg', theme: 'indigo' },
    { eyebrow: 'Work, study, play', title: 'Laptops that fit your life', text: 'Slim everyday laptops and 144 Hz gaming machines, with official warranty.', cta: 'Browse laptops', link: ['/category', 'laptop'], art: '/images/placeholders/laptop.svg', theme: 'violet' },
    { eyebrow: 'Limited-time prices', title: 'Today’s best deals', text: 'Sale prices on processors, RAM, storage and monitors while stock lasts.', cta: 'See all offers', link: ['/shop'], query: { onSale: 'true' }, art: '/images/placeholders/processor.svg', theme: 'teal' },
  ];

  protected readonly index = signal(0);
  protected readonly hover = signal(false);
  protected readonly focused = signal(false);
  protected readonly userPaused = signal(false);
  protected readonly canAutoplay = signal(false);
  protected readonly playing = computed(() => this.canAutoplay() && !this.userPaused() && !this.hover() && !this.focused());

  constructor() {
    const destroyRef = inject(DestroyRef);
    afterNextRender(() => {
      const reduce = typeof matchMedia === 'function' && matchMedia('(prefers-reduced-motion: reduce)').matches;
      this.canAutoplay.set(!reduce);
      const timer = setInterval(() => { if (this.playing()) this.go(this.index() + 1); }, 6000);
      destroyRef.onDestroy(() => clearInterval(timer));
    });
  }

  protected go(i: number): void {
    const n = this.slides.length;
    this.index.set(((i % n) + n) % n);
  }
}
