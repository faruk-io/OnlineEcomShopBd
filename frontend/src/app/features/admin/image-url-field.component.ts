import { ChangeDetectionStrategy, Component, inject, input, output, signal } from '@angular/core';
import { AdminApiService } from '../../core/services/admin-api.service';
import { AdminFieldComponent, describedBy } from './admin-field.component';
import { imageProblem, inputValue, problemText } from './admin.util';

/** URL text box plus an "Upload" button that stores the file through the admin upload endpoint and fills in the URL. */
@Component({
  selector: 'adm-image-url',
  imports: [AdminFieldComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <adm-field [label]="label()" [forId]="id()" [error]="error() ?? problem()" hint="Site path (/uploads/...) or https:// URL">
      <div class="row">
        <input class="input" [id]="id()" type="text" [value]="value()" (input)="valueChange.emit(text($event))" maxlength="500"
               [attr.aria-invalid]="(error() ?? problem()) ? 'true' : null" [attr.aria-describedby]="desc()" />
        <label class="btn btn-outline btn-sm" [class.disabled]="busy()">
          {{ busy() ? 'Uploading…' : 'Upload' }}
          <input class="visually-hidden" type="file" accept="image/png,image/jpeg,image/gif,image/webp" (change)="pick($event)" [disabled]="busy()" [attr.aria-label]="'Upload ' + label().toLowerCase()" />
        </label>
      </div>
      @if (value()) { <img class="prev" [src]="value()" alt="" width="64" height="64" /> }
    </adm-field>
  `,
  styles: `.row { display: flex; gap: .5rem; align-items: center; } .prev { object-fit: contain; border: 1px solid var(--border); border-radius: var(--radius); margin-top: .35rem; background: #fff; } .disabled { opacity: .6; } label.btn:focus-within { box-shadow: var(--focus); }`,
})
export class ImageUrlFieldComponent {
  private readonly api = inject(AdminApiService);
  readonly label = input.required<string>();
  readonly id = input.required<string>();
  readonly value = input('');
  readonly error = input<string | null>(null);
  readonly valueChange = output<string>();
  protected readonly busy = signal(false);
  protected readonly problem = signal<string | null>(null);

  protected readonly text = inputValue;
  protected desc(): string | null { return describedBy(this.id(), this.error() ?? this.problem(), true); }

  protected pick(e: Event): void {
    const input = e.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = '';
    if (!file) return;
    const bad = imageProblem(file);
    if (bad) { this.problem.set(bad); return; }
    this.problem.set(null);
    this.busy.set(true);
    this.api.uploadImage(file).subscribe({
      next: (r) => { this.busy.set(false); this.valueChange.emit(r.url); },
      error: (err: unknown) => { this.busy.set(false); this.problem.set(problemText(err, 'Upload failed.')); },
    });
  }
}
