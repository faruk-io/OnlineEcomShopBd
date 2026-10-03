import { Injectable } from '@angular/core';

/** Full-page navigation to an external URL (hosted payment page). A service so tests can observe it instead of jsdom navigating. */
@Injectable({ providedIn: 'root' })
export class Redirector {
  to(url: string): void {
    window.location.assign(url);
  }
}
