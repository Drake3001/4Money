# Post-hoc audit of frozen holdout baseline

This is interpretation of `data/output/2026-10-25_103739_852ypuo7/holdout-baseline/report.json` against the unchanged blind `holdout.review.json`. No labels, taxonomy, prompt or accuracy score were changed. IDs below are zero-based parser item IDs. Reference labels remain assistant-produced silver labels, not human gold.

## What exact name matching missed

There are 31 nonmatching names among the 68 names scored by the frozen benchmark. For explanatory purposes only, their primary causes are: 1 formatting variant, 6 unresolved Polish-letter/diacritic corrections, 3 semantic/token corruptions introduced by the model, and 21 inherited parser/OCR contaminations or unresolved OCR errors. Causes can overlap: for example, a merged amount may accompany missing Polish letters. These counts are not an alternative accuracy score.

| Source | ID | Model result / relevant fragment | Primary cause and interpretation |
|---|---:|---|---|
| 20261018_090822.jpg | 0 | `NapGazPolVitMIX 1,5l` | Formatting variant. Semantically identical to the reference; the accepted list has fully joined or fully spaced versions but not this partly spaced variant. No new product detail invented. |
| 20261018_090822.jpg | 1 | `Woda Gaz Zyw 1,75l` | Missing Polish letter: `Żyw`. |
| 20261018_090822.jpg | 5 | `Chleb kr ksiaz 400g` | Missing diacritics in `książ`. Expanding `Chle` to `Chleb` is reasonable and was not enumerated, but the output still fails to restore the readable Polish letters. |
| 20261018_090822.jpg | 6 | `JabtPolskieGalaluz 00` | Inherited OCR contamination: wrong letter and trailing `00`; model requests review. |
| 20261018_090822.jpg | 7 | `Mieso Miel Wiephot 400g` | Unresolved OCR corruption of `Mięso ... WiepWoł`; wrong meat abbreviation retained without review. |
| 20261018_090822.jpg | 8 | `Sos Culine Bolonsk 520` | Missing `ń` in `Bolońsk`; adding spaces is acceptable. |
| 20261018_090822.jpg | 9 | `Przyp Pap Wekan 16 20g` | Unresolved compact OCR abbreviation (`WęKam` in silver reference). Visible digits retained. The precise short token warrants human confirmation; this is not evidence for inventing a brand expansion. |
| IMG_6794.JPEG | 4 | `BIO KONC.CYTR.LIM 20OML` | Inherited numeric OCR error `20O` versus visually confirmed `200`. Kept with review; conservative refusal is preferable to guessing a size without reliable evidence. |
| IMG_6794.JPEG | 6 | `CEBULA ZŁOTA KG` | Model semantic corruption: source says `ŻÓŁTA`; golden and yellow are not interchangeable product labels here. No review requested. |
| IMG_6798.JPEG | 1 | `SOKOL.SZYNE.DROB. 10OG` | Inherited numeric OCR error `10O` versus source `100`; retained with review. |
| IMG_6798.JPEG | 2 | `ODŚWIEŻACZ POW 2EL 150G` | Unresolved OCR `2EL` versus `ŻEL`; adds correct Polish letters elsewhere but retains the false digit without review. |
| IMG_6798.JPEG | 5 | `SASY. ZAPACH. DO SZAFY` | Model token corruption: previously valid `SASZ.` becomes unsupported `SASY.`. No review requested. |
| IMG_6882.JPEG | 1 | `Jogurt Frukys Trus 150g` | Unresolved OCR of the printed `FruWyś` abbreviation; spacing alone does not repair it. |
| IMG_6886.JPEG | 0 | `ROLLO KEBAB (MALE)` | Missing `Ł` in `MAŁE`. |
| IMG_6904.JPEG | 1 | `NAP MONST RUBY 500nl-A` | Unit OCR corruption: source has `ml`, output retains `nl`; review requested. VAT suffix retention is already allowed and is not the problem. |
| lidl-20261017-524861-parts | 3 | `Marlenka Miod. kulki Falaow21` | Inherited trailing OCR garbage; review requested. |
| lidl-20261017-524861-parts | 4 | `20,170 Ser cheddar kawatek` | Inherited previous-line amount plus incorrect Polish letter; review requested. |
| lidl-20261017-524861-parts | 5 | `Boczek w plast.130g box` | Inherited trailing garbage `box`; no review requested. |
| lidl-20261017-524861-parts | 6 | `9,160 Ziemniaki macz.2.5kg F lii9sol w dsis1` | Merged previous-line amount and adjacent OCR garbage; review requested. |
| lidl-20261017-524861-parts | 7 | `Pyzy z miesen proz.` | Inherited OCR errors in `mięsem mroż.`; review requested. |
| lidl-20261017-524861-parts | 8 | `1.790 Butka z dt. ferment.` | Previous-line amount merged into name plus Polish-letter errors; review requested. |
| lidl-20261017-524861-parts | 9 | `Cukinia tuzwiyq.bi f xageaob` | Inherited merged OCR garbage after readable `Cukinia`; review requested. |
| lidl-20261017-524861-parts | 10 | `Szynka Szuarcualdzka` | Inherited OCR corruption of `Szwarcwaldzka`; review requested. |
| lidl-20261017-524861-parts | 11 | `Śliwki węgierskie 1kg` | Model semantic overcorrection. Source cultivar is `węgierki`; `węgierskie` may imply origin and is not a faithful equivalent. No review requested. |
| lidl-20261017-524861-parts | 16 | `Boczek w plast.1309` | Inherited unit OCR error: `130g` misread as `1309`; review requested. No basis to treat 1309 as a real weight. |
| lidl-20261017-524861-parts | 17 | `Ziemniaki nacz. 2.5kg` | Some garbage removed, but `mącz.` still corrupted as `nacz.`; no review requested. |
| lidl-20261017-524861-parts | 18 | `Pyzy z mięsem mroz.` | Missing `ż` in `mroż.`. |
| lidl-20261017-524861-parts | 19 | `Bułka z dt. ferment.` | Polish-letter OCR error remains: `dł.` printed as `dt.`. |
| lidl-20261017-524861-parts | 23 | `6.410 Dynia Pizmowa luz` | Previous-line amount still attached; also missing `ż` in `Piżmowa`; review requested. |
| lidl-20261017-524861-parts | 25 | `5,410 Curry XXL` | Previous-line amount attached to product; review requested. |
| lidl-20261017-524861-parts | 28 | `Cebula czerwona luz L` | Trailing OCR artefact retained without review. |

At least the partly spaced drink name demonstrates an exact-match false negative. Other reasonable expansions could exist outside the finite accepted list. However, the overall name gap is not explained away by formatting: most listed outputs still contain observable OCR errors, residual garbage or changed product meaning. The benchmark's two exact-match regressions are the onion and sachet changes; the plum overcorrection is also undesirable even though its raw name was already incorrect.

## Categories and uncertainty

All three category mismatches are on `IMG_6798.JPEG`: air freshener, colouring discs and wardrobe fragrance. The model assigns `dom i ogród`, whereas the blind silver labels use `chemia gospodarcza`. These are genuine taxonomy boundary/product-use uncertainties already flagged during blind annotation. The colouring-disc purpose in particular needs a human/product confirmation. This concentration is evidence to review the definitions, not evidence that all three classifications are unquestionably wrong. Do not retroactively change these labels to improve the reported score.

The model requested review on 23/73 entries, including the five entries with no defensible reference identity. It also leaves several visibly damaged strings unflagged, as documented above. Thus high category accuracy does not establish reliable spelling correction or reliable automatic acceptance. The review flag and its precision/recall need separate evaluation with a human-approved review policy.

## Parser completeness and multi-photo overlap

Summing `parser_evaluation.groups.items` across the 12 holdout documents gives **63 expected purchases, 73 parser entries, 63 matched, 0 missing indices and 10 excess indices**. The entire count excess occurs in the grouped Lidl document: **19 expected purchases, 29 parser entries, 19 matched, 0 missing and 10 excess**. The evaluator selects excess IDs `[5,6,7,8,9,10,11,12,13,14]` under its matching assignment. These IDs are assignment results, not a claim that each corresponding photographed product is fictitious.

The two Lidl images overlap substantially. The parser treats repeated views of purchases as extra entries and also emits detached amount/unreadable-name fragments. Blind notes mark the repeated observations independently of which occurrence the matcher chooses. The enrichment model preserves item IDs/count, so even perfect classification of every entry cannot remove this structural overcount.

Zero `missing_indices` is a narrow matching result against the existing parser reference. Two other documents have an entry whose parser name is null despite a readable product on the photo. A placeholder matched on other fields does not equal complete product recognition. These parser references also remain pending user review. Therefore neither the 0-missing result nor category accuracy should be presented as end-to-end receipt correctness.

## Practical interpretation

The next engineering priority is reconstructing multi-photo receipt structure and distinguishing name tokens from amounts before enrichment. Further prompt work should target demonstrable regressions and conservative handling of uncertain OCR, while retaining numeric protections. A human pass should confirm category boundaries and difficult abbreviations. Future experiments need a newly held-out set because this post-hoc audit has now exposed the current holdout's errors; it must not silently become another prompt-development set while retaining the same claims of independence.
