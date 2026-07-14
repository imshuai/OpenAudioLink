from __future__ import annotations

import copy
import hashlib
import io
import unittest
from contextlib import redirect_stderr
from unittest.mock import patch

from validate_aac_fixture import (
    FixtureValidationError,
    validate_adts,
    validate_asc,
    validate_manifest,
    main,
)

RAW = b"\x12\x34\x56"
ADTS = "aac-lc-48k-stereo-1024.adts"
RAW_NAME = "aac-lc-48k-stereo-1024.raw"
ASC = "aac-lc-48k-stereo.asc"


def adts(
    payload: bytes = RAW,
    *,
    mpeg_id: int = 0,
    layer: int = 0,
    protection: int = 1,
    profile: int = 1,
    rate: int = 3,
    channels: int = 2,
    blocks: int = 0,
) -> bytes:
    length = 7 + len(payload)
    return bytes(
        [
            0xFF,
            0xF0 | (mpeg_id << 3) | (layer << 1) | protection,
            (profile << 6) | (rate << 2) | ((channels >> 2) & 1),
            ((channels & 3) << 6) | ((length >> 11) & 3),
            (length >> 3) & 0xFF,
            ((length & 7) << 5) | 0x1F,
            0xFC | blocks,
        ]
    ) + payload


def asc(
    *,
    object_type: int = 2,
    rate: int = 3,
    channels: int = 2,
    frame_length: int = 0,
    core_coder: int = 0,
    extension: int = 0,
) -> bytes:
    value = (
        (object_type << 11)
        | (rate << 7)
        | (channels << 3)
        | (frame_length << 2)
        | (core_coder << 1)
        | extension
    )
    return value.to_bytes(2, "big")


def files() -> dict[str, bytes]:
    return {ADTS: adts(), RAW_NAME: RAW, ASC: asc()}


def manifest(data: dict[str, bytes]) -> dict[str, object]:
    return {
        "format": 1,
        "generator": {
            "ffmpegVersion": "ffmpeg version test-build",
            "command": ["ffmpeg", "-f", "lavfi"],
            "selectedFrameIndex": 2,
        },
        "files": {
            name: {
                "length": len(value),
                "sha256": hashlib.sha256(value).hexdigest(),
            }
            for name, value in sorted(data.items())
        },
    }


class FixtureValidationTests(unittest.TestCase):
    def rejected(self, message: str, call) -> None:
        with self.assertRaisesRegex(FixtureValidationError, message):
            call()

    def test_valid_values_pass(self) -> None:
        data = files()
        self.assertEqual(b"\x11\x90", asc())
        validate_adts(data[ADTS], data[RAW_NAME])
        validate_asc(data[ASC])
        validate_manifest(manifest(data), data)

    def test_adts_mutations_are_independently_rejected(self) -> None:
        cases = [
            ("sync", "ADTS sync", bytes([0]) + adts()[1:], RAW),
            ("mpeg", "MPEG-4", adts(mpeg_id=1), RAW),
            ("layer", "layer", adts(layer=1), RAW),
            ("crc", "protection_absent", adts(protection=0), RAW),
            ("profile", "AAC-LC profile", adts(profile=0), RAW),
            ("rate", "sample-rate index", adts(rate=4), RAW),
            ("channels", "channel configuration", adts(channels=1), RAW),
            ("blocks", "raw data block", adts(blocks=1), RAW),
            ("trailing", "frame length", adts() + b"\x00", RAW),
            ("empty", "payload is empty", adts(b""), b""),
            ("raw", "raw payload", adts(), b"\x00"),
        ]
        for name, message, frame, raw in cases:
            with self.subTest(name=name):
                self.rejected(message, lambda f=frame, r=raw: validate_adts(f, r))

    def test_asc_mutations_are_independently_rejected(self) -> None:
        cases = [
            ("length", "exactly two bytes", asc() + b"\x00"),
            ("object", "audio object type", asc(object_type=1)),
            ("rate", "sample-rate index", asc(rate=4)),
            ("channels", "channel configuration", asc(channels=1)),
            ("960", "frameLengthFlag", asc(frame_length=1)),
            ("core", "dependsOnCoreCoder", asc(core_coder=1)),
            ("extension", "extensionFlag", asc(extension=1)),
        ]
        for name, message, value in cases:
            with self.subTest(name=name):
                self.rejected(message, lambda v=value: validate_asc(v))

    def test_manifest_mutations_are_independently_rejected(self) -> None:
        data = files()
        base = manifest(data)

        def changed(update):
            value = copy.deepcopy(base)
            update(value)
            return value

        cases = [
            (
                "format",
                "manifest format",
                changed(lambda value: value.update(format=2)),
            ),
            (
                "missing",
                "missing manifest record",
                changed(lambda value: value["files"].pop(RAW_NAME)),
            ),
            (
                "extra",
                "manifest file set",
                changed(
                    lambda value: value["files"].update(
                        {"extra.bin": {"length": 0, "sha256": hashlib.sha256(b"").hexdigest()}}
                    )
                ),
            ),
            (
                "length",
                "length mismatch",
                changed(lambda value: value["files"][RAW_NAME].update(length=99)),
            ),
            (
                "hash",
                "SHA-256 mismatch",
                changed(lambda value: value["files"][RAW_NAME].update(sha256="0" * 64)),
            ),
            (
                "version",
                "FFmpeg version",
                changed(lambda value: value["generator"].update(ffmpegVersion="")),
            ),
            (
                "command",
                "FFmpeg command",
                changed(lambda value: value["generator"].update(command=[])),
            ),
            (
                "index",
                "selected frame index",
                changed(lambda value: value["generator"].update(selectedFrameIndex=1)),
            ),
        ]
        for name, message, value in cases:
            with self.subTest(name=name):
                self.rejected(message, lambda v=value: validate_manifest(v, data))

    def test_manifest_rejects_boolean_and_float_integers(self) -> None:
        data = files()
        cases = [
            ("format bool", lambda value: value.update(format=True), "manifest format"),
            (
                "selected index float",
                lambda value: value["generator"].update(selectedFrameIndex=2.0),
                "selected frame index",
            ),
            (
                "length float",
                lambda value: value["files"][RAW_NAME].update(length=float(len(RAW))),
                "length mismatch",
            ),
        ]
        for name, update, message in cases:
            with self.subTest(name=name):
                value = copy.deepcopy(manifest(data))
                update(value)
                self.rejected(message, lambda v=value: validate_manifest(v, data))

    def test_manifest_rejects_empty_or_non_string_command_arguments(self) -> None:
        data = files()
        for argument in ("", 1):
            with self.subTest(argument=argument):
                value = copy.deepcopy(manifest(data))
                value["generator"]["command"] = ["ffmpeg", argument]
                self.rejected("FFmpeg command", lambda v=value: validate_manifest(v, data))

    def test_main_reports_unicode_decode_error(self) -> None:
        error = UnicodeDecodeError("utf-8", b"\xff", 0, 1, "invalid start byte")
        stderr = io.StringIO()
        with patch("validate_aac_fixture.validate_fixture", side_effect=error):
            with redirect_stderr(stderr):
                result = main()
        self.assertEqual(result, 1)
        self.assertIn("aac fixture validation failed", stderr.getvalue())


if __name__ == "__main__":
    unittest.main()
