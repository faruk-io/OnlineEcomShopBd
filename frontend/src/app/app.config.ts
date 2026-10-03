import { ApplicationConfig, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideHttpClient, withFetch, withInterceptors } from '@angular/common/http';
import { provideClientHydration, withEventReplay, withHttpTransferCacheOptions } from '@angular/platform-browser';
import { TitleStrategy, provideRouter, withComponentInputBinding, withInMemoryScrolling } from '@angular/router';
import { routes } from './app.routes';
import { authInterceptor } from './core/interceptors/auth.interceptor';
import { errorInterceptor } from './core/interceptors/error.interceptor';
import { loadingInterceptor } from './core/interceptors/loading.interceptor';
import { AppTitleStrategy } from './core/services/app-title.strategy';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(
      routes,
      withComponentInputBinding(),
      withInMemoryScrolling({ scrollPositionRestoration: 'enabled', anchorScrolling: 'enabled' }),
    ),
    // Order matters: auth is innermost so it sees raw HttpErrorResponses (401 -> silent refresh); the error
    // interceptor then normalises whatever finally fails; loading wraps everything.
    provideHttpClient(withFetch(), withInterceptors([loadingInterceptor, errorInterceptor, authInterceptor])),
    provideClientHydration(withEventReplay(), withHttpTransferCacheOptions({ includePostRequests: false })),
    { provide: TitleStrategy, useClass: AppTitleStrategy },
  ],
};
