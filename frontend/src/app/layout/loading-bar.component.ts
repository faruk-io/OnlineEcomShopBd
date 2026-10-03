import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { LoadingService } from '../core/services/loading.service';

@Component({
  selector: 'app-loading-bar',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `@if (loading.isLoading()) { <div class="bar" role="progressbar" aria-label="Loading"><span></span></div> }`,
  styles: `
    .bar { position: fixed; z-index: 300; top: 0; left: 0; right: 0; height: 3px; background: rgb(67 56 202 / 0.2); overflow: hidden; }
    span { display: block; height: 100%; width: 40%; background: var(--accent); animation: slide 1s ease-in-out infinite; }
    @keyframes slide { from { transform: translateX(-100%); } to { transform: translateX(260%); } }
  `,
})
export class LoadingBarComponent {
  protected readonly loading = inject(LoadingService);
}
