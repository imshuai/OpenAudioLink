# Canonical AAC-LC Fixture

One AAC-LC, 48 kHz, stereo, 1024-sample raw access unit.

- FFmpeg: `ffmpeg version 4.4.2-0ubuntu0.22.04.1 Copyright (c) 2000-2021 the FFmpeg developers`
- Command: `ffmpeg -hide_banner -loglevel error -f lavfi -i sine=frequency=1000:sample_rate=48000:duration=0.25 -map 0:a:0 -ac 2 -ar 48000 -c:a aac -profile:a aac_low -b:a 192k -threads 1 -f adts -y source.adts`
- Selected zero-based ADTS frame: `2`
- AudioSpecificConfig: `11 90`

| File | Bytes | SHA-256 |
|------|------:|---------|
| `aac-lc-48k-stereo-1024.adts` | 420 | `9b122450a3e73c2a0b2aec45b4439d7b7a3ddd4fc942cce599f5cd9b36a9260c` |
| `aac-lc-48k-stereo-1024.raw` | 413 | `a81a4ab313901a56dbe128fbd6004fdaad16fde6bd1e58076c39b545f9e6811e` |
| `aac-lc-48k-stereo.asc` | 2 | `b65f22439176f7827a0d82a66b34477888f0287450985d8e3dc836d7e32ce79b` |

The checked-in bytes and manifest are canonical. Different FFmpeg builds may emit different valid bytes; replacement requires reviewed wire-contract correction.
