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
  /** Null for the web client: the refresh token travels in an HttpOnly cookie. */
  refreshToken: string | null;
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

// ------------------------------------------------------------------ Phase 3: checkout & orders
export type ShippingMethod = 'HomeDeliveryInsideDhaka' | 'HomeDeliveryOutsideDhaka' | 'StorePickup';
export type PaymentMethod = 'CashOnDelivery' | 'BKash' | 'Nagad' | 'Card' | 'BankTransfer' | 'Online';
export type PaymentStatus = 'Unpaid' | 'Paid' | 'Refunded' | 'Failed';
export type PaymentAttemptStatus = 'Pending' | 'Paid' | 'Failed' | 'Cancelled' | 'Refunded';
export type OrderStatus = 'Pending' | 'Confirmed' | 'Processing' | 'Shipped' | 'Delivered' | 'Cancelled' | 'Returned' | 'ReadyForPickup';
export type DiscountType = 'Percentage' | 'FixedAmount';

export interface Address {
  id: number;
  label: string;
  fullName: string;
  phone: string;
  division: string;
  district: string;
  upazila: string | null;
  addressLine: string;
  postalCode: string | null;
  isDefault: boolean;
}
export type SaveAddressRequest = Omit<Address, 'id'>;

export interface ShippingOption { method: ShippingMethod; label: string; fee: number; description: string }
export interface PaymentOption { method: PaymentMethod; label: string; description: string; enabled: boolean }
export interface StoreInfo { name: string; address: string }
export interface CheckoutOptions { shipping: ShippingOption[]; payment: PaymentOption[]; store: StoreInfo; divisions: string[] }

export interface CheckoutQuoteRequest { shippingMethod: ShippingMethod; addressId: number | null; couponCode: string | null }
export interface CheckoutLine {
  productId: number; slug: string; name: string; imageUrl: string | null; unitPrice: number; quantity: number; lineTotal: number;
  stockStatus: StockStatus; available: number | null; problem: string | null;
}
export interface CheckoutQuote {
  lines: CheckoutLine[]; subtotal: number; discount: number; shippingMethod: ShippingMethod; shippingFee: number; grandTotal: number;
  couponCode: string | null; couponApplied: boolean; couponMessage: string | null; canPlaceOrder: boolean; problems: string[];
}
export interface PlaceOrderRequest {
  shippingMethod: ShippingMethod; addressId: number | null; contactName: string | null; contactPhone: string | null;
  paymentMethod: PaymentMethod; couponCode: string | null; note: string | null;
}

export interface OrderLine { productId: number; slug: string | null; name: string; sku: string; imageUrl: string | null; unitPrice: number; quantity: number; lineTotal: number }
export interface ShipAddress { fullName: string; phone: string; division: string; district: string; upazila: string | null; addressLine: string; postalCode: string | null }
export interface PaymentAttempt { gateway: string; method: PaymentMethod; status: PaymentAttemptStatus; amount: number; createdAt: string; paidAt: string | null; failureReason: string | null }
export interface StatusEntry { status: OrderStatus; note: string | null; at: string }
export interface TimelineStep { status: OrderStatus; label: string; reachedAt: string | null; done: boolean; current: boolean }

export interface OrderSummary {
  orderNumber: string; createdAt: string; status: OrderStatus; paymentStatus: PaymentStatus; paymentMethod: PaymentMethod;
  shippingMethod: ShippingMethod; grandTotal: number; itemCount: number; firstItemName: string | null; firstItemImage: string | null;
}
export interface OrderDetail {
  orderNumber: string; createdAt: string; status: OrderStatus; paymentStatus: PaymentStatus; paymentMethod: PaymentMethod;
  shippingMethod: ShippingMethod; subtotal: number; discountTotal: number; shippingFee: number; grandTotal: number;
  couponCode: string | null; note: string | null; contactEmail: string; shipTo: ShipAddress; pickup: StoreInfo | null;
  items: OrderLine[]; history: StatusEntry[]; timeline: TimelineStep[]; payments: PaymentAttempt[]; canCancel: boolean; canPay: boolean;
}
export interface PaymentRedirect { redirectUrl: string | null; error: string | null }
export interface PlaceOrderResult { order: OrderDetail; payment: PaymentRedirect | null }

// ------------------------------------------------------------------ Phase 3: PC builder
export type BuildSlot = 'Cpu' | 'Motherboard' | 'Ram' | 'Storage' | 'Gpu' | 'Psu' | 'Case' | 'Cooler' | 'Monitor';
export type IssueSeverity = 'Error' | 'Warning' | 'Info';

export interface BuilderSlot { slot: BuildSlot; label: string; categorySlug: string; required: boolean; allowMultiple: boolean }
export interface BuildItemRequest { slot: BuildSlot; productId: number; quantity?: number }
export interface BuildLine {
  slot: BuildSlot; productId: number; name: string; slug: string; sku: string; imageUrl: string | null; brandName: string;
  listPrice: number; unitPrice: number; quantity: number; lineTotal: number; stockStatus: StockStatus; purchasable: boolean;
}
export interface BuildIssue { severity: IssueSeverity; code: string; message: string; slots: BuildSlot[] }
export interface CompatibilityReport {
  isComplete: boolean; isCompatible: boolean; estimatedWatts: number; recommendedPsuWatts: number; psuWatts: number | null;
  issues: BuildIssue[]; slotFilters: Partial<Record<BuildSlot, string[]>>;
}
export interface BuildReport { lines: BuildLine[]; total: number; savings: number; allPurchasable: boolean; compatibility: CompatibilityReport }
export interface SavedBuild { code: string; name: string | null; createdAt: string; report: BuildReport }

// ------------------------------------------------------------------ Phase 3: admin
export interface AdminProductListItem {
  id: number; name: string; slug: string; sku: string; brandName: string; categoryName: string; price: number; discountPrice: number | null;
  stockStatus: StockStatus; stockQuantity: number; isActive: boolean; isFeatured: boolean; imageUrl: string | null; updatedAt: string | null;
}
export interface SpecInput { group: string; key: string; value: string; isFilterable: boolean | null }
export interface ImageInput { url: string; altText: string | null; isPrimary: boolean }
export interface SaveProductRequest {
  name: string; slug: string | null; sku: string; categoryId: number; brandId: number; price: number; discountPrice: number | null;
  stockStatus: StockStatus; stockQuantity: number; warrantyMonths: number; warrantyDetails: string | null; shortDescription: string | null;
  description: string | null; isFeatured: boolean; isActive: boolean; keyFeatures: string[]; specifications: SpecInput[]; images: ImageInput[];
}
export interface AdminProductDetail extends SaveProductRequest { id: number; slug: string }

export interface AdminCategory {
  id: number; name: string; slug: string; description: string | null; imageUrl: string | null; parentId: number | null; parentName: string | null;
  displayOrder: number; isActive: boolean; productCount: number; childCount: number;
}
export interface SaveCategoryRequest { name: string; slug: string | null; description: string | null; imageUrl: string | null; parentId: number | null; displayOrder: number; isActive: boolean }
export interface AdminBrand { id: number; name: string; slug: string; description: string | null; logoUrl: string | null; isActive: boolean; productCount: number }
export interface SaveBrandRequest { name: string; slug: string | null; description: string | null; logoUrl: string | null; isActive: boolean }

export interface AdminCoupon {
  id: number; code: string; description: string | null; discountType: DiscountType; value: number; minOrderAmount: number | null;
  maxDiscountAmount: number | null; usageLimit: number | null; usedCount: number; startsAt: string | null; expiresAt: string | null; isActive: boolean;
}
export type SaveCouponRequest = Omit<AdminCoupon, 'id' | 'usedCount'>;

export interface AdminOrderListItem {
  orderNumber: string; createdAt: string; customerName: string; phone: string; email: string; status: OrderStatus; paymentStatus: PaymentStatus;
  paymentMethod: PaymentMethod; shippingMethod: ShippingMethod; grandTotal: number; itemCount: number;
}
export interface AdminOrderDetail { order: OrderDetail; allowedNext: OrderStatus[]; canMarkPaid: boolean }

export interface Dashboard {
  days: number; revenue: number; orders: number; averageOrderValue: number; pendingOrders: number; ordersToday: number; revenueToday: number;
  salesByDay: { date: string; revenue: number; orders: number }[];
  ordersByStatus: { status: OrderStatus; count: number }[];
  topProducts: { productId: number; name: string; quantity: number; revenue: number }[];
  lowStock: { id: number; name: string; sku: string; slug: string; stockQuantity: number }[];
  lowStockThreshold: number;
  recentOrders: AdminOrderListItem[];
}
