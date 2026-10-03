import { Routes } from '@angular/router';

/** Lazy admin area. Every page is its own chunk under the `AdminLayout` shell; the parent route in app.routes.ts applies `adminGuard`. */
export const ADMIN_ROUTES: Routes = [
  {
    path: '',
    loadComponent: () => import('./admin.layout').then((m) => m.AdminLayout),
    children: [
      { path: '', pathMatch: 'full', title: 'Admin dashboard', loadComponent: () => import('./dashboard.page').then((m) => m.AdminDashboardPage) },
      { path: 'orders', title: 'Orders (admin)', loadComponent: () => import('./orders.page').then((m) => m.AdminOrdersPage) },
      { path: 'orders/:number', title: 'Order (admin)', loadComponent: () => import('./order-detail.page').then((m) => m.AdminOrderDetailPage) },
      { path: 'products', title: 'Products (admin)', loadComponent: () => import('./products.page').then((m) => m.AdminProductsPage) },
      { path: 'products/new', title: 'New product', loadComponent: () => import('./product-form.page').then((m) => m.AdminProductFormPage) },
      { path: 'products/:id', title: 'Edit product', loadComponent: () => import('./product-form.page').then((m) => m.AdminProductFormPage) },
      { path: 'categories', title: 'Categories (admin)', loadComponent: () => import('./categories.page').then((m) => m.AdminCategoriesPage) },
      { path: 'brands', title: 'Brands (admin)', loadComponent: () => import('./brands.page').then((m) => m.AdminBrandsPage) },
      { path: 'coupons', title: 'Coupons (admin)', loadComponent: () => import('./coupons.page').then((m) => m.AdminCouponsPage) },
      { path: '**', redirectTo: '' },
    ],
  },
];
