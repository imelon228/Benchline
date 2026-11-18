import { expect, test } from '@playwright/test';
import { checkAccessibility } from './axe.js';
import { signIn } from './session.js';

test('sign-in, member board, booking and component library pass checks in both themes', async ({ page }) => {
  test.setTimeout(90000);
  await page.goto('/');
  await expect(page.getByRole('button', { name: 'Sign in', exact: true })).toBeVisible({ timeout: 15000 });
  await checkAccessibility(page);
  await page.getByRole('button', { name: 'Dark mode' }).click();
  await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark');
  await checkAccessibility(page);

  await signIn(page);
  const openBooking = async () => {
    await page.getByRole('button', { name: 'Reserve Desktop CNC', exact: true }).click();
    const dialog = page.getByRole('dialog', { name: 'Reserve Desktop CNC' });
    await expect(dialog).toBeVisible();
    await checkAccessibility(page);
    await page.getByRole('button', { name: 'Close reservation' }).click();
    await expect(dialog).toHaveCount(0);
  };
  await checkAccessibility(page);
  await openBooking();
  await page.getByLabel('Filter').selectOption('Laser cutting');
  await checkAccessibility(page);
  await page.getByRole('button', { name: 'Light mode' }).click();
  await expect(page.locator('html')).toHaveAttribute('data-theme', 'light');
  await checkAccessibility(page);
  await page.getByLabel('Filter').selectOption('');
  await openBooking();
  await page.getByRole('link', { name: 'Component library' }).click();
  await expect(page.getByRole('heading', { name: 'Component library' })).toBeVisible();
  await checkAccessibility(page);
  await page.getByRole('button', { name: 'Dark mode' }).click();
  await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark');
  await checkAccessibility(page);
});
