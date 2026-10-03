import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/**
 * Label + projected control + hint + inline error. Convention: the projected control has `id = forId`, and when `error` is set it
 * should carry `aria-invalid="true"` and `aria-describedby="<forId>-err"` (use {@link describedBy}).
 */
@Component({
  selector: 'adm-field',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="field">
      <label [attr.for]="forId()">{{ label() }}@if (required()) { <span aria-hidden="true"> *</span><span class="visually-hidden"> (required)</span> }</label>
      <ng-content />
      @if (hint()) { <span class="hint" [id]="forId() + '-hint'">{{ hint() }}</span> }
      @if (error()) { <span class="error-text" [id]="forId() + '-err'" role="alert">{{ error() }}</span> }
    </div>
  `,
  styles: `:host { display: block; } .field { margin-bottom: 0; }`,
})
export class AdminFieldComponent {
  readonly label = input.required<string>();
  readonly forId = input.required<string>();
  readonly error = input<string | null | undefined>(null);
  readonly hint = input<string | null>(null);
  readonly required = input(false);
}

/** Value for `aria-describedby` on a control inside <adm-field>. */
export const describedBy = (id: string, error: string | null | undefined, hasHint = false): string | null => {
  const ids = [hasHint ? `${id}-hint` : '', error ? `${id}-err` : ''].filter(Boolean).join(' ');
  return ids || null;
};
