import { test, expect } from '@playwright/test';

// The model filter is dependent on the brand filter: it is only offered once a brand is
// selected, and it must not survive the brand being cleared.
test('model filter appears only once a brand is selected', async ({ page }) => {
  const group = (name: string) =>
    page.locator('.catalog-search-group').filter({
      has: page.getByRole('heading', { name, exact: true }),
    });

  const legend = group('Brand').getByRole('link').filter({ hasText: 'Legend' });

  await page.goto('/');
  await expect(page.getByRole('heading', { name: 'Ready for a new adventure?' })).toBeVisible();

  // No brand selected, so there is no model filter at all.
  await expect(group('Model')).toHaveCount(0);

  await legend.click();
  await expect(page).toHaveURL(/[?&]brand=/);

  // Selecting a brand reveals the model filter, scoped to that brand's models.
  await expect(group('Model')).toBeVisible();
  const everest = group('Model').getByRole('link').filter({ hasText: 'Everest Line 3' });
  await expect(everest).toBeVisible();

  await everest.click();
  // The space may be encoded as %20 or + depending on who built the URI.
  await expect(page).toHaveURL(/[?&]model=Everest(\+|%20)Line(\+|%20)3/);
  await expect(page.locator('.catalog-product')).toHaveCount(3);

  // Clearing the brand also clears the model selection, so no hidden filter is left
  // narrowing the results.
  await legend.click();
  await expect(page).not.toHaveURL(/[?&]model=/);
  await expect(group('Model')).toHaveCount(0);
  await expect(page.locator('.catalog-product')).toHaveCount(9);
});
