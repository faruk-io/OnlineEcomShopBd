import { isPlatformBrowser } from '@angular/common';
import { Injectable, PLATFORM_ID, inject } from '@angular/core';

/** localStorage wrapper that is a no-op on the server and tolerates blocked/full storage. */
@Injectable({ providedIn: 'root' })
export class StorageService {
  private readonly browser = isPlatformBrowser(inject(PLATFORM_ID));

  get isBrowser(): boolean {
    return this.browser;
  }

  get<T>(key: string, fallback: T): T {
    if (!this.browser) return fallback;
    try {
      const raw = localStorage.getItem(key);
      return raw === null ? fallback : (JSON.parse(raw) as T);
    } catch {
      return fallback;
    }
  }

  getString(key: string): string | null {
    if (!this.browser) return null;
    try {
      return localStorage.getItem(key);
    } catch {
      return null;
    }
  }

  set(key: string, value: unknown): void {
    if (!this.browser) return;
    try {
      localStorage.setItem(key, typeof value === 'string' ? value : JSON.stringify(value));
    } catch {
      /* quota exceeded or storage disabled: keep working in memory */
    }
  }

  remove(key: string): void {
    if (!this.browser) return;
    try {
      localStorage.removeItem(key);
    } catch {
      /* ignore */
    }
  }
}
