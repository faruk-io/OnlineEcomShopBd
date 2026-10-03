import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ConfirmDialogComponent } from './confirm-dialog.component';

@Component({
  imports: [ConfirmDialogComponent],
  template: `<button id="opener" (click)="open.set(true)">Open</button>
    <adm-confirm [open]="open()" heading="Delete it?" message="Really?" (confirmed)="log.push('yes'); open.set(false)" (cancelled)="log.push('no'); open.set(false)" />`,
})
class Host { readonly open = signal(false); log: string[] = []; }

describe('ConfirmDialogComponent', () => {
  it('opens as a labelled modal, focuses Cancel, and emits confirm / cancel', async () => {
    const f = TestBed.createComponent(Host);
    f.detectChanges();
    const el = f.nativeElement as HTMLElement;
    const dlg = el.querySelector('dialog')!;
    expect(dlg.hasAttribute('open')).toBe(false);

    f.componentInstance.open.set(true);
    f.detectChanges();
    TestBed.tick();
    expect(dlg.hasAttribute('open')).toBe(true);
    expect(dlg.getAttribute('aria-labelledby')).toBe('cf-title');
    expect(dlg.querySelector('#cf-title')!.textContent).toBe('Delete it?');
    await Promise.resolve();
    expect(document.activeElement?.textContent).toBe('Cancel');

    dlg.querySelectorAll('button')[1].click();
    f.detectChanges();
    TestBed.tick();
    expect(f.componentInstance.log).toEqual(['yes']);
    expect(dlg.hasAttribute('open')).toBe(false);

    f.componentInstance.open.set(true);
    f.detectChanges();
    TestBed.tick();
    dlg.dispatchEvent(new Event('cancel', { cancelable: true })); // Esc
    expect(f.componentInstance.log).toEqual(['yes', 'no']);
  });
});
