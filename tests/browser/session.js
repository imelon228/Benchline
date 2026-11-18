import { expect } from '@playwright/test';

const names = {
  member: 'Aidos Serikov',
  member2: 'Aruzhan Nurgaliyeva',
  tech: 'Nurlan Tolegenov',
  coord: 'Dana Kassymova'
};

export async function signIn(page, username = 'member') {
  await page.goto('/');
  await page.getByLabel('Username').fill(username);
  await page.getByLabel('Password').fill(process.env.BENCHLINE_DEMO_PASSWORD ?? 'Benchline-Demo!26');
  await page.getByRole('button', { name: 'Sign in', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Workshop board' })).toBeVisible();
  await expect(page.getByText('Live', { exact: true })).toBeVisible();
  await expect(page.getByRole('banner').getByText(names[username], { exact: true })).toBeVisible();
}
