import { expect, test } from '@playwright/test';

test('demo username and timetable cells work with the keyboard', async ({ page }) => {
  await page.goto('/');
  await page.getByRole('button', { name: 'member', exact: true }).click();
  await expect(page.getByLabel('Username')).toHaveValue('member');
  await page.getByLabel('Password').fill(process.env.BENCHLINE_DEMO_PASSWORD ?? 'Benchline-Demo!26');
  await page.getByRole('button', { name: 'Sign in', exact: true }).click();

  const row = page.getByRole('table', { name: /Equipment schedule/ }).getByRole('row').filter({ hasText: 'Desktop CNC' });
  await expect(page.getByText(/One-hour sessions in Astana time \(UTC\+5\)/)).toBeVisible();
  await expect(row.getByText('FabLab')).toBeVisible();
  const cells = row.locator('button[data-slot]:enabled');
  await expect.poll(() => cells.count()).toBeGreaterThan(1);
  await cells.first().focus();
  const column = await cells.first().getAttribute('data-column');
  const next = row.locator('xpath=following-sibling::tr')
    .locator(`button[data-slot][data-column="${column}"]:enabled`).first();
  await page.keyboard.press('ArrowDown');
  await expect(next).toBeFocused();
  await page.keyboard.press('ArrowUp');
  await expect(cells.first()).toBeFocused();
  await page.keyboard.press('ArrowRight');
  await expect(cells.nth(1)).toBeFocused();
  const name = await cells.nth(1).getAttribute('aria-label');
  const timeLabel = name.match(/\d{2}:\d{2} UTC\+5$/)?.[0];
  expect(timeLabel).toBeTruthy();
  const selectedColumn = Number(await cells.nth(1).getAttribute('data-column'));
  const headerTime = page.locator('.time-heading time').nth(selectedColumn);
  const displayedInstant = await headerTime.getAttribute('datetime');
  expect(displayedInstant).toMatch(/\+05:00$/);
  const response = await page.request.get('/api/board');
  expect(response.ok()).toBeTruthy();
  const board = await response.json();
  expect(board.slots.some(slot => new Date(slot.start).getTime() === new Date(displayedInstant).getTime())).toBeTruthy();
  await expect(headerTime).toHaveText(new Date(new Date(displayedInstant).getTime() + 5 * 3600000).toISOString().slice(11, 16));
  await page.keyboard.press('Enter');
  const dialog = page.getByRole('dialog', { name: 'Reserve Desktop CNC' });
  await expect(dialog).toBeVisible();
  await expect(dialog.getByLabel('Session start · Astana time (UTC+5)').locator('option:checked')).toContainText(timeLabel);
  await page.keyboard.press('Escape');
  await expect(dialog).toHaveCount(0);
  await expect(cells.nth(1)).toBeFocused();
});

test('an unknown route provides a working return link', async ({ page }) => {
  await page.goto('/missing-workshop-page');
  await expect(page.getByRole('heading', { name: 'Page not found' })).toBeVisible();
  await page.getByRole('link', { name: 'Return to the board' }).click();
  await expect(page.getByRole('button', { name: 'Sign in', exact: true })).toBeVisible();
});
