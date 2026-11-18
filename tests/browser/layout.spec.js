import { expect, test } from '@playwright/test';
import { signIn } from './session.js';

test('board fits the viewport and supports theme and filter controls', async ({ page }, testInfo) => {
  await signIn(page);
  await page.getByRole('button', { name: 'Dark mode' }).click();
  await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark');
  await page.reload();
  await expect(page.getByRole('button', { name: 'Light mode' })).toBeVisible();
  await page.getByRole('button', { name: 'Light mode' }).click();
  const table = page.getByRole('table', { name: /Equipment schedule/ });
  const widths = testInfo.project.name === 'chromium-desktop' ? [360, 820, 1440] : [page.viewportSize().width];
  for (const width of widths) {
    await page.setViewportSize({ width, height: 900 });
    await expect(table.getByRole('rowheader')).toHaveCount(4);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBeTruthy();
    await page.getByRole('button', { name: 'Dark mode' }).click();
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBeTruthy();
    await page.getByRole('button', { name: 'Light mode' }).click();
  }
  await page.getByLabel('Filter').selectOption('Laser cutting');
  await expect(table.getByRole('rowheader')).toHaveCount(2);
});
