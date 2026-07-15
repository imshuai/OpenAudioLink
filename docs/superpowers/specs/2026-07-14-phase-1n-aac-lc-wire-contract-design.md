# Phase 1-N AAC-LC Wire Contract Design

**Status:** Implemented

**Date:** 2026-07-14

**Scope:** Freeze the Version 1 AAC-LC access-unit contract and add a structurally validated real AAC fixture.

---

## Goal

Phase 1-N gives the future Android `MediaCodec` encoder and Windows Media Foundation decoder one unambiguous AAC-LC contract.

After this phase:

- `AUDIO.EncodedData` is defined as one complete raw AAC-LC compressed frame.
- ADTS headers and codec-configuration buffers are not transmitted as `AUDIO` data.
- AAC-LC uses 1024 PCM samples per channel at 48 kHz.
- The integer wire field `Frame Duration` uses nominal `21 ms`; exact timing comes from sample count and monotonic timestamps.
- A real, generated AAC-LC fixture proves the framing contract and replaces arbitrary bytes in the golden `AUDIO` packet.
- The executable Android fake-stream path sends that same canonical raw frame instead of invalid placeholder bytes.

This phase is the prerequisite for native decode and encode work. It does not itself add a runtime codec.

---

## Why This Phase Is Required

The current documents say that Android emits “AAC access units,” but they do not define whether an `AUDIO` packet contains:

- raw AAC,
- ADTS AAC,
- codec configuration,
- or a mixture of those forms.

The current golden packet uses four arbitrary bytes:

```text
11 22 33 44
```

Those bytes validate packet layout but are not a real AAC frame.

The current documents also use a 20 ms / 960-sample model. AAC-LC normally uses 1024 samples per compressed frame. At 48 kHz, the exact frame duration is:

```text
1024 / 48000 seconds = 21.333333... ms
```

Microsoft's AAC Decoder documentation states that:

- raw AAC and ADTS are supported;
- a raw input sample must contain exactly one complete AAC compressed frame;
- 960-sample AAC-LC frames are unsupported; only 1024-sample frames are supported;
- raw AAC requires `AudioSpecificConfig` data.

Reference:

```text
https://learn.microsoft.com/en-us/windows/win32/medfound/aac-decoder
```

Implementing either endpoint before resolving these details risks producing two individually working but wire-incompatible codecs.

---

## Selected Contract

### EncodedData framing

For Version 1 and `Codec = AAC-LC`:

```text
AUDIO.EncodedData = exactly one raw_data_block() AAC-LC access unit
```

Requirements:

- Exactly one complete compressed AAC frame per `AUDIO` packet.
- No ADTS header.
- No LATM or LOAS framing.
- No MP4 container bytes.
- No concatenated AAC frames.
- No partial AAC frame split across packets.
- No Android `BUFFER_FLAG_CODEC_CONFIG` buffer sent as audio.

### Fixed Version 1 codec configuration

The negotiated format remains:

| Property | Value |
|----------|------:|
| MPEG-4 audio object type | 2 (`AAC-LC`) |
| Sample rate | 48000 Hz |
| Sampling-frequency index | 3 |
| Channels | 2 |
| Channel configuration | 2 |
| PCM samples per AAC frame, per channel | 1024 |
| PCM output | signed 16-bit little-endian stereo |

The two-byte MPEG-4 `AudioSpecificConfig` is:

```text
11 90
```

The value is derived from the fixed Version 1 profile, sample rate and channel count; it is not added to the protocol payload.

### Android MediaCodec behavior

The Android encoder boundary and later sender runtime integration must:

1. Configure AAC-LC, 48 kHz and stereo.
2. Read the codec-specific output (`csd-0` / `BUFFER_FLAG_CODEC_CONFIG`).
3. Require `AudioSpecificConfig = 11 90`.
4. Never wrap that codec-config buffer in an `AUDIO` packet.
5. For each access unit accepted for transmission by the sender runtime, assign
   wire metadata and put exactly that one raw access unit into one `AUDIO`
   packet. A codec output candidate is not automatically transmit-ready; the
   codec-added candidate policy belongs to later runtime integration.

If the codec reports a different profile, sample rate, channel configuration or frame model, the stream must fail before transmitting audio.

### Windows Media Foundation behavior

The future Windows decoder must configure:

```text
Subtype: MFAudioFormat_AAC
MF_MT_AAC_PAYLOAD_TYPE: 0 (raw AAC)
MF_MT_AAC_AUDIO_PROFILE_LEVEL_INDICATION: FE
MF_MT_USER_DATA: 00 00 FE 00 00 00 00 00 00 00 00 00 11 90
Output: 48 kHz, stereo, signed PCM 16-bit
```

`MF_MT_USER_DATA` is a 14-byte blob. Its first 12 bytes are the little-endian `HEAACWAVEINFO` members after `WAVEFORMATEX`:

| Offset | Size | Field | Value |
|-------:|-----:|-------|------:|
| 0 | 2 | `wPayloadType` | `0` (raw AAC) |
| 2 | 2 | `wAudioProfileLevelIndication` | `0x00FE` (no profile level specified) |
| 4 | 2 | `wStructType` | `0` |
| 6 | 2 | `wReserved1` | `0` |
| 8 | 4 | `dwReserved2` | `0` |
| 12 | 2 | `AudioSpecificConfig` | `11 90` |

The optional `MF_MT_AAC_AUDIO_PROFILE_LEVEL_INDICATION` attribute uses the same `0xFE` value. AAC-LC identity still comes from `AudioSpecificConfig.audioObjectType = 2`.

Each `AUDIO` payload is submitted as one complete decoder input sample.

---

## Frame Duration And Timestamps

### Exact duration

One AAC-LC frame represents exactly 1024 samples per channel.

The exact duration is therefore represented by:

```text
samplesPerChannel = 1024
sampleRate = 48000
```

### Wire duration field

The existing `UInt16 Frame Duration (ms)` field cannot represent `21.333333...` exactly. Version 1 uses:

```text
Frame Duration = 21
```

This is nominal metadata for validation, diagnostics and queue estimates. It must not be accumulated as the playback clock.

### Clock ownership

Exact sender timing comes from monotonic capture timestamps. Exact receiver timing comes from decoded PCM sample count and the audio-device clock.

When timestamps are generated from sample positions, implementations must preserve the fractional duration rather than adding a constant 21,000 microseconds forever. For example, rational arithmetic or an accumulated sample counter avoids drift.

For zero-based frame index `n`, the canonical sample-derived formula is:

```text
timestamp[n] = timestamp[0] + round(n * 1024 * 1,000,000 / 48,000)
             = timestamp[0] + round(n * 64,000 / 3) microseconds
```

The denominator is `3`, so no half-way rounding case occurs. Consecutive integer deltas repeat `21,333`, `21,334`, `21,333` microseconds. Implementations must not alternate `21,333` and `21,334` indefinitely because that averages `21,333.5` microseconds and drifts.

---

## Canonical Real AAC Fixture

### Files

Add:

```text
testdata/audio/aac-lc-48k-stereo-1024.adts
testdata/audio/aac-lc-48k-stereo-1024.raw
testdata/audio/aac-lc-48k-stereo.asc
testdata/audio/fixture-manifest.json
testdata/audio/README.md
```

The ADTS file is the provenance container for one real encoded AAC-LC frame. It uses `protection_absent = 1`, so its header is exactly 7 bytes. The raw file is exactly the ADTS payload after removing that header. The ASC file contains exactly `11 90`. The JSON manifest is the machine-readable source for generation provenance, file lengths and SHA-256 hashes; the README explains the fixture for humans.

### Generation

Add `tools/audio/generate_aac_fixture.py`. It invokes the local `ffmpeg` binary to generate 250 ms of deterministic 1 kHz, 48 kHz, stereo AAC-LC input at 192 kbps, parses the ADTS output, and selects zero-based frame index `2`. Selecting a fixed complete frame after encoder priming makes the generation procedure reproducible.

The generator stores:

- the one-frame ADTS representation;
- the extracted raw access unit;
- the two-byte ASC;
- the exact FFmpeg version/build string and command arguments;
- the selected zero-based ADTS frame index;
- byte lengths and SHA-256 hashes in `testdata/audio/fixture-manifest.json`.

The checked-in bytes and manifest are canonical. Different FFmpeg versions or builds may produce different valid compressed bytes; such output is not an automatic replacement. Regeneration requires an intentional fixture/wire-contract correction and review of the resulting manifest and golden-packet changes.

FFmpeg is fixture-generation tooling only. It is not a runtime or CI dependency.

### Structural validation

Add a Python standard-library validator that requires:

- ADTS sync word `0xFFF`;
- MPEG-4;
- layer `0`;
- `protection_absent = 1` (no CRC, 7-byte header);
- AAC-LC profile;
- sampling-frequency index `3` (48 kHz);
- channel configuration `2` (stereo);
- exactly one raw data block;
- ADTS frame length equal to the stored file length;
- non-empty raw payload;
- stored raw bytes exactly equal the ADTS payload.

The validator also parses the stored ASC and requires:

- exactly two bytes;
- audio object type `2` (AAC-LC);
- sampling-frequency index `3`;
- channel configuration `2`;
- `frameLengthFlag = 0` (1024-sample AAC frame);
- `dependsOnCoreCoder = 0`;
- `extensionFlag = 0`.

Finally, it validates the manifest length and SHA-256 hash for each of the three binary fixture files against the checked-in bytes and requires a non-empty recorded FFmpeg version/build string, non-empty command-argument list and selected frame index `2`.

The validator proves framing and metadata, including the signaled 1024-sample frame model. It does not prove that arbitrary compressed payload bytes decode correctly. FFmpeg provenance establishes how the canonical payload was produced; native decode in the following phase proves decoder semantics.

---

## Golden Packet Changes

`tools/protocol/generate_golden_packets.py` must read the canonical raw fixture instead of using arbitrary bytes.

Update the golden protocol values to:

```text
START_STREAM.FrameDuration = 21
AUDIO.FrameDuration = 21
AUDIO.EncodedData = canonical raw AAC fixture
```

Android and Windows writer/parser tests must read the same raw fixture and continue asserting exact packet bytes.

The common header and 19-byte `AUDIO` payload header do not change.

---

## Documentation Alignment

Update focused AAC-specific statements in:

- `docs/03-Protocol.md`
- `docs/04-Android.md`
- `docs/05-Windows.md`
- `docs/06-Audio.md`
- `docs/10-Testing.md`

The update must distinguish:

- exact AAC frame size: 1024 samples per channel;
- exact duration: 1024 / 48000 seconds;
- nominal integer wire duration: 21 ms;
- raw AAC packet framing;
- fixed `AudioSpecificConfig = 11 90`.

Do not blindly replace every `20 ms` value. Network, queue, timeout and latency-budget values that are not AAC frame duration remain unchanged.

`docs/10-Testing.md` must also clarify that golden binaries are regenerated for an intentional wire-contract correction, not only for a protocol-version-number change.

---

## Existing Fake Runtime

The current Android `HandshakeClient` remains an explicitly fake transport scaffold: it does not capture or encode device audio. However, an executable path that labels packets as `AAC-LC` must still conform to the frozen wire contract.

Add a small internal Android constant containing the canonical raw fixture as Base64. `HandshakeClient` decodes it once and uses that same valid raw AAC frame for each of its three development packets. A JVM test must prove the embedded bytes exactly match `testdata/audio/aac-lc-48k-stereo-1024.raw`.

Align fake stream metadata:

```text
START_STREAM.FrameDuration = 21
AUDIO.FrameDuration = 21
frame 1 capture timestamp = 123456003 us
frame 2 capture timestamp = 123477336 us  (+21333)
frame 3 capture timestamp = 123498670 us  (+21334)
PING payload timestamp = 123498671 us
PING common-header timestamp = 123498672 us
STOP_STREAM common-header timestamp = 123498673 us
```

The three fake frame timestamps are the first three values from the accumulated formula above. The next theoretical frame delta would be `+21333`, completing the repeating three-delta pattern; the fake runtime remains at three packets. Advancing the following PING and STOP_STREAM timestamps keeps the sender's common-header clock monotonic. A later sender runtime-integration phase replaces the embedded development frame with live encoded output.

---

## Non-Goals

Phase 1-N must not add:

- Android `MediaCodec`, `MediaProjection`, `AudioRecord` or foreground service.
- Windows Media Foundation decoder implementation, WASAPI or WaveOut playback.
- Discovery, mDNS service-type changes, heartbeat policy or automatic retry.
- A new protocol packet, new payload field or protocol-major-version change.
- FFmpeg or any codec library as an application dependency.
- A general production fixture/asset loading subsystem.

---

## Testing Requirements

### Fixture validation

Run the pure Python fixture validator and its standard-library unit tests in docs CI and local checks. Update `.github/workflows/docs.yml` explicitly; merely adding a script is insufficient.

ADTS mutation tests must independently reject and assert the specific failure for:

- invalid sync;
- MPEG-2 instead of MPEG-4;
- non-zero layer;
- `protection_absent = 0` instead of the required 7-byte-header form;
- unsupported profile;
- wrong sample-rate index;
- wrong channel configuration;
- multiple raw data blocks;
- declared frame-length mismatch, including trailing bytes;
- empty raw payload;
- stored raw payload mismatch.

ASC mutation tests must independently reject and assert the specific failure for:

- a length other than two bytes;
- wrong audio object type;
- wrong sampling-frequency index;
- wrong channel configuration;
- `frameLengthFlag = 1`;
- `dependsOnCoreCoder = 1`;
- `extensionFlag = 1`.

Manifest mutation tests must independently reject a stale file length, stale SHA-256 hash, empty FFmpeg version/build string, empty command-argument list and wrong selected frame index. ADTS and ASC sample-rate/channel mutations are separate cases; passing one parser cannot mask a missing check in the other.

The checked-in canonical fixture must pass.

### Protocol tests

Both Android and Windows tests must prove:

- `START_STREAM` with nominal duration `21` matches golden bytes;
- `AUDIO` with nominal duration `21` and the real raw AAC fixture matches golden bytes;
- parsing exposes the same complete raw bytes;
- the 19-byte `AUDIO` payload-header layout is unchanged.

### Fake stream regression

Existing Android and Windows fake-stream tests must remain green after changing nominal duration from `20` to `21`, correcting capture-timestamp deltas, replacing arbitrary encoded bytes with the canonical raw frame, and advancing the PING payload/common-header and STOP_STREAM common-header timestamps. Tests must assert all three exact AUDIO timestamps and the exact post-audio control timestamps.

### Repository checks

Run:

```bash
python3 tools/check_docs_consistency.py
python3 -m unittest discover -s tools/audio -p 'test_*.py'
python3 tools/audio/validate_aac_fixture.py
python3 tools/protocol/generate_golden_packets.py --check
git diff --check HEAD
```

Windows CI must run:

```powershell
dotnet test receiver-windows/OpenAudioLink.sln -c Release
```

Android CI must run:

```bash
cd sender-android
./gradlew :app:testDebugUnitTest
```

---

## Acceptance Criteria

Phase 1-N is complete when:

- Version 1 explicitly defines one raw 1024-sample AAC-LC frame per `AUDIO` packet.
- ADTS, LATM/LOAS, container bytes and codec-config buffers are explicitly excluded from `AUDIO.EncodedData`.
- AAC-LC/48 kHz/stereo `AudioSpecificConfig` is fixed to `11 90`.
- Nominal wire duration is `21 ms`, while exact timing remains sample/timestamp driven.
- A real one-frame ADTS fixture and its exact raw payload are checked in with a machine-validated provenance/hash manifest.
- The stored ASC proves AAC-LC/48 kHz/stereo with `frameLengthFlag = 0`.
- The Python validator and mutation tests prove fixture framing and reject all required malformed cases.
- Golden `START_STREAM` and `AUDIO` packets use the new contract.
- Android and Windows exact-byte tests use the same raw fixture.
- The executable fake transport sends the canonical valid raw frame, uses nominal duration `21`, follows the accumulated rational timestamp formula, and keeps post-audio control timestamps monotonic.
- No native codec, playback, discovery, packet-layout change or runtime dependency is added.
- Docs, Android and Windows CI workflows on the phase branch are green.

---

## Follow-Up Order

After Phase 1-N:

1. Windows Media Foundation AAC decoder consumes the canonical fixture and emits PCM.
2. Android MediaCodec encoder emits raw access units matching this contract.
3. Android playback capture feeds real PCM into the encoder.
4. Windows renderer sends decoded PCM to the audio device.
