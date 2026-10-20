"""Reading order and original-fragment provenance, independent of the OCR engine."""

import math
from dataclasses import dataclass
from statistics import median

from ...contracts import OCRFragment, OCRResult


def reconstruct_rows(raw):
    """Return a row view without replacing the source fragments or their boxes."""
    pages = []
    for page in raw["pages"]:
        fragments = [line for line in page["lines"] if line["text"].strip()]
        if not fragments:
            pages.append({**page, "lines": []})
            continue
        angle = median(math.atan2(line["box"][1][1] - line["box"][0][1],
                                 line["box"][1][0] - line["box"][0][0]) for line in fragments)
        c, s = math.cos(angle), math.sin(angle)
        projected = []
        for line in fragments:
            points = [(x*c + y*s, -x*s + y*c) for x, y in line["box"]]
            projected.append({"line": line, "points": points,
                              "x": sum(p[0] for p in points)/4,
                              "y": sum(p[1] for p in points)/4,
                              "height": max(1, max(p[1] for p in points)-min(p[1] for p in points))})
        tolerance = 0.5 * median(item["height"] for item in projected)
        groups = []
        for item in sorted(projected, key=lambda value: (value["y"], value["x"])):
            candidates = [(abs(item["y"]-median(member["y"] for member in group)), index)
                          for index, group in enumerate(groups)]
            distance, index = min(candidates) if candidates else (math.inf, -1)
            if distance <= tolerance:
                groups[index].append(item)
            else:
                groups.append([item])
        rows = []
        for group in sorted(groups, key=lambda values: median(item["y"] for item in values)):
            group.sort(key=lambda value: value["x"])
            points = [point for item in group for point in item["points"]]
            x1, x2 = min(p[0] for p in points), max(p[0] for p in points)
            y1, y2 = min(p[1] for p in points), max(p[1] for p in points)
            box = [[x*c-y*s, x*s+y*c] for x, y in ((x1,y1),(x2,y1),(x2,y2),(x1,y2))]
            length = sum(len(item["line"]["text"].strip()) for item in group)
            rows.append({"text": " ".join(item["line"]["text"].strip() for item in group),
                         "confidence": sum(item["line"]["confidence"] * len(item["line"]["text"].strip())
                                           for item in group)/length,
                         "box": box, "members": [item["line"] for item in group]})
        pages.append({**page, "lines": rows})
    return {**raw, "pages": pages}


def center(fragment: OCRFragment):
    return tuple(sum(point[i] for point in fragment.box) / 4 for i in (0, 1))


def height(fragment: OCRFragment):
    return max(1, (math.dist(fragment.box[0], fragment.box[3])
                   + math.dist(fragment.box[1], fragment.box[2])) / 2)


def y_at(fragment: OCRFragment, x: float):
    cx, cy = center(fragment)
    dx = fragment.box[1][0] - fragment.box[0][0]
    slope = (fragment.box[1][1] - fragment.box[0][1]) / dx if dx else 0
    return cy + max(-0.5, min(0.5, slope)) * (x - cx)


@dataclass(frozen=True)
class Row:
    page: int
    members: tuple[tuple[int, OCRFragment], ...]

    @property
    def text(self):
        return " ".join(fragment.text.strip() for _, fragment in self.members)

    @property
    def refs(self):
        return tuple((self.page, index) for index, _ in self.members)


def receipt_rows(ocr: OCRResult) -> list[Row]:
    rows = []
    for page in ocr.pages:
        raw = {"pages": [{"lines": [
            {"text": fragment.text, "confidence": fragment.confidence,
             "box": fragment.box, "index": index}
            for index, fragment in enumerate(page.lines)
        ]}]}
        for row in reconstruct_rows(raw)["pages"][0]["lines"]:
            rows.append(Row(page.page_number, tuple(
                (member["index"], page.lines[member["index"]]) for member in row["members"]
            )))
    return rows
