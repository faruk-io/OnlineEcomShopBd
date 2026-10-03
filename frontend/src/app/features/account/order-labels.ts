import { OrderStatus, PaymentMethod, PaymentStatus, ShippingMethod } from '../../core/models/api.models';

const STATUS: Record<OrderStatus, string> = {
  Pending: 'Pending', Confirmed: 'Confirmed', Processing: 'Processing', Shipped: 'Shipped', Delivered: 'Delivered',
  Cancelled: 'Cancelled', Returned: 'Returned', ReadyForPickup: 'Ready for pickup',
};
export const statusLabel = (s: OrderStatus): string => STATUS[s] ?? s;

/** CSS class for the status pill (colours live in the page styles). */
export function statusClass(s: OrderStatus): string {
  switch (s) {
    case 'Delivered': return 'st-ok';
    case 'Cancelled': case 'Returned': return 'st-bad';
    case 'Pending': return 'st-warn';
    default: return 'st-info';
  }
}

const PAY: Record<PaymentStatus, string> = { Unpaid: 'Payment due', Paid: 'Paid', Refunded: 'Refunded', Failed: 'Payment failed' };
export const paymentStatusLabel = (s: PaymentStatus): string => PAY[s] ?? s;

const METHOD: Record<PaymentMethod, string> = {
  CashOnDelivery: 'Cash on delivery', BKash: 'bKash', Nagad: 'Nagad', Card: 'Card', BankTransfer: 'Bank transfer', Online: 'Online payment',
};
export const paymentMethodLabel = (m: PaymentMethod): string => METHOD[m] ?? m;

const SHIP: Record<ShippingMethod, string> = {
  HomeDeliveryInsideDhaka: 'Home delivery (inside Dhaka)', HomeDeliveryOutsideDhaka: 'Home delivery (outside Dhaka)', StorePickup: 'Store pickup',
};
export const shippingLabel = (m: ShippingMethod): string => SHIP[m] ?? m;
