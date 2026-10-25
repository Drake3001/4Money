# Holdout source-image reference verification

All 12 holdout documents were verified from original source images before new gemma-vision-v2 predictions; the already source-verified vision-v1 Lidl reference was reused for its 19 unique purchases. Zero API calls. Original references and code unchanged.

**12 documents; 63 reference purchases. Zero changes to existing numerical, date, item-name or deposit values.** All original fields are retained; category and verification metadata were added. Every item passes quantity × unit_price rounded to cents = before_discount; before_discount + signed discount = item total; sum items = goods total; goods + deposits - returns = amount_due. Arithmetic consistency supports but does not replace the image review.

Three category ambiguities were recorded prospectively on IMG_6798.JPEG: household air freshener, colouring discs and wardrobe fragrance. Primary category is household chemistry; accepted_categories also includes home and garden. Receipt text alone does not unambiguously settle these broad category boundaries. All other purchase categories are singleton labels.

Retained source uncertainties: compact yoghurt abbreviation in IMG_6882.JPEG; unprinted ORLEN time; Curry printed marker I/i in Lidl. The lemonade deposit quantity and unit price remain null because only the total is printed. No monetary/date uncertainty was discovered.

These are assistant-verified silver labels, not human gold. Existing historical notes are retained; current review_status supersedes their pending-review wording. Limited coverage (six categories, mostly groceries) remains a dataset limitation.

| Source | Purchases | Goods total | Due | Uncertainty entries |
|---|---:|---:|---:|---:|
| 20261018_090822.jpg | 12 | 72.71 | 73.71 | 0 |
| IMG_6794.JPEG | 7 | 37.00 | 37.00 | 0 |
| IMG_6798.JPEG | 6 | 59.47 | 59.47 | 3 |
| IMG_6882.JPEG | 4 | 15.45 | 15.45 | 1 |
| IMG_6885.JPEG | 3 | 26.39 | 26.89 | 0 |
| IMG_6886.JPEG | 1 | 19.79 | 19.79 | 0 |
| 20261019_181950.jpg | 1 | 219.34 | 219.34 | 1 |
| Screenshot_20261011_140305_appka.jpg | 2 | 6.29 | 6.29 | 0 |
| IMG_6904.JPEG | 6 | 44.96 | 45.96 | 0 |
| IMG_6896.JPEG | 1 | 6.99 | 6.99 | 0 |
| Screenshot_20261011_140250_appka.jpg | 1 | 8.50 | 9.00 | 0 |
| lidl-20261017-524861-parts | 19 | 221.45 | 221.45 | 1 |

Primary category coverage: żywność i napoje: 55, chemia gospodarcza: 3, alkohol i tytoń: 1, restauracje i kawiarnie: 1, transport i samochód: 1, dom i ogród: 2.
