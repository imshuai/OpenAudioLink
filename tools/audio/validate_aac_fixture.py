#!/usr/bin/env python3
from __future__ import annotations

import hashlib
import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
AUDIO_DIR = ROOT / "testdata" / "audio"
ADTS_NAME = "aac-lc-48k-stereo-1024.adts"
RAW_NAME = "aac-lc-48k-stereo-1024.raw"
ASC_NAME = "aac-lc-48k-stereo.asc"
MANIFEST_NAME = "fixture-manifest.json"
BINARY_NAMES = (ADTS_NAME, RAW_NAME, ASC_NAME)


class FixtureValidationError(ValueError):
    pass


def require(condition: bool, message: str) -> None:
    if not condition:
        raise FixtureValidationError(message)


def validate_adts(adts: bytes, raw: bytes) -> None:
    require(len(adts) >= 7, "ADTS header is shorter than 7 bytes")
    require(adts[0] == 0xFF and adts[1] & 0xF0 == 0xF0, "invalid ADTS sync")
    require((adts[1] >> 3) & 1 == 0, "ADTS must signal MPEG-4")
    require((adts[1] >> 1) & 3 == 0, "ADTS layer must be zero")
    require(adts[1] & 1 == 1, "ADTS protection_absent must be one")
    require((adts[2] >> 6) & 3 == 1, "ADTS must signal AAC-LC profile")
    require((adts[2] >> 2) & 15 == 3, "ADTS sample-rate index must be 3")
    channels = ((adts[2] & 1) << 2) | ((adts[3] >> 6) & 3)
    require(channels == 2, "ADTS channel configuration must be 2")
    require(adts[6] & 3 == 0, "ADTS must contain exactly one raw data block")
    length = ((adts[3] & 3) << 11) | (adts[4] << 3) | ((adts[5] >> 5) & 7)
    require(length == len(adts), "ADTS frame length does not match file length")
    payload = adts[7:]
    require(payload, "ADTS payload is empty")
    require(payload == raw, "stored raw payload does not match ADTS payload")


def validate_asc(asc: bytes) -> None:
    require(len(asc) == 2, "ASC must contain exactly two bytes")
    bits = int.from_bytes(asc, "big")
    require((bits >> 11) & 31 == 2, "ASC audio object type must be 2")
    require((bits >> 7) & 15 == 3, "ASC sample-rate index must be 3")
    require((bits >> 3) & 15 == 2, "ASC channel configuration must be 2")
    require((bits >> 2) & 1 == 0, "ASC frameLengthFlag must be zero")
    require((bits >> 1) & 1 == 0, "ASC dependsOnCoreCoder must be zero")
    require(bits & 1 == 0, "ASC extensionFlag must be zero")


def validate_manifest(manifest: dict[str, object], files: dict[str, bytes]) -> None:
    require(
        type(manifest.get("format")) is int and manifest.get("format") == 1,
        "manifest format must be 1",
    )
    generator = manifest.get("generator")
    require(isinstance(generator, dict), "manifest generator is missing")
    version = generator.get("ffmpegVersion")
    require(
        isinstance(version, str) and bool(version.strip()),
        "manifest FFmpeg version is empty",
    )
    command = generator.get("command")
    require(
        isinstance(command, list)
        and bool(command)
        and all(type(argument) is str and bool(argument) for argument in command),
        "manifest FFmpeg command is empty",
    )
    require(
        type(generator.get("selectedFrameIndex")) is int
        and generator.get("selectedFrameIndex") == 2,
        "manifest selected frame index must be 2",
    )
    records = manifest.get("files")
    require(isinstance(records, dict), "manifest files map is missing")
    for name, data in files.items():
        record = records.get(name)
        require(isinstance(record, dict), f"missing manifest record for {name}")
        require(
            type(record.get("length")) is int and record.get("length") == len(data),
            f"manifest length mismatch for {name}",
        )
        require(
            record.get("sha256") == hashlib.sha256(data).hexdigest(),
            f"manifest SHA-256 mismatch for {name}",
        )
    require(
        not (set(records) - set(files)),
        "manifest file set contains unexpected records",
    )


def validate_fixture(directory: Path = AUDIO_DIR) -> None:
    files = {name: (directory / name).read_bytes() for name in BINARY_NAMES}
    validate_adts(files[ADTS_NAME], files[RAW_NAME])
    validate_asc(files[ASC_NAME])
    manifest = json.loads((directory / MANIFEST_NAME).read_text(encoding="utf-8"))
    require(isinstance(manifest, dict), "manifest root must be an object")
    validate_manifest(manifest, files)


def main() -> int:
    try:
        validate_fixture()
    except (FixtureValidationError, OSError, UnicodeDecodeError, json.JSONDecodeError) as error:
        print(f"aac fixture validation failed: {error}", file=sys.stderr)
        return 1
    print("aac fixture validation ok")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
