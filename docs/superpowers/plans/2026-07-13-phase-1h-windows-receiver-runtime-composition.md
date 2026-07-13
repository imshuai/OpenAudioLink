# Phase 1-H Windows Receiver Runtime Composition Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a minimal Windows `ReceiverRuntime` that composes `TcpReceiver`, `AudioFrameQueue`, `FakeAacDecoder`, and `FakeAudioRenderer` into one production-side fake receiver path.

**Architecture:** Keep UI, protocol, and existing receiver/session behavior unchanged. `ReceiverRuntime` owns the queue, decoder, renderer, and TCP receiver; the TCP audio sink enqueues each accepted payload and synchronously drains the queue through the fake decoder into the fake renderer.

**Tech Stack:** C#/.NET Framework 4.8 MSTest, existing `TcpReceiver`, `AudioFrameQueue`, fake decoder/renderer seams, existing protocol helpers, Python docs/golden checks.

---

## Scope Check

This plan implements only `docs/superpowers/specs/2026-07-13-phase-1h-windows-receiver-runtime-composition-design.md`.

In scope:

- `ReceiverRuntime` production composition object.
- Runtime tests for start validation and end-to-end TCP fake render path.

Out of scope:

- Real AAC decode.
- WASAPI, NAudio, Media Foundation, or speaker playback.
- Android sender changes.
- Protocol wire-format changes.
- UI controls, tray behavior, installer work, settings, or device selection.
- New background render loops, timers, clocks, jitter buffers, async loops, backpressure, or dependencies.

---

## Files and Responsibilities

- Create `receiver-windows/src/OpenAudioLink/Receiver/ReceiverRuntime.cs`
  - Owns `AudioFrameQueue`, `FakeAacDecoder`, `FakeAudioRenderer`, and `TcpReceiver`.
  - Starts receiver with an audio sink that enqueues and drains synchronously.
  - Exposes `Port`, `Queue`, and `Renderer`.
  - Disposes owned `TcpReceiver`.

- Create `receiver-windows/tests/OpenAudioLink.Tests/Receiver/ReceiverRuntimeTests.cs`
  - Tests loopback startup, invalid start arguments, and TCP fake render path.

No `.csproj` edits are needed because SDK-style projects include `.cs` files by default.

---

### Task 1: Add failing runtime tests

**Files:**
- Create: `receiver-windows/tests/OpenAudioLink.Tests/Receiver/ReceiverRuntimeTests.cs`

- [ ] **Step 1: Create runtime tests before implementation**

Create `receiver-windows/tests/OpenAudioLink.Tests/Receiver/ReceiverRuntimeTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenAudioLink.Protocol;
using OpenAudioLink.Receiver;

namespace OpenAudioLink.Tests.Receiver
{
    [TestClass]
    public sealed class ReceiverRuntimeTests
    {
        private const int SocketTimeoutMilliseconds = 5000;

        [TestMethod]
        public void StartLoopbackExposesReceiverState()
        {
            using (ReceiverRuntime runtime = ReceiverRuntime.StartLoopback())
            {
                Assert.AreNotEqual(0, runtime.Port);
                Assert.AreEqual(0, runtime.Queue.Count);
                Assert.AreEqual(0, runtime.Renderer.RenderedCount);
            }
        }

        [TestMethod]
        public void StartRejectsNullAddress()
        {
            Assert.ThrowsException<ArgumentNullException>(() => ReceiverRuntime.Start(null, 0));
        }

        [TestMethod]
        public void StartRejectsNonPositiveQueueCapacity()
        {
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => ReceiverRuntime.Start(IPAddress.Loopback, 0, 0));
        }

        [TestMethod]
        public void ClientAudioFramesAreDecodedAndRendered()
        {
            using (ReceiverRuntime runtime = ReceiverRuntime.StartLoopback())
            using (TcpClient client = Connect(runtime.Port))
            {
                NetworkStream stream = client.GetStream();

                Write(stream, ProtocolConstants.PacketTypeHello, 1u, HandshakePayloads.Hello("Android Phone", "1.0.0", ProtocolConstants.PlatformAndroid, ProtocolConstants.CapabilityAacSupported));
                AssertPacket(stream, ProtocolConstants.PacketTypeWelcome, HandshakePayloads.Welcome(ProtocolConstants.ResultSuccess, "Windows PC", "1.0.0", 1));

                Write(stream, ProtocolConstants.PacketTypeStartStream, 2u, HandshakePayloads.StartStream(ProtocolConstants.CodecAacLc, 48000u, 2, 192000u, 20));
                AssertPacket(stream, ProtocolConstants.PacketTypeStreamReady, HandshakePayloads.StreamReady(ProtocolConstants.StreamResultSuccess, ProtocolConstants.CodecAacLc, 48000u, 2));

                byte[][] encodedFrames =
                {
                    new byte[] { 0x11, 0x22, 0x33, 0x44 },
                    new byte[] { 0x21, 0x22, 0x23, 0x24 },
                    new byte[] { 0x31, 0x32, 0x33, 0x34 },
                };

                for (int i = 0; i < encodedFrames.Length; i++)
                {
                    byte[] payload = HandshakePayloads.Audio(ProtocolConstants.CodecAacLc, (uint)(i + 1), 123456003UL + (ulong)(20 * i), 20, encodedFrames[i]);
                    Write(stream, ProtocolConstants.PacketTypeAudio, 3u + (uint)i, payload);
                }

                Assert.AreEqual(3, runtime.Renderer.RenderedCount);
                Assert.AreEqual(0, runtime.Queue.Count);
                IReadOnlyList<FakePcmFrame> renderedFrames = runtime.Renderer.RenderedFrames;
                for (int i = 0; i < encodedFrames.Length; i++)
                {
                    Assert.AreEqual((uint)(i + 1), renderedFrames[i].FrameNumber);
                    Assert.AreEqual(123456003UL + (ulong)(20 * i), renderedFrames[i].CaptureTimestamp);
                    Assert.AreEqual((ushort)20, renderedFrames[i].FrameDuration);
                    CollectionAssert.AreEqual(encodedFrames[i], renderedFrames[i].PcmBytes);
                }

                byte[] ping = HandshakePayloads.Ping(5u, 123456005UL);
                Write(stream, ProtocolConstants.PacketTypePing, 6u, ping);
                AssertPacket(stream, ProtocolConstants.PacketTypePong, ping);

                Write(stream, ProtocolConstants.PacketTypeStopStream, 7u, new byte[0]);
            }
        }

        private static TcpClient Connect(int port)
        {
            TcpClient client = new TcpClient();
            client.ReceiveTimeout = SocketTimeoutMilliseconds;
            client.SendTimeout = SocketTimeoutMilliseconds;
            client.Connect(IPAddress.Loopback, port);
            return client;
        }

        private static void Write(NetworkStream stream, byte type, uint sequence, byte[] payload)
        {
            byte[] packet = PacketWriter.WritePacket(type, sequence, 0, payload);
            stream.Write(packet, 0, packet.Length);
        }

        private static void AssertPacket(NetworkStream stream, byte type, byte[] payload)
        {
            byte[] packet = PacketReader.ReadPacket(stream);
            Assert.AreEqual(type, PacketParser.ParseHeader(packet).PacketType);
            CollectionAssert.AreEqual(payload, PacketParser.Payload(packet));
        }
    }
}
```

- [ ] **Step 2: Run runtime tests and verify RED**

Run on a Windows/.NET build host:

```powershell
dotnet test receiver-windows/OpenAudioLink.sln -c Release --filter ReceiverRuntimeTests
```

Expected: FAIL because `ReceiverRuntime` does not exist.

On this Linux workspace, `dotnet` may be unavailable; if so, record `dotnet not found` and rely on branch CI.

- [ ] **Step 3: Commit failing runtime tests**

```bash
git add receiver-windows/tests/OpenAudioLink.Tests/Receiver/ReceiverRuntimeTests.cs
git commit -m "test: cover receiver runtime composition"
```

---

### Task 2: Implement `ReceiverRuntime`

**Files:**
- Create: `receiver-windows/src/OpenAudioLink/Receiver/ReceiverRuntime.cs`

- [ ] **Step 1: Add minimal runtime composition**

Create `receiver-windows/src/OpenAudioLink/Receiver/ReceiverRuntime.cs`:

```csharp
using System;
using System.Net;

namespace OpenAudioLink.Receiver
{
    public sealed class ReceiverRuntime : IDisposable
    {
        private readonly FakeAacDecoder decoder;
        private readonly TcpReceiver receiver;

        private ReceiverRuntime(AudioFrameQueue queue, FakeAacDecoder decoder, FakeAudioRenderer renderer, TcpReceiver receiver)
        {
            Queue = queue;
            this.decoder = decoder;
            Renderer = renderer;
            this.receiver = receiver;
            Port = receiver.Port;
        }

        public int Port { get; }

        public AudioFrameQueue Queue { get; }

        public FakeAudioRenderer Renderer { get; }

        public static ReceiverRuntime StartLoopback(int queueCapacity = 8)
        {
            return Start(IPAddress.Loopback, 0, queueCapacity);
        }

        public static ReceiverRuntime Start(IPAddress address, int port, int queueCapacity = 8)
        {
            if (address == null)
            {
                throw new ArgumentNullException(nameof(address));
            }

            AudioFrameQueue queue = new AudioFrameQueue(queueCapacity);
            FakeAacDecoder decoder = new FakeAacDecoder();
            FakeAudioRenderer renderer = new FakeAudioRenderer();
            TcpReceiver receiver = TcpReceiver.Start(address, port, payload =>
            {
                queue.Enqueue(payload);
                renderer.Drain(queue, decoder);
            });

            return new ReceiverRuntime(queue, decoder, renderer, receiver);
        }

        public void Dispose()
        {
            receiver.Dispose();
        }
    }
}
```

- [ ] **Step 2: Run runtime tests and verify GREEN**

Run on a Windows/.NET build host:

```powershell
dotnet test receiver-windows/OpenAudioLink.sln -c Release --filter ReceiverRuntimeTests
```

Expected: PASS.

- [ ] **Step 3: Commit runtime implementation**

```bash
git add receiver-windows/src/OpenAudioLink/Receiver/ReceiverRuntime.cs
git commit -m "feat: compose receiver runtime"
```

---

### Task 3: Final verification and branch push

**Files:**
- Verify only.

- [ ] **Step 1: Run local deterministic checks**

```bash
python3 tools/check_docs_consistency.py
python3 tools/protocol/generate_golden_packets.py --check
git diff --check HEAD
```

Expected:

```text
docs consistency ok: ... markdown files checked
protocol golden packets ok
```

`git diff --check HEAD` prints no output.

- [ ] **Step 2: Run platform tests where available**

Windows CI/build host:

```powershell
dotnet test receiver-windows/OpenAudioLink.sln -c Release
```

Android CI/build host:

```bash
cd sender-android
./gradlew :app:testDebugUnitTest
```

Expected: PASS. If local Linux lacks `dotnet` or Android SDK, record the missing tool output and rely on branch CI.

- [ ] **Step 3: Scope guard**

Run:

```bash
rg -n "WASAPI|NAudio|Media Foundation|MediaProjection|AudioRecord|MediaCodec|Task\.Run|Thread|Timer|Jitter|Clock" receiver-windows/src receiver-windows/tests sender-android/app/src || true
```

Expected: no new real playback, decode, Android capture, thread, timer, or jitter implementation is added. Existing `TcpReceiver` thread-pool code and `Program` STA thread may still appear.

- [ ] **Step 4: Push branch**

Because this environment has a known path MTU issue when pushing to `192.168.3.20`, add a temporary host route before push and remove it after push:

```bash
ip route add 192.168.3.20/32 via 192.168.4.1 dev eth0 mtu 1200 || true
git push -u origin phase-1h-windows-receiver-runtime-composition
ip route del 192.168.3.20/32 via 192.168.4.1 dev eth0 || true
```

Expected: branch `phase-1h-windows-receiver-runtime-composition` exists on Gitea and GitHub mirror CI starts for the `phase-*` push.

- [ ] **Step 5: Wait for branch CI**

Wait for the three GitHub mirror CI workflows:

- docs
- android
- windows

Expected: all green before merging to `main`.
