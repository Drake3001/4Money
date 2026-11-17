# Profile image fixture

`profile-synthetic.heic` is a generated solid RGB (190, 40, 60) image, 1280 × 640.
It contains no real user data. It was generated with Pillow 13.0.0 and
pillow-heif 1.8.1 (`quality=80`) because Magick.NET decodes HEIC but does not
ship a HEVC encoder on Windows. Tests use this file for both HEIC/HEIF MIME types.

To regenerate with those development-only Python packages installed:

```python
from PIL import Image
import pillow_heif

pillow_heif.register_heif_opener()
Image.new("RGB", (1280, 640), (190, 40, 60)).save("profile-synthetic.heic", quality=80)
```
