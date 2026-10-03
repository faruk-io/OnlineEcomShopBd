import { ChangeDetectionStrategy, Component, ElementRef, effect, input, output, viewChild } from '@angular/core';

/**
 * Accessible confirmation built on the native <dialog> (modal: focus trap, Esc to close, inert background).
 * The parent owns the state: bind `[open]`, handle `(confirmed)` and `(cancelled)`. Focus starts on "Cancel" (the safe choice)
 * and returns to the element that opened the dialog.
 */
@Component({
  selector: 'adm-confirm',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <dialog #dlg class="confirm" [attr.aria-labelledby]="'cf-title'" [attr.aria-describedby]="'cf-msg'" (cancel)="$event.preventDefault(); cancelled.emit()">
      @if (open()) {
        <h2 id="cf-title">{{ heading() }}</h2>
        <p id="cf-msg">{{ message() }}</p>
        <div class="row">
          <button type="button" class="btn btn-outline" #cancelBtn (click)="cancelled.emit()">Cancel</button>
          <button type="button" class="btn btn-danger" (click)="confirmed.emit()" [disabled]="busy()">{{ confirmLabel() }}</button>
        </div>
      }
    </dialog>
  `,
  styles: `
    dialog.confirm { border: 1px solid var(--border); border-radius: var(--radius-lg); padding: 1.25rem; max-width: min(26rem, calc(100vw - 2rem)); box-shadow: var(--shadow-md); color: var(--text); }
    dialog.confirm::backdrop { background: rgb(15 23 42 / 0.5); }
    .row { display: flex; gap: .5rem; justify-content: flex-end; flex-wrap: wrap; }
    .btn-danger { background: var(--danger); color: #fff; } .btn-danger:hover:not(:disabled) { background: #991b1b; }
  `,
})
export class ConfirmDialogComponent {
  readonly open = input(false);
  readonly heading = input('Are you sure?');
  readonly message = input('');
  readonly confirmLabel = input('Delete');
  readonly busy = input(false);
  readonly confirmed = output<void>();
  readonly cancelled = output<void>();

  private readonly dlg = viewChild.required<ElementRef<HTMLDialogElement>>('dlg');
  private readonly cancelBtn = viewChild<ElementRef<HTMLButtonElement>>('cancelBtn');
  private opener: HTMLElement | null = null;

  constructor() {
    effect(() => {
      const el = this.dlg().nativeElement;
      if (this.open()) {
        this.opener = document.activeElement as HTMLElement | null;
        if (!el.open) {
          if (typeof el.showModal === 'function') el.showModal();
          else el.setAttribute('open', '');
        }
        queueMicrotask(() => this.cancelBtn()?.nativeElement.focus());
      } else if (el.open || el.hasAttribute('open')) {
        if (typeof el.close === 'function') el.close();
        else el.removeAttribute('open');
        this.opener?.focus?.();
        this.opener = null;
      }
    });
  }
}
