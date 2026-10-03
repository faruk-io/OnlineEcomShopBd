import { bdt, expect, parseBdt, test, uniqueEmail } from './support/fixtures';
import { CartPage, CheckoutPage, Header, ListingPage, OrderDetailPage, ProductPage, RegisterPage } from './support/pages';

/**
 * The core customer journey, as one readable flow:
 * browse -> filter -> add to cart (guest) -> register (cart merges) -> checkout -> cash on delivery -> order tracking.
 * Runs against the seeded catalogue of the E2E API host (SQLite, recreated on every host start).
 */
test('guest browses, filters, registers and pays by cash on delivery', async ({ page, consoleErrors }) => {
  const header = new Header(page);
  const listing = new ListingPage(page);
  const product = new ProductPage(page);
  const cart = new CartPage(page);
  const checkout = new CheckoutPage(page);
  const order = new OrderDetailPage(page);
  const email = uniqueEmail('cod');

  await test.step('home page renders hero, categories and deals without console errors', async () => {
    await page.goto('/');
    await expect(page).toHaveTitle(/TechBazar/);
    await expect(page.locator('app-hero-slider')).toBeVisible();
    await expect(page.getByRole('heading', { name: 'Shop by category' })).toBeVisible();
    await expect(page.locator('ul.cat-grid li').first()).toBeVisible();
    await expect(page.getByRole('heading', { name: /Hot deals/ })).toBeVisible();
    await expect(page.locator('app-product-card').first()).toBeVisible();
    await expect(header.cartBadge).toHaveCount(0);
    consoleErrors.assertNone();
  });

  let allProcessors = 0;
  await test.step('open Processor from the mega menu', async () => {
    await header.openCategoryFromMegaMenu('Component', 'Processor');
    await expect(page).toHaveURL(/\/category\/processor$/);
    await expect(listing.heading).toHaveText('Processor');
    await expect(listing.cards.first()).toBeVisible();
    allProcessors = await listing.count();
    expect(allProcessors).toBeGreaterThan(3);
    await expect(listing.cards).toHaveCount(allProcessors);
  });

  let afterBrand = 0;
  await test.step('filter by brand: URL, count and every card follow', async () => {
    await listing.brand('AMD').check();
    await expect(page).toHaveURL(/[?&]brand=amd(&|$)/);
    await expect(listing.total).not.toHaveText(String(allProcessors));
    afterBrand = await listing.count();
    expect(afterBrand).toBeGreaterThan(0);
    expect(afterBrand).toBeLessThan(allProcessors);
    await expect(listing.cards).toHaveCount(afterBrand);
    await expect(listing.cards.locator('a.brand')).toHaveText(Array<string>(afterBrand).fill('AMD'));
    await expect(page.getByRole('list', { name: 'Active filters' })).toContainText('AMD');
  });

  let afterStock = 0;
  await test.step('filter in-stock only: pre-order items disappear', async () => {
    await listing.inStockOnly.check();
    await expect(page).toHaveURL(/[?&]inStock=true(&|$)/);
    await expect(page).toHaveURL(/[?&]brand=amd(&|$)/);
    await expect(listing.total).not.toHaveText(String(afterBrand));
    afterStock = await listing.count();
    expect(afterStock).toBeGreaterThan(1);
    expect(afterStock).toBeLessThan(afterBrand);
    await expect(listing.cards).toHaveCount(afterStock);
    await expect(listing.cards.locator('a.brand')).toHaveText(Array<string>(afterStock).fill('AMD'));
    await expect(listing.cards.locator('app-stock-badge')).toHaveText(Array<string>(afterStock).fill('In stock'));
  });

  await test.step('narrow with a price range, then remove that chip again', async () => {
    const max = page.getByRole('spinbutton', { name: 'Maximum price' });
    await max.fill('20000');
    await max.blur();
    await expect(page).toHaveURL(/[?&]maxPrice=20000(&|$)/);
    await expect(listing.cards).toHaveCount(1);
    for (const price of await listing.cards.locator('app-price .now').allInnerTexts()) {
      expect(parseBdt(price)).toBeLessThanOrEqual(20000);
    }
    await page.getByRole('list', { name: 'Active filters' }).getByRole('button', { name: /Remove filter ৳/ }).click();
    await expect(page).not.toHaveURL(/maxPrice/);
    await expect(listing.cards).toHaveCount(afterStock);
  });

  let firstName = '';
  let firstPrice = 0;
  await test.step('open a product page: specs and price in ৳, then add it to the cart as a guest', async () => {
    await listing.card(0).locator('.title a').click();
    await expect(page).toHaveURL(/\/product\/amd-/);
    firstName = await product.name.innerText();
    firstPrice = parseBdt(await product.price.innerText());
    expect(firstPrice).toBeGreaterThan(0);
    await expect(product.price).toContainText('৳');
    await expect(product.specs.getByRole('row', { name: /Socket/ })).toBeVisible();
    await expect(page.getByText('In stock').first()).toBeVisible();
    await product.addToCart.click();
    await expect(header.cartBadge).toHaveText('1');
  });

  let secondName = '';
  let secondPrice = 0;
  await test.step('go back to the filtered listing (state kept in URL) and add a second product', async () => {
    await page.goBack();
    await expect(page).toHaveURL(/brand=amd/);
    await expect(listing.cards).toHaveCount(afterStock);
    const second = listing.card(1);
    secondName = (await second.locator('.title').innerText()).trim();
    secondPrice = parseBdt(await second.locator('app-price .now').innerText());
    await listing.addToCartOnCard(1).click();
    await expect(header.cartBadge).toHaveText('2');
    expect(secondName).not.toBe(firstName);
  });

  const subtotal = firstPrice + secondPrice;
  await test.step('register a new customer through the UI; the guest cart merges into the account', async () => {
    await new RegisterPage(page).register({ fullName: 'Playwright Customer', email });
    await expect(page).toHaveURL('/');
    await expect(header.nav.getByRole('link', { name: /Hi, Playwright/ })).toBeVisible();
    await expect(header.cartBadge).toHaveText('2');
  });

  await test.step('cart page lists both items with correct totals', async () => {
    await header.cartLink.click();
    await expect(page).toHaveURL(/\/cart$/);
    await expect(cart.lines).toHaveCount(2);
    await expect(cart.row(firstName)).toBeVisible();
    await expect(cart.row(secondName)).toBeVisible();
    expect((await cart.lineTotals()).sort((a, b) => a - b)).toEqual([firstPrice, secondPrice].sort((a, b) => a - b));
    expect((await cart.lineTotals()).reduce((a, b) => a + b, 0)).toBe(subtotal);
    expect(await cart.subtotal()).toBe(subtotal);
  });

  await test.step('proceed to checkout and add a delivery address', async () => {
    await cart.checkout.click();
    await expect(page).toHaveURL(/\/checkout$/);
    await expect(page.getByRole('heading', { name: 'Checkout', level: 1 })).toBeVisible();
    await checkout.addAddress({
      fullName: 'Playwright Customer',
      phone: '01712345678',
      division: 'Dhaka',
      district: 'Dhaka',
      line: 'House 12, Road 5, Dhanmondi',
      postal: '1205',
    });
    await expect(page.getByRole('radio', { name: /Home.*Playwright Customer/s })).toBeChecked();
  });

  const delivery = 70;
  const discount = Math.min(Math.round(subtotal * 0.1), 1000);
  await test.step('home delivery shows the inside-Dhaka fee; WELCOME10 applies a discount', async () => {
    await expect(page.getByRole('radio', { name: /Home delivery/ })).toBeChecked();
    await expect(checkout.summaryRow('Delivery')).toHaveText('৳70');
    await expect(checkout.summaryRow('Subtotal')).toHaveText(bdt(subtotal));
    await checkout.applyCoupon('WELCOME10');
    await expect(page.getByRole('status').filter({ hasText: 'Coupon WELCOME10 applied' })).toBeVisible();
    await expect(checkout.summaryRow('Discount')).toContainText(`-${bdt(discount)}`);
  });

  let grandTotal = 0;
  await test.step('choose cash on delivery and check the total', async () => {
    const cod = page.getByRole('radio', { name: /Cash on delivery/ });
    await cod.check();
    await expect(cod).toBeChecked();
    grandTotal = subtotal + delivery - discount;
    await expect(checkout.summaryRow('Total').last()).toHaveText(bdt(grandTotal));
    expect(await checkout.grandTotal()).toBe(grandTotal);
  });

  let orderNumber = '';
  await test.step('place the order and land on the order page with its tracking timeline', async () => {
    await checkout.placeOrder.click();
    await expect(page).toHaveURL(/\/account\/orders\/TB-[A-Z0-9-]+/);
    orderNumber = /\/account\/orders\/(TB-[^/?#]+)/.exec(page.url())?.[1] ?? '';
    expect(orderNumber).toMatch(/^TB-/);
    await expect(order.placedBanner).toContainText(`Your order ${orderNumber} has been placed`);
    await expect(page.getByRole('heading', { name: `Order ${orderNumber}` })).toBeVisible();
    await expect(order.timeline).toBeVisible();
    await expect(order.timeline.locator('li')).not.toHaveCount(0);
    await expect(order.timeline.locator('li.current')).toContainText(/Pending|Placed|Confirmed/i);
    expect(await order.grandTotal()).toBe(grandTotal);
    await expect(page.getByRole('region', { name: 'Payment' }).or(page.locator('section[aria-labelledby="pm-h"]'))).toContainText('Cash on delivery');
    await expect(page.getByText(firstName)).toBeVisible();
    await expect(page.getByText(secondName)).toBeVisible();
  });

  await test.step('the order is listed in order history and the cart is empty', async () => {
    await page.goto('/account/orders');
    const row = page.getByRole('link', { name: new RegExp(orderNumber) });
    await expect(row).toBeVisible();
    await expect(row).toContainText(bdt(grandTotal));
    await expect(row).toContainText('2 items');
    await expect(header.cartBadge).toHaveCount(0);
    await header.cartLink.click();
    await expect(page.getByRole('heading', { name: 'Your cart is empty' })).toBeVisible();
    consoleErrors.assertNone();
  });
});
