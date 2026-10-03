import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { HeroSliderComponent } from './hero-slider.component';

describe('HeroSliderComponent', () => {
  beforeEach(() => {
    vi.useFakeTimers({ toFake: ['setInterval', 'clearInterval'] }); // only what RxJS debounce / the slider use; Angular's scheduler keeps real timers
    vi.stubGlobal('matchMedia', () => ({ matches: false, addEventListener: () => undefined, removeEventListener: () => undefined }));
    TestBed.configureTestingModule({ providers: [provideRouter([])] });
  });
  afterEach(() => {
    vi.useRealTimers();
    vi.unstubAllGlobals();
  });

  const create = async () => {
    const f = TestBed.createComponent(HeroSliderComponent);
    await f.whenStable();
    const root = f.nativeElement as HTMLElement;
    return {
      f, root,
      active: () => Array.from(root.querySelectorAll('.slide')).findIndex((s) => s.classList.contains('active')),
      click: async (label: string) => { root.querySelector<HTMLButtonElement>(`button[aria-label="${label}"]`)!.click(); await f.whenStable(); },
    };
  };

  it('is a labelled carousel; inactive slides are inert', async () => {
    const { root } = await create();
    const region = root.querySelector('[role=region]')!;
    expect(region.getAttribute('aria-roledescription')).toBe('carousel');
    const slides = Array.from(root.querySelectorAll('.slide'));
    expect(slides).toHaveLength(3);
    expect(slides[0].hasAttribute('inert')).toBe(false);
    expect(slides[1].hasAttribute('inert')).toBe(true);
    expect(slides[0].getAttribute('aria-label')).toBe('1 of 3');
  });

  it('next / previous wrap around and the dot reflects the current slide', async () => {
    const { active, click, root } = await create();
    await click('Previous slide');
    expect(active()).toBe(2);
    await click('Next slide');
    expect(active()).toBe(0);
    await click('Go to slide 2');
    expect(active()).toBe(1);
    expect(root.querySelectorAll('.dot')[1].getAttribute('aria-current')).toBe('true');
  });

  it('auto-advances every 6 s, and the pause button stops it', async () => {
    const { active, click } = await create();
    await vi.advanceTimersByTimeAsync(6100);
    expect(active()).toBe(1);
    await click('Pause automatic slide show');
    await vi.advanceTimersByTimeAsync(13000);
    expect(active()).toBe(1);
    await click('Start automatic slide show');
    await vi.advanceTimersByTimeAsync(6100);
    expect(active()).toBe(2);
  });

  it('does not auto-play (and hides the pause control) when the user prefers reduced motion', async () => {
    vi.stubGlobal('matchMedia', () => ({ matches: true }));
    const { active, root } = await create();
    await vi.advanceTimersByTimeAsync(20000);
    expect(active()).toBe(0);
    expect(root.querySelector('.pause')).toBeNull();
  });

  it('pauses while the pointer is over the carousel', async () => {
    const { f, active, root } = await create();
    root.querySelector('[role=region]')!.dispatchEvent(new Event('mouseenter'));
    await f.whenStable();
    await vi.advanceTimersByTimeAsync(13000);
    expect(active()).toBe(0);
  });
});
