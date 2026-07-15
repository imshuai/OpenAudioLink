# Phase 1-Q Windows Decoder Runtime Integration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Route canonical TCP `AUDIO` packets through the existing Windows Media Foundation AAC decoder and record real 48 kHz stereo PCM in the existing fake renderer, with decoder ownership scoped to one TCP stream.

**Architecture:** Extend `TcpReceiver` with two optional stream-lifecycle callbacks executed on the accepted-client thread. `ReceiverRuntime` uses them to create, feed, drain, and dispose one `MediaFoundationAacDecoder`; a private 4096-byte assembler maps arbitrary PCM output chunks to FIFO wire metadata before calling `FakeAudioRenderer.Render`. No new worker, decoder interface, PCM queue, dependency, Android behavior, or audible playback is added.

**Tech Stack:** C#/.NET Framework 4.8, MSTest, Windows Media Foundation `IMFTransform`, TCP loopback, GitHub Actions `windows-2022` x86/x64, Python documentation/fixture checks, Markdown.

---

## Scope Check

This plan implements only:

```text
docs/superpowers/specs/2026-07-15-phase-1q-windows-decoder-runtime-integration-design.md
```

In scope:

- Non-empty raw AAC payload validation.
- `TcpReceiver` stream-start/end callbacks on one accepted-client thread.
- The accepted-client publication versus `Dispose` race fix.
- One stream-scoped `MediaFoundationAacDecoder` inside `ReceiverRuntime`.
- FIFO frame metadata and fixed 4096-byte PCM assembly.
- Final decoder drain/disposal on clean stop, disconnect, failure, and shutdown.
- Real x86/x64 TCP loopback decode, reconnect, and WinForms status tests.
- Focused active-document corrections and exact-head CI evidence.

Out of scope:

- Android runtime integration, capture, encoding, pacing, or packet changes.
- WASAPI, speaker output, playback devices, PCM queues, decoder workers, clocks, or latency claims.
- Live-stream decoder recreation, corruption thresholds, diagnostics, discovery, settings, installer, or UI layout.
- New packet types, result values, error codes, fixtures, dependencies, interfaces, factories, or DI.

---

## Files And Responsibilities

Production:

- `receiver-windows/src/OpenAudioLink/Protocol/AudioPayloadValidator.cs` — reject an empty raw AAC access unit.
- `receiver-windows/src/OpenAudioLink/Protocol/PacketParseException.cs` — preserve native failure context.
- `receiver-windows/src/OpenAudioLink/Receiver/TcpReceiver.cs` — stream lifecycle and disposal-race-safe client ownership.
- `receiver-windows/src/OpenAudioLink/Receiver/ReceiverRuntime.cs` — native decode, PCM assembly, metadata mapping, render, and teardown.

Tests:

- `receiver-windows/tests/OpenAudioLink.Tests/Protocol/PacketParserTests.cs`
- `receiver-windows/tests/OpenAudioLink.Tests/Receiver/TcpReceiverTests.cs`
- `receiver-windows/tests/OpenAudioLink.Tests/Receiver/ReceiverRuntimeTests.cs`
- `receiver-windows/tests/OpenAudioLink.Tests/UI/MainFormTests.cs`

Documentation:

- `docs/03-Protocol.md`
- `docs/05-Windows.md`
- `docs/06-Audio.md`
- `docs/10-Testing.md`
- `docs/11-Roadmap.md`
- Phase 1-Q spec status/evidence.

No new production source file is required. The stream helper remains private in `ReceiverRuntime.cs`.

---

## Execution Conventions

Branch:

```text
phase-1q-windows-decoder-runtime-integration
```

Base main SHA:

```text
5dbd9a71ef161743caed133c4960b55165081e58
```

Execute implementation only in:

```text
/root/.config/superpowers/worktrees/OpenAudioLink/phase-1q-windows-decoder-runtime-integration
```

The spec commit and this plan commit precede Task 1 and remain in the phase diff.

After committing the reviewed spec and plan, create the required isolated worktree from the primary checkout:

```bash
test -z "$(git status --porcelain)"
git switch main
git worktree add \
  /root/.config/superpowers/worktrees/OpenAudioLink/phase-1q-windows-decoder-runtime-integration \
  phase-1q-windows-decoder-runtime-integration
```

Run all following task commands from that worktree. Do not implement in the primary checkout.

The Linux host has no `dotnet`, .NET Framework, or Media Foundation runtime. Python checks run locally; every intentional C# RED and GREEN is established by the exact pushed SHA in the existing `windows-2022` matrix. Intermediate pushes need only the decisive Windows result before continuing; the final phase head requires all `docs`, `windows`, and `android` workflows green.

Push to Gitea with the MTU workaround:

```bash
set -e
ADDED_ROUTE=0
if ip route add 192.168.3.20/32 via 192.168.4.1 dev eth0 mtu 1200 2>/dev/null; then
  ADDED_ROUTE=1
fi
cleanup() {
  if [ "$ADDED_ROUTE" -eq 1 ]; then
    ip route del 192.168.3.20/32 via 192.168.4.1 dev eth0 || true
  fi
}
trap cleanup EXIT
git push origin phase-1q-windows-decoder-runtime-integration
```

Verify source and mirror refs:

```bash
HEAD_SHA=$(git rev-parse HEAD)
GITEA_SHA=$(git ls-remote origin refs/heads/phase-1q-windows-decoder-runtime-integration | cut -f1)
test "$GITEA_SHA" = "$HEAD_SHA"
for attempt in $(seq 1 60); do
  GITHUB_SHA=$(gh api repos/imshuai/OpenAudioLink/commits/phase-1q-windows-decoder-runtime-integration --jq .sha 2>/dev/null || true)
  [ "$GITHUB_SHA" = "$HEAD_SHA" ] && break
  sleep 5
done
test "$GITHUB_SHA" = "$HEAD_SHA"
```

List exact-head runs:

```bash
HEAD_SHA=$(git rev-parse HEAD)
gh api 'repos/imshuai/OpenAudioLink/actions/runs?branch=phase-1q-windows-decoder-runtime-integration&per_page=30' \
  --jq ".workflow_runs[] | select(.head_sha == \"$HEAD_SHA\") | [.id,.name,.status,.conclusion] | @tsv"
```

Use this final gate to wait for exactly one successful `push` run per workflow; pull-request runs at the same SHA are listed separately and never substituted for the branch-push gate:

```bash
set -e
HEAD_SHA=$(git rev-parse HEAD)
ALL_SUCCESS=0
for attempt in $(seq 1 180); do
  ALL_SUCCESS=1
  for WORKFLOW in docs windows android; do
    COUNT=$(gh api 'repos/imshuai/OpenAudioLink/actions/runs?branch=phase-1q-windows-decoder-runtime-integration&per_page=100' \
      --jq "[.workflow_runs[] | select(.head_sha == \"$HEAD_SHA\" and .event == \"push\" and .name == \"$WORKFLOW\")] | length")
    test "$COUNT" -le 1
    if [ "$COUNT" -ne 1 ]; then
      ALL_SUCCESS=0
      continue
    fi
    RESULT=$(gh api 'repos/imshuai/OpenAudioLink/actions/runs?branch=phase-1q-windows-decoder-runtime-integration&per_page=100' \
      --jq "[.workflow_runs[] | select(.head_sha == \"$HEAD_SHA\" and .event == \"push\" and .name == \"$WORKFLOW\")][0] | [.status,.conclusion] | @tsv")
    [ "$RESULT" = $'completed\tsuccess' ] || ALL_SUCCESS=0
  done
  [ "$ALL_SUCCESS" -eq 1 ] && break
  sleep 10
done
test "$ALL_SUCCESS" -eq 1

gh api 'repos/imshuai/OpenAudioLink/actions/runs?branch=phase-1q-windows-decoder-runtime-integration&per_page=100' \
  --jq ".workflow_runs[] | select(.head_sha == \"$HEAD_SHA\" and .event == \"push\") | [.id,.name,.status,.conclusion] | @tsv"
gh api 'repos/imshuai/OpenAudioLink/actions/runs?branch=phase-1q-windows-decoder-runtime-integration&per_page=100' \
  --jq ".workflow_runs[] | select(.head_sha == \"$HEAD_SHA\" and .event == \"pull_request\") | [.id,.name,.status,.conclusion] | @tsv"
```

Every implementation task ends with specification review, then code-quality review. Fix every Critical or Important finding and repeat the affected review before advancing.

---

### Task 1: Reject Empty AAC Access Units

**Files:**
- Modify: `receiver-windows/tests/OpenAudioLink.Tests/Protocol/PacketParserTests.cs`
- Modify: `receiver-windows/src/OpenAudioLink/Protocol/AudioPayloadValidator.cs`

- [ ] **Step 1: Add the focused failing test**

Add beside the existing AAC validator tests:

```csharp
[TestMethod]
public void ValidateAacPayload_EmptyEncodedData_Throws()
{
    byte[] payload = HandshakePayloads.Audio(
        ProtocolConstants.CodecAacLc,
        1u,
        2UL,
        21,
        new byte[0]);

    Assert.ThrowsException<PacketParseException>(
        () => AudioPayloadValidator.ValidateAacPayload(payload));
}
```

- [ ] **Step 2: Commit and prove intentional RED**

```bash
git add receiver-windows/tests/OpenAudioLink.Tests/Protocol/PacketParserTests.cs
git commit -m "test: reject empty AAC access units"
```

Push and inspect the exact-head Windows run. Expected: x86 and x64 fail only because the new test observes no exception.

- [ ] **Step 3: Add minimum production validation**

Retain malformed-length precedence, then reject zero:

```csharp
uint encodedSize = PacketParser.ReadUInt32(payload, 15);
if ((ulong)payload.Length != (ulong)ProtocolConstants.AudioPayloadHeaderSize + encodedSize)
{
    throw new PacketParseException("AAC payload length mismatch.");
}

if (encodedSize == 0)
{
    throw new PacketParseException("AAC access unit is empty.");
}
```

- [ ] **Step 4: Commit and prove GREEN**

```bash
git add receiver-windows/src/OpenAudioLink/Protocol/AudioPayloadValidator.cs
git commit -m "fix: reject empty AAC payload data"
```

Push and require exact-head Windows x86/x64 green, including all existing validator tests.

- [ ] **Step 5: Review Task 1**

Specification review confirms no wire-byte change. Quality review confirms length mismatch still wins over the empty-data error and no parser duplication was added.

---

### Task 2: Add Stream-Scoped TCP Lifecycle

**Files:**
- Modify: `receiver-windows/tests/OpenAudioLink.Tests/Receiver/TcpReceiverTests.cs`
- Modify: `receiver-windows/src/OpenAudioLink/Receiver/TcpReceiver.cs`

- [ ] **Step 1: Add a compiling API-shape test**

Add this reflection test first so RED is an assertion failure rather than a C# compilation error:

```csharp
[TestMethod]
public void StartLoopbackExposesStreamLifecycleCallbacks()
{
    Assert.IsNotNull(typeof(TcpReceiver).GetMethod(
        "StartLoopback",
        new[] { typeof(Action<byte[]>), typeof(Action), typeof(Action) }));
}
```

- [ ] **Step 2: Commit and prove API RED**

```bash
git add receiver-windows/tests/OpenAudioLink.Tests/Receiver/TcpReceiverTests.cs
git commit -m "test: require TCP stream lifecycle API"
```

Push and require exact-head Windows x86/x64 to compile, execute, and fail only because `GetMethod` returns `null`.

- [ ] **Step 3: Add the minimum callback API without behavior**

Add fields and constructor wiring:

```csharp
private readonly Action streamStarted;
private readonly Action streamEnded;
private readonly object lifecycleGate = new object();

private TcpReceiver(
    TcpListener listener,
    Action<byte[]> audioSink,
    Action streamStarted,
    Action streamEnded)
{
    this.listener = listener;
    this.audioSink = audioSink ?? (_ => { });
    this.streamStarted = streamStarted ?? (() => { });
    this.streamEnded = streamEnded ?? (() => { });
    Port = ((IPEndPoint)listener.LocalEndpoint).Port;
    ThreadPool.QueueUserWorkItem(_ => AcceptLoop());
}
```

Extend `Start` and `StartLoopback` by appending `Action streamStarted = null, Action streamEnded = null`; retain all existing parameter order and defaults. Do not invoke the callbacks yet.

- [ ] **Step 4: Commit and prove API GREEN**

```bash
git add receiver-windows/src/OpenAudioLink/Receiver/TcpReceiver.cs
git commit -m "feat: expose TCP stream lifecycle callbacks"
```

Push and require exact-head Windows x86/x64 green, including the reflection test.

- [ ] **Step 5: Add callback ordering and owner-thread test**

Add this test and use the existing socket helpers:

```csharp
[TestMethod]
public void StreamCallbacksRunInOrderOnTheSessionThread()
{
    List<string> calls = new List<string>();
    int startedThread = 0;
    int audioThread = 0;
    int endedThread = 0;

    using (ManualResetEventSlim startEntered = new ManualResetEventSlim())
    using (ManualResetEventSlim releaseStart = new ManualResetEventSlim())
    using (CountdownEvent ended = new CountdownEvent(1))
    using (TcpReceiver receiver = TcpReceiver.StartLoopback(
        payload =>
        {
            audioThread = Environment.CurrentManagedThreadId;
            calls.Add("audio");
        },
        () =>
        {
            startedThread = Environment.CurrentManagedThreadId;
            calls.Add("start");
            startEntered.Set();
            if (!releaseStart.Wait(SocketTimeoutMilliseconds))
            {
                throw new PacketParseException("Timed out waiting to release stream start.");
            }
        },
        () =>
        {
            endedThread = Environment.CurrentManagedThreadId;
            calls.Add("end");
            ended.Signal();
        }))
    using (TcpClient client = Connect(receiver))
    {
        NetworkStream stream = client.GetStream();
        CompleteHello(stream, 1UL);
        Write(stream, ProtocolConstants.PacketTypeStartStream, 2u,
            HandshakePayloads.StartStream(ProtocolConstants.CodecAacLc,
                48000u, 2, 192000u, 21));
        try
        {
            Assert.IsTrue(startEntered.Wait(SocketTimeoutMilliseconds),
                "Timed out waiting for stream start callback.");
            Assert.IsFalse(stream.DataAvailable,
                "STREAM_READY was written before streamStarted completed.");
        }
        finally
        {
            releaseStart.Set();
        }
        AssertPacket(stream, ProtocolConstants.PacketTypeStreamReady,
            HandshakePayloads.StreamReady(ProtocolConstants.StreamResultSuccess,
                ProtocolConstants.CodecAacLc, 48000u, 2));

        byte[] encoded = TestFixtures.Read("testdata/audio/aac-lc-48k-stereo-1024.raw");
        Write(stream, ProtocolConstants.PacketTypeAudio, 3u,
            HandshakePayloads.Audio(ProtocolConstants.CodecAacLc, 1u, 2UL, 21, encoded));
        Write(stream, ProtocolConstants.PacketTypeStopStream, 4u, new byte[0]);

        Assert.IsTrue(ended.Wait(SocketTimeoutMilliseconds), "Timed out waiting for stream end.");
        CollectionAssert.AreEqual(new[] { "start", "audio", "end" }, calls.ToArray());
        Assert.AreEqual(startedThread, audioThread);
        Assert.AreEqual(startedThread, endedThread);
    }
}
```

Add this helper:

```csharp
private static void CompleteHello(NetworkStream stream, ulong sessionId)
{
    Write(stream, ProtocolConstants.PacketTypeHello, 1u,
        HandshakePayloads.Hello("Android Phone", "1.0.0",
            ProtocolConstants.PlatformAndroid,
            ProtocolConstants.CapabilityAacSupported));
    AssertPacket(stream, ProtocolConstants.PacketTypeWelcome,
        HandshakePayloads.Welcome(ProtocolConstants.ResultSuccess,
            "Windows PC", "1.0.0", sessionId));
}

private static void CompleteHandshake(NetworkStream stream, ulong sessionId)
{
    CompleteHello(stream, sessionId);
    Write(stream, ProtocolConstants.PacketTypeStartStream, 2u,
        HandshakePayloads.StartStream(ProtocolConstants.CodecAacLc,
            48000u, 2, 192000u, 21));
    AssertPacket(stream, ProtocolConstants.PacketTypeStreamReady,
        HandshakePayloads.StreamReady(ProtocolConstants.StreamResultSuccess,
            ProtocolConstants.CodecAacLc, 48000u, 2));
}
```

- [ ] **Step 6: Add lifecycle failure and shutdown tests**

Add `StreamStartFailureClosesOnlyThatClientAndAllowsReconnect` with a start callback that throws `new PacketParseException("Injected stream start failure.")` only on its first invocation; require EOF for the first client, a complete second handshake with session ID `2`, and end-callback count `1`.

Also add these bounded tests without sleeps or production test hooks:

- `AbruptDisconnectInvokesStreamEndedOnOwnerThread`: complete the handshake, close without `STOP_STREAM`, wait for end, and compare start/end thread IDs.
- `BusyClientDoesNotInvokeStreamLifecycle`: start the first stream, require busy `WELCOME` for a second client, and prove no extra lifecycle callback occurred.
- `DisposeBeforeStartClosesClientWithoutStartingStream`: complete only `HELLO`, dispose the receiver, require EOF or `IOException` within the socket timeout, and require start/end counts `0`.
- `DisposeDuringStreamInvokesStreamEndedOnOwnerThread`: complete `START_STREAM`, call `receiver.Dispose()`, wait for end, and require the end callback to run on the recorded start-callback thread.

- [ ] **Step 7: Commit and prove behavioral RED**

```bash
git add receiver-windows/tests/OpenAudioLink.Tests/Receiver/TcpReceiverTests.cs
git commit -m "test: specify TCP stream lifecycle"
```

Push and require exact-head Windows x86/x64 to compile and fail in the new lifecycle assertions/timeouts because the callbacks are stored but not invoked.

- [ ] **Step 8: Replace `Handle` with lifecycle-safe ownership**

Use this complete control structure while retaining the current busy packet:

```csharp
private void Handle(TcpClient client)
{
    using (client)
    {
        bool ownsActiveSlot = false;
        bool streamActive = false;
        try
        {
            NetworkStream stream = client.GetStream();
            if (Volatile.Read(ref disposed) != 0)
            {
                return;
            }

            if (Interlocked.CompareExchange(ref active, 1, 0) != 0)
            {
                PacketReader.ReadPacket(stream);
                byte[] busy = ReceiverSession.BusyWelcome();
                stream.Write(busy, 0, busy.Length);
                return;
            }

            ownsActiveSlot = true;
            lock (lifecycleGate)
            {
                if (disposed != 0)
                {
                    return;
                }
                currentClient = client;
            }

            ReceiverSession session = new ReceiverSession(
                (ulong)Interlocked.Increment(ref nextSessionId), audioSink);
            while (session.State != ReceiverSessionState.Stopped)
            {
                ReceiverSessionState previousState = session.State;
                byte[] response = session.Process(PacketReader.ReadPacket(stream));
                if (!streamActive
                    && previousState == ReceiverSessionState.WaitingForStartStream
                    && session.State == ReceiverSessionState.Streaming)
                {
                    lock (lifecycleGate)
                    {
                        if (disposed != 0)
                        {
                            return;
                        }
                        streamStarted();
                        streamActive = true;
                    }
                }
                if (response != null)
                {
                    stream.Write(response, 0, response.Length);
                }
            }
        }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
        catch (PacketParseException) { }
        finally
        {
            try
            {
                if (streamActive)
                {
                    try { streamEnded(); }
                    catch (PacketParseException) { }
                }
            }
            finally
            {
                if (ownsActiveSlot)
                {
                    lock (lifecycleGate)
                    {
                        if (ReferenceEquals(currentClient, client))
                        {
                            currentClient = null;
                        }
                    }
                    Interlocked.Exchange(ref active, 0);
                }
            }
        }
    }
}
```

Replace `Dispose` so it shares the same ordering point and closes resources outside the lock:

```csharp
public void Dispose()
{
    TcpClient client;
    lock (lifecycleGate)
    {
        if (disposed != 0)
        {
            return;
        }
        disposed = 1;
        client = currentClient;
        currentClient = null;
    }

    listener.Stop();
    client?.Close();
}
```

`Dispose` may wait for an already-entered `streamStarted` callback, but client close and owner-thread `streamEnded` cannot overlap an uncommitted startup transition.

- [ ] **Step 9: Commit and prove behavioral GREEN**

```bash
git add receiver-windows/src/OpenAudioLink/Receiver/TcpReceiver.cs
git commit -m "feat: add TCP stream lifecycle callbacks"
```

Push and require exact-head Windows x86/x64 green.

- [ ] **Step 10: Review Task 2**

Specification review traces every callback path. Quality review inspects `active/currentClient/disposed`, callback-before-`STREAM_READY`, end-before-EOF, nested cleanup, and source compatibility.

---

### Task 3: Integrate Native Decode Into `ReceiverRuntime`

**Files:**
- Modify: `receiver-windows/tests/OpenAudioLink.Tests/Receiver/ReceiverRuntimeTests.cs`
- Modify: `receiver-windows/tests/OpenAudioLink.Tests/UI/MainFormTests.cs`
- Modify: `receiver-windows/src/OpenAudioLink/Protocol/PacketParseException.cs`
- Modify: `receiver-windows/src/OpenAudioLink/Receiver/ReceiverRuntime.cs`

- [ ] **Step 1: Replace fake runtime expectations with real PCM expectations**

Change `ClientAudioFramesAreDecodedAndRendered` into a two-session test. Each session sends three canonical raw frames with distinct metadata, verifies `PING -> PONG`, sends `STOP_STREAM`, and reads EOF as the finalization barrier. After both sessions require six frames in exact wire order and `runtime.Queue.Count == 0`.

For every rendered frame require:

```csharp
Assert.AreEqual(4096, frame.PcmBytes.Length);
AssertStereoEnergy(frame.PcmBytes);
```

Add this complete helper to `ReceiverRuntimeTests`:

```csharp
private static void AssertStereoEnergy(byte[] pcm)
{
    long leftEnergy = 0;
    long rightEnergy = 0;
    for (int offset = 0; offset < pcm.Length; offset += 4)
    {
        short left = (short)(pcm[offset] | (pcm[offset + 1] << 8));
        short right = (short)(pcm[offset + 2] | (pcm[offset + 3] << 8));
        leftEnergy += Math.Abs((long)left);
        rightEnergy += Math.Abs((long)right);
    }
    Assert.IsTrue(leftEnergy > 0, "left channel is silent");
    Assert.IsTrue(rightEnergy > 0, "right channel is silent");
}
```

- [ ] **Step 2: Update the WinForms status test**

Rename `FakeStreamUpdatesRenderedFrameStatus` to `DecodedStreamUpdatesRenderedFrameStatus`. Keep its three frames and `PING` check, then send `STOP_STREAM` and read EOF before inspecting the renderer so final decoder drain is inside the test barrier. Require `runtime.Renderer.RenderedCount == 3`, preserve metadata checks, and replace encoded-byte equality with a 4096-byte length check plus the same complete `AssertStereoEnergy` helper inside `MainFormTests`.

Because `MainForm.UpdateRenderedFrames` may use `BeginInvoke`, add `using System.Diagnostics;` and wait through the STA message queue without sleeps:

```csharp
private static void WaitForVisibleText(Control parent, string expected)
{
    Stopwatch timeout = Stopwatch.StartNew();
    while (timeout.ElapsedMilliseconds < SocketTimeoutMilliseconds)
    {
        Application.DoEvents();
        if (VisibleText(parent).Contains(expected))
        {
            return;
        }
        Thread.Yield();
    }
    StringAssert.Contains(VisibleText(parent), expected);
}
```

Use `WaitForVisibleText(form, "Rendered frames: 3")`. Keep all helpers test-local; add no production test utility.

- [ ] **Step 3: Add corrupt-stream isolation and reconnect coverage**

Add `CorruptAacClosesCurrentStreamAndAllowsHealthyReconnect` to `ReceiverRuntimeTests`:

1. Start a supported stream and send a non-empty one-byte truncation of the canonical raw fixture, which passes packet-shape validation but cannot represent the complete canonical access unit.
2. Send `STOP_STREAM` if the decoder has not already closed the socket; accept `IOException` from that write when native submit already failed.
3. Require connection EOF/`IOException` without receiving an `ERROR` packet and require zero rendered frames from the corrupt stream.
4. Connect a second client to the same runtime, send one complete canonical frame, stop, read EOF, and require one 4096-byte non-silent frame with the healthy packet's metadata.

This test remains valid whether the MFT rejects during `Submit` or accepts input and the final drain detects unmatched metadata. The current fake runtime fails because it renders the one-byte encoded payload.

- [ ] **Step 4: Commit and prove intentional native RED**

```bash
git add receiver-windows/tests/OpenAudioLink.Tests/Receiver/ReceiverRuntimeTests.cs \
  receiver-windows/tests/OpenAudioLink.Tests/UI/MainFormTests.cs
git commit -m "test: require native receiver runtime decode"
```

Push and require exact-head Windows x86/x64 failure because the current runtime stores encoded AAC rather than 4096-byte PCM.

- [ ] **Step 5: Preserve native failure context**

Add to `PacketParseException`:

```csharp
public PacketParseException(string message, Exception innerException)
    : base(message, innerException)
{
}
```

- [ ] **Step 6: Add private decoder-session state**

Inside `ReceiverRuntime`, add a private `DecoderSession` with:

```csharp
private const int PcmFrameSize = 4096;
private const int PcmBlockAlignment = 4;
private readonly AudioFrameQueue queue;
private readonly FakeAudioRenderer renderer;
private readonly Action<int> renderedCountChanged;
private readonly MediaFoundationAacDecoder decoder;
private readonly Queue<FrameMetadata> metadata = new Queue<FrameMetadata>();
private readonly byte[] pcmFrame = new byte[PcmFrameSize];
private int pcmLength;
private bool faulted;
```

Add `using System.Collections.Generic;`, `using System.Runtime.ExceptionServices;`, and `using OpenAudioLink.Protocol;` to `ReceiverRuntime.cs`.

Initialize every managed field/buffer before constructing `MediaFoundationAacDecoder`. Add a private immutable `FrameMetadata` with `uint FrameNumber`, `ulong CaptureTimestamp`, and `ushort FrameDuration`; add no public parser or decoder type.

The constructor performs no potentially failing work after native construction:

```csharp
public DecoderSession(
    AudioFrameQueue queue,
    FakeAudioRenderer renderer,
    Action<int> renderedCountChanged)
{
    this.queue = queue ?? throw new ArgumentNullException(nameof(queue));
    this.renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
    this.renderedCountChanged = renderedCountChanged
        ?? throw new ArgumentNullException(nameof(renderedCountChanged));
    decoder = new MediaFoundationAacDecoder();
}
```

The public-to-helper audio path is exactly:

```csharp
queue.Enqueue(payload);
while (queue.TryDequeue(out byte[] acceptedPayload))
{
    Submit(acceptedPayload);
}
renderedCountChanged(renderer.RenderedCount);
```

`Submit` reads metadata at offsets `1`, `5`, and `13`, copies exactly `Encoded Size` bytes from `ProtocolConstants.AudioPayloadHeaderSize`, enqueues metadata before native submit, and forwards all returned chunks to `RenderChunks`.

- [ ] **Step 7: Implement arbitrary-chunk PCM assembly**

Use this assembler:

```csharp
foreach (byte[] chunk in chunks)
{
    if (chunk == null || chunk.Length == 0 || chunk.Length % PcmBlockAlignment != 0)
    {
        Fault("Media Foundation returned invalid PCM alignment.");
    }

    int offset = 0;
    while (offset < chunk.Length)
    {
        int count = Math.Min(PcmFrameSize - pcmLength, chunk.Length - offset);
        Buffer.BlockCopy(chunk, offset, pcmFrame, pcmLength, count);
        pcmLength += count;
        offset += count;
        if (pcmLength == PcmFrameSize)
        {
            if (metadata.Count == 0)
            {
                Fault("Decoder produced PCM without AUDIO metadata.");
            }
            FrameMetadata frame = metadata.Dequeue();
            renderer.Render(new FakePcmFrame(
                frame.FrameNumber,
                frame.CaptureTimestamp,
                frame.FrameDuration,
                pcmFrame));
            pcmLength = 0;
        }
    }
}
```

`Fault` sets `faulted = true` and throws `PacketParseException`. Wrap only native `PlatformNotSupportedException` and `InvalidOperationException` in the two-argument exception; do not catch resource exhaustion or unrelated callback/programming errors.

- [ ] **Step 8: Implement final drain and guaranteed disposal**

`Finish` captures any primary failure and then always attempts decoder disposal before rethrowing. If not faulted, call `Drain`, assemble every delayed chunk, notify the rendered count, and require:

```csharp
if (pcmLength != 0)
{
    Fault("Decoder ended with a partial PCM frame.");
}
if (metadata.Count != 0)
{
    Fault("Decoder ended with unmatched AUDIO metadata.");
}
if (queue.Count != 0)
{
    Fault("Audio queue was not empty at stream end.");
}
```

If submit already faulted, skip another `Drain` but still dispose on the owner thread. Convert expected drain/dispose failures to `PacketParseException`.

Use this control shape so every exit reaches native teardown, the primary failure keeps its stack, and cleanup cannot mask it:

```csharp
public void Finish()
{
    Exception failure = null;
    try
    {
        if (!faulted)
        {
            IReadOnlyList<byte[]> chunks;
            try
            {
                chunks = decoder.Drain();
            }
            catch (PlatformNotSupportedException ex)
            {
                faulted = true;
                throw new PacketParseException("AAC decoder drain failed.", ex);
            }
            catch (InvalidOperationException ex)
            {
                faulted = true;
                throw new PacketParseException("AAC decoder drain failed.", ex);
            }
            RenderChunks(chunks);
            RequireCompleteOutput();
            renderedCountChanged(renderer.RenderedCount);
        }
    }
    catch (Exception ex)
    {
        failure = ex;
    }

    try
    {
        decoder.Dispose();
    }
    catch (Exception ex)
    {
        if (failure == null)
        {
            failure = new PacketParseException("AAC decoder disposal failed.", ex);
        }
        else
        {
            failure.Data["OpenAudioLink.DecoderDisposeError"] = ex;
        }
    }

    if (failure != null)
    {
        ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
```

- [ ] **Step 9: Wire the helper through lifecycle callbacks**

Replace the fake composition in `ReceiverRuntime.Start` with:

```csharp
DecoderSession session = null;
TcpReceiver receiver = TcpReceiver.Start(
    address,
    port,
    payload =>
    {
        if (session == null)
        {
            throw new PacketParseException("Audio arrived before decoder startup.");
        }
        session.Accept(payload);
    },
    () =>
    {
        try
        {
            session = new DecoderSession(queue, renderer, renderedCountChanged);
        }
        catch (PlatformNotSupportedException ex)
        {
            throw new PacketParseException("AAC decoder startup failed.", ex);
        }
        catch (InvalidOperationException ex)
        {
            throw new PacketParseException("AAC decoder startup failed.", ex);
        }
    },
    () =>
    {
        DecoderSession ending = session;
        session = null;
        if (ending != null)
        {
            ending.Finish();
        }
    });
```

No operation after successful native construction may fail before publishing `session`; buffers initialize first and the existing decoder constructor unwinds its own partial startup.

- [ ] **Step 10: Commit and prove GREEN**

```bash
git add receiver-windows/src/OpenAudioLink/Protocol/PacketParseException.cs \
  receiver-windows/src/OpenAudioLink/Receiver/ReceiverRuntime.cs
git commit -m "feat: decode TCP AAC in Windows runtime"
```

Push and require exact-head Windows x86/x64 green. Confirm runtime, UI, TCP lifecycle, standalone decoder, protocol, and architecture tests all executed.

- [ ] **Step 11: Review Task 3**

Specification review traces one packet through validation, queue, metadata FIFO, submit, arbitrary chunks, render, drain, and reconnect. Quality review inspects exception filtering, partial-start cleanup, disposal under every throw path, buffer clone safety, leftover gates, and absence of new abstractions/dependencies/threads.

---

### Task 4: Update Active Documentation

**Files:**
- Modify: `docs/03-Protocol.md`
- Modify: `docs/05-Windows.md`
- Modify: `docs/06-Audio.md`
- Modify: `docs/10-Testing.md`
- Modify: `docs/11-Roadmap.md`

- [ ] **Step 1: Make non-empty wire validation explicit**

After the encoded-size equality rule in `docs/03-Protocol.md`, add:

```text
For Version 1 AAC-LC, Encoded Size MUST be greater than zero because Encoded
Data carries one complete raw AAC access unit.
```

- [ ] **Step 2: Replace stale Windows runtime status**

In `docs/05-Windows.md`, record:

```text
Phase 1-Q connects MediaFoundationAacDecoder to ReceiverRuntime. One supported
TCP stream creates, submits to, drains, and disposes its decoder on the same
accepted-client ThreadPool thread. Decoder output is assembled into 4096-byte
PCM16 stereo frames and recorded by FakeAudioRenderer. This is real runtime
decode but still not audible playback; a slow decoder still blocks that
session's network reads.
```

Keep the recommended decoder-thread/playback topology labeled future.

- [ ] **Step 3: Correct audio architecture status**

In `docs/06-Audio.md`, record:

```text
Phase 1-Q integrates the concrete MediaFoundationAacDecoder directly; it does
not add the documented future IAudioDecoder/factory tree. The current runtime
uses the accepted TCP session thread, preserves zero/one/many output chunks,
assembles exact 4096-byte PCM frames, and explicitly drains at session end.
The dedicated decoder thread, PCM playback queue, recovery threshold, and
audible renderer remain future work.
```

Update decoder-loop commentary so it no longer says all runtime integration is future.

- [ ] **Step 4: Record native integration coverage**

Add `Phase 1-Q Runtime Decode Gate` to `docs/10-Testing.md`. State that both x86 and x64 prove decoder startup before successful `STREAM_READY`, ordered 4096-byte non-silent PCM with exact metadata, `PING/PONG`, stop/drain, disconnect, busy handling, reconnect, and the WinForms count. Explicitly exclude audible playback, asynchronous backpressure, latency, recovery thresholds, and Windows 7. Remove the stale sentence that `ReceiverRuntime` uses `FakeAacDecoder`.

- [ ] **Step 5: Update roadmap status only**

Change the Windows current-status cell to:

```text
Phase 1 in progress; TCP runtime AAC decode with fake renderer
```

Do not mark Phase 1 or Version 1.0 complete.

- [ ] **Step 6: Run local checks and commit**

```bash
python3 tools/check_docs_consistency.py
python3 -m unittest discover -s tools/audio -p 'test_*.py'
python3 tools/audio/validate_aac_fixture.py
python3 tools/protocol/generate_golden_packets.py --check
git diff --check
git add docs/03-Protocol.md docs/05-Windows.md docs/06-Audio.md docs/10-Testing.md docs/11-Roadmap.md
git commit -m "docs: record Windows runtime AAC decode"
```

Expected: all checks exit `0`; fixture and golden bytes remain unchanged.

- [ ] **Step 7: Review Task 4**

Specification review compares every active claim with executable code. Quality review scans for contradictory `ReceiverRuntime still uses FakeAacDecoder` wording while preserving clearly marked future architecture.

---

### Task 5: Review Final Candidate And Run Exact-Head CI

**Files:**
- Review: complete diff from `5dbd9a71ef161743caed133c4960b55165081e58`

- [ ] **Step 1: Run complete local gate**

```bash
python3 tools/check_docs_consistency.py
python3 -m unittest discover -s tools/audio -p 'test_*.py'
python3 tools/audio/validate_aac_fixture.py
python3 tools/protocol/generate_golden_packets.py --check
git diff --check 5dbd9a71ef161743caed133c4960b55165081e58..HEAD
test -z "$(git status --porcelain)"
git status --short --branch
```

Expected: all checks exit `0`; the worktree is clean.

- [ ] **Step 2: Run full specification review**

Map every Phase 1-Q acceptance criterion to actual source, test, documentation, and CI evidence. Fix and repeat review for every missing or contradictory item.

- [ ] **Step 3: Run full quality review**

Inspect owner-thread lifetime, `currentClient/disposed/active`, callback order, chunk assembly, FIFO mapping, exception filtering, reconnect/busy isolation, x86/x64 determinism, and all unnecessary scope. Fix and repeat every Critical or Important finding.

- [ ] **Step 4: Push reviewed head and require all workflows**

Verify Gitea/GitHub SHA equality, then require at that exact SHA:

```text
docs     success
windows  success (x86 and x64)
android  success
```

For failure, run `gh run view <run-id> --log-failed`, make one root-cause correction, rerun focused checks, commit, push, and restart exact-head verification. Never accept an older green SHA.

---

### Task 6: Record Completion, Reverify, Merge, And Pause

**Files:**
- Modify: `docs/superpowers/specs/2026-07-15-phase-1q-windows-decoder-runtime-integration-design.md`

- [ ] **Step 1: Record verified status**

After Task 5 exact-head success, change `**Status:** Draft for implementation` to `**Status:** Implemented`. Add a compact evidence paragraph with the candidate SHA, `docs`/`windows`/`android` run IDs, and x86/x64 success. Add no audible playback, latency, Android runtime, or Windows-version claim.

- [ ] **Step 2: Commit, push, and reverify final head**

```bash
git add docs/superpowers/specs/2026-07-15-phase-1q-windows-decoder-runtime-integration-design.md
git commit -m "docs: record phase 1q verification"
```

Run the Task 5 local gate, push, verify source/mirror refs, and require new successful `docs`, `windows`, and `android` runs whose `head_sha` equals this status commit.

- [ ] **Step 3: Fast-forward main**

From the primary worktree:

```bash
git switch main
git merge --ff-only phase-1q-windows-decoder-runtime-integration
```

Verify `main` equals the final tested SHA, push Gitea `main` with the MTU workaround, then verify local main, Gitea main, GitHub main, Gitea phase branch, and GitHub phase branch all equal that SHA.

- [ ] **Step 4: Confirm no duplicate main CI**

Wait at least 60 seconds, query Actions for `head_branch == "main"` and the merged SHA, and require count `0` under the existing workflow triggers.

- [ ] **Step 5: Clean local phase state and stop**

Remove only the Phase 1-Q worktree and local Phase 1-Q branch after ref verification; retain the remote phase branch as CI evidence. Report final SHA, exact run IDs, decisive x86/x64 evidence, refs, and cleanup. Then pause without starting Phase 1-R or creating its documents.
