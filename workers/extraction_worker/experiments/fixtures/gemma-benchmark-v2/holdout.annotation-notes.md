# Blind holdout annotations

These are assistant-produced **silver labels**, not human gold labels. The reviewer inspected all 13 source images for 12 holdout documents without consulting model predictions, development labels or prior result reports. Every one of the 73 parser entries is preserved, including phantom records and repeated observations from overlapping photographs.

Category coverage: food and beverages 60, household chemistry 3, home and garden 2, alcohol and tobacco 1, restaurants and cafes 1, transport and car 1. Five entries have no defensible product identity/category. Thus this split covers only 6 of 18 categories and is heavily weighted towards groceries; it cannot establish general classification accuracy across the application taxonomy.

The three household freshener/colouring-disc products have silver chemistry labels. Human confirmation of those product-use/category boundaries would improve reference quality.

Five entries have `accepted_names=null` and `category=null`: two empty parser names, a detached amount, another empty grouped-receipt entry and an unreadable fragment. The source image being readable does not grant the text-only corrector a licence to invent a missing parser product.

Some readable names include merged prices, VAT markers or adjacent OCR garbage. Per-item notes identify these structural failures. Expected cleaned names in these cases should be analysed separately from ordinary spelling correction. Two other names contain letter/digit confusions in their sizes; those repairs are explicitly flagged as visually confirmed numeric OCR corruption. A correction guard may correctly refuse these repairs without that visual evidence.

Several grouped-receipt entries are overlapping observations of the same purchases. They remain separate rows here because the current benchmark operates on parser entries. This file does not establish product-count accuracy, successful deduplication, missing-product recall, arithmetic correctness or end-to-end receipt accuracy.

Legitimate abbreviations remain acceptable. Explicit VAT-suffix retention/removal variants are included where the parser contains that suffix. Case/spacing normalization and a semantic manual audit should be distinguished from exact string acceptance: an unlisted equivalent expansion can be a correct name despite failing an exact-match metric.
