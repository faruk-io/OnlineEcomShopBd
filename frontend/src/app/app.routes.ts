import { Routes } from '@angular/router';
import { adminGuard } from './core/guards/admin.guard';
import { authGuard, guestGuard } from './core/guards/auth.guard';

/** Every feature is lazy-loaded; URLs are human-readable and shareable. */
export const routes: Routes = [
  { path: '', pathMatch: 'full', loadComponent: () => import('./features/home/home.page').then((m) => m.HomePage) },
  { path: 'category/:slug', data: { mode: 'category' }, loadComponent: () => import('./features/listing/listing.page').then((m) => m.ListingPage) },
  { path: 'shop', title: 'Shop all products', data: { mode: 'shop' }, loadComponent: () => import('./features/listing/listing.page').then((m) => m.ListingPage) },
  { path: 'search', data: { mode: 'search' }, loadComponent: () => import('./features/listing/listing.page').then((m) => m.ListingPage) },
  { path: 'product/:slug', loadComponent: () => import('./features/product/product-detail.page').then((m) => m.ProductDetailPage) },
  { path: 'compare', title: 'Compare products', loadComponent: () => import('./features/compare/compare.page').then((m) => m.ComparePage) },
  { path: 'wishlist', title: 'Wishlist', loadComponent: () => import('./features/wishlist/wishlist.page').then((m) => m.WishlistPage) },
  { path: 'cart', title: 'Shopping cart', loadComponent: () => import('./features/cart/cart.page').then((m) => m.CartPage) },
  { path: 'checkout', title: 'Checkout', canActivate: [authGuard], loadComponent: () => import('./features/checkout/checkout.page').then((m) => m.CheckoutPage) },
  { path: 'builder', title: 'PC Builder', loadComponent: () => import('./features/builder/builder.page').then((m) => m.BuilderPage) },
  { path: 'login', title: 'Login', canActivate: [guestGuard], loadComponent: () => import('./features/auth/login.page').then((m) => m.LoginPage) },
  { path: 'register', title: 'Create account', canActivate: [guestGuard], loadComponent: () => import('./features/auth/register.page').then((m) => m.RegisterPage) },
  {
    path: 'account',
    canActivate: [authGuard],
    loadComponent: () => import('./features/account/account.layout').then((m) => m.AccountLayout),
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'profile' },
      { path: 'profile', title: 'My profile', loadComponent: () => import('./features/account/profile.page').then((m) => m.ProfilePage) },
      { path: 'addresses', title: 'Address book', loadComponent: () => import('./features/account/addresses.page').then((m) => m.AddressesPage) },
      { path: 'orders', title: 'My orders', loadComponent: () => import('./features/account/orders.page').then((m) => m.OrdersPage) },
      { path: 'orders/:number', title: 'Order details', loadComponent: () => import('./features/account/order-detail.page').then((m) => m.OrderDetailPage) },
    ],
  },
  {
    path: 'admin',
    canActivate: [adminGuard],
    loadChildren: () => import('./features/admin/admin.routes').then((m) => m.ADMIN_ROUTES),
  },
  { path: '**', title: 'Page not found', loadComponent: () => import('./features/not-found/not-found.page').then((m) => m.NotFoundPage) },
];
