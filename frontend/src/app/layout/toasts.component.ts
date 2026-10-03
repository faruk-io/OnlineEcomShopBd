import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { ToastService } from '../core/services/toast.service';
import { IconComponent } from '../shared/icon.component';

/** Polite live region: screen readers hear toasts without focus being stolen. */
@Component({
  selector: 'app-toasts',
  imports: [IconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="stack" role="status" aria-live="polite">
      @for (t of toasts.toasts(); track t.id) {
        <div class="toast" [class]="t.kind">
          <span>{{ t.message }}</span>
          <button type="button" (click)="toasts.dismiss(t.id)" aria-label="Dismiss notification"><app-icon name="close" [size]="16" /></button>
        </div>
      }
    </div>
  `,
  styles: `
    .stack { position: fixed; z-index: 200; bottom: 1rem; left: 1rem; right: 1rem; display: grid; gap: 0.5rem; justify-items: center; pointer-events: none; }
    .toast { pointer-events: auto; display: flex; align-items: center; gap: 0.75rem; max-width: 28rem; padding: 0.7rem 0.9rem; border-radius: var(--radius); background: var(--ink); color: #fff; box-shadow: var(--shadow-md); font-size: 0.92rem; border-left: 4px solid var(--primary); }
    .toast.success { border-left-color: #22c55e; } .toast.error { border-left-color: #ef4444; }
    button { background: none; border: 0; color: inherit; cursor: pointer; display: grid; place-items: center; padding: 0.15rem; opacity: 0.8; } button:hover { opacity: 1; }
    @media (min-width: 640px) { .stack { left: auto; right: 1.25rem; justify-items: end; } }
  `,
})
export class ToastsComponent {
  protected readonly toasts = inject(ToastService);
}
