#!/usr/bin/env python3
from __future__ import annotations

import argparse
import hashlib
import json
import struct
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "testdata" / "protocol"

MAGIC = b"OALP"
MAJOR = 1
MINOR = 0
HEADER_SIZE = 24
MAX_PACKET_SIZE = 65536
PACKET_HELLO = 0x01
PACKET_AUDIO = 0x05
CODEC_AAC_LC = 1
PLATFORM_ANDROID = 1
CAP_AAC_SUPPORTED = 1


def pack_header(packet_type: int, sequence: int, timestamp: int, payload: bytes) -> bytes:
    return b"".join(
        [
            MAGIC,
            struct.pack(">B", MAJOR),
            struct.pack(">B", MINOR),
            struct.pack(">B", packet_type),
            struct.pack(">B", 0),
            struct.pack(">I", sequence),
            struct.pack(">Q", timestamp),
            struct.pack(">I", len(payload)),
            payload,
        ]
    )


def pack_string(value: str) -> bytes:
    data = value.encode("utf-8")
    return struct.pack(">H", len(data)) + data


def hello_payload() -> bytes:
    return b"".join(
        [
            pack_string("Android Phone"),
            pack_string("1.0.0"),
            struct.pack(">B", MAJOR),
            struct.pack(">B", MINOR),
            struct.pack(">B", PLATFORM_ANDROID),
            struct.pack(">I", CAP_AAC_SUPPORTED),
        ]
    )


def audio_payload() -> bytes:
    encoded = bytes([0x11, 0x22, 0x33, 0x44])
    return b"".join(
        [
            struct.pack(">B", CODEC_AAC_LC),
            struct.pack(">I", 1),
            struct.pack(">Q", 123456789),
            struct.pack(">H", 20),
            struct.pack(">I", len(encoded)),
            encoded,
        ]
    )


def packet_set() -> dict[str, bytes]:
    valid_hello = pack_header(PACKET_HELLO, 1, 123456000, hello_payload())
    valid_audio = pack_header(PACKET_AUDIO, 2, 123456789, audio_payload())
    invalid_length = b"".join(
        [
            MAGIC,
            struct.pack(">B", MAJOR),
            struct.pack(">B", MINOR),
            struct.pack(">B", PACKET_AUDIO),
            struct.pack(">B", 0),
            struct.pack(">I", 3),
            struct.pack(">Q", 123456790),
            struct.pack(">I", MAX_PACKET_SIZE + 1),
        ]
    )
    return {
        "valid-hello.bin": valid_hello,
        "valid-audio-aac.bin": valid_audio,
        "invalid-magic.bin": b"BAD!" + valid_hello[4:],
        "invalid-length.bin": invalid_length,
    }


def manifest(files: dict[str, bytes]) -> dict[str, object]:
    return {
        "protocol": "1.0",
        "headerSize": HEADER_SIZE,
        "maxPacketSize": MAX_PACKET_SIZE,
        "byteOrder": "big-endian",
        "files": {
            name: {"length": len(data), "sha256": hashlib.sha256(data).hexdigest()}
            for name, data in sorted(files.items())
        },
    }


def write_files() -> None:
    OUT.mkdir(parents=True, exist_ok=True)
    files = packet_set()
    for name, data in files.items():
        (OUT / name).write_bytes(data)
    (OUT / "golden-manifest.json").write_text(
        json.dumps(manifest(files), indent=2, sort_keys=True) + "\n",
        encoding="utf-8",
    )


def check_files() -> int:
    files = packet_set()
    expected_manifest = json.dumps(manifest(files), indent=2, sort_keys=True) + "\n"
    errors: list[str] = []
    for name, data in files.items():
        path = OUT / name
        if not path.exists():
            errors.append(f"missing {path}")
        elif path.read_bytes() != data:
            errors.append(f"changed {path}")
    manifest_path = OUT / "golden-manifest.json"
    if not manifest_path.exists():
        errors.append(f"missing {manifest_path}")
    elif manifest_path.read_text(encoding="utf-8") != expected_manifest:
        errors.append(f"changed {manifest_path}")
    if errors:
        for error in errors:
            print(error)
        return 1
    print("protocol golden packets ok")
    return 0


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    if args.check:
        return check_files()
    write_files()
    print(f"wrote protocol golden packets to {OUT.relative_to(ROOT)}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
