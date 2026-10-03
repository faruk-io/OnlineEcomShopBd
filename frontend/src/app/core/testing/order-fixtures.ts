import { Address, CheckoutOptions, CheckoutQuote, OrderDetail, TimelineStep } from '../models/api.models';

export const ADDRESS: Address = {
  id: 7, label: 'Home', fullName: 'Rahim Uddin', phone: '01712345678', division: 'Dhaka', district: 'Dhaka', upazila: 'Mirpur',
  addressLine: 'House 1, Road 2', postalCode: '1216', isDefault: true,
};

export const OPTIONS: CheckoutOptions = {
  shipping: [
    { method: 'HomeDeliveryInsideDhaka', label: 'Home delivery — inside Dhaka', fee: 70, description: '1–2 days' },
    { method: 'HomeDeliveryOutsideDhaka', label: 'Home delivery — outside Dhaka', fee: 130, description: '2–4 days' },
    { method: 'StorePickup', label: 'Store pickup', fee: 0, description: 'Collect from our store' },
  ],
  payment: [
    { method: 'CashOnDelivery', label: 'Cash on delivery', description: 'Pay when you receive', enabled: true },
    { method: 'Online', label: 'Pay online', description: 'Cards, bKash, Nagad via SSLCommerz', enabled: true },
  ],
  store: { name: 'TechBazar BD Store', address: 'Dhaka' },
  divisions: ['Dhaka', 'Chattogram'],
  requireVerifiedEmail: false,
};

export const quoteOf = (over: Partial<CheckoutQuote> = {}): CheckoutQuote => ({
  lines: [{ productId: 13, slug: 'ryzen', name: 'AMD Ryzen 5 5600', imageUrl: null, unitPrice: 11900, quantity: 2, lineTotal: 23800, stockStatus: 'InStock', available: 5, problem: null }],
  subtotal: 23800, discount: 0, shippingMethod: 'HomeDeliveryInsideDhaka', shippingFee: 70, grandTotal: 23870, couponCode: null, couponApplied: false,
  couponMessage: null, canPlaceOrder: true, problems: [], ...over,
});

export const TIMELINE: TimelineStep[] = [
  { status: 'Pending', label: 'Order placed', reachedAt: '2026-10-01T10:00:00Z', done: true, current: false },
  { status: 'Confirmed', label: 'Confirmed', reachedAt: '2026-10-01T11:00:00Z', done: true, current: true },
  { status: 'Processing', label: 'Processing', reachedAt: null, done: false, current: false },
  { status: 'Shipped', label: 'Shipped', reachedAt: null, done: false, current: false },
  { status: 'Delivered', label: 'Delivered', reachedAt: null, done: false, current: false },
];

export const orderOf = (over: Partial<OrderDetail> = {}): OrderDetail => ({
  orderNumber: 'TB-261001-AB12', createdAt: '2026-10-01T10:00:00Z', status: 'Confirmed', paymentStatus: 'Unpaid', paymentMethod: 'CashOnDelivery',
  shippingMethod: 'HomeDeliveryInsideDhaka', subtotal: 23800, discountTotal: 0, shippingFee: 70, grandTotal: 23870, couponCode: null, note: null,
  contactEmail: 'rahim@example.com',
  shipTo: { fullName: 'Rahim Uddin', phone: '01712345678', division: 'Dhaka', district: 'Dhaka', upazila: 'Mirpur', addressLine: 'House 1, Road 2', postalCode: '1216' },
  pickup: null,
  items: [{ productId: 13, slug: 'ryzen', name: 'AMD Ryzen 5 5600', sku: 'TB-CPU-0005', imageUrl: null, unitPrice: 11900, quantity: 2, lineTotal: 23800 }],
  history: [{ status: 'Pending', note: null, at: '2026-10-01T10:00:00Z' }, { status: 'Confirmed', note: 'Payment received', at: '2026-10-01T11:00:00Z' }],
  timeline: TIMELINE, payments: [], canCancel: true, canPay: false, ...over,
});
