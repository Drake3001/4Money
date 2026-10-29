# Hard-set development annotations — frozen before predictions

Review status: assistant_verified_not_human. Reviewed the three ORIGINAL photographs, then nine enlarged crops spanning their item and footer sections. EXIF orientation was applied only for visual inspection. No model predictions, OCR transcriptions or external APIs were read/used in preparing these labels. These references are assistant silver labels, not human gold.

## Scope and changes

80 purchase rows across three genuinely long/difficult receipts: 31 + 29 + 20. Added all 80 category labels using taxonomy v1. Coverage: 76 food and beverages, 3 animal supplies, 1 cosmetics/personal hygiene. No category alternatives were added because there is no unresolved category boundary requiring them. The set broadens difficulty substantially but still does not cover all 18 categories.

0 name corrections, 0 financial corrections, 0 date corrections, 0 missing/extra reference items. All original fields and item values are retained. Existing uncertain_fields are preserved rather than silently normalizing ambiguous typography. Review metadata and notes were added.

## Why these are hard

The two Biedronka receipts are long and dense with many abbreviated names, repeated purchases, signed discounts on adjacent OPUST rows, faint print, creases, shadows and perspective. The Lidl receipt has 20 individually discounted purchases, weight quantities with three decimal places, repeated dog treats, separate product/discount markers, and strong reverse-side bleed-through. The challenge is allocating observed numbers to the correct repeated row and keeping human food separate from pet treats, not merely recognizing one or two obvious products.

## Numerical verification

Every quantity, unit price, printed pre-discount gross amount, signed discount and post-discount amount was visually compared with the original image/enlarged crops. Independent Decimal checks passed for each rounded quantity x price, before_discount + discount = total, sum of purchase totals = document total = payable amount, sum of signed discounts, and gross turnover by tax group. These checks supplement image reading; numerical consistency alone would not prove the transcription.

- 31 rows: gross 323.64 - discounts 72.37 = payable 251.27; 15 explicit discounts.
- 29 rows: gross 396.85 - discounts 85.67 = payable 311.18; 16 explicit discounts.
- 20 rows: gross 235.20 - discounts 63.15 = payable 172.05; 20 explicit discounts.

## Remaining uncertainties

First receipt: faint name prefix Prosciutt/Cotto; I/l typography in the abbreviated butter name; l/I typography in Schlacciata. Second receipt: I/l typography in Nutella CacIn abbreviation. Third receipt: narrow printed marker I versus lowercase i. These were already documented in uncertain_fields and remain unresolved at the character level. They do not change identified product categories or any monetary value. No field with unresolved financial digits was forced to a convenient arithmetic solution.

## 20261018_090700.jpg

31 purchase rows; 15 discounted rows. The upper receipt is faded and creased, and quantities/prices on its first two rows are faint but readable in enlarged source crops. Repeated SzynSzwarDelAlpe100g rows are separate purchases. Receipt total 251.27 = gross before discounts 323.64 plus signed discounts -72.37. Printed date 2026-10-03 and time 06:51 differ from filename. Przysm Act Pet100g is an animal treat, not human food. DrażOdśwArbuzMięt45g is an edible breath-freshening sweet, not a hygiene product. Preserve the existing uncertainties in ProsciuttCotto, MasłoIrlmleDol and Schlacciata; they concern typography/abbreviation boundaries, not numerical values.

## 20261019_181904.jpg

29 purchase rows; 16 discounted rows. Long narrow receipt photographed at a perspective angle with shadows; small abbreviated product names. All quantities/prices/discounts confirmed in enlarged source crops. Papier Queen 10r has quantity 3, unit price 17.99, before_discount 53.97, discount -17.99 and total 35.98; it is toilet paper and assigned cosmetics/personal hygiene, not cleaning chemicals. Milk quantity 4 at 4.49 has discount -8.98 and total 8.98. Final bread rows have quantities 4 and 5. Total 311.18 = gross before discounts 396.85 plus signed discounts -85.67. Date 2026-10-17, time 06:52. Preserve original CacIn I/l typography uncertainty; no invented brand/variant expansion.

## 20261019_182028.jpg

20 purchase rows; all 20 have explicit signed OPUST discounts. Back-side advertising shows through the receipt, type is faint, and the document is photographed at an angle. Each repeated product-name discount line belongs to the preceding purchase; it is not a separate item. The two Snack d.psaSuperfood purchases remain separate and are animal supplies, not human food. Preserve raw printed marker F versus I separately from tax codes C/B. Turkey quantity 1.369 at 33.99 = gross 46.53; discount -23.22 gives 23.31. Chicken quantity 0.975 at 23.99 = 23.39; discount -3.47 gives 19.92. Other fractional quantities 1.19, 1.09 and 0.464 are printed without an explicit unit token, so units stay null. Total 172.05 = gross before discounts 235.20 plus discounts -63.15. Printed date 2026-05-16, time 07:35 differs greatly from filename. Existing narrow I/i marker uncertainty is retained; no financial fields remain unresolved.
