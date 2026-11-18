import { readFile } from 'node:fs/promises';
import { expect, test } from '@playwright/test';
import { signIn } from './session.js';
import { checkAccessibility } from './axe.js';

test('member can reserve and cancel a session with the keyboard', async ({ page }) => {
  await signIn(page);
  const reserve = page.getByRole('button', { name: 'Reserve Desktop CNC', exact: true });
  await reserve.focus();
  await page.keyboard.press('Enter');
  await expect(page.getByRole('dialog', { name: 'Reserve Desktop CNC' })).toBeVisible();
  await page.getByRole('button', { name: 'Confirm reservation' }).click();
  await expect(page.getByText('Reservation confirmed.')).toBeVisible();
  await expect(page.getByRole('dialog')).toHaveCount(0);
  await page.getByRole('button', { name: 'My bookings' }).click();
  await page.getByRole('button', { name: 'Cancel Desktop CNC reservation' }).click();
  await expect(page.getByText('Reservation cancelled.')).toBeVisible();
});

test('published shell survives offline and blocks writes', async ({ page, context }) => {
  await signIn(page);
  await page.evaluate(() => navigator.serviceWorker.ready.then(() => true));
  await expect.poll(() => page.evaluate(() => Boolean(navigator.serviceWorker.controller))).toBeTruthy();
  await context.setOffline(true);
  await expect(page.getByText('You are offline.', { exact: false })).toBeVisible();
  const reserve = page.getByRole('button', { name: 'Reserve Desktop CNC', exact: true });
  await expect(reserve).toBeDisabled();
  await expect(reserve).toHaveAccessibleDescription('Reconnect to change the schedule');
  await page.reload();
  await expect(page.getByRole('button', { name: 'Sign in', exact: true })).toBeVisible();
  await context.setOffline(false);
  await expect(page.getByRole('heading', { name: 'Workshop board' })).toBeVisible();
});

test('outage and recovery propagate to a separate member session', async ({ browser }) => {
  test.setTimeout(90000);
  const sessions = await Promise.all(Array.from({ length: 4 }, () => browser.newContext()));
  try {
    const [member, tech, coord, member2] = await Promise.all(sessions.map(context => context.newPage()));
    await Promise.all([signIn(member), signIn(tech, 'tech'), signIn(coord, 'coord'), signIn(member2, 'member2')]);

    await tech.getByRole('button', { name: 'Report outage Laser Cutter 01' }).click();
    await expect(member.getByText('Awaiting replacement')).toBeVisible();
    await expect(member2.getByText('Awaiting replacement')).toBeVisible();
    const unavailable = member.getByRole('button', { name: 'Reserve Laser Cutter 01', exact: true });
    await expect(unavailable).toBeDisabled();
    await expect(unavailable).toHaveAccessibleDescription('Station is out of service');
    const preview = coord.getByRole('button', { name: 'Preview plan for Laser Cutter 01' });
    await preview.click();
    const dialog = coord.getByRole('dialog', { name: 'Recovery plan' });
    await expect(dialog.getByText('2 replacement sessions, 0 still waiting')).toBeVisible();
    await expect(dialog.getByText('Open Space', { exact: true })).toHaveCount(2);
    await checkAccessibility(coord);
    await coord.keyboard.press('Escape');
    await expect(dialog).toHaveCount(0);
    await coord.getByRole('button', { name: 'Dark mode' }).click();
    await expect(coord.locator('html')).toHaveAttribute('data-theme', 'dark');
    await preview.click();
    await expect(dialog.getByText('2 replacement sessions, 0 still waiting')).toBeVisible();
    await checkAccessibility(coord);
    await tech.getByRole('button', { name: 'Restore station Laser Cutter 01' }).click();
    await expect(dialog.getByText('The schedule changed.', { exact: false })).toBeVisible();
    await expect(dialog.getByRole('button', { name: 'Publish recovery' })).toBeDisabled();
    await coord.keyboard.press('Escape');
    await expect(dialog).toHaveCount(0);
    await coord.getByRole('button', { name: 'Preview plan for Laser Cutter 01' }).click();
    await dialog.getByRole('button', { name: 'Publish recovery' }).click();
    await expect(coord.getByText('Recovery published. 2 reservations moved to Open Space.', { exact: true })).toBeVisible();
    await expect(member.getByText('Awaiting replacement')).toHaveCount(0);
    await expect(member.getByRole('complementary').getByText('Laser Cutter 02')).toBeVisible();
    await expect(member2.getByRole('complementary').getByText('Laser Cutter 02')).toBeVisible();
    await coord.getByRole('button', { name: 'Audit log' }).click();
    await expect(coord.getByText('Recovery published', { exact: true })).toBeVisible();
    await expect(coord.getByText(/UTC\+5, Dana Kassymova/)).toBeVisible();
    await checkAccessibility(coord);
    await coord.getByRole('button', { name: 'Light mode' }).click();
    await expect(coord.locator('html')).toHaveAttribute('data-theme', 'light');
    await checkAccessibility(coord);
    const pendingDownload = coord.waitForEvent('download');
    await coord.getByRole('link', { name: 'Export CSV' }).click();
    const csv = await pendingDownload;
    expect(csv.suggestedFilename()).toBe('benchline-audit.csv');
    const exportText = await readFile(await csv.path(), 'utf8');
    expect(exportText).toContain('"Recovery published"');
    expect(exportText).toContain('Recorded Astana time');
    expect(exportText).toMatch(/\+05:00"/);
  } finally {
    await Promise.all(sessions.map(context => context.close()));
  }
});
