"""OCR geometry as document data, with no filenames or reference labels."""


def ocr_pages(ocr):
    pages = []
    for page in ocr.pages:
        if page.width <= 0 or page.height <= 0:
            raise ValueError("OCR page dimensions must be positive")
        pages.append({"page": page.page_number, "fragments": [
            {"id": f"p{page.page_number}:f{index}", "text": fragment.text,
             "confidence": round(fragment.confidence, 3),
             "box": [[round(x / page.width * 1000), round(y / page.height * 1000)]
                     for x, y in fragment.box]}
            for index, fragment in enumerate(page.lines)
        ]})
    return pages
