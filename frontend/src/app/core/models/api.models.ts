/** Mirrors the TechBazar.Api JSON contracts (camelCase, enums as strings). */

export type StockStatus = 'InStock' | 'OutOfStock' | 'PreOrder' | 'UpComing';

export interface Paged<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
  hasNext: boolean;
  hasPrevious: boolean;
}

export interface ProductListItem {
  id: number;
  name: string;
  slug: string;
  sku: string;
  price: number;
  discountPrice: number | null;
  effectivePrice: number;
  discountPercent: number;
  stockStatus: StockStatus;
  imageUrl: string | null;
  brandName: string;
  brandSlug: string;
  categoryName: string;
  categorySlug: string;
  ratingAverage: number;
  reviewCount: number;
  warrantyMonths: number;
  keyFeatures: string[];
}

export interface CategoryRef {
  id: number;
  name: string;
  slug: string;
}

export interface Brand {
  id: number;
  name: string;
  slug: string;
  logoUrl: string | null;
}

export interface BrandListItem extends Brand {
  productCount: number;
}

export interface ProductImage {
  url: string;
  altText: string | null;
  isPrimary: boolean;
}

export interface SpecificationItem {
  key: string;
  value: string;
}

export interface SpecificationGroup {
  group: string;
  items: SpecificationItem[];
}

export interface Review {
  reviewerName: string;
  rating: number;
  title: string | null;
  comment: string | null;
  createdAt: string;
}

export interface ProductDetail {
  id: number;
  name: string;
  slug: string;
  sku: string;
  shortDescription: string | null;
  description: string | null;
  price: number;
  discountPrice: number | null;
  effectivePrice: number;
  discountPercent: number;
  savingsAmount: number;
  stockStatus: StockStatus;
  warrantyMonths: number;
  warrantyDetails: string | null;
  ratingAverage: number;
  reviewCount: number;
  brand: Brand;
  category: CategoryRef;
  breadcrumbs: CategoryRef[];
  images: ProductImage[];
  keyFeatures: string[];
  specifications: SpecificationGroup[];
  reviews: Review[];
}

export interface CategoryTreeNode {
  id: number;
  name: string;
  slug: string;
  imageUrl: string | null;
  productCount: number;
  children: CategoryTreeNode[];
}

export interface CategoryDetail {
  id: number;
  name: string;
  slug: string;
  description: string | null;
  imageUrl: string | null;
  breadcrumbs: CategoryRef[];
  children: CategoryRef[];
}

export interface FacetValue {
  value: string;
  count: number;
}

export interface SpecFacet {
  key: string;
  values: FacetValue[];
}

export interface BrandFacet {
  name: string;
  slug: string;
  count: number;
}

export interface ProductFacets {
  brands: BrandFacet[];
  minPrice: number | null;
  maxPrice: number | null;
  inStockCount: number;
  totalCount: number;
  specifications: SpecFacet[];
}

export interface AutocompleteProduct {
  name: string;
  slug: string;
  price: number;
  imageUrl: string | null;
  categoryName: string;
}

export interface AutocompleteResult {
  products: AutocompleteProduct[];
  categories: CategoryRef[];
  brands: Brand[];
}

export interface CartItemDto {
  productId: number;
  slug: string;
  name: string;
  sku: string;
  imageUrl: string | null;
  listPrice: number;
  unitPrice: number;
  quantity: number;
  stockStatus: StockStatus;
  lineTotal: number;
  purchasable: boolean;
}

export interface CartDto {
  items: CartItemDto[];
  itemCount: number;
  subtotal: number;
  savings: number;
}

export interface UserDto {
  id: string;
  email: string;
  fullName: string;
  phone: string | null;
  roles: string[];
}

export interface AuthResponse {
  accessToken: string;
  accessTokenExpiresAt: string;
  refreshToken: string;
  refreshTokenExpiresAt: string;
  user: UserDto;
}

/** Normalised RFC 7807 problem (what error handlers/forms receive). */
export interface ApiError {
  status: number;
  title: string;
  detail: string | null;
  errors: Record<string, string[]> | null;
  traceId: string | null;
}
