import { expect, test } from '@playwright/test';

test('component showcase loads its own assembly when opened', async ({ page }) => {
  const showcaseAsset = /\/Benchline\.Showcase\.[^/]*wasm$/;
  var loaded = false;
  page.on('request', route => { if (showcaseAsset.test(route.url())) loaded = true; });
  await page.goto('/');
  await expect(page.getByRole('button', { name: 'Sign in', exact: true })).toBeVisible();
  expect(loaded).toBe(false);
  await page.getByRole('link', { name: 'Component library' }).click();
  await expect(page.getByRole('heading', { name: 'Component library' })).toBeVisible();
  await expect(page.getByRole('list', { name: 'Example stations' })).toBeVisible();
  expect(loaded).toBe(true);
});
