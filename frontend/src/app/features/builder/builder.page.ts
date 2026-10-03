import { DOCUMENT, Location } from '@angular/common';
import { ChangeDetectionStrategy, Component, InjectionToken, Injector, afterNextRender, computed, effect, inject, signal } from '@angular/core';
import { rxResource, takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { catchError, debounceTime, map, of, switchMap } from 'rxjs';
import { BuildItemRequest, BuildLine, BuildReport, BuilderSlot, ProductListItem } from '../../core/models/api.models';
import { BuilderService } from '../../core/services/builder.service';
import { CartService } from '../../core/services/cart.service';
import { SeoService } from '../../core/services/seo.service';
import { StorageService } from '../../core/services/storage.service';
import { ToastService } from '../../core/services/toast.service';
import { BdtPipe } from '../../core/util/bdt.pipe';
import { apiErrorOf, safeValue } from '../../core/util/resource';
import { BreadcrumbComponent } from '../../shared/breadcrumb.component';
import { IconComponent } from '../../shared/icon.component';
import { QuantityInputComponent } from '../../shared/quantity-input.component';
import { StockBadgeComponent } from '../../shared/stock-badge.component';
import {
  BUILDER_STORAGE_KEY, MAX_PART_QTY, SEVERITIES, SEVERITY_LABEL, STATUS_LABEL, addPart, buildStatus, issuesBySeverity, itemsFromReport, partitionLines,
  previewLine, psuMeter, removePart, sanitizeItems, setQuantity, shareUrl, slotState, toCartable, toRequestItems,
} from './builder.helpers';
import { PartPickerComponent } from './part-picker.component';

/** Delay between the last change and the compatibility check (overridable in tests). */
export const BUILDER_EVALUATE_DEBOUNCE = new InjectionToken<number>('BUILDER_EVALUATE_DEBOUNCE', { providedIn: 'root', factory: () => 250 });

interface Row {
  slot: BuilderSlot;
  state: 'error' | 'warn' | null;
  parts: { item: BuildItemRequest; line: BuildLine | null }[];
}

@Component({
  selector: 'app-builder',
  imports: [RouterLink, BdtPipe, BreadcrumbComponent, IconComponent, PartPickerComponent, QuantityInputComponent, StockBadgeComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './builder.page.html',
  styleUrl: './builder.page.scss',
})
export class BuilderPage {
  private readonly builder = inject(BuilderService);
  private readonly cart = inject(CartService);
  private readonly storage = inject(StorageService);
  private readonly toast = inject(ToastService);
  private readonly route = inject(ActivatedRoute);
  private readonly location = inject(Location);
  private readonly doc = inject(DOCUMENT);
  private readonly injector = inject(Injector);
  private readonly debounce = inject(BUILDER_EVALUATE_DEBOUNCE);

  protected readonly maxQty = MAX_PART_QTY;
  protected readonly severities = SEVERITIES;
  protected readonly severityLabel = SEVERITY_LABEL;
  protected readonly statusLabel = STATUS_LABEL;

  private readonly slotsRes = rxResource({ stream: () => this.builder.slots() });
  protected readonly slots = computed(() => safeValue(this.slotsRes) ?? []);
  protected readonly slotsError = computed(() => (this.slotsRes.error() ? (apiErrorOf(this.slotsRes.error())?.detail ?? 'Could not load the builder.') : null));

  /** The selection (source of truth the user edits). */
  readonly items = signal<BuildItemRequest[]>([]);
  readonly report = signal<BuildReport | null>(null);
  protected readonly evaluating = signal(false);
  protected readonly evalError = signal<string | null>(null);
  /** Product data from the picker, shown until the server report catches up. */
  private readonly previews = signal<Record<number, ProductListItem>>({});

  protected readonly pickerSlot = signal<BuilderSlot | null>(null);
  private pickerOpener: string | null = null;

  protected readonly sharedNotice = signal<string | null>(null);
  protected readonly savedCode = signal<string | null>(null);
  protected readonly saving = signal(false);
  protected readonly saveError = signal<string | null>(null);
  protected readonly copyState = signal<'idle' | 'copied' | 'failed'>('idle');
  protected readonly cartResult = signal<{ added: number; skipped: string[] } | null>(null);
  private readonly restored = signal(false);
  private reportKey = '';

  protected readonly link = computed(() => {
    const code = this.savedCode();
    return code ? shareUrl(this.doc.location?.origin ?? '', code) : null;
  });
  protected readonly compat = computed(() => this.report()?.compatibility ?? null);
  protected readonly status = computed(() => buildStatus(this.items().length, this.report()));
  protected readonly rows = computed<Row[]>(() => {
    const report = this.report();
    const previews = this.previews();
    return this.slots().map((slot) => ({
      slot,
      state: slotState(report?.compatibility.issues, slot.slot),
      parts: this.items()
        .filter((i) => i.slot === slot.slot)
        .map((item) => {
          const fromReport = report?.lines.find((l) => l.slot === item.slot && l.productId === item.productId);
          const p = previews[item.productId];
          return { item, line: fromReport ?? (p ? previewLine(item.slot, p, item.quantity ?? 1) : null) };
        }),
    }));
  });
  protected readonly meter = computed(() => {
    const c = this.compat();
    return c ? psuMeter(c.estimatedWatts, c.psuWatts, c.recommendedPsuWatts) : null;
  });
  protected readonly buyable = computed(() => partitionLines(this.report()?.lines ?? []));
  protected readonly pickerFilters = computed(() => {
    const s = this.pickerSlot();
    return s ? (this.compat()?.slotFilters[s.slot] ?? []) : [];
  });
  protected readonly issues = (sev: 'Error' | 'Warning' | 'Info') => issuesBySeverity(this.compat()?.issues, sev);

  constructor() {
    const code = this.route.snapshot.queryParamMap.get('b');
    inject(SeoService).set({
      title: 'PC Builder – build a compatible custom PC',
      description: 'Pick a processor, motherboard, RAM, storage, graphics card, PSU and case. We check compatibility and power needs and price the whole build in BDT.',
      canonicalPath: '/builder',
      noindex: !!code,
    });

    // Debounced, cancelling compatibility check. The server owns prices and rules.
    toObservable(this.items)
      .pipe(
        debounceTime(this.debounce),
        switchMap((items) => {
          if (!items.length) return of({ report: null as BuildReport | null, error: null as string | null });
          const key = JSON.stringify(toRequestItems(items));
          if (key === this.reportKey && this.report()) return of({ report: this.report(), error: null });
          return this.builder.evaluate(toRequestItems(items)).pipe(
            map((report) => ({ report: report as BuildReport | null, error: null as string | null })),
            catchError((e) => of({ report: null as BuildReport | null, error: apiErrorOf(e)?.detail ?? 'Could not check compatibility right now.' })),
          );
        }),
        takeUntilDestroyed(),
      )
      .subscribe((r) => {
        this.evaluating.set(false);
        this.evalError.set(r.error);
        if (r.error) return; // keep the previous report visible next to the error
        this.report.set(r.report);
        this.reportKey = r.report ? JSON.stringify(toRequestItems(this.items())) : '';
      });

    effect(() => {
      const items = this.items();
      if (this.restored()) this.storage.set(BUILDER_STORAGE_KEY, items);
    });

    // Browser-only state after hydration, so the first client render equals the server HTML.
    afterNextRender(() => {
      if (code) this.loadShared(code);
      else this.restoreLocal();
    });
  }

  private restoreLocal(): void {
    const items = sanitizeItems(this.storage.get<unknown>(BUILDER_STORAGE_KEY, []));
    if (items.length) this.commit(items, false);
    this.restored.set(true);
  }

  private loadShared(code: string): void {
    this.builder.load(code).subscribe({
      next: (saved) => {
        this.report.set(saved.report);
        const items = itemsFromReport(saved.report);
        this.reportKey = JSON.stringify(toRequestItems(items));
        this.items.set(items);
        this.savedCode.set(saved.code);
        this.restored.set(true);
      },
      error: (e) => {
        const notFound = apiErrorOf(e)?.status === 404;
        this.sharedNotice.set(notFound ? 'That shared build could not be found. It may have been removed or the link is incomplete.' : 'We could not load that shared build right now.');
        this.location.replaceState('/builder');
        this.restoreLocal();
      },
    });
  }

  /** Applies a selection change: marks the report stale, un-shares, and lets the debounced pipeline re-check. */
  private commit(items: BuildItemRequest[], touchedByUser = true): void {
    this.items.set(items);
    this.evaluating.set(items.length > 0);
    this.cartResult.set(null);
    if (items.length === 0) this.report.set(null);
    if (touchedByUser && this.savedCode()) {
      this.savedCode.set(null);
      this.copyState.set('idle');
      this.location.replaceState('/builder');
    }
  }

  // ---- picker -----------------------------------------------------------------------------------------------
  protected openPicker(slot: BuilderSlot): void {
    this.pickerOpener = `choose-${slot.slot}`;
    this.pickerSlot.set(slot);
  }

  protected closePicker(): void {
    this.pickerSlot.set(null);
    const id = this.pickerOpener;
    if (id) afterNextRender(() => this.doc.getElementById(id)?.focus(), { injector: this.injector });
  }

  protected pick(p: ProductListItem): void {
    const slot = this.pickerSlot();
    if (!slot) return;
    this.previews.update((m) => ({ ...m, [p.id]: p }));
    this.commit(addPart(this.items(), slot, p.id));
    this.closePicker();
  }

  protected remove(slot: BuilderSlot, productId: number): void {
    this.commit(removePart(this.items(), slot.slot, productId));
  }

  protected changeQty(slot: BuilderSlot, productId: number, quantity: number): void {
    this.commit(setQuantity(this.items(), slot.slot, productId, quantity));
  }

  protected retry(): void {
    this.evalError.set(null);
    this.evaluating.set(true);
    this.items.update((i) => [...i]);
  }

  protected reset(): void {
    this.commit([]);
    this.previews.set({});
    this.sharedNotice.set(null);
    this.saveError.set(null);
    this.location.replaceState('/builder');
    this.savedCode.set(null);
  }

  // ---- actions ----------------------------------------------------------------------------------------------
  protected addAllToCart(): void {
    const { buy, skipped } = this.buyable();
    if (!buy.length) {
      this.toast.error('None of the selected parts can be bought right now.');
      this.cartResult.set({ added: 0, skipped: skipped.map((l) => l.name) });
      return;
    }
    for (const l of buy) this.cart.add(toCartable(l), l.quantity);
    this.cartResult.set({ added: buy.length, skipped: skipped.map((l) => l.name) });
    this.toast.success(`${buy.length} ${buy.length === 1 ? 'part' : 'parts'} from your build added to cart`);
  }

  protected save(): void {
    if (!this.items().length || this.saving()) return;
    this.saving.set(true);
    this.saveError.set(null);
    this.builder.save(null, toRequestItems(this.items())).subscribe({
      next: (saved) => {
        this.saving.set(false);
        this.savedCode.set(saved.code);
        this.copyState.set('idle');
        this.location.replaceState('/builder', `b=${encodeURIComponent(saved.code)}`);
      },
      error: (e) => {
        this.saving.set(false);
        this.saveError.set(apiErrorOf(e)?.detail ?? 'Could not save your build. Please try again.');
      },
    });
  }

  protected async copyLink(): Promise<void> {
    const link = this.link();
    const clip = typeof navigator !== 'undefined' ? navigator.clipboard : undefined;
    if (!link || !clip?.writeText) {
      this.copyState.set('failed');
      return;
    }
    try {
      await clip.writeText(link);
      this.copyState.set('copied');
    } catch {
      this.copyState.set('failed');
    }
  }

  protected selectAll(e: Event): void {
    (e.target as HTMLInputElement).select();
  }
}
