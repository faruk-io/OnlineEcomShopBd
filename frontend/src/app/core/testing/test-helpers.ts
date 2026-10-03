import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { AuthResponse, UserDto } from '../models/api.models';
import { authInterceptor } from '../interceptors/auth.interceptor';
import { errorInterceptor } from '../interceptors/error.interceptor';
import { loadingInterceptor } from '../interceptors/loading.interceptor';

/** Same interceptor chain (and order) as app.config.ts, backed by HttpTestingController. */
export const provideTestHttp = () => [
  provideHttpClient(withInterceptors([loadingInterceptor, errorInterceptor, authInterceptor])),
  provideHttpClientTesting(),
];

export const USER: UserDto = { id: 'u-1', email: 'rahim@example.com', fullName: 'Rahim Uddin', phone: null, roles: ['Customer'] };

export const authResponse = (n = 1, user: UserDto = USER): AuthResponse => ({
  accessToken: `access-${n}`,
  accessTokenExpiresAt: '2099-01-01T00:00:00Z',
  refreshToken: `refresh-${n}`,
  refreshTokenExpiresAt: '2099-02-01T00:00:00Z',
  user,
});

export const problem = (status: number, title: string, extra: Record<string, unknown> = {}) => ({
  status, statusText: title, error: { status, title, ...extra },
});

import { ProductDetail, ProductListItem } from '../models/api.models';

export const productItem = (over: Partial<ProductListItem> = {}): ProductListItem => ({
  id: 13, name: 'AMD Ryzen 5 5600 Processor', slug: 'amd-ryzen-5-5600-processor', sku: 'TB-CPU-0005', price: 12800, discountPrice: 11900,
  effectivePrice: 11900, discountPercent: 7, stockStatus: 'InStock', imageUrl: '/images/placeholders/processor.svg', brandName: 'AMD', brandSlug: 'amd',
  categoryName: 'Processor', categorySlug: 'processor', ratingAverage: 0, reviewCount: 0, warrantyMonths: 36,
  keyFeatures: ['6 cores / 12 threads', 'Up to 4.4 GHz boost'], ...over,
});

export const productDetail = (over: Partial<ProductDetail> = {}): ProductDetail => ({
  id: 13, name: 'AMD Ryzen 5 5600 Processor', slug: 'amd-ryzen-5-5600-processor', sku: 'TB-CPU-0005', shortDescription: '6 cores / 12 threads',
  description: 'A great CPU.', price: 12800, discountPrice: 11900, effectivePrice: 11900, discountPercent: 7, savingsAmount: 900, stockStatus: 'InStock',
  warrantyMonths: 36, warrantyDetails: '3-year warranty', ratingAverage: 0, reviewCount: 0,
  brand: { id: 2, name: 'AMD', slug: 'amd', logoUrl: null }, category: { id: 8, name: 'Processor', slug: 'processor' },
  breadcrumbs: [{ id: 7, name: 'Component', slug: 'component' }, { id: 8, name: 'Processor', slug: 'processor' }],
  images: [{ url: '/images/placeholders/processor.svg', altText: 'AMD Ryzen 5 5600', isPrimary: true }],
  keyFeatures: ['6 cores / 12 threads', 'AM4 platform, DDR4'],
  specifications: [{ group: 'General', items: [{ key: 'Socket', value: 'AM4' }, { key: 'Cores', value: '6' }] }, { group: 'Power', items: [{ key: 'TDP', value: '65 W' }] }],
  reviews: [], ...over,
});
