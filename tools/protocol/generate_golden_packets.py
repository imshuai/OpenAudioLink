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
PACKET_WELCOME = 0x02
PACKET_START_STREAM = 0x03
PACKET_STREAM_READY = 0x04
PACKET_AUDIO = 0x05
PACKET_STOP_STREAM = 0x06
PACKET_PING = 0x07
PACKET_PONG = 0x08
PACKET_ERROR = 0x09
CODEC_AAC_LC = 1
PLATFORM_ANDROID = 1
PLATFORM_WINDOWS = 2
CAP_AAC_SUPPORTED = 1
WELCOME_SUCCESS = 0
STREAM_READY_SUCCESS = 0
ERROR_UNSUPPORTED_CODEC = 1003
ERROR_SEVERITY_RECOVERABLE = 2


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


def welcome_payload() -> bytes:
    return b"".join(
        [
            struct.pack(">B", WELCOME_SUCCESS),
            pack_string("Windows PC"),
            pack_string("1.0.0"),
            struct.pack(">Q", 0x0102030405060708),
        ]
    )


def start_stream_payload() -> bytes:
    return b"".join(
        [
            struct.pack(">B", CODEC_AAC_LC),
            struct.pack(">I", 48000),
            struct.pack(">B", 2),
            struct.pack(">I", 192000),
            struct.pack(">H", 20),
        ]
    )


def stream_ready_payload() -> bytes:
    return b"".join(
        [
            struct.pack(">B", STREAM_READY_SUCCESS),
            struct.pack(">B", CODEC_AAC_LC),
            struct.pack(">I", 48000),
            struct.pack(">B", 2),
        ]
    )


def ping_payload() -> bytes:
    return struct.pack(">I", 5) + struct.pack(">Q", 123456005)


def error_payload() -> bytes:
    return b"".join(
        [
            struct.pack(">H", ERROR_UNSUPPORTED_CODEC),
            struct.pack(">B", ERROR_SEVERITY_RECOVERABLE),
            pack_string("Unsupported codec"),
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
    valid_welcome = pack_header(PACKET_WELCOME, 2, 123456001, welcome_payload())
    valid_start_stream = pack_header(PACKET_START_STREAM, 3, 123456002, start_stream_payload())
    valid_stream_ready = pack_header(PACKET_STREAM_READY, 4, 123456003, stream_ready_payload())
    valid_ping = pack_header(PACKET_PING, 5, 123456004, ping_payload())
    valid_pong = pack_header(PACKET_PONG, 6, 123456004, ping_payload())
    valid_stop_stream = pack_header(PACKET_STOP_STREAM, 7, 123456006, b"")
    valid_error = pack_header(PACKET_ERROR, 8, 123456007, error_payload())
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
        "valid-welcome.bin": valid_welcome,
        "valid-start-stream.bin": valid_start_stream,
        "valid-stream-ready.bin": valid_stream_ready,
        "valid-ping.bin": valid_ping,
        "valid-pong.bin": valid_pong,
        "valid-stop-stream.bin": valid_stop_stream,
        "valid-error.bin": valid_error,
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
