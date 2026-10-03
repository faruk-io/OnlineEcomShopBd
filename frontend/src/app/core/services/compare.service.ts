import { Injectable, computed, inject, signal } from '@angular/core';
import { MAX_COMPARE } from '../config';
import { StorageService } from './storage.service';
import { ToastService } from './toast.service';

const KEY = 'tb.compare.v1';

/** Up to {@link MAX_COMPARE} product slugs to compare side by side (kept in localStorage; no account needed). */
@Injectable({ providedIn: 'root' })
export class CompareService {
  private readonly storage = inject(StorageService);
  private readonly toast = inject(ToastService);

  private readonly _slugs = signal<string[]>([]);
  readonly slugs = this._slugs.asReadonly();
  readonly count = computed(() => this._slugs().length);

  init(): void {
    const stored = this.storage.get<string[]>(KEY, []);
    this._slugs.set(Array.isArray(stored) ? stored.filter((s) => typeof s === 'string').slice(0, MAX_COMPARE) : []);
  }

  has(slug: string): boolean {
    return this._slugs().includes(slug);
  }

  /** Adds or removes. Returns false (and tells the user) when the list is full. */
  toggle(slug: string): boolean {
    if (this.has(slug)) {
      this.remove(slug);
      return true;
    }
    if (this._slugs().length >= MAX_COMPARE) {
      this.toast.error(`You can compare up to ${MAX_COMPARE} products. Remove one first.`);
      return false;
    }
    this.save([...this._slugs(), slug]);
    this.toast.success('Added to compare');
    return true;
  }

  remove(slug: string): void {
    this.save(this._slugs().filter((s) => s !== slug));
  }

  clear(): void {
    this.save([]);
  }

  private save(slugs: string[]): void {
    this._slugs.set(slugs);
    this.storage.set(KEY, slugs);
  }
}
