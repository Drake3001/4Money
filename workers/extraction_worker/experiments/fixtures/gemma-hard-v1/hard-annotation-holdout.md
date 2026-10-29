# Hard-set independent visual reference verification

Five document representations were checked against six original photographs before new gemma-hard-v1 predictions. No prediction output or OCR/parser output was consulted for this annotation task. Zero API calls; original references and code unchanged.

**5 representations, 90 reference purchase records, 4 independent physical transactions, 60 unique purchase records after grouping the duplicated transaction.** The 30-item Biedronka receipt appears both as a single photograph and as two overlapping photographs; both explicitly carry same_receipt_id=biedronka-20260516-63488. Do not treat these as independent test transactions or split them between development and test.

No original numerical, date, item-name or deposit corrections were required after source inspection. All original fields were preserved; verification metadata and per-item category were added. Every line passes quantity × unit_price rounded to cents = before_discount and before_discount + signed discount = total. Every document passes purchase sum = goods total = amount_due, and tax-summary sales/tax sums. No deposits or returns appear in these five cases.

| Reference stem | Purchases | Before discounts | Signed discounts | Goods / due | Existing uncertainties |
|---|---:|---:|---:|---:|---:|
| 20261019_181829 | 30 | 404.25 | -116.21 | 288.04 | 2 |
| biedronka-20260516-63488-parts | 30 | 404.25 | -116.21 | 288.04 | 2 |
| 20261010_201603 | 23 | 317.83 | -51.08 | 266.75 | 0 |
| IMG_6881 | 4 | 47.11 | 0.00 | 47.11 | 2 |
| 20261010_201656 | 3 | 11.53 | 0.00 | 11.53 | 0 |

## Preserved uncertainties and distinctions

- Both Biedronka views: CAFF? punctuation/character after CAFF and compact Vf/VF after Marchewki remain uncertain. They do not affect food classification or amounts. No catalogue brand/name completion was invented.
- IMG_6881: the small abbreviated cheese token remains uncertain; both weighed lines have clearly different quantities and totals and must remain separate purchases.
- For the 30-item receipt, two identical bouillon rows are separate purchases; OPUST name repetitions and image overlap are not purchases.
- For the 23-item Lidl, all products including salt/chocolate are food; VAT markers do not define the expense category.
- For 20261010_201656, the background receipt and reverse-side print are excluded, and a ready-made supermarket pizzerka remains food rather than restaurant spending.
- Lenor conditioner is household chemistry; Queen pocket tissues are cosmetics/hygiene. These classifications are sufficiently supported; no accepted_categories alternatives were necessary.
- No new numerical/date uncertainty was discovered; null unit fields remain unfilled where the receipt lacks an explicit unit token.

Primary category coverage across all five representations: żywność i napoje: 86, chemia gospodarcza: 2, kosmetyki i higiena: 2. Only three categories occur; this hard-set tests structure and arithmetic more than taxonomy breadth.

These labels are assistant-verified silver references, not human gold. Arithmetic agreement is a secondary check, not independent proof of every character.

## Additional naturally truncated control

`biedronka-63488-top-only.expected.json` uses ONLY original `data/input/biedronka-20260516-63488-parts/20261019_181833.jpg`, without cropping or image editing. It has **20 visible purchase names: 19 complete rows + 1 truncated Marchewki row**. The last row has null quantity/price/discount/amount fields. Header store and receipt number are visible. Date/time/currency/full total/due/payment/tax/deposit sections are outside this image and are null, with explicit expected_abstentions. No values were imported from the full receipt or second photograph. This dependent partial control is excluded from the full-purchase and independent-transaction totals above.
