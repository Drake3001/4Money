"""Supported input formats shared by preparation and local file discovery."""

MIME_TYPES = {
    ".jpg": "image/jpeg", ".jpeg": "image/jpeg", ".png": "image/png",
    ".webp": "image/webp", ".heic": "image/heic", ".heif": "image/heif",
    ".tif": "image/tiff", ".tiff": "image/tiff", ".bmp": "image/bmp",
    ".pdf": "application/pdf",
}

IMAGE_FORMATS = {"JPEG", "MPO", "PNG", "WEBP", "HEIF", "TIFF", "BMP"}


def is_pdf(content):
    # PDF readers permit the header within the first 1024 bytes.
    return b"%PDF-" in content[:1024]


def has_image_signature(content):
    """Distinguish damaged supported images from unknown input formats."""
    return (content.startswith((b"\xff\xd8", b"\x89PNG", b"BM", b"II*\x00", b"MM\x00*"))
            or content[:4] == b"RIFF" and content[8:12] == b"WEBP"
            or content[4:8] == b"ftyp" and content[8:12] in
            {b"heic", b"heix", b"hevc", b"hevx", b"mif1", b"msf1"})
