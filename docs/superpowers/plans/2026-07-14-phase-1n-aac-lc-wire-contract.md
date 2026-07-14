# Phase 1-N AAC-LC Wire Contract Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Freeze one raw 1024-sample AAC-LC frame per `AUDIO` packet, replace placeholder bytes with a reproducible real fixture, and align golden packets, fake runtime behavior, documentation, and CI.

**Architecture:** Keep protocol Version 1 and both packet layouts unchanged. Generate one canonical AAC-LC frame with FFmpeg, validate its ADTS/ASC/manifest structure using Python standard-library code, reuse its raw payload in protocol goldens and fake-stream tests, and embed the same bytes in the Android development sender. FFmpeg remains generation-only; CI validates checked-in bytes without installing a codec tool.

**Tech Stack:** Python 3 standard library, FFmpeg fixture generation, Kotlin/JVM and JUnit 4, C#/.NET Framework 4.8 and MSTest, GitHub Actions, Markdown, existing protocol-golden tooling.

---

## Scope Check

This plan implements only `docs/superpowers/specs/2026-07-14-phase-1n-aac-lc-wire-contract-design.md`.

In scope:

- Raw AAC-LC framing and fixed `AudioSpecificConfig = 11 90`.
- 1024 samples/channel at 48 kHz, exact 21.333... ms cadence, nominal wire duration `21`.
- One generated ADTS frame, its raw access unit, ASC, provenance/hash manifest, and README.
- Pure-Python structural validation and independent ADTS, ASC, and manifest mutations.
- Real AAC bytes in golden packets and Android/Windows exact-byte tests.
- Android fake transport using the canonical frame and monotonic rational timestamps.
- Existing Windows fake-pipeline tests aligned with the same metadata.
- Focused AAC documentation and docs-CI updates.

Out of scope:

- Android `MediaCodec`, `MediaProjection`, `AudioRecord`, or foreground service.
- Windows Media Foundation decoder implementation or device output.
- Discovery, retry policy, packet-layout/version changes, or FFmpeg runtime/CI dependency.
- General codec, fixture, or asset-loading abstractions.

---

## Files and Responsibilities

Create:

- `tools/audio/test_validate_aac_fixture.py` — in-memory mutation tests.
- `tools/audio/validate_aac_fixture.py` — standard-library validator and CLI.
- `tools/audio/generate_aac_fixture.py` — generation-only FFmpeg driver.
- `testdata/audio/aac-lc-48k-stereo-1024.adts` — one complete ADTS frame.
- `testdata/audio/aac-lc-48k-stereo-1024.raw` — exact raw access unit.
- `testdata/audio/aac-lc-48k-stereo.asc` — `11 90`.
- `testdata/audio/fixture-manifest.json` and `testdata/audio/README.md` — provenance.
- `sender-android/app/src/test/java/com/openaudiolink/TestFixtures.kt` — JVM fixture reader.
- `receiver-windows/tests/OpenAudioLink.Tests/TestFixtures.cs` — MSTest fixture reader.
- `sender-android/app/src/main/java/com/openaudiolink/network/FakeAacFrame.kt` — decoded-once Base64 bytes.

Modify:

- `.github/workflows/docs.yml`, `tools/protocol/generate_golden_packets.py`, and affected protocol goldens.
- Android/Windows protocol writer and parser tests.
- Android `HandshakeClient` and its tests.
- Windows fake receiver/runtime/UI tests.
- `docs/03-Protocol.md`, `docs/04-Android.md`, `docs/05-Windows.md`, `docs/06-Audio.md`, and `docs/10-Testing.md`.

No application dependency, project file, protocol field, or Windows production decoder file is added.

---

### Task 1: Specify the pure-Python fixture validator

**Files:**
- Create: `tools/audio/test_validate_aac_fixture.py`
- Test: `tools/audio/test_validate_aac_fixture.py`

- [ ] **Step 1: Create the mutation-test module**

Create `tools/audio/test_validate_aac_fixture.py`:

```python
from __future__ import annotations

import copy
import hashlib
import unittest

from validate_aac_fixture import (
    FixtureValidationError,
    validate_adts,
    validate_asc,
    validate_manifest,
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


if __name__ == "__main__":
    unittest.main()
```

- [ ] **Step 2: Verify RED**

Run:

```bash
python3 -m unittest discover -s tools/audio -p 'test_*.py'
```

Expected: `ModuleNotFoundError: No module named 'validate_aac_fixture'`.

- [ ] **Step 3: Commit the failing tests**

```bash
git add tools/audio/test_validate_aac_fixture.py
git commit -m "test: specify aac fixture validation"
```

---

### Task 2: Implement the fixture validator

**Files:**
- Create: `tools/audio/validate_aac_fixture.py`
- Test: `tools/audio/test_validate_aac_fixture.py`

- [ ] **Step 1: Add the minimal validator**

Create `tools/audio/validate_aac_fixture.py`:

```python
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
    require(manifest.get("format") == 1, "manifest format must be 1")
    generator = manifest.get("generator")
    require(isinstance(generator, dict), "manifest generator is missing")
    version = generator.get("ffmpegVersion")
    require(
        isinstance(version, str) and bool(version.strip()),
        "manifest FFmpeg version is empty",
    )
    command = generator.get("command")
    require(
        isinstance(command, list) and bool(command),
        "manifest FFmpeg command is empty",
    )
    require(
        generator.get("selectedFrameIndex") == 2,
        "manifest selected frame index must be 2",
    )
    records = manifest.get("files")
    require(isinstance(records, dict), "manifest files map is missing")
    for name, data in files.items():
        record = records.get(name)
        require(isinstance(record, dict), f"missing manifest record for {name}")
        require(record.get("length") == len(data), f"manifest length mismatch for {name}")
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
    except (FixtureValidationError, OSError, json.JSONDecodeError) as error:
        print(f"aac fixture validation failed: {error}", file=sys.stderr)
        return 1
    print("aac fixture validation ok")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
```

- [ ] **Step 2: Verify GREEN without FFmpeg or repository fixtures**

Run:

```bash
python3 -m unittest discover -s tools/audio -p 'test_*.py'
```

Expected: all tests and subtests pass; no `ffmpeg` invocation occurs.

- [ ] **Step 3: Commit the validator**

```bash
git add tools/audio/validate_aac_fixture.py
git commit -m "feat: validate canonical aac fixture"
```

---

### Task 3: Generate the canonical real AAC frame

**Files:**
- Create: `tools/audio/generate_aac_fixture.py`
- Create: `testdata/audio/aac-lc-48k-stereo-1024.adts`
- Create: `testdata/audio/aac-lc-48k-stereo-1024.raw`
- Create: `testdata/audio/aac-lc-48k-stereo.asc`
- Create: `testdata/audio/fixture-manifest.json`
- Create: `testdata/audio/README.md`
- Test: `tools/audio/validate_aac_fixture.py`

- [ ] **Step 1: Install FFmpeg only on the generation host if absent**

```bash
if ! command -v ffmpeg >/dev/null 2>&1; then
  apt-get update
  apt-get install -y ffmpeg
fi
ffmpeg -version | head -n 1
```

Expected: one FFmpeg version line. Do not add FFmpeg to application or CI dependencies. If the configured package mirror fails, retry downloads through the user-provided `https://proxy.v2up.eu.org/http/` transport.

- [ ] **Step 2: Add the generation-only script**

Create `tools/audio/generate_aac_fixture.py`:

```python
#!/usr/bin/env python3
from __future__ import annotations

import hashlib
import json
import shlex
import subprocess
import tempfile
from pathlib import Path

from validate_aac_fixture import ADTS_NAME, ASC_NAME, AUDIO_DIR, RAW_NAME, validate_fixture

SELECTED_FRAME_INDEX = 2
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


def split_adts_frames(data: bytes) -> list[bytes]:
    frames: list[bytes] = []
    offset = 0
    while offset < len(data):
        if len(data) - offset < 7:
            raise ValueError("truncated ADTS header from FFmpeg")
        if data[offset] != 0xFF or data[offset + 1] & 0xF0 != 0xF0:
            raise ValueError("invalid ADTS sync from FFmpeg")
        length = (
            ((data[offset + 3] & 3) << 11)
            | (data[offset + 4] << 3)
            | ((data[offset + 5] >> 5) & 7)
        )
        if length < 7 or offset + length > len(data):
            raise ValueError("invalid ADTS frame length from FFmpeg")
        frames.append(data[offset : offset + length])
        offset += length
    return frames


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

    frames = split_adts_frames(encoded)
    if len(frames) <= SELECTED_FRAME_INDEX:
        raise ValueError("FFmpeg did not produce frame index 2")
    adts = frames[SELECTED_FRAME_INDEX]
    if adts[1] & 1 != 1:
        raise ValueError("FFmpeg generated a CRC-bearing ADTS frame")
    raw = adts[7:]

    AUDIO_DIR.mkdir(parents=True, exist_ok=True)
    files = {ADTS_NAME: adts, RAW_NAME: raw, ASC_NAME: ASC}
    for name, data in files.items():
        (AUDIO_DIR / name).write_bytes(data)

    manifest = {
        "format": 1,
        "generator": {
            "ffmpegVersion": version,
            "command": FFMPEG_COMMAND,
            "selectedFrameIndex": SELECTED_FRAME_INDEX,
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
        f"- Selected zero-based ADTS frame: `{SELECTED_FRAME_INDEX}`\n"
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
```

- [ ] **Step 3: Generate and validate**

```bash
python3 tools/audio/generate_aac_fixture.py
python3 tools/audio/validate_aac_fixture.py
python3 -m unittest discover -s tools/audio -p 'test_*.py'
xxd -p testdata/audio/aac-lc-48k-stereo.asc
```

Expected: generation succeeds, validator/tests pass, and ASC prints `1190`.

- [ ] **Step 4: Verify provenance fields and exact selected index**

```bash
python3 - <<'PY'
import json
from pathlib import Path
root = Path("testdata/audio")
value = json.loads((root / "fixture-manifest.json").read_text())
assert value["generator"]["selectedFrameIndex"] == 2
assert (root / "aac-lc-48k-stereo.asc").read_bytes() == b"\x11\x90"
print(value["generator"]["ffmpegVersion"].splitlines()[0])
print(value["files"])
PY
```

Expected: selected index `2` and three length/hash records.

- [ ] **Step 5: Commit generation tooling and artifacts**

```bash
git add tools/audio/generate_aac_fixture.py testdata/audio
git commit -m "test: add canonical aac-lc fixture"
```

---

### Task 4: Add fixture validation to docs CI

**Files:**
- Modify: `.github/workflows/docs.yml`

- [ ] **Step 1: Add the two checks after docs consistency**

```yaml
      - run: python3 -m unittest discover -s tools/audio -p 'test_*.py'
      - run: python3 tools/audio/validate_aac_fixture.py
```

Keep the protocol golden check last.

- [ ] **Step 2: Run the exact docs job locally**

```bash
python3 tools/check_docs_consistency.py
python3 -m unittest discover -s tools/audio -p 'test_*.py'
python3 tools/audio/validate_aac_fixture.py
python3 tools/protocol/generate_golden_packets.py --check
git diff --check HEAD
```

Expected: all commands exit `0`.

- [ ] **Step 3: Commit**

```bash
git add .github/workflows/docs.yml
git commit -m "ci: validate canonical aac fixture"
```

---

### Task 5: Rebuild protocol goldens from the real raw fixture

**Files:**
- Modify: `tools/protocol/generate_golden_packets.py`
- Modify: `testdata/protocol/valid-start-stream.bin`
- Modify: `testdata/protocol/valid-audio-aac.bin`
- Modify: `testdata/protocol/golden-manifest.json`

- [ ] **Step 1: Add canonical generator constants**

After `OUT`:

```python
AAC_RAW_FIXTURE = ROOT / "testdata" / "audio" / "aac-lc-48k-stereo-1024.raw"
AAC_FRAME_DURATION_MS = 21
```

Use `AAC_FRAME_DURATION_MS` in `start_stream_payload()`. Replace `audio_payload()` with:

```python
def audio_payload() -> bytes:
    encoded = AAC_RAW_FIXTURE.read_bytes()
    return b"".join(
        [
            struct.pack(">B", CODEC_AAC_LC),
            struct.pack(">I", 1),
            struct.pack(">Q", 123456789),
            struct.pack(">H", AAC_FRAME_DURATION_MS),
            struct.pack(">I", len(encoded)),
            encoded,
        ]
    )
```

- [ ] **Step 2: Regenerate and check**

```bash
python3 tools/protocol/generate_golden_packets.py
python3 tools/protocol/generate_golden_packets.py --check
python3 - <<'PY'
from pathlib import Path
root = Path("testdata")
raw = (root / "audio/aac-lc-48k-stereo-1024.raw").read_bytes()
start = (root / "protocol/valid-start-stream.bin").read_bytes()
payload = (root / "protocol/valid-audio-aac.bin").read_bytes()[24:]
assert start[-2:] == b"\x00\x15"
assert payload[13:15] == b"\x00\x15"
assert int.from_bytes(payload[15:19], "big") == len(raw)
assert payload[19:] == raw
print("golden AAC contract ok")
PY
```

Expected: both checks and the explicit contract assertion pass.

- [ ] **Step 3: Commit**

```bash
git add tools/protocol/generate_golden_packets.py testdata/protocol
git commit -m "test: use real aac bytes in protocol goldens"
```

---

### Task 6: Align Android and Windows exact-byte tests

**Files:**
- Create: `sender-android/app/src/test/java/com/openaudiolink/TestFixtures.kt`
- Modify: `sender-android/app/src/test/java/com/openaudiolink/protocol/PacketWriterTest.kt`
- Modify: `sender-android/app/src/test/java/com/openaudiolink/protocol/PacketParserTest.kt`
- Create: `receiver-windows/tests/OpenAudioLink.Tests/TestFixtures.cs`
- Modify: `receiver-windows/tests/OpenAudioLink.Tests/Protocol/PacketWriterTests.cs`
- Modify: `receiver-windows/tests/OpenAudioLink.Tests/Protocol/PacketParserTests.cs`

- [ ] **Step 1: Add the Android fixture helper**

```kotlin
package com.openaudiolink

import java.io.File
import java.io.FileNotFoundException

internal object TestFixtures {
    fun read(relativePath: String): ByteArray {
        var directory: File? = File(System.getProperty("user.dir")).absoluteFile
        while (directory != null) {
            val candidate = File(directory, relativePath)
            if (candidate.isFile) return candidate.readBytes()
            directory = directory.parentFile
        }
        throw FileNotFoundException("Fixture not found: $relativePath")
    }
}
```

- [ ] **Step 2: Update Android writer/parser cases**

In `PacketWriterTest.kt`:

- Import `com.openaudiolink.TestFixtures`.
- Change `START_STREAM` duration to `21`.
- Make `readFixture(name)` return `TestFixtures.read("testdata/protocol/$name")`.
- Replace the AUDIO test with:

```kotlin
@Test
fun write_audio_matchesFixture() {
    val encoded = TestFixtures.read("testdata/audio/aac-lc-48k-stereo-1024.raw")
    assertPacket(
        "valid-audio-aac.bin",
        ProtocolConstants.PacketTypeAudio,
        2,
        123456789,
        HandshakePayloads.audio(
            ProtocolConstants.CodecAacLc,
            1,
            123456789,
            21,
            encoded,
        ),
    )
}
```

In `PacketParserTest.kt`, replace the valid AUDIO test with:

```kotlin
@Test
fun validateAacPayload_validAudioPayload_exposesCanonicalRawFrame() {
    val packet = TestFixtures.read("testdata/protocol/valid-audio-aac.bin")
    val payload = PacketParser.payload(packet)
    val encoded = TestFixtures.read("testdata/audio/aac-lc-48k-stereo-1024.raw")

    assertEquals(ProtocolConstants.PacketTypeAudio, PacketParser.parseHeader(packet).packetType)
    assertEquals(ProtocolConstants.AudioPayloadHeaderSize + encoded.size, payload.size)
    assertArrayEquals(
        HandshakePayloads.audio(
            ProtocolConstants.CodecAacLc,
            1,
            123456789,
            21,
            encoded,
        ),
        payload,
    )
    assertArrayEquals(
        encoded,
        payload.copyOfRange(ProtocolConstants.AudioPayloadHeaderSize, payload.size),
    )
    AudioPayloadValidator.validateAacPayload(payload)
}
```

Also:

- import `TestFixtures` and use it in the private protocol reader;
- change `valid-start-stream.bin` expected suffix from `0014` to `0015`;
- remove the giant static AUDIO hex case, now covered by the dedicated fixture assertion;
- remove unused `File`/`FileNotFoundException` imports.

- [ ] **Step 3: Add the Windows fixture helper**

Create `receiver-windows/tests/OpenAudioLink.Tests/TestFixtures.cs`:

```csharp
using System;
using System.IO;

namespace OpenAudioLink.Tests
{
    internal static class TestFixtures
    {
        public static byte[] Read(string relativePath)
        {
            DirectoryInfo directory = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (directory != null)
            {
                string path = Path.Combine(directory.FullName, relativePath);
                if (File.Exists(path))
                {
                    return File.ReadAllBytes(path);
                }

                directory = directory.Parent;
            }

            throw new FileNotFoundException("Fixture not found.", relativePath);
        }
    }
}
```

- [ ] **Step 4: Update Windows writer/parser cases**

In both classes add `using OpenAudioLink.Tests;` and replace duplicate readers with `TestFixtures.Read(...)`.

Replace `WriteAudio_MatchesGoldenPacket()` with:

```csharp
[TestMethod]
public void WriteAudio_MatchesGoldenPacket()
{
    byte[] encoded = TestFixtures.Read(
        "testdata/audio/aac-lc-48k-stereo-1024.raw");
    byte[] packet = PacketWriter.WritePacket(
        ProtocolConstants.PacketTypeAudio,
        2u,
        123456789UL,
        HandshakePayloads.Audio(
            ProtocolConstants.CodecAacLc,
            1u,
            123456789UL,
            21,
            encoded));

    CollectionAssert.AreEqual(
        TestFixtures.Read("testdata/protocol/valid-audio-aac.bin"),
        packet);
}
```

Change the Windows START_STREAM writer duration and parser expected hex suffix to `21` / `0015`.

Replace the valid parser AUDIO test with:

```csharp
[TestMethod]
public void ValidateAacPayload_ValidAudioPayload_ExposesCanonicalRawFrame()
{
    byte[] packet = TestFixtures.Read("testdata/protocol/valid-audio-aac.bin");
    byte[] payload = PacketParser.Payload(packet);
    byte[] encoded = TestFixtures.Read(
        "testdata/audio/aac-lc-48k-stereo-1024.raw");
    byte[] extracted = new byte[encoded.Length];
    Buffer.BlockCopy(
        payload,
        ProtocolConstants.AudioPayloadHeaderSize,
        extracted,
        0,
        extracted.Length);

    Assert.AreEqual(ProtocolConstants.PacketTypeAudio, PacketParser.ParseHeader(packet).PacketType);
    Assert.AreEqual(ProtocolConstants.AudioPayloadHeaderSize + encoded.Length, payload.Length);
    CollectionAssert.AreEqual(
        HandshakePayloads.Audio(
            ProtocolConstants.CodecAacLc,
            1u,
            123456789UL,
            21,
            encoded),
        payload);
    CollectionAssert.AreEqual(encoded, extracted);
    AudioPayloadValidator.ValidateAacPayload(payload);
}
```

- [ ] **Step 5: Run or record both platform suites**

```bash
if [ -n "${ANDROID_HOME:-}" ]; then
  (cd sender-android && ./gradlew :app:testDebugUnitTest)
else
  echo 'ANDROID_HOME not set; Android tests require CI'
fi
if command -v dotnet >/dev/null 2>&1; then
  dotnet test receiver-windows/OpenAudioLink.sln -c Release
else
  echo 'dotnet not found; Windows tests require CI'
fi
```

Expected on the current baseline if tools remain absent: both explicit CI-required messages. If a runner has since been installed, the corresponding suite executes and must pass.

- [ ] **Step 6: Commit**

```bash
git add sender-android/app/src/test receiver-windows/tests/OpenAudioLink.Tests
git commit -m "test: align protocol tests with aac fixture"
```

---

### Task 7: Specify the Android fake-stream contract

**Files:**
- Modify: `sender-android/app/src/test/java/com/openaudiolink/network/HandshakeClientTest.kt`

- [ ] **Step 1: Replace success assertions with the exact contract**

Import `TestFixtures` and replace the success test with:

```kotlin
@Test
fun runWritesHandshakePacketsOnSuccess() {
    val input = ByteArrayInputStream(successfulResponses())
    val output = ByteArrayOutputStream()
    val encoded = TestFixtures.read("testdata/audio/aac-lc-48k-stereo-1024.raw")

    assertTrue(HandshakeClient().run(input, output))

    val written = ByteArrayInputStream(output.toByteArray())
    assertPacket(written, ProtocolConstants.PacketTypeHello, 1)
    assertArrayEquals(
        HandshakePayloads.startStream(
            ProtocolConstants.CodecAacLc,
            48000,
            2,
            192000,
            21,
        ),
        assertPacket(written, ProtocolConstants.PacketTypeStartStream, 2),
    )
    val timestamps = longArrayOf(123456003, 123477336, 123498670)
    timestamps.forEachIndexed { index, timestamp ->
        assertArrayEquals(
            HandshakePayloads.audio(
                ProtocolConstants.CodecAacLc,
                (index + 1).toLong(),
                timestamp,
                21,
                encoded,
            ),
            assertPacket(
                written,
                ProtocolConstants.PacketTypeAudio,
                (index + 3).toLong(),
                timestamp,
            ),
        )
    }
    assertArrayEquals(
        HandshakePayloads.ping(5, 123498671),
        assertPacket(written, ProtocolConstants.PacketTypePing, 6, 123498672),
    )
    assertPacket(written, ProtocolConstants.PacketTypeStopStream, 7, 123498673)
    assertEquals(0, written.available())
}
```

- [ ] **Step 2: Add direct Base64 equality and update PONG fixtures**

Add:

```kotlin
@Test
fun fakeAacFrame_matchesCanonicalFixture() {
    assertArrayEquals(
        TestFixtures.read("testdata/audio/aac-lc-48k-stereo-1024.raw"),
        FakeAacFrameBytes,
    )
}
```

Use `HandshakePayloads.ping(5, 123498671)` in `successfulResponses()` and `HandshakePayloads.ping(6, 123498671)` in the mismatch test.

Replace `assertPacket` with:

```kotlin
private fun assertPacket(
    input: ByteArrayInputStream,
    packetType: Int,
    sequenceNumber: Long,
    timestamp: Long? = null,
): ByteArray {
    val packet = PacketReader.readPacket(input)
    val header = PacketParser.parseHeader(packet)
    assertEquals(packetType, header.packetType)
    assertEquals(sequenceNumber, header.sequenceNumber)
    if (timestamp != null) assertEquals(timestamp, header.timestamp)
    return PacketParser.payload(packet)
}
```

- [ ] **Step 3: Verify RED and commit**

Run on an Android runner:

```bash
cd sender-android
./gradlew :app:testDebugUnitTest --tests com.openaudiolink.network.HandshakeClientTest
```

Expected: compilation fails because `FakeAacFrameBytes` does not exist, or old output mismatches the new assertions.

```bash
git add sender-android/app/src/test/java/com/openaudiolink/network/HandshakeClientTest.kt
git commit -m "test: specify valid fake aac stream"
```

---

### Task 8: Make Android emit the canonical fake stream

**Files:**
- Create: `sender-android/app/src/main/java/com/openaudiolink/network/FakeAacFrame.kt`
- Modify: `sender-android/app/src/main/java/com/openaudiolink/network/HandshakeClient.kt`

- [ ] **Step 1: Generate a reviewable Base64 constant from the canonical bytes**

```bash
python3 - <<'PY'
import base64
from pathlib import Path
raw = Path("testdata/audio/aac-lc-48k-stereo-1024.raw").read_bytes()
encoded = base64.b64encode(raw).decode("ascii")
Path("sender-android/app/src/main/java/com/openaudiolink/network/FakeAacFrame.kt").write_text(
    "package com.openaudiolink.network\n\n"
    "import java.util.Base64\n\n"
    "internal val FakeAacFrameBytes: ByteArray = Base64.getDecoder().decode(\n"
    f'    "{encoded}",\n'
    ")\n",
    encoding="utf-8",
)
PY
```

This is a one-off source-generation command, not a runtime loader.

- [ ] **Step 2: Replace HandshakeClient fake metadata**

Use:

```kotlin
private val pingPayload = HandshakePayloads.ping(5, 123498671)
private val fakeAudioFrames = listOf(
    FakeAudioFrame(1, 123456003),
    FakeAudioFrame(2, 123477336),
    FakeAudioFrame(3, 123498670),
)

private data class FakeAudioFrame(
    val frameNumber: Long,
    val captureTimestamp: Long,
)
```

Change START_STREAM and AUDIO duration to `21`, and pass `FakeAacFrameBytes` for every frame. Set the PING common-header timestamp to `123498672` and STOP_STREAM common-header timestamp to `123498673`. Preserve PONG echo validation, final flush, EOF synchronization, and exception handling.

- [ ] **Step 3: Verify GREEN and commit**

```bash
cd sender-android
./gradlew :app:testDebugUnitTest --tests com.openaudiolink.network.HandshakeClientTest
```

Expected on CI/Android runner: all tests pass.

```bash
git add \
  sender-android/app/src/main/java/com/openaudiolink/network/FakeAacFrame.kt \
  sender-android/app/src/main/java/com/openaudiolink/network/HandshakeClient.kt
git commit -m "fix: send canonical fake aac frames"
```

---

### Task 9: Align Windows fake pipeline and remaining AAC metadata

**Files:**
- Modify: `receiver-windows/tests/OpenAudioLink.Tests/Receiver/ReceiverRuntimeTests.cs`
- Modify: `receiver-windows/tests/OpenAudioLink.Tests/Receiver/TcpReceiverTests.cs`
- Modify: `receiver-windows/tests/OpenAudioLink.Tests/UI/MainFormTests.cs`
- Modify: `receiver-windows/tests/OpenAudioLink.Tests/Receiver/ReceiverSessionTests.cs`
- Modify: `receiver-windows/tests/OpenAudioLink.Tests/Receiver/FakeAacDecoderTests.cs`
- Modify: `receiver-windows/tests/OpenAudioLink.Tests/Receiver/FakeAudioRendererTests.cs`

- [ ] **Step 1: Use the canonical frame in all three-frame integration paths**

In `ReceiverRuntimeTests.cs`, `TcpReceiverTests.cs`, and `MainFormTests.cs`:

- add `using OpenAudioLink.Tests;`;
- change START_STREAM and AUDIO duration to `21`;
- replace arbitrary frame arrays with:

```csharp
byte[] encodedFrame = TestFixtures.Read(
    "testdata/audio/aac-lc-48k-stereo-1024.raw");
ulong[] captureTimestamps =
{
    123456003UL,
    123477336UL,
    123498670UL,
};
```

Build each payload with:

```csharp
byte[] payload = HandshakePayloads.Audio(
    ProtocolConstants.CodecAacLc,
    (uint)(i + 1),
    captureTimestamps[i],
    21,
    encodedFrame);
```

Assert `captureTimestamps[i]`, duration `21`, and `encodedFrame`. Change fake PING payload timestamp to `123498671UL`. Do not change queue sizes, render count, PONG echo, or STOP/EOF behavior.

- [ ] **Step 2: Align isolated AAC metadata tests**

In `ReceiverSessionTests.cs`:

- use duration `21` for every START_STREAM/AUDIO fixture; negative tests keep
  their unsupported codec or malformed bytes as the single changed variable;
- add `using OpenAudioLink.Tests;`;
- replace `ValidAudioPayload()` with:

```csharp
private static byte[] ValidAudioPayload()
{
    return HandshakePayloads.Audio(
        ProtocolConstants.CodecAacLc,
        1u,
        123456789UL,
        21,
        TestFixtures.Read("testdata/audio/aac-lc-48k-stereo-1024.raw"));
}
```

Keep unsupported-codec bytes minimal because rejection happens before AAC semantics.

In `FakeAacDecoderTests.cs` and `FakeAudioRendererTests.cs`, change nominal AAC durations/assertions from `20` to `21`. Keep their tiny payloads: these isolated fake tests verify copying, FIFO, cloning, and length rejection, not AAC decode validity.

- [ ] **Step 3: Search for stale executable AAC metadata**

```bash
if rg -n "\\b20\\b|123456023|123456043|11223344|21222324|31323334|11, 0x22, 0x33, 0x44|0x21, 0x22, 0x23, 0x24|0x31, 0x32, 0x33, 0x34" \
  sender-android/app/src/main/java/com/openaudiolink/network/HandshakeClient.kt \
  sender-android/app/src/test/java/com/openaudiolink/network/HandshakeClientTest.kt \
  sender-android/app/src/test/java/com/openaudiolink/protocol/PacketWriterTest.kt \
  sender-android/app/src/test/java/com/openaudiolink/protocol/PacketParserTest.kt \
  receiver-windows/tests/OpenAudioLink.Tests/Protocol/PacketWriterTests.cs \
  receiver-windows/tests/OpenAudioLink.Tests/Protocol/PacketParserTests.cs \
  receiver-windows/tests/OpenAudioLink.Tests/Receiver \
  receiver-windows/tests/OpenAudioLink.Tests/UI; then
  echo 'stale fake AAC fixture metadata remains'
  exit 1
fi
```

Expected: no output. This search is deliberately limited to exact-byte
writer/parser tests and fake-flow code, so unrelated protocol-header offsets
and documentation latency targets are not candidates for replacement.

- [ ] **Step 4: Run/record Windows tests and commit**

```bash
if command -v dotnet >/dev/null 2>&1; then
  dotnet test receiver-windows/OpenAudioLink.sln -c Release
else
  echo 'dotnet not found; Windows tests require CI'
fi
git add receiver-windows/tests/OpenAudioLink.Tests
git commit -m "test: align fake receiver with aac cadence"
```

Expected locally: CI-required message; expected in Windows CI: pass.

---

### Task 10: Align the five focused AAC documents

**Files:**
- Modify: `docs/03-Protocol.md`
- Modify: `docs/04-Android.md`
- Modify: `docs/05-Windows.md`
- Modify: `docs/06-Audio.md`
- Modify: `docs/10-Testing.md`

- [ ] **Step 1: Freeze protocol wording**

In `docs/03-Protocol.md`:

- change the START_STREAM AAC example and Default Frame from `20 ms` to nominal `21 ms`;
- change only the AAC duration table row to `21 ms (nominal)`; keep Opus at `20 ms`;
- in the Audio Packet `# Codec` section, immediately after the exact sentence
  `Version 1 requires AAC-LC.`, add:

```markdown
For Version 1 AAC-LC, `Encoded Data` contains exactly one complete raw
`raw_data_block()` access unit. It contains no ADTS, LATM/LOAS, MP4 container
bytes, concatenated frames, partial frames, or codec-configuration buffer.

The fixed configuration is AAC-LC, 48 kHz, stereo, 1024 samples per channel
per frame. Its two-byte `AudioSpecificConfig` is `11 90` and is derived from
Version 1 rather than transmitted in each packet.
```

Replace the AAC Frame Duration explanation with:

````markdown
An AAC-LC frame represents exactly 1024 samples per channel:

```text
1024 / 48000 = 21.333333... ms
```

The integer wire field carries nominal `21 ms`. Implementations use monotonic
capture timestamps and decoded sample count for exact timing; they do not
accumulate the nominal integer as the playback clock.
````

- [ ] **Step 2: Align Android encoder design**

In `docs/04-Android.md` replace the 20 ms / 3840-byte PCM frame section with:

````markdown
# PCM Frame Size

Version 1 AAC-LC uses 1024 PCM samples per channel per encoder frame.

```text
1024 / 48000 = 21.333333... ms
1024 samples × 2 channels × 2 bytes = 4096 bytes
```

AudioRecord reads may be combined or split so MediaCodec receives a continuous
PCM stream; capture timestamps remain sample-count based.
````

Replace `Frame Duration | 20 ms` with:

```markdown
| Samples per channel per AAC frame | 1024 |
| Nominal wire frame duration | 21 ms |
```

Describe MediaCodec input as `1024 samples/channel, 4096 bytes, exact 21.333... ms`. Extend output wording:

```markdown
Each transmitted access unit is one complete raw AAC-LC frame. ADTS headers,
LATM/LOAS framing, container bytes, and `BUFFER_FLAG_CODEC_CONFIG` output are
not sent as `AUDIO.EncodedData`. The encoder requires `csd-0 = 11 90` before
sending audio.
```

Do not alter queue-delay or encoder-performance targets that happen to be 20 ms.

- [ ] **Step 3: Align Windows decoder design**

In `docs/05-Windows.md` extend Decoder Input:

````markdown
Each protocol packet supplies exactly one complete raw AAC-LC access unit:
no ADTS header and no codec-config packet. Version 1 derives the fixed
`AudioSpecificConfig = 11 90` from AAC-LC, 48 kHz, and stereo.

```text
Subtype: MFAudioFormat_AAC
MF_MT_AAC_PAYLOAD_TYPE: 0
MF_MT_AAC_AUDIO_PROFILE_LEVEL_INDICATION: FE
MF_MT_USER_DATA: 00 00 FE 00 00 00 00 00 00 00 00 00 11 90
```

The first 12 `MF_MT_USER_DATA` bytes are the little-endian `HEAACWAVEINFO`
tail; the final two bytes are `AudioSpecificConfig`.
````

Do not claim the native decoder is implemented in this phase.

- [ ] **Step 4: Align shared audio timing**

In `docs/06-Audio.md`:

- add the one-raw-frame and fixed `11 90` contract after AAC Buffer;
- add payload type `0` and the exact 14-byte `MF_MT_USER_DATA` under Media Foundation Required Media Type;
- replace the Audio Timestamp example with:

```json
{
  "timestamp": 1234567890,
  "samplesPerChannel": 1024,
  "sampleRate": 48000
}
```

Describe it as one exact 21.333333... ms frame. Where jitter prose specifically models AAC packet cadence, use exact `21.333... ms` and explain wire duration is nominal `21`. Keep queue targets, latency budgets, timing-error examples, and generic network-arrival examples unchanged.

- [ ] **Step 5: Align testing policy and data names**

In `docs/10-Testing.md` replace the normative 20 ms AUDIO statement with:

```markdown
- AAC-LC represents exactly 1024 samples/channel at 48 kHz
  (21.333333... ms); the integer wire Frame Duration is nominally 21 ms.
- Encoded Data contains exactly one raw AAC-LC access unit and no ADTS or
  codec-configuration bytes.
```

Replace the suggested audio fixture tree with the five actual files under `testdata/audio/`. Replace golden regeneration policy with:

```markdown
Binary golden files are regenerated only for an intentional protocol-version
change or a reviewed pre-release wire-contract correction. Regeneration
updates the generator, manifest, platform exact-byte tests, and affected
protocol documentation in the same change.
```

Add:

```bash
python3 -m unittest discover -s tools/audio -p 'test_*.py'
python3 tools/audio/validate_aac_fixture.py
```

State that this fixture proves structure/provenance; native Media Foundation decode is proved next phase.

- [ ] **Step 6: Verify focused docs and commit**

```bash
python3 - <<'PY'
from pathlib import Path
import re

def read(name):
    return Path(name).read_text(encoding="utf-8")

def section(text, title):
    start = text.index(f"# {title}\n")
    end = text.find("\n# ", start + 2)
    return text[start:] if end == -1 else text[start:end]

protocol = read("docs/03-Protocol.md")
android = read("docs/04-Android.md")
audio = read("docs/06-Audio.md")
testing = read("docs/10-Testing.md")
checks = [
    ("protocol START_STREAM", "20 ms" in section(protocol, "START_STREAM Packet")),
    ("protocol AAC duration row", "| AAC | 20 ms |" in protocol),
    (
        "protocol default frame",
        "20 ms" in section(protocol, "Protocol Constants").split("Default Frame", 1)[1],
    ),
    (
        "Android PCM frame",
        "20 ms" in section(android, "PCM Frame Size")
        or "3840 Bytes" in section(android, "PCM Frame Size"),
    ),
    (
        "Android encoder config",
        "| Frame Duration | 20 ms |" in section(android, "Encoder Configuration"),
    ),
    ("Android MediaCodec input", "20 ms" in section(android, "MediaCodec Input")),
    (
        "audio timestamp",
        "960" in section(audio, "Audio Timestamp")
        or "20ms audio frame" in section(audio, "Audio Timestamp"),
    ),
    (
        "AAC playback cadence",
        bool(re.search(r"Playback requires:.*?(?:20ms\s*){5}", section(audio, "Jitter Buffer"), re.S)),
    ),
    (
        "AAC jitter expectation",
        bool(re.search(r"Expected:.*?20 ms", section(audio, "Jitter Measurement"), re.S)),
    ),
    ("testing AUDIO duration", "Frame Duration is normally 20 ms" in testing),
]
stale = [name for name, failed in checks if failed]
if stale:
    raise SystemExit("stale AAC documentation: " + ", ".join(stale))
print("focused AAC documentation values ok")
PY
python3 tools/check_docs_consistency.py
python3 tools/audio/validate_aac_fixture.py
python3 tools/protocol/generate_golden_packets.py --check
git diff --check
```

Expected: no stale 960-sample or normative AAC 20 ms statement; unrelated 20 ms performance/network values may remain after context review. All checks exit `0`.

```bash
git add docs/03-Protocol.md docs/04-Android.md docs/05-Windows.md docs/06-Audio.md docs/10-Testing.md
git commit -m "docs: freeze raw aac-lc frame contract"
```

---

### Task 11: Verify Phase 1-N locally and on the mirrored CI branch

**Files:**
- Verify: all Phase 1-N files
- No new implementation files

- [ ] **Step 1: Run complete local checks**

```bash
python3 tools/check_docs_consistency.py
python3 -m unittest discover -s tools/audio -p 'test_*.py'
python3 tools/audio/validate_aac_fixture.py
python3 tools/protocol/generate_golden_packets.py --check
git diff --check HEAD
git status --short --branch
```

Expected: docs, validator tests, canonical fixture, and protocol goldens pass; `git diff --check` is silent and the branch is clean.

- [ ] **Step 2: Run or record platform tests**

```bash
if [ -n "${ANDROID_HOME:-}" ]; then
  (cd sender-android && ./gradlew :app:testDebugUnitTest)
else
  echo 'ANDROID_HOME not set; Android tests require CI'
fi
if command -v dotnet >/dev/null 2>&1; then
  dotnet test receiver-windows/OpenAudioLink.sln -c Release
else
  echo 'dotnet not found; Windows tests require CI'
fi
```

Expected on the current baseline if tools remain absent: both CI-required messages. If a runner has since been installed, the corresponding suite executes and must pass.

- [ ] **Step 3: Review phase boundaries**

```bash
git diff --stat a8b2a23..HEAD
git log --oneline --reverse a8b2a23..HEAD
git diff --check a8b2a23..HEAD
```

Confirm no packet layout, FFmpeg runtime/CI dependency, native codec, playback, or discovery change.

- [ ] **Step 4: Push the phase branch using the known MTU workaround**

```bash
set -e
ip route add 192.168.3.20/32 via 192.168.4.1 dev eth0 mtu 1200 || true
trap 'ip route del 192.168.3.20/32 via 192.168.4.1 dev eth0 || true' EXIT
git push -u origin phase-1n-aac-lc-wire-contract
HEAD_SHA=$(git rev-parse HEAD)
GITEA_SHA=$(git ls-remote origin refs/heads/phase-1n-aac-lc-wire-contract | cut -f1)
test "$GITEA_SHA" = "$HEAD_SHA"
```

Expected: Gitea reports the exact local HEAD. Then verify the GitHub mirror:

```bash
HEAD_SHA=$(git rev-parse HEAD)
GITHUB_SHA=
for attempt in $(seq 1 60); do
  GITHUB_SHA=$(gh api \
    repos/imshuai/OpenAudioLink/commits/phase-1n-aac-lc-wire-contract \
    --jq .sha 2>/dev/null || true)
  [ "$GITHUB_SHA" = "$HEAD_SHA" ] && break
  sleep 5
done
test "$GITHUB_SHA" = "$HEAD_SHA"
```

Expected: both source-of-truth and mirror branch tips equal the local HEAD.

- [ ] **Step 5: Verify all three Actions runs against exact HEAD**

```bash
python3 - <<'PY'
import json
import subprocess
import time

repo = "imshuai/OpenAudioLink"
branch = "phase-1n-aac-lc-wire-contract"
head = subprocess.check_output(["git", "rev-parse", "HEAD"], text=True).strip()
required = {"docs", "android", "windows"}
deadline = time.monotonic() + 1800

while True:
    runs = json.loads(
        subprocess.check_output(
            [
                "gh",
                "run",
                "list",
                "--repo",
                repo,
                "--branch",
                branch,
                "--limit",
                "30",
                "--json",
                "databaseId,workflowName,status,conclusion,headSha",
            ],
            text=True,
        )
    )
    current = [
        run
        for run in runs
        if run["headSha"] == head and run["workflowName"] in required
    ]
    failed = [
        run
        for run in current
        if run["status"] == "completed" and run["conclusion"] != "success"
    ]
    if failed:
        raise SystemExit(f"CI failed for {head}: {failed}")
    if len(current) > len(required):
        raise SystemExit(f"duplicate CI runs for {head}: {current}")
    if (
        len(current) == len(required)
        and {run["workflowName"] for run in current} == required
        and all(
            run["status"] == "completed" and run["conclusion"] == "success"
            for run in current
        )
    ):
        print(f"CI green for {head}: docs, android, windows")
        break
    if time.monotonic() >= deadline:
        raise SystemExit(f"timed out waiting for CI at {head}: {current}")
    print(f"waiting for CI at {head}: {current}")
    time.sleep(15)
PY
```

Expected: exactly one completed successful `docs`, `android`, and `windows` run for the local HEAD. Older, queued, duplicated, failed, or different-HEAD runs do not pass this gate.

- [ ] **Step 6: Request final review before integration**

Use `superpowers:requesting-code-review` with:

```bash
BASE_SHA=a8b2a23
HEAD_SHA=$(git rev-parse HEAD)
REQUIREMENTS=docs/superpowers/specs/2026-07-14-phase-1n-aac-lc-wire-contract-design.md
PLAN=docs/superpowers/plans/2026-07-14-phase-1n-aac-lc-wire-contract.md
```

Fix every Critical/Important finding, re-run local checks, push corrected HEAD, and verify all three workflows again before invoking `superpowers:finishing-a-development-branch`.
