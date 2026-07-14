# Phase 1-O Windows Media Foundation AAC Decoder Design

**Status:** Draft for implementation

**Date:** 2026-07-15

**Scope:** Prove native AAC-LC decoding through the Windows Media Foundation AAC Decoder MFT without connecting it to the current fake receiver runtime.

---

## Goal

Phase 1-O proves that the canonical Version 1 AAC-LC wire format can be decoded by the Windows platform codec.

After this phase:

- A small `MediaFoundationAacDecoder` accepts one raw AAC-LC access unit at a time and returns all currently available interleaved PCM output.
- The decoder uses `CLSID_CMSAACDecMFT` and `IMFTransform` directly, with no third-party runtime or NuGet codec wrapper.
- A checked-in six-frame continuous ADTS fixture supplies consecutive access units for a real native decode test.
- Windows CI runs the native test in both x86 and x64 processes on `windows-2022`.
- The existing `FakeAacDecoder`, `ReceiverRuntime`, network session, queue, renderer and UI behavior remain unchanged.

This is a standalone codec proof. It establishes the native boundary needed by a later receiver integration phase; it does not claim real playback or runtime decode.

---

## Current Baseline

Phase 1-N froze the Version 1 codec contract:

| Property | Value |
|----------|------:|
| MPEG-4 audio object type | 2 (`AAC-LC`) |
| Sample rate | 48000 Hz |
| Channels | 2 |
| PCM samples per access unit, per channel | 1024 |
| `AudioSpecificConfig` | `11 90` |
| `AUDIO.EncodedData` | one complete raw AAC access unit |
| ADTS on the wire | forbidden |
| Nominal wire `Frame Duration` | 21 ms |

The canonical one-frame fixture proves framing and provenance, but it deliberately does not prove native decoding. One access unit is also insufficient for a decoder test because an MFT may buffer input before producing PCM.

The executable Windows path is still fake:

```text
TcpReceiver
  -> ReceiverSession
  -> ReceiverRuntime callback
  -> AudioFrameQueue
  -> FakeAudioRenderer.Drain
  -> FakeAacDecoder
  -> FakePcmFrame
```

`FakeAacDecoder` copies encoded bytes into a fake PCM object. The current session and runtime do not expose native decoder create, drain, flush or teardown boundaries. Replacing the fake object now would mix codec proof with session lifecycle, reconnect, queue and renderer work.

The Windows projects target .NET Framework 4.8. Local Linux validation cannot execute them, so native behavior is accepted only from exact-head Windows CI evidence.

Microsoft documents the desktop AAC Decoder MFT on Windows 7 and later. Phase 1-O verifies only the `windows-2022` runner and therefore does not replace the separate minimum-OS release gate.

---

## Selected Approach

### Direct AAC Decoder MFT

Create the Microsoft AAC Decoder COM class directly and drive its `IMFTransform` interface:

```text
raw AAC access unit
  -> IMFSample / IMFMediaBuffer
  -> CLSID_CMSAACDecMFT
  -> IMFTransform.ProcessInput / ProcessOutput
  -> 48 kHz stereo signed 16-bit PCM bytes
```

Only the COM interfaces, structures, GUIDs, HRESULTs and Media Foundation functions required by this flow are declared. The implementation is limited to:

```text
receiver-windows/src/OpenAudioLink/Receiver/MediaFoundationAacDecoder.cs
receiver-windows/src/OpenAudioLink/Receiver/MediaFoundationInterop.cs
```

This is the smallest native proof that exercises the same raw-access-unit boundary the future runtime will use.

### Rejected: MediaFoundation.Net

A wrapper adds a runtime dependency and a second interop surface for two focused files. Phase 1-O needs neither a general Media Foundation framework nor wrapper-specific abstractions.

### Rejected: `IMFSourceReader`

The Source Reader is useful when a byte stream or media source owns container parsing and decode. OpenAudioLink already receives discrete raw AAC access units. Feeding those units through a custom media source would add buffering and container machinery before proving the decoder itself.

### Rejected: immediate runtime integration

The current fake runtime has no correct native codec session lifecycle. Integration would require new ownership, worker-thread, flush, reconnect and renderer rules. Those concerns remain a separate phase after this proof.

---

## Non-Goals

Phase 1-O must not:

- Replace or modify `FakeAacDecoder`, `FakeAudioRenderer`, `ReceiverRuntime`, `ReceiverSession`, `TcpReceiver` or `AudioFrameQueue`.
- Add an `IAudioDecoder`, factory, dependency-injection container or configurable codec selection.
- Add WASAPI, WaveOut, audible playback, a PCM queue, jitter buffering or latency control.
- Add a decoder worker thread to production code.
- Add an asynchronous-MFT event loop; this proof accepts only the synchronous processing model.
- Add FFmpeg, MediaFoundation.Net or another runtime codec dependency.
- Add Android capture/encoding work or change protocol bytes.
- Add a PCM golden hash or require bit-exact PCM across Windows versions and architectures.
- Add a decode-latency or CPU performance gate; this phase proves correctness and lifetime only.
- Claim Windows 7 release validation from a `windows-2022` CI run.

---

## Continuous AAC Fixture

### Exact extension

Keep the existing three binary fixtures byte-for-byte unchanged and add:

```text
testdata/audio/aac-lc-48k-stereo-6frames.adts
```

`tools/audio/generate_aac_fixture.py` continues generating the same deterministic 250 ms FFmpeg source. It selects zero-based ADTS frame index `2` for the existing one-frame `.adts` and `.raw` files, then concatenates exactly source frame indices `2..7` into the new continuous file.

The manifest remains `format: 1` and gains:

```json
"generator": {
  "selectedFrameIndex": 2,
  "selectedFrameCount": 6
}
```

Its exact `files` map contains these four binary records:

```text
aac-lc-48k-stereo-1024.adts
aac-lc-48k-stereo-1024.raw
aac-lc-48k-stereo.asc
aac-lc-48k-stereo-6frames.adts
```

Each record stores the checked-in byte length and SHA-256 hash. Generation fails unless at least eight complete source frames exist. `README.md` records the selected range and the new file's length and hash.

Using the already-recorded canonical FFmpeg 4.4.2 build and unchanged command, frames `2..7` have ADTS lengths `420, 158, 159, 160, 158, 159`. The new canonical file is therefore frozen as:

```text
length: 1214
sha256: 587a29c59fa6fb508090f75404643e8b567fc4c621b0df6033ed7c46841e38ac
```

### Structural validation

The standard-library validator splits the continuous file by declared ADTS frame lengths and requires:

- exactly six complete frames with no truncated header, frame or trailing bytes;
- every frame is MPEG-4 AAC-LC, 48 kHz, stereo, `protection_absent = 1`, with exactly one raw data block;
- every frame has a non-empty payload;
- the first continuous ADTS frame exactly equals `aac-lc-48k-stereo-1024.adts`;
- `selectedFrameIndex == 2` and `selectedFrameCount == 6` are integers, not booleans or floating-point values;
- the manifest has exact length/hash records and no unexpected file record.

Mutation tests independently reject a wrong frame count, malformed middle frame, truncated final frame, trailing bytes, stale length/hash and invalid manifest selection metadata.

The Windows test project contains a small test-only ADTS splitter. It validates sync, the seven-byte no-CRC header and frame boundaries only, then removes each header and submits six raw access units separately. The Python validator remains the single complete semantic validator; production decoder code never accepts or parses ADTS.

No continuous raw file and no PCM golden are added.

---

## Decoder API And State

The production surface is deliberately narrow:

```csharp
public sealed class MediaFoundationAacDecoder : IDisposable
{
    public MediaFoundationAacDecoder();

    public IReadOnlyList<byte[]> Submit(byte[] rawAccessUnit);

    public IReadOnlyList<byte[]> Drain();

    public void Dispose();
}
```

The class remains in the existing `OpenAudioLink.Receiver` namespace. Its public constructor has no options because Version 1 has one fixed audio format.

`Submit` copies one complete raw access unit into an `IMFSample`, drives `ProcessInput` until that sample is accepted exactly once, and returns every PCM chunk made available while accepting and processing it. A call may return zero, one or multiple chunks. Returned arrays are owned by the caller and are not reused by the decoder.

`Drain` signals end-of-stream, extracts all delayed PCM, and moves the object to `Drained`. A second `Drain` returns an empty list without sending duplicate MFT messages. `Submit` after the first drain is invalid.

The internal state is:

```text
Active -> Drained
   |          |
   v          v
Faulted -> Disposed
```

`Active` can also transition directly to `Disposed`. `Dispose` is deterministic and idempotent on the owner thread. It does not implicitly drain because discarded PCM cannot be returned from `Dispose`. There is no finalizer.

---

## Media Foundation Configuration

### Startup and transform creation

Construction on the owner thread performs, in order:

```text
CoInitializeEx(COINIT_MULTITHREADED)
MFStartup(MF_VERSION, MFSTARTUP_FULL)
CoCreateInstance(CLSID_CMSAACDecMFT, CLSCTX_INPROC_SERVER, IID_IMFTransform)
Require one input stream and one output stream, both ID 0
Reject MF_TRANSFORM_ASYNC = 1
SetInputType
SetOutputType
GetOutputStreamInfo
MFT_MESSAGE_NOTIFY_BEGIN_STREAMING
MFT_MESSAGE_NOTIFY_START_OF_STREAM
```

The constructor requires exactly one input and one output stream. If `GetStreamIDs` returns `E_NOTIMPL`, the standard default IDs are `0`; otherwise the returned IDs must both be `0`. Phase 1-O does not add dynamic stream-ID mapping.

The AAC decoder class identifier is fixed to:

```text
CLSID_CMSAACDecMFT = 32D186A7-218F-4C75-8876-DD77273A8999
IID_IMFTransform = BF94C121-5B05-4E6F-8000-BA598961414D
MF_VERSION = 0x00020070
```

### Input media type

The input `IMFMediaType` is set explicitly to:

| Attribute | Value |
|-----------|------:|
| `MF_MT_MAJOR_TYPE` | `MFMediaType_Audio` |
| `MF_MT_SUBTYPE` | `MFAudioFormat_AAC` |
| `MF_MT_AUDIO_SAMPLES_PER_SECOND` | `48000` |
| `MF_MT_AUDIO_NUM_CHANNELS` | `2` |
| `MF_MT_AAC_PAYLOAD_TYPE` | `0` (raw AAC) |
| `MF_MT_AAC_AUDIO_PROFILE_LEVEL_INDICATION` | `0xFE` |
| `MF_MT_USER_DATA` | `00 00 FE 00 00 00 00 00 00 00 00 00 11 90` |

The first 12 `MF_MT_USER_DATA` bytes are the little-endian `HEAACWAVEINFO` members after `WAVEFORMATEX`; the final two bytes are `AudioSpecificConfig = 11 90`. Media Foundation treats payload type and profile-level indication as optional in this case, but Phase 1-O sets both explicitly to make the raw-wire contract unambiguous. `MF_MT_AUDIO_BITS_PER_SAMPLE` is not used as an AAC framing requirement; the exact 16-bit requirement is set and verified on the PCM output type.

Phase 1-O uses the synchronous MFT processing model. If the transform attributes report `MF_TRANSFORM_ASYNC = 1`, construction fails with `PlatformNotSupportedException` rather than incorrectly driving an asynchronous MFT without its event protocol. `GetAttributes == E_NOTIMPL`, a missing attribute, or value zero selects the synchronous path; other attribute-query failures remain fatal HRESULT errors.

### Output media type

The output `IMFMediaType` is set explicitly to:

| Attribute | Value |
|-----------|------:|
| `MF_MT_MAJOR_TYPE` | `MFMediaType_Audio` |
| `MF_MT_SUBTYPE` | `MFAudioFormat_PCM` |
| `MF_MT_AUDIO_SAMPLES_PER_SECOND` | `48000` |
| `MF_MT_AUDIO_NUM_CHANNELS` | `2` |
| `MF_MT_AUDIO_BITS_PER_SAMPLE` | `16` |
| `MF_MT_AUDIO_BLOCK_ALIGNMENT` | `4` |
| `MF_MT_AUDIO_AVG_BYTES_PER_SECOND` | `192000` |

After `SetOutputType`, the decoder reads back and requires these exact attributes. A different type is an initialization failure rather than an implicit conversion.

`GetOutputStreamInfo.cbSize` determines caller-allocated output capacity; `4096` is not hard-coded. If `MFT_OUTPUT_STREAM_PROVIDES_SAMPLES` is set, the MFT supplies the sample. Otherwise the decoder requires non-zero `cbSize` and supplies an `IMFSample` and buffer of at least that size. A zero `cbAlignment` uses `MFCreateMemoryBuffer`; a non-zero power-of-two byte alignment uses `MFCreateAlignedMemoryBuffer(cbSize, cbAlignment - 1)`, matching that function's alignment-mask constants. An invalid alignment fails initialization. For `MFT_OUTPUT_STREAM_CAN_PROVIDE_SAMPLES`, the decoder still supplies its own sample for one deterministic ownership path.

### Interop ABI boundary

`MediaFoundationInterop.cs` uses `[ComImport]`, `InterfaceIsIUnknown`, SDK interface GUIDs and sequential native structures. COM methods remain in exact Windows SDK vtable order; unused methods or inherited slots are not omitted before a method that Phase 1-O calls. Derived interfaces such as `IMFMediaType` and `IMFSample` explicitly preserve the inherited `IMFAttributes` slots required by .NET Framework COM interop. Called method signatures use pointer-sized `IntPtr`/`UIntPtr` fields where the native ABI does.

Every called COM or P/Invoke operation that returns `HRESULT` uses `[PreserveSig] int`. The implementation examines control-flow results first, then converts unexpected failures through one helper that includes the operation name and hexadecimal HRESULT. In particular:

| Result | Value | Meaning in this phase |
|--------|-------|-----------------------|
| `MF_E_NOTACCEPTING` | `0xC00D36B5` | collect output and retry the same input |
| `MF_E_TRANSFORM_STREAM_CHANGE` | `0xC00D6D61` | fail; no format renegotiation |
| `MF_E_TRANSFORM_NEED_MORE_INPUT` | `0xC00D6D72` | output collection is complete for now |

The interop file does not expose a general-purpose Media Foundation wrapper.

---

## Thread, COM And Media Foundation Lifetime

The constructor captures `Environment.CurrentManagedThreadId`. The constructor, `Submit`, `Drain` and `Dispose` must all execute on that same MTA thread. A wrong-thread call fails before touching COM.

`CoInitializeEx` results `S_OK` and `S_FALSE` are both successful and both require one matching `CoUninitialize`. `RPC_E_CHANGED_MODE` and other failures include the operation and HRESULT in the reported exception.

Each successful `MFStartup` has exactly one matching `MFShutdown`. Construction failure releases every acquired object and unwinds successful startup calls in reverse order. Normal disposal performs:

```text
release transform and remaining COM objects
MFShutdown
CoUninitialize
```

No RCW is cached globally. Each owned COM interface reference is released exactly once. The implementation uses `Marshal.ReleaseComObject`, not `Marshal.FinalReleaseComObject`, so releasing one local owner cannot invalidate an alias held elsewhere.

This per-decoder startup/shutdown model is intentional for the standalone proof. A later runtime-integration design may centralize process-wide Media Foundation lifetime after defining session ownership.

---

## Input Timing

MFT sample times use 100 ns units. For zero-based submitted access-unit index `n`:

```text
sampleTime[n]
  = round(n * 1024 * 10,000,000 / 48,000)
  = round(n * 640,000 / 3)
```

The sample duration is:

```text
sampleDuration[n] = sampleTime[n + 1] - sampleTime[n]
```

This produces the repeating exact integer pattern `213333`, `213334`, `213333` in 100 ns units. The implementation uses checked integer arithmetic from the frame index; it never accumulates the nominal wire value `21 ms`.

---

## ProcessInput And ProcessOutput

### Submit flow

For each access unit:

1. Create one input sample and one buffer sized to the compressed bytes.
2. Copy under `IMFMediaBuffer.Lock`, with `Unlock` in `finally`.
3. Set current length, sample time and sample duration.
4. Call `ProcessInput(0, sample, 0)`.
5. On `S_OK`, release the caller's sample reference, increment the accepted-input index and collect available output.
6. On `MF_E_NOTACCEPTING`, collect available output and retry the same unaccepted sample without changing its index or timestamp.
7. Treat any other failure as fatal.

`MF_E_NOTACCEPTING` is control flow, not a dropped frame. The input sample remains owned by the caller until a successful `ProcessInput` and is released on every terminal path.

The retry loop has a no-progress guard: if the same input is rejected again without an intervening successful `ProcessOutput`, the decoder faults instead of spinning indefinitely.

### Output flow

`ProcessOutput` repeats until `MF_E_TRANSFORM_NEED_MORE_INPUT`:

- `S_OK` must yield a non-empty sample. Its buffers are converted to one contiguous buffer and copied to a new `byte[]`.
- The returned byte length must be divisible by block alignment `4`.
- `MF_E_TRANSFORM_NEED_MORE_INPUT` ends the current output collection successfully.
- `MF_E_TRANSFORM_STREAM_CHANGE` and every other unexpected HRESULT fail with the operation and hexadecimal HRESULT; Phase 1-O does not renegotiate formats.

Every caller-allocated output sample and buffer, MFT-provided sample, contiguous buffer and `MFT_OUTPUT_DATA_BUFFER.pEvents` reference is released on success and failure. All `Lock` calls have `Unlock` in `finally`.

### Drain flow

The first `Drain` sends:

```text
MFT_MESSAGE_NOTIFY_END_OF_STREAM (input stream ID 0)
MFT_MESSAGE_COMMAND_DRAIN (parameter 0)
ProcessOutput until MF_E_TRANSFORM_NEED_MORE_INPUT
MFT_MESSAGE_NOTIFY_END_STREAMING (parameter 0)
```

All delayed output is returned. The decoder then enters `Drained` even when no PCM was buffered.

---

## Error Model

Public argument and state failures are deterministic:

| Condition | Result |
|-----------|--------|
| `rawAccessUnit == null` | `ArgumentNullException` |
| empty access unit | `ArgumentException` |
| `Submit` after `Drain` | `InvalidOperationException` |
| `Submit` or `Drain` after a native decode failure | `InvalidOperationException` |
| call after `Dispose` | `ObjectDisposedException` |
| cross-thread call | `InvalidOperationException` |
| Media Foundation DLL/entry point missing, `CoCreateInstance` returns `REGDB_E_CLASSNOTREG (0x80040154)`, or the MFT reports asynchronous processing | `PlatformNotSupportedException` |
| other failing HRESULT | `InvalidOperationException` with operation and `0xXXXXXXXX` HRESULT |

`MF_E_NOTACCEPTING` and `MF_E_TRANSFORM_NEED_MORE_INPUT` are handled only in their valid state-machine positions and are never surfaced as generic failures.

Instance validation order is owner thread, disposed state, operation-specific faulted/drained state, then `Submit` argument shape. Thus a cross-thread call always reports thread affinity, and an owner-thread call on a disposed object reports `ObjectDisposedException` before inspecting its argument.

Argument, state and wrong-thread validation do not change decoder state. An unexpected native HRESULT, invalid native output or failed drain moves an already-constructed decoder to `Faulted`; only owner-thread `Dispose` remains valid. No input corruption recovery is added. Per-frame skip/restart policy belongs to runtime integration.

---

## Native Windows Tests

All native tests execute their complete decoder lifecycle on a dedicated thread configured with `ApartmentState.MTA`. Test helpers propagate worker exceptions to MSTest and always dispose the decoder on its owner thread.

The continuous decode test:

1. Reads `aac-lc-48k-stereo-6frames.adts`.
2. Splits and strips six ADTS headers in test code.
3. Calls `Submit` for each raw access unit without assuming any individual call returns PCM.
4. Calls `Drain` and appends delayed output.
5. Requires exactly `6 * 1024 * 2 channels * 2 bytes = 24576` PCM bytes; output-call boundaries may differ, but no submitted AAC frame may disappear.
6. Interprets output as little-endian signed 16-bit interleaved stereo and requires non-zero energy independently in the left and right channels.
7. Repeats create, decode, drain and dispose in the same process and requires the second run to succeed.

Construction validates the exact output media type before decoding, so successful decode also proves 48 kHz, stereo, 16-bit PCM, block alignment `4` and average bytes per second `192000` were accepted and read back.

Focused tests also prove null/empty rejection, idempotent second `Drain`, `Submit` after drain, use after dispose and wrong-thread rejection. The CI lane passes its expected architecture to the test process, which asserts `IntPtr.Size == 4` for x86 and `8` for x64 before constructing COM objects. Tests do not require the first access unit to emit PCM, a fixed number of output calls or a platform-independent PCM hash.

The native CI test never treats `PlatformNotSupportedException` as a skip. Missing x86 or x64 AAC MFT registration is a failed Phase 1-O proof.

---

## Windows CI

`.github/workflows/windows.yml` is pinned to:

```yaml
runs-on: windows-2022
```

Its test job uses an `x86` / `x64` matrix and runs:

```yaml
- name: Test (${{ matrix.architecture }})
  env:
    OAL_TEST_ARCHITECTURE: ${{ matrix.architecture }}
  run: dotnet test receiver-windows/OpenAudioLink.sln -c Release -- RunConfiguration.TargetPlatform=${{ matrix.architecture }}
```

The target-platform setting chooses the testhost process architecture, so the same Any CPU test assembly exercises 32-bit and 64-bit COM ABI declarations. x86 is an interop verification lane, not a new product-support promise; the documented product target remains x64.

Docs and Android workflows remain unchanged except for any repository checks already triggered by the new fixture records. Phase completion requires the `docs`, `android` and matrix-expanded `windows` workflows to succeed for the exact phase-branch HEAD.

---

## Documentation Alignment

Implementation updates focused statements in:

- `docs/05-Windows.md`: record the standalone native proof, distinguish it from runtime integration, remove any unsupported hardware-acceleration claim, and clarify that `192 kbps` describes the canonical fixture/Version 1 stream metadata rather than an additional MFT framing requirement.
- `docs/06-Audio.md`: replace the stale Source Reader diagram with direct MFT processing for discrete raw access units; identify the documented `IAudioDecoder` and queue/thread/playback architecture as future integration work; replace the misleading “AAC headers” validation wording because production input has no ADTS header.
- `docs/10-Testing.md`: add the continuous fixture, native decode assertions, buffering/drain semantics and x86/x64 CI policy.

The docs must not say that `ReceiverRuntime` uses the native decoder or that playback is audible.

---

## Repository Verification

Local checks:

```bash
python3 tools/check_docs_consistency.py
python3 -m unittest discover -s tools/audio -p 'test_*.py'
python3 tools/audio/validate_aac_fixture.py
python3 tools/protocol/generate_golden_packets.py --check
git diff --check HEAD
```

Windows CI performs the two target-platform `dotnet test` invocations described above. Existing Android tests remain green; Phase 1-O makes no Android production change.

---

## Acceptance Criteria

Phase 1-O is complete when:

- The existing one-frame AAC fixtures remain byte-for-byte unchanged.
- The canonical continuous fixture contains exactly FFmpeg source frames `2..7`, is fully manifest-validated and supplies six raw access units to Windows tests.
- `MediaFoundationAacDecoder` uses the Microsoft AAC Decoder MFT directly and has only the approved `Submit`, `Drain` and `Dispose` surface.
- Input configuration matches raw AAC-LC, 48 kHz, stereo and `AudioSpecificConfig = 11 90` exactly.
- Output is read back and validated as 48 kHz stereo signed 16-bit PCM with four-byte block alignment and 192000 average bytes per second.
- Buffered input/output, `MF_E_NOTACCEPTING`, `MF_E_TRANSFORM_NEED_MORE_INPUT` and drain are handled without assuming one-in/one-out behavior.
- Sample times derive from the 1024/48000 rational clock in 100 ns units, not nominal `21 ms` accumulation.
- COM interfaces, buffers, events, Media Foundation startup and COM apartment initialization are deterministically balanced on one MTA thread, including constructor failure.
- Real native decode produces exactly 6144 stereo sample frames, has non-zero energy in both channels, and passes repeat create/decode/dispose in x86 and x64 Windows CI processes.
- State, argument, thread-affinity and unavailable-platform errors follow the specified model.
- Active docs clearly say native decode is proven standalone but not connected to the fake runtime or playback.
- No protocol, Android, runtime receiver, renderer or playback behavior changes.
- Docs, Android and Windows workflows are green for the exact phase-branch HEAD.

---

## Official References

- AAC Decoder: <https://learn.microsoft.com/en-us/windows/win32/medfound/aac-decoder>
- Basic MFT Processing Model: <https://learn.microsoft.com/en-us/windows/win32/medfound/basic-mft-processing-model>
- `IMFTransform::ProcessInput`: <https://learn.microsoft.com/en-us/windows/win32/api/mftransform/nf-mftransform-imftransform-processinput>
- `IMFTransform::ProcessOutput`: <https://learn.microsoft.com/en-us/windows/win32/api/mftransform/nf-mftransform-imftransform-processoutput>
- `IMFTransform::GetOutputStreamInfo`: <https://learn.microsoft.com/en-us/windows/win32/api/mftransform/nf-mftransform-imftransform-getoutputstreaminfo>
- `MFT_OUTPUT_STREAM_INFO`: <https://learn.microsoft.com/en-us/windows/win32/api/mftransform/ns-mftransform-mft_output_stream_info>
- `MFCreateAlignedMemoryBuffer`: <https://learn.microsoft.com/en-us/windows/win32/api/mfapi/nf-mfapi-mfcreatealignedmemorybuffer>
- MFT messages: <https://learn.microsoft.com/en-us/windows/win32/api/mftransform/ne-mftransform-mft_message_type>
- `MF_TRANSFORM_ASYNC`: <https://learn.microsoft.com/en-us/windows/win32/medfound/mf-transform-async>
- `MFStartup`: <https://learn.microsoft.com/en-us/windows/win32/api/mfapi/nf-mfapi-mfstartup>
- `MFShutdown`: <https://learn.microsoft.com/en-us/windows/win32/api/mfapi/nf-mfapi-mfshutdown>
- `CoInitializeEx`: <https://learn.microsoft.com/en-us/windows/win32/api/combaseapi/nf-combaseapi-coinitializeex>
- RunSettings `TargetPlatform`: <https://learn.microsoft.com/en-us/visualstudio/test/configure-unit-tests-by-using-a-dot-runsettings-file#runconfiguration-element>
- Windows SDK `wmcodecdsp.h`: <https://github.com/microsoft/win32metadata/blob/main/generation/WinSDK/RecompiledIdlHeaders/um/wmcodecdsp.h>
- Windows SDK `Mferror.h`: <https://github.com/microsoft/win32metadata/blob/main/generation/WinSDK/RecompiledIdlHeaders/um/Mferror.h>
