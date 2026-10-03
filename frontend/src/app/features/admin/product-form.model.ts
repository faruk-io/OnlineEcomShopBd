import { AdminProductDetail, SaveProductRequest, StockStatus } from '../../core/models/api.models';
import { SLUG_PATTERN, nextUid } from './admin.util';

export const STOCK_STATUSES: StockStatus[] = ['InStock', 'OutOfStock', 'PreOrder', 'UpComing'];
/** Canonical spec keys the PC Builder compatibility engine reads. */
export const CANONICAL_SPEC_KEYS = ['Socket', 'RAM Type', 'TDP', 'Wattage', 'Form Factor'];
export const MAX_FEATURES = 12;
export const MAX_SPECS = 120;
export const MAX_IMAGES = 10;

export interface FeatureRow { uid: number; text: string }
export interface SpecRow { uid: number; group: string; key: string; value: string; isFilterable: boolean | null }
export interface ImageRow { uid: number; url: string; altText: string; isPrimary: boolean }

/** Form state as typed by the user: numbers stay strings until validated. */
export interface ProductFormModel {
  name: string; slug: string; sku: string; categoryId: string; brandId: string; price: string; discountPrice: string;
  stockStatus: StockStatus; stockQuantity: string; warrantyMonths: string; warrantyDetails: string; shortDescription: string; description: string;
  isFeatured: boolean; isActive: boolean; features: FeatureRow[]; specs: SpecRow[]; images: ImageRow[];
}

export const emptyProduct = (): ProductFormModel => ({
  name: '', slug: '', sku: '', categoryId: '', brandId: '', price: '', discountPrice: '', stockStatus: 'InStock', stockQuantity: '0', warrantyMonths: '0',
  warrantyDetails: '', shortDescription: '', description: '', isFeatured: false, isActive: true, features: [], specs: [], images: [],
});

export const emptySpec = (group = ''): SpecRow => ({ uid: nextUid(), group, key: '', value: '', isFilterable: null });

export function fromDetail(p: AdminProductDetail): ProductFormModel {
  const primaryIdx = Math.max(0, p.images.findIndex((i) => i.isPrimary));
  return {
    name: p.name, slug: p.slug, sku: p.sku, categoryId: String(p.categoryId), brandId: String(p.brandId), price: String(p.price),
    discountPrice: p.discountPrice === null ? '' : String(p.discountPrice), stockStatus: p.stockStatus, stockQuantity: String(p.stockQuantity),
    warrantyMonths: String(p.warrantyMonths), warrantyDetails: p.warrantyDetails ?? '', shortDescription: p.shortDescription ?? '', description: p.description ?? '',
    isFeatured: p.isFeatured, isActive: p.isActive,
    features: p.keyFeatures.map((text) => ({ uid: nextUid(), text })),
    specs: p.specifications.map((s) => ({ uid: nextUid(), group: s.group, key: s.key, value: s.value, isFilterable: s.isFilterable })),
    images: p.images.map((i, idx) => ({ uid: nextUid(), url: i.url, altText: i.altText ?? '', isPrimary: idx === primaryIdx })),
  };
}

/** Drops fully blank feature/spec rows so row indexes line up 1:1 with the request (and with server error paths). */
export function pruneBlank(m: ProductFormModel): ProductFormModel {
  return {
    ...m,
    features: m.features.filter((f) => f.text.trim()),
    specs: m.specs.filter((s) => s.group.trim() || s.key.trim() || s.value.trim()),
    images: m.images.filter((i) => i.url.trim()),
  };
}

const MONEY = /^\d+(\.\d{1,2})?$/;
const INT = /^\d+$/;

export interface BuildResult { request: SaveProductRequest | null; errors: Record<string, string> }

/** Validates the form (mirroring the server rules) and builds the request. `request` is null when there are errors. */
export function buildProductRequest(input: ProductFormModel): BuildResult {
  const m = pruneBlank(input);
  const e: Record<string, string> = {};

  const name = m.name.trim();
  const slug = m.slug.trim();
  const sku = m.sku.trim();
  if (!name) e['name'] = 'Name is required.';
  else if (name.length > 250) e['name'] = 'Name must be 250 characters or fewer.';
  if (slug && (slug.length > 300 || !SLUG_PATTERN.test(slug))) e['slug'] = 'Slug may contain lower-case letters, numbers and single hyphens.';
  if (!sku) e['sku'] = 'SKU is required.';
  else if (sku.length > 64) e['sku'] = 'SKU must be 64 characters or fewer.';

  const categoryId = Number(m.categoryId);
  const brandId = Number(m.brandId);
  if (!(categoryId > 0)) e['categoryId'] = 'Choose a category.';
  if (!(brandId > 0)) e['brandId'] = 'Choose a brand.';

  const price = m.price.trim();
  if (!MONEY.test(price) || Number(price) <= 0) e['price'] = 'Enter a price greater than 0 (up to 2 decimals).';
  else if (Number(price) >= 100_000_000) e['price'] = 'Price must be below ৳10,00,00,000.';
  const dp = m.discountPrice.trim();
  let discountPrice: number | null = null;
  if (dp) {
    if (!MONEY.test(dp) || Number(dp) <= 0) e['discountPrice'] = 'Enter a sale price greater than 0 (up to 2 decimals).';
    else if (!e['price'] && Number(dp) >= Number(price)) e['discountPrice'] = 'Sale price must be lower than the regular price.';
    else discountPrice = Number(dp);
  }

  const qty = m.stockQuantity.trim();
  if (!INT.test(qty) || Number(qty) > 1_000_000) e['stockQuantity'] = 'Enter a whole number between 0 and 1,000,000.';
  const warranty = m.warrantyMonths.trim();
  if (!INT.test(warranty) || Number(warranty) > 600) e['warrantyMonths'] = 'Enter a whole number of months between 0 and 600.';
  if (m.warrantyDetails.length > 500) e['warrantyDetails'] = 'Warranty details must be 500 characters or fewer.';
  if (m.shortDescription.length > 500) e['shortDescription'] = 'Short description must be 500 characters or fewer.';
  if (m.description.length > 4000) e['description'] = 'Description must be 4,000 characters or fewer.';

  if (m.features.length > MAX_FEATURES) e['keyFeatures'] = `At most ${MAX_FEATURES} key features.`;
  m.features.forEach((f, i) => { if (f.text.trim().length > 300) e[`keyFeatures[${i}]`] = 'Keep each feature under 300 characters.'; });

  if (m.specs.length > MAX_SPECS) e['specifications'] = `At most ${MAX_SPECS} specifications.`;
  m.specs.forEach((s, i) => {
    if (!s.group.trim()) e[`specifications[${i}].group`] = 'Group is required.';
    if (!s.key.trim()) e[`specifications[${i}].key`] = 'Name is required.';
    if (!s.value.trim()) e[`specifications[${i}].value`] = 'Value is required.';
  });

  if (m.images.length > MAX_IMAGES) e['images'] = `At most ${MAX_IMAGES} images.`;

  if (Object.keys(e).length) return { request: null, errors: e };

  const primary = Math.max(0, m.images.findIndex((i) => i.isPrimary));
  const request: SaveProductRequest = {
    name, slug: slug || null, sku, categoryId, brandId, price: Number(price), discountPrice,
    stockStatus: m.stockStatus, stockQuantity: Number(qty), warrantyMonths: Number(warranty),
    warrantyDetails: m.warrantyDetails.trim() || null, shortDescription: m.shortDescription.trim() || null, description: m.description.trim() || null,
    isFeatured: m.isFeatured, isActive: m.isActive,
    keyFeatures: m.features.map((f) => f.text.trim()),
    specifications: m.specs.map((s) => ({ group: s.group.trim(), key: s.key.trim(), value: s.value.trim(), isFilterable: s.isFilterable })),
    images: m.images.map((i, idx) => ({ url: i.url.trim(), altText: i.altText.trim() || null, isPrimary: idx === primary })),
  };
  return { request, errors: {} };
}
