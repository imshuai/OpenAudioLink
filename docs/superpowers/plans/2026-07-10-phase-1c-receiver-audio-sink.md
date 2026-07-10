# Phase 1-C Receiver Audio Sink Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver accepted fake `AUDIO` payloads from the Windows receiver session into a minimal synchronous audio sink callback.

**Architecture:** Reuse the existing `ReceiverSession` and `TcpReceiver` flow. Add an optional `Action<byte[]>` sink to `ReceiverSession`, pass it through `TcpReceiver`, and keep the default no-sink behavior source-compatible. The sink receives a cloned validated payload; no decoder, playback stack, queue, worker, UI, discovery, or Android changes are introduced.

**Tech Stack:** C#/.NET Framework 4.8 MSTest, existing OpenAudioLink Version 1 protocol helpers, Python docs/golden-packet checks, GitHub Actions via mirrored `phase-*` branch push.

---

## Reference Spec

Implement exactly this design:

- `docs/superpowers/specs/2026-07-10-phase-1c-receiver-audio-sink-design.md`

Phase 1-C is Windows receiver only. It must not add AAC decode, WASAPI/NAudio/Media Foundation playback, Android capture/encoding, mDNS, UI, tray, installer, configuration, queues, worker threads, jitter buffers, or playback clocks.

## Known Local Verification Limits

- Current Linux host has no `dotnet`; Windows tests run on Windows or GitHub Actions `windows-latest`.
- Current Linux host is `aarch64`; Android Gradle resource processing uses an `x86-64` `aapt2` binary. Android tests run on an `x86-64` Android build host or GitHub Actions `ubuntu-latest`.
- Local Linux can run docs checks, golden packet checks, and whitespace checks.
- Pushes to `phase-*` branches trigger GitHub Actions after the Gitea-to-GitHub mirror sync.

---

## File Structure

Modify existing files only:

```text
receiver-windows/src/OpenAudioLink/Receiver/ReceiverSession.cs
receiver-windows/tests/OpenAudioLink.Tests/Receiver/ReceiverSessionTests.cs
receiver-windows/src/OpenAudioLink/Receiver/TcpReceiver.cs
receiver-windows/tests/OpenAudioLink.Tests/Receiver/TcpReceiverTests.cs
```

Do not create new production files for Phase 1-C. `Action<byte[]>` is the sink seam.

---

## Task 1: Add ReceiverSession Audio Sink Callback

**Files:**

- Modify: `receiver-windows/tests/OpenAudioLink.Tests/Receiver/ReceiverSessionTests.cs`
- Modify: `receiver-windows/src/OpenAudioLink/Receiver/ReceiverSession.cs`

- [ ] **Step 1: Add failing ReceiverSession sink tests**

Modify `receiver-windows/tests/OpenAudioLink.Tests/Receiver/ReceiverSessionTests.cs`.

Add this using at the top, before the existing MSTest using:

```csharp
using System;
```

Replace the existing `ProcessAudioWhileStreaming_RecordsFrameAndReturnsNull` test with:

```csharp
        [TestMethod]
        public void ProcessAudioWhileStreaming_RecordsFrameCallsSinkAndReturnsNull()
        {
            int calls = 0;
            byte[] received = null;
            ReceiverSession session = StreamingSession(payload =>
            {
                calls++;
                received = payload;
            });
            byte[] payload = ValidAudioPayload();

            byte[] response = session.Process(PacketWriter.WritePacket(ProtocolConstants.PacketTypeAudio, 5u, 123456789UL, payload));

            Assert.IsNull(response);
            Assert.AreEqual(ReceiverSessionState.Streaming, session.State);
            Assert.AreEqual(1, session.AudioFramesReceived);
            Assert.AreEqual(1, calls);
            CollectionAssert.AreEqual(payload, received);
            CollectionAssert.AreEqual(payload, session.LastAudioPayload);
        }
```

Append this test immediately after `ProcessAudioWhileStreaming_RecordsFrameCallsSinkAndReturnsNull`:

```csharp
        [TestMethod]
        public void ProcessAudioWhileStreaming_SinkMutationDoesNotChangeLastAudioPayload()
        {
            ReceiverSession session = StreamingSession(payload => payload[0] = ProtocolConstants.CodecOpus);
            byte[] payload = ValidAudioPayload();

            session.Process(PacketWriter.WritePacket(ProtocolConstants.PacketTypeAudio, 5u, 123456789UL, payload));

            CollectionAssert.AreEqual(payload, session.LastAudioPayload);
        }
```

Replace `ProcessAudioBeforeStartStream_Throws` with:

```csharp
        [TestMethod]
        public void ProcessAudioBeforeStartStream_ThrowsAndDoesNotCallSink()
        {
            int calls = 0;
            ReceiverSession session = ReadySession(_ => calls++);

            Assert.ThrowsException<PacketParseException>(() => session.Process(PacketWriter.WritePacket(
                ProtocolConstants.PacketTypeAudio,
                5u,
                123456789UL,
                ValidAudioPayload())));
            Assert.AreEqual(0, calls);
        }
```

Replace `ProcessInvalidAudioWhileStreaming_Throws` with:

```csharp
        [TestMethod]
        public void ProcessInvalidAudioWhileStreaming_ThrowsAndDoesNotCallSink()
        {
            int calls = 0;
            ReceiverSession session = StreamingSession(_ => calls++);

            Assert.ThrowsException<PacketParseException>(() => session.Process(PacketWriter.WritePacket(
                ProtocolConstants.PacketTypeAudio,
                5u,
                123456789UL,
                new byte[] { ProtocolConstants.CodecAacLc })));
            Assert.AreEqual(0, session.AudioFramesReceived);
            Assert.AreEqual(0, calls);
            Assert.IsNull(session.LastAudioPayload);
        }
```

Replace the helper methods at the end of the file with these overloads:

```csharp
        private static ReceiverSession ReadySession()
        {
            return ReadySession(null);
        }

        private static ReceiverSession ReadySession(Action<byte[]> audioSink)
        {
            ReceiverSession session = audioSink == null ? new ReceiverSession(SessionId) : new ReceiverSession(SessionId, audioSink);
            session.Process(PacketWriter.WritePacket(
                ProtocolConstants.PacketTypeHello,
                1u,
                123456000UL,
                HandshakePayloads.Hello("Android Phone", "1.0.0", ProtocolConstants.PlatformAndroid, ProtocolConstants.CapabilityAacSupported)));
            return session;
        }

        private static byte[] ValidAudioPayload()
        {
            return HandshakePayloads.Audio(ProtocolConstants.CodecAacLc, 1u, 123456789UL, 20, new byte[] { 0x11, 0x22, 0x33, 0x44 });
        }

        private static ReceiverSession StreamingSession()
        {
            return StreamingSession(null);
        }

        private static ReceiverSession StreamingSession(Action<byte[]> audioSink)
        {
            ReceiverSession session = ReadySession(audioSink);
            session.Process(PacketWriter.WritePacket(
                ProtocolConstants.PacketTypeStartStream,
                3u,
                123456002UL,
                HandshakePayloads.StartStream(ProtocolConstants.CodecAacLc, 48000u, 2, 192000u, 20)));
            return session;
        }
```

- [ ] **Step 2: Run receiver session tests to verify failure on supported hosts**

On Windows:

```powershell
dotnet test receiver-windows/OpenAudioLink.sln -c Release --filter ReceiverSessionTests
```

Expected: FAIL because `ReceiverSession` does not yet have a constructor that accepts `Action<byte[]>`.

On the current Linux host, this command is expected to fail with `dotnet: command not found`.

- [ ] **Step 3: Implement ReceiverSession sink constructor and callback**

Modify `receiver-windows/src/OpenAudioLink/Receiver/ReceiverSession.cs`.

Add this using at the top, before `using OpenAudioLink.Protocol;`:

```csharp
using System;
```

Add this field after `private readonly ulong sessionId;`:

```csharp
        private readonly Action<byte[]> audioSink;
```

Replace the existing constructor with these two constructors:

```csharp
        public ReceiverSession(ulong sessionId)
            : this(sessionId, null)
        {
        }

        public ReceiverSession(ulong sessionId, Action<byte[]> audioSink)
        {
            this.sessionId = sessionId;
            this.audioSink = audioSink ?? (_ => { });
            State = ReceiverSessionState.WaitingForHello;
        }
```

Replace the `PacketTypeAudio` branch in `ProcessStreaming` with:

```csharp
            if (header.PacketType == ProtocolConstants.PacketTypeAudio)
            {
                AudioPayloadValidator.ValidateAacPayload(payload);
                byte[] acceptedPayload = (byte[])payload.Clone();
                audioSink((byte[])acceptedPayload.Clone());
                LastAudioPayload = acceptedPayload;
                AudioFramesReceived++;
                return null;
            }
```

- [ ] **Step 4: Run ReceiverSession verification**

Run local checks:

```bash
python3 tools/check_docs_consistency.py
python3 tools/protocol/generate_golden_packets.py --check
git diff --check HEAD
```

Expected local output includes:

```text
docs consistency ok: 12 markdown files checked
protocol golden packets ok
```

On Windows, run:

```powershell
dotnet test receiver-windows/OpenAudioLink.sln -c Release --filter ReceiverSessionTests
```

Expected: PASS.

- [ ] **Step 5: Commit Task 1**

```bash
git add receiver-windows/src/OpenAudioLink/Receiver/ReceiverSession.cs \
        receiver-windows/tests/OpenAudioLink.Tests/Receiver/ReceiverSessionTests.cs
git commit -m "feat: deliver receiver audio frames to sink"
```

---

## Task 2: Pass Audio Sink Through TcpReceiver

**Files:**

- Modify: `receiver-windows/tests/OpenAudioLink.Tests/Receiver/TcpReceiverTests.cs`
- Modify: `receiver-windows/src/OpenAudioLink/Receiver/TcpReceiver.cs`

- [ ] **Step 1: Add failing TCP loopback sink test**

Modify `receiver-windows/tests/OpenAudioLink.Tests/Receiver/TcpReceiverTests.cs`.

Add this using after the existing socket using:

```csharp
using System.Threading;
```

Replace `ClientCompletesPhase1aHandshake` with:

```csharp
        [TestMethod]
        public void ClientCompletesPhase1aHandshakeAndDeliversAudioToSink()
        {
            int audioCalls = 0;
            byte[] receivedAudio = null;
            using (ManualResetEventSlim audioReceived = new ManualResetEventSlim(false))
            using (TcpReceiver receiver = TcpReceiver.StartLoopback(payload =>
            {
                receivedAudio = payload;
                Interlocked.Increment(ref audioCalls);
                audioReceived.Set();
            }))
            using (TcpClient client = Connect(receiver))
            {
                NetworkStream stream = client.GetStream();

                Write(stream, ProtocolConstants.PacketTypeHello, 1u, HandshakePayloads.Hello("Android Phone", "1.0.0", ProtocolConstants.PlatformAndroid, ProtocolConstants.CapabilityAacSupported));
                AssertPacket(stream, ProtocolConstants.PacketTypeWelcome, HandshakePayloads.Welcome(ProtocolConstants.ResultSuccess, "Windows PC", "1.0.0", 1));

                Write(stream, ProtocolConstants.PacketTypeStartStream, 2u, HandshakePayloads.StartStream(ProtocolConstants.CodecAacLc, 48000u, 2, 192000u, 20));
                AssertPacket(stream, ProtocolConstants.PacketTypeStreamReady, HandshakePayloads.StreamReady(ProtocolConstants.StreamResultSuccess, ProtocolConstants.CodecAacLc, 48000u, 2));

                byte[] audioPayload = HandshakePayloads.Audio(
                    ProtocolConstants.CodecAacLc,
                    1u,
                    123456789UL,
                    20,
                    new byte[] { 0x11, 0x22, 0x33, 0x44 });
                Write(stream, ProtocolConstants.PacketTypeAudio, 3u, audioPayload);
                Assert.IsTrue(audioReceived.Wait(SocketTimeoutMilliseconds), "Timed out waiting for audio sink callback.");
                Assert.AreEqual(1, audioCalls);
                CollectionAssert.AreEqual(audioPayload, receivedAudio);

                byte[] ping = HandshakePayloads.Ping(4u, 123UL);
                Write(stream, ProtocolConstants.PacketTypePing, 4u, ping);
                AssertPacket(stream, ProtocolConstants.PacketTypePong, ping);

                Write(stream, ProtocolConstants.PacketTypeStopStream, 5u, new byte[0]);
            }
        }
```

- [ ] **Step 2: Run TCP receiver tests to verify failure on supported hosts**

On Windows:

```powershell
dotnet test receiver-windows/OpenAudioLink.sln -c Release --filter TcpReceiverTests
```

Expected: FAIL because `TcpReceiver.StartLoopback` does not yet accept an `Action<byte[]>` sink.

On the current Linux host, this command is expected to fail with `dotnet: command not found`.

- [ ] **Step 3: Implement TcpReceiver sink plumbing**

Modify `receiver-windows/src/OpenAudioLink/Receiver/TcpReceiver.cs`.

Add this field after `private readonly TcpListener listener;`:

```csharp
        private readonly Action<byte[]> audioSink;
```

Replace the private constructor with:

```csharp
        private TcpReceiver(TcpListener listener, Action<byte[]> audioSink)
        {
            this.listener = listener;
            this.audioSink = audioSink ?? (_ => { });
            Port = ((IPEndPoint)listener.LocalEndpoint).Port;
            ThreadPool.QueueUserWorkItem(_ => AcceptLoop());
        }
```

Replace `Start` with:

```csharp
        public static TcpReceiver Start(IPAddress address, int port, Action<byte[]> audioSink = null)
        {
            TcpListener listener = new TcpListener(address, port);
            listener.Start();
            return new TcpReceiver(listener, audioSink);
        }
```

Replace `StartLoopback` with:

```csharp
        public static TcpReceiver StartLoopback(Action<byte[]> audioSink = null)
        {
            return Start(IPAddress.Loopback, 0, audioSink);
        }
```

In `Handle`, replace this line:

```csharp
                    ReceiverSession session = new ReceiverSession((ulong)Interlocked.Increment(ref nextSessionId));
```

with:

```csharp
                    ReceiverSession session = new ReceiverSession((ulong)Interlocked.Increment(ref nextSessionId), audioSink);
```

- [ ] **Step 4: Run TCP receiver verification**

Run local checks:

```bash
python3 tools/check_docs_consistency.py
python3 tools/protocol/generate_golden_packets.py --check
git diff --check HEAD
```

Expected local output includes:

```text
docs consistency ok: 12 markdown files checked
protocol golden packets ok
```

On Windows, run:

```powershell
dotnet test receiver-windows/OpenAudioLink.sln -c Release --filter TcpReceiverTests
```

Expected: PASS.

- [ ] **Step 5: Commit Task 2**

```bash
git add receiver-windows/src/OpenAudioLink/Receiver/TcpReceiver.cs \
        receiver-windows/tests/OpenAudioLink.Tests/Receiver/TcpReceiverTests.cs
git commit -m "feat: pass receiver audio sink through tcp receiver"
```

---

## Task 3: Final Verification and CI

**Files:**

- No source files should change unless CI proves a small fix is required.

- [ ] **Step 1: Run local Linux checks**

```bash
git status --short --branch
python3 tools/check_docs_consistency.py
python3 tools/protocol/generate_golden_packets.py --check
git diff --check HEAD
```

Expected:

```text
docs consistency ok: 12 markdown files checked
protocol golden packets ok
```

`git diff --check HEAD` exits `0` with no output.

- [ ] **Step 2: Run platform tests where available**

On Windows or GitHub Actions:

```powershell
dotnet test receiver-windows/OpenAudioLink.sln -c Release
```

Expected: PASS.

On an `x86-64` Android build host or GitHub Actions:

```bash
cd sender-android
./gradlew :app:testDebugUnitTest
```

Expected: PASS.

On the current Linux host, expected local limits are:

- `dotnet test ...` fails with `dotnet: command not found`.
- Android Gradle may fail before tests at `:app:processDebugResources` because `aapt2` is an `x86-64` binary on a Linux `aarch64` host.

- [ ] **Step 3: Push branch for CI**

```bash
git push -u origin phase-1c-receiver-audio-sink
```

After mirror sync, GitHub Actions should run automatically because workflows now include `push.branches: [main, 'phase-*']`.

- [ ] **Step 4: Merge only after CI is green**

After all GitHub Actions checks are green, fast-forward Gitea `main`:

```bash
git fetch origin main phase-1c-receiver-audio-sink
git merge-base --is-ancestor origin/main phase-1c-receiver-audio-sink
git push origin phase-1c-receiver-audio-sink:main
git fetch origin main
git merge-base --is-ancestor phase-1c-receiver-audio-sink origin/main
```

Expected:

- The first `merge-base --is-ancestor` exits `0` before pushing.
- The push updates `main` from the previous commit to the Phase 1-C head.
- The final `merge-base --is-ancestor` exits `0` after pushing.

---

## Self-Review Checklist

- Spec coverage:
  - Optional `ReceiverSession` sink constructor: Task 1.
  - Valid `AUDIO` validation before sink callback: Task 1.
  - Separate sink clone that cannot mutate `LastAudioPayload`: Task 1.
  - Invalid streaming `AUDIO` does not call sink: Task 1.
  - Out-of-order `AUDIO` does not call sink: Task 1.
  - `TcpReceiver` passes sink into each `ReceiverSession`: Task 2.
  - TCP loopback proves valid `AUDIO` reaches sink exactly once: Task 2.
  - CI and merge flow: Task 3.
- Scope control:
  - No AAC decode.
  - No speaker playback.
  - No Android changes.
  - No mDNS discovery.
  - No UI, tray, installer, or configuration changes.
  - No background queue, worker thread, jitter buffer, or playback clock.
- Type consistency:
  - Sink type is `Action<byte[]>` everywhere.
  - `TcpReceiver.StartLoopback(Action<byte[]> audioSink = null)` forwards to `Start(IPAddress.Loopback, 0, audioSink)`.
  - `ReceiverSession` keeps the existing `ReceiverSession(ulong sessionId)` constructor.
  - `LastAudioPayload` and sink payload are separate clones.
