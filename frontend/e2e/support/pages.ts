import { expect, type Locator, type Page } from '@playwright/test';
import { parseBdt, PASSWORD } from './fixtures';

/** Page objects: thin wrappers that hold selectors so specs read as a journey. */

export class Header {
  constructor(private readonly page: Page) {}
  get nav(): Locator {
    return this.page.getByRole('navigation', { name: 'Account and shopping' });
  }
  get cartLink(): Locator {
    return this.nav.getByRole('link', { name: /cart/i });
  }
  /** The numeric badge; absent when the cart is empty. */
  get cartBadge(): Locator {
    return this.cartLink.locator('.badge');
  }

  async openCategoryFromMegaMenu(root: string, child: string): Promise<void> {
    const mega = this.page.getByRole('navigation', { name: 'Product categories' });
    await mega.getByRole('link', { name: root, exact: true }).hover();
    const panel = mega.getByRole('group', { name: `${root} subcategories` });
    await expect(panel).toBeVisible();
    await panel.getByRole('link', { name: new RegExp(`^${child}\\b`) }).click();
  }
}

export class ListingPage {
  constructor(private readonly page: Page) {}
  get heading(): Locator {
    return this.page.getByRole('heading', { level: 1 });
  }
  get filters(): Locator {
    return this.page.getByRole('complementary', { name: 'Product filters' });
  }
  get results(): Locator {
    return this.page.getByRole('region', { name: 'Product results' });
  }
  get cards(): Locator {
    return this.results.locator('app-product-card');
  }
  get total(): Locator {
    return this.results.locator('p.total strong');
  }

  async count(): Promise<number> {
    return Number(await this.total.innerText());
  }
  brand(name: string): Locator {
    return this.filters.getByRole('checkbox', { name: new RegExp(`^${name}\\s*\\d*$`) });
  }
  get inStockOnly(): Locator {
    return this.filters.getByRole('checkbox', { name: 'In stock only' });
  }
  card(index: number): Locator {
    return this.cards.nth(index);
  }
  cardNames(): Promise<string[]> {
    return this.cards.locator('.title').allInnerTexts();
  }
  addToCartOnCard(index: number): Locator {
    return this.card(index).getByRole('button', { name: /^Add to cart/ });
  }
}

export class ProductPage {
  constructor(private readonly page: Page) {}
  get name(): Locator {
    return this.page.getByRole('heading', { level: 1 });
  }
  get price(): Locator {
    return this.page.locator('article.product app-price .now');
  }
  get specs(): Locator {
    return this.page.locator('#specification');
  }
  get addToCart(): Locator {
    return this.page.locator('article.product').getByRole('button', { name: 'Add to cart', exact: true });
  }
}

export class RegisterPage {
  constructor(private readonly page: Page) {}
  async register(user: { fullName: string; email: string; phone?: string }): Promise<void> {
    await this.page.goto('/register');
    await this.page.getByLabel('Full name').fill(user.fullName);
    await this.page.getByLabel('Email').fill(user.email);
    await this.page.getByLabel(/Mobile number/).fill(user.phone ?? '');
    await this.page.getByLabel('Password', { exact: true }).fill(PASSWORD);
    await this.page.getByLabel('Confirm password').fill(PASSWORD);
    await this.page.getByRole('button', { name: 'Create account' }).click();
  }
}

export class CartPage {
  constructor(private readonly page: Page) {}
  get lines(): Locator {
    return this.page.locator('ul.lines > li.line');
  }
  get summary(): Locator {
    return this.page.getByRole('complementary', { name: 'Order summary' });
  }
  get checkout(): Locator {
    return this.page.getByRole('link', { name: 'Proceed to checkout' });
  }
  row(name: string | RegExp): Locator {
    return this.lines.filter({ has: this.page.getByRole('link', { name }) });
  }
  async subtotal(): Promise<number> {
    return parseBdt(await this.summary.locator('dl > div', { hasText: 'Subtotal' }).locator('dd').innerText());
  }
  async lineTotals(): Promise<number[]> {
    const texts = await this.lines.locator('.total').allInnerTexts();
    return texts.map(parseBdt);
  }
}

export class CheckoutPage {
  constructor(private readonly page: Page) {}
  get summary(): Locator {
    return this.page.getByRole('complementary', { name: 'Order summary' });
  }
  get placeOrder(): Locator {
    return this.page.getByRole('button', { name: /^Place order/ });
  }
  get addAddressButton(): Locator {
    return this.page.getByRole('button', { name: '+ Add a new address' });
  }

  summaryRow(label: string | RegExp): Locator {
    return this.summary.locator('dl > div').filter({ has: this.page.locator('dt', { hasText: label }) }).locator('dd');
  }
  async grandTotal(): Promise<number> {
    return parseBdt(await this.summaryRow('Total').last().innerText());
  }
  async addAddress(a: { fullName: string; phone: string; division: string; district: string; line: string; postal?: string }): Promise<void> {
    const form = this.page.getByRole('form', { name: 'New address' });
    await form.getByLabel('Full name').fill(a.fullName);
    await form.getByLabel('Mobile number').fill(a.phone);
    await form.getByLabel('Division').selectOption(a.division);
    await form.getByLabel('District').fill(a.district);
    await form.getByLabel('Street address').fill(a.line);
    if (a.postal) await form.getByLabel(/Postal code/).fill(a.postal);
    await form.getByRole('button', { name: 'Save address' }).click();
  }
  async applyCoupon(code: string): Promise<void> {
    await this.page.getByLabel('Coupon code').fill(code);
    await this.page.getByRole('button', { name: 'Apply', exact: true }).click();
  }
}

export class OrderDetailPage {
  constructor(private readonly page: Page) {}
  get placedBanner(): Locator {
    return this.page.getByRole('status').filter({ hasText: 'has been placed' });
  }
  get timeline(): Locator {
    return this.page.locator('ol.timeline');
  }
  get totals(): Locator {
    return this.page.locator('section[aria-labelledby="it-h"] dl');
  }
  async grandTotal(): Promise<number> {
    return parseBdt(await this.totals.locator('div.grand dd').innerText());
  }
}
