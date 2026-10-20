"""Reading order and original-fragment provenance, independent of the OCR engine."""

import math
import re
from dataclasses import dataclass, replace
from statistics import median

from ...contracts import OCRFragment, OCRResult


def upright_ocr(ocr: OCRResult) -> OCRResult:
    """Rotate the reading view, keeping original page/fragment evidence indices.

    OCR boxes are ordered geometrically, even when the recognized text is vertical.
    The fiscal heading and currency total establish the direction of the receipt.
    Without both anchors we retain the supplied orientation.
    """
    pages = []
    for page in ocr.pages:
        headings = [line for line in page.lines if re.search(r"\bparagon\b", line.text, re.IGNORECASE)]
        totals = [line for line in page.lines
                  if re.search(r"suma\s*:?\s*(?:pln|zł)", line.text, re.IGNORECASE)]
        elongated = [line for line in page.lines if len(line.text.strip()) >= 5]
        vertical = bool(elongated) and sum(
            height(line) > 2 * math.dist(line.box[0], line.box[1]) for line in elongated
        ) > len(elongated) / 2
        turns = 0
        if headings and totals:
            hx, hy = center(headings[0])
            tx, ty = center(totals[0])
            turns = (1 if tx < hx else -1) if vertical else (2 if ty < hy else 0)
        if not turns:
            pages.append(page)
            continue
        def rotate(point):
            x, y = point
            if turns == 1:
                return y, page.width - x
            if turns == -1:
                return page.height - y, x
            return page.width - x, page.height - y
        lines = []
        for line in page.lines:
            points = [rotate(point) for point in line.box]
            offset = turns % 4
            points = points[offset:] + points[:offset]
            lines.append(replace(line, box=tuple(points)))
        pages.append(replace(page, lines=tuple(lines),
                             width=page.height if vertical else page.width,
                             height=page.width if vertical else page.height))
    return replace(ocr, pages=tuple(pages))


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
        groups = []
        def distance(left, right):
            # Compare local baselines halfway between fragments: one global skew
            # cannot describe a curved receipt or differently sloped columns.
            x = (left["x"] + right["x"]) / 2
            def baseline(item):
                a, b = item["points"][:2]
                slope = (b[1] - a[1]) / (b[0] - a[0]) if b[0] != a[0] else 0
                return item["y"] + max(-0.5, min(0.5, slope)) * (x - item["x"])
            lx = [p[0] for p in left["points"]]
            rx = [p[0] for p in right["points"]]
            lw, rw = max(lx)-min(lx), max(rx)-min(rx)
            if lw > 2.5 * rw:
                x = right["x"]
            elif rw > 2.5 * lw:
                x = left["x"]
            overlap = min(max(lx), max(rx)) - max(min(lx), min(rx))
            if (overlap > 0.35 * min(lw, rw)
                    and abs(left["y"] - right["y"]) > 0.15 * min(left["height"], right["height"])):
                return math.inf
            return abs(baseline(left) - baseline(right)) / min(left["height"], right["height"])
        for item in sorted(projected, key=lambda value: (value["y"], value["x"])):
            candidates = [(max(distance(item, member) for member in group), index)
                          for index, group in enumerate(groups)]
            gap, index = min(candidates) if candidates else (math.inf, -1)
            if gap <= 0.55:
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
