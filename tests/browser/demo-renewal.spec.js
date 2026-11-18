import { expect, test } from '@playwright/test';
import { signIn } from './session.js';

test('member can reserve after an old demo volume is renewed', async ({ page }) => {
  test.skip(process.env.BENCHLINE_OLD_VOLUME !== '1', 'Requires a demo volume seeded with the old clock and restarted.');
  await signIn(page);
  const response = await page.request.get('/api/board');
  expect(response.ok()).toBeTruthy();
  const board = await response.json();
  expect(board.revision).toBeGreaterThan(1);
  const cnc = board.stations.find(station => station.name === 'Desktop CNC');
  expect(board.slots.some(slot => slot.stationId === cnc.id && slot.available && new Date(slot.start) > new Date())).toBeTruthy();
  await page.getByRole('button', { name: 'Reserve Desktop CNC', exact: true }).click();
  await page.getByRole('button', { name: 'Confirm reservation' }).click();
  await expect(page.getByText('Reservation confirmed.')).toBeVisible();
  await page.getByRole('button', { name: 'My bookings' }).click();
  await page.getByRole('button', { name: 'Cancel Desktop CNC reservation' }).click();
  await expect(page.getByText('Reservation cancelled.')).toBeVisible();
});
