#!/usr/bin/env python3
from __future__ import annotations

import hashlib
import json
import shlex
import subprocess
import tempfile
from pathlib import Path

from validate_aac_fixture import (
    ADTS_NAME,
    ASC_NAME,
    AUDIO_DIR,
    CONTINUOUS_ADTS_NAME,
    RAW_NAME,
    split_adts_frames,
    validate_fixture,
)

SELECTED_FRAME_INDEX = 2
SELECTED_FRAME_COUNT = 6
ASC = b"\x11\x90"
FFMPEG_COMMAND = [
    "ffmpeg",
    "-hide_banner",
    "-loglevel",
    "error",
    "-f",
    "lavfi",
    "-i",
    "sine=frequency=1000:sample_rate=48000:duration=0.25",
    "-map",
    "0:a:0",
    "-ac",
    "2",
    "-ar",
    "48000",
    "-c:a",
    "aac",
    "-profile:a",
    "aac_low",
    "-b:a",
    "192k",
    "-threads",
    "1",
    "-f",
    "adts",
    "-y",
    "source.adts",
]


def select_continuous_frames(frames: list[bytes]) -> list[bytes]:
    end = SELECTED_FRAME_INDEX + SELECTED_FRAME_COUNT
    if len(frames) < end:
        raise ValueError("FFmpeg did not produce source frames 2..7")
    return frames[SELECTED_FRAME_INDEX:end]


def record(data: bytes) -> dict[str, object]:
    return {"length": len(data), "sha256": hashlib.sha256(data).hexdigest()}


def main() -> int:
    version = subprocess.run(
        ["ffmpeg", "-version"],
        check=True,
        capture_output=True,
        text=True,
    ).stdout.strip()
    with tempfile.TemporaryDirectory() as temporary:
        subprocess.run(FFMPEG_COMMAND, cwd=temporary, check=True)
        encoded = (Path(temporary) / "source.adts").read_bytes()

    selected = select_continuous_frames(split_adts_frames(encoded))
    adts = selected[0]
    raw = adts[7:]
    continuous = b"".join(selected)

    AUDIO_DIR.mkdir(parents=True, exist_ok=True)
    files = {
        ADTS_NAME: adts,
        RAW_NAME: raw,
        ASC_NAME: ASC,
        CONTINUOUS_ADTS_NAME: continuous,
    }
    for name, data in files.items():
        (AUDIO_DIR / name).write_bytes(data)

    manifest = {
        "format": 1,
        "generator": {
            "ffmpegVersion": version,
            "command": FFMPEG_COMMAND,
            "selectedFrameIndex": SELECTED_FRAME_INDEX,
            "selectedFrameCount": SELECTED_FRAME_COUNT,
        },
        "files": {name: record(data) for name, data in sorted(files.items())},
    }
    (AUDIO_DIR / "fixture-manifest.json").write_text(
        json.dumps(manifest, indent=2, sort_keys=True) + "\n",
        encoding="utf-8",
    )

    rows = "\n".join(
        f"| `{name}` | {len(data)} | `{hashlib.sha256(data).hexdigest()}` |"
        for name, data in sorted(files.items())
    )
    (AUDIO_DIR / "README.md").write_text(
        "# Canonical AAC-LC Fixture\n\n"
        "One AAC-LC, 48 kHz, stereo, 1024-sample raw access unit.\n\n"
        f"- FFmpeg: `{version.splitlines()[0]}`\n"
        f"- Command: `{shlex.join(FFMPEG_COMMAND)}`\n"
        "- Selected zero-based ADTS frame range: `2..7` "
        f"({SELECTED_FRAME_COUNT} frames)\n"
        "- AudioSpecificConfig: `11 90`\n\n"
        "| File | Bytes | SHA-256 |\n"
        "|------|------:|---------|\n"
        f"{rows}\n\n"
        "The checked-in bytes and manifest are canonical. Different FFmpeg "
        "builds may emit different valid bytes; replacement requires reviewed "
        "wire-contract correction.\n",
        encoding="utf-8",
    )

    validate_fixture(AUDIO_DIR)
    print(f"wrote canonical AAC fixture to {AUDIO_DIR}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
