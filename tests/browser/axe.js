import { expect } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';

export async function checkAccessibility(page) {
  await page.evaluate(async () => {
    const dialog = document.querySelector('dialog[open]');
    await Promise.allSettled((dialog?.getAnimations() ?? []).map(animation => animation.finished));
  });
  const results = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa']).analyze();
  expect(results.violations).toEqual([]);
}
