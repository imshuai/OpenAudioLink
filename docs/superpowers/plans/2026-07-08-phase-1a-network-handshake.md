# Phase 1-A Network Handshake Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the smallest TCP handshake skeleton that proves Android Sender and Windows Receiver can exchange OpenAudioLink Version 1 control packets before audio/discovery work starts.

**Architecture:** Keep protocol code byte-focused and deterministic. Add serializers and stream readers beside the existing parsers, then build tiny session/client wrappers around standard library sockets. No audio, mDNS, DI framework, persistent config, or background service is added in this phase.

**Tech Stack:** Python 3 stdlib golden fixtures, C# .NET Framework 4.8 + MSTest + `System.Net.Sockets`, Android Kotlin + JUnit 4 + Java/Kotlin standard networking.

---

## Scope Check

This plan implements `docs/superpowers/specs/2026-07-08-phase-1a-network-handshake-design.md` only.

Included:

- Golden packets for all Phase 1-A packet types.
- Windows protocol serialization, payload helpers, packet stream reader, receiver session, and loopback TCP listener.
- Android protocol serialization, payload helpers, packet stream reader, handshake client, TCP wrapper, and `INTERNET` permission.
- Unit tests for exact wire bytes and state transitions.

Excluded:

- Real audio capture.
- AAC encode/decode.
- mDNS discovery.
- Foreground service.
- Windows installer.
- Full UI.

Known local verification limits:

- Current host is `aarch64`; Android Gradle fails at `aapt2` because the downloaded binary is `x86-64`.
- Windows `net48` tests require Windows/MSBuild.
- Local Linux can always run Python docs/golden checks.

---

## File Structure

Modify existing files:

```text
tools/protocol/generate_golden_packets.py
receiver-windows/src/OpenAudioLink/Protocol/ProtocolConstants.cs
receiver-windows/tests/OpenAudioLink.Tests/Protocol/PacketParserTests.cs
sender-android/app/src/main/AndroidManifest.xml
sender-android/app/src/main/java/com/openaudiolink/protocol/ProtocolConstants.kt
sender-android/app/src/test/java/com/openaudiolink/protocol/PacketParserTest.kt
```

Create Windows files:

```text
receiver-windows/src/OpenAudioLink/Protocol/PacketWriter.cs
receiver-windows/src/OpenAudioLink/Protocol/PacketReader.cs
receiver-windows/src/OpenAudioLink/Protocol/HandshakePayloads.cs
receiver-windows/src/OpenAudioLink/Receiver/ReceiverSession.cs
receiver-windows/src/OpenAudioLink/Receiver/TcpReceiver.cs
receiver-windows/tests/OpenAudioLink.Tests/Protocol/PacketWriterTests.cs
receiver-windows/tests/OpenAudioLink.Tests/Receiver/ReceiverSessionTests.cs
receiver-windows/tests/OpenAudioLink.Tests/Receiver/TcpReceiverTests.cs
```

Create Android files:

```text
sender-android/app/src/main/java/com/openaudiolink/protocol/PacketWriter.kt
sender-android/app/src/main/java/com/openaudiolink/protocol/PacketReader.kt
sender-android/app/src/main/java/com/openaudiolink/protocol/HandshakePayloads.kt
sender-android/app/src/main/java/com/openaudiolink/network/HandshakeClient.kt
sender-android/app/src/main/java/com/openaudiolink/network/TcpHandshakeClient.kt
sender-android/app/src/test/java/com/openaudiolink/protocol/PacketWriterTest.kt
sender-android/app/src/test/java/com/openaudiolink/network/HandshakeClientTest.kt
```

Generated testdata after Task 1:

```text
testdata/protocol/valid-welcome.bin
testdata/protocol/valid-start-stream.bin
testdata/protocol/valid-stream-ready.bin
testdata/protocol/valid-ping.bin
testdata/protocol/valid-pong.bin
testdata/protocol/valid-stop-stream.bin
testdata/protocol/valid-error.bin
```

---

## Shared Fixture Values

Use these deterministic values in Python, C#, and Kotlin tests:

```text
Receiver Name = Windows PC
Receiver Version = 1.0.0
Session ID = 0x0102030405060708
Start stream sequence = 3
Stream ready sequence = 4
Ping sequence = 5
Pong sequence = 6
Stop sequence = 7
Error sequence = 8
Timestamp base = 123456000
Codec = AAC-LC = 1
Sample Rate = 48000
Channels = 2
Bitrate = 192000
Frame Duration = 20
STREAM_READY Success = 0
STREAM_READY Unsupported Codec = 1
Error Code Unsupported Codec = 1003
Error Severity Recoverable = 2
Error Description = Unsupported codec
```

---

### Task 1: Add Phase 1-A Golden Packets

**Files:**

- Modify: `tools/protocol/generate_golden_packets.py`
- Generate: `testdata/protocol/*.bin`
- Generate: `testdata/protocol/golden-manifest.json`

- [ ] **Step 1: Extend packet constants and payload helpers**

Modify `tools/protocol/generate_golden_packets.py` constants near the existing packet constants:

```python
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
```

Add these helper functions after `hello_payload()`:

```python
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
```

- [ ] **Step 2: Add Phase 1-A packets to `packet_set()`**

Modify `packet_set()` so it creates these packets before the return:

```python
    valid_welcome = pack_header(PACKET_WELCOME, 2, 123456001, welcome_payload())
    valid_start_stream = pack_header(PACKET_START_STREAM, 3, 123456002, start_stream_payload())
    valid_stream_ready = pack_header(PACKET_STREAM_READY, 4, 123456003, stream_ready_payload())
    valid_ping = pack_header(PACKET_PING, 5, 123456004, ping_payload())
    valid_pong = pack_header(PACKET_PONG, 6, 123456004, ping_payload())
    valid_stop_stream = pack_header(PACKET_STOP_STREAM, 7, 123456006, b"")
    valid_error = pack_header(PACKET_ERROR, 8, 123456007, error_payload())
```

Add these entries to the returned dictionary:

```python
        "valid-welcome.bin": valid_welcome,
        "valid-start-stream.bin": valid_start_stream,
        "valid-stream-ready.bin": valid_stream_ready,
        "valid-ping.bin": valid_ping,
        "valid-pong.bin": valid_pong,
        "valid-stop-stream.bin": valid_stop_stream,
        "valid-error.bin": valid_error,
```

- [ ] **Step 3: Verify the check fails before generated files exist**

Run:

```bash
python3 tools/protocol/generate_golden_packets.py --check
```

Expected: FAIL with lines containing missing Phase 1-A fixture files, including `valid-stream-ready.bin` and `valid-error.bin`.

- [ ] **Step 4: Generate fixtures**

Run:

```bash
python3 tools/protocol/generate_golden_packets.py
```

Expected output:

```text
wrote protocol golden packets to testdata/protocol
```

- [ ] **Step 5: Verify generated fixtures**

Run:

```bash
python3 tools/protocol/generate_golden_packets.py --check
```

Expected output:

```text
protocol golden packets ok
```

- [ ] **Step 6: Commit**

```bash
git add tools/protocol/generate_golden_packets.py testdata/protocol
git commit -m "test: add phase 1a protocol golden packets"
```

---

### Task 2: Add Windows Protocol Serialization

**Files:**

- Modify: `receiver-windows/src/OpenAudioLink/Protocol/ProtocolConstants.cs`
- Create: `receiver-windows/src/OpenAudioLink/Protocol/PacketWriter.cs`
- Create: `receiver-windows/src/OpenAudioLink/Protocol/HandshakePayloads.cs`
- Create: `receiver-windows/src/OpenAudioLink/Protocol/PacketReader.cs`
- Create: `receiver-windows/tests/OpenAudioLink.Tests/Protocol/PacketWriterTests.cs`
- Modify: `receiver-windows/tests/OpenAudioLink.Tests/Protocol/PacketParserTests.cs`

- [ ] **Step 1: Write failing Windows exact-byte tests**

Create `receiver-windows/tests/OpenAudioLink.Tests/Protocol/PacketWriterTests.cs`:

```csharp
using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenAudioLink;

namespace OpenAudioLink.Tests.Protocol
{
    [TestClass]
    public sealed class PacketWriterTests
    {
        [TestMethod]
        public void WriteWelcome_MatchesGoldenPacket()
        {
            byte[] packet = PacketWriter.WritePacket(
                ProtocolConstants.PacketTypeWelcome,
                2u,
                123456001UL,
                HandshakePayloads.Welcome(ProtocolConstants.ResultSuccess, "Windows PC", "1.0.0", 0x0102030405060708UL));

            CollectionAssert.AreEqual(ReadFixture("valid-welcome.bin"), packet);
        }

        [TestMethod]
        public void WriteStartStream_MatchesGoldenPacket()
        {
            byte[] packet = PacketWriter.WritePacket(
                ProtocolConstants.PacketTypeStartStream,
                3u,
                123456002UL,
                HandshakePayloads.StartStream(ProtocolConstants.CodecAacLc, 48000u, 2, 192000u, 20));

            CollectionAssert.AreEqual(ReadFixture("valid-start-stream.bin"), packet);
        }

        [TestMethod]
        public void WriteStreamReady_MatchesGoldenPacket()
        {
            byte[] packet = PacketWriter.WritePacket(
                ProtocolConstants.PacketTypeStreamReady,
                4u,
                123456003UL,
                HandshakePayloads.StreamReady(ProtocolConstants.StreamResultSuccess, ProtocolConstants.CodecAacLc, 48000u, 2));

            CollectionAssert.AreEqual(ReadFixture("valid-stream-ready.bin"), packet);
        }

        [TestMethod]
        public void WritePingAndPong_MatchGoldenPackets()
        {
            byte[] payload = HandshakePayloads.Ping(5u, 123456005UL);

            CollectionAssert.AreEqual(
                ReadFixture("valid-ping.bin"),
                PacketWriter.WritePacket(ProtocolConstants.PacketTypePing, 5u, 123456004UL, payload));
            CollectionAssert.AreEqual(
                ReadFixture("valid-pong.bin"),
                PacketWriter.WritePacket(ProtocolConstants.PacketTypePong, 6u, 123456004UL, payload));
        }

        [TestMethod]
        public void WriteStopStream_MatchesGoldenPacket()
        {
            byte[] packet = PacketWriter.WritePacket(ProtocolConstants.PacketTypeStopStream, 7u, 123456006UL, new byte[0]);

            CollectionAssert.AreEqual(ReadFixture("valid-stop-stream.bin"), packet);
        }

        [TestMethod]
        public void WriteError_MatchesGoldenPacket()
        {
            byte[] packet = PacketWriter.WritePacket(
                ProtocolConstants.PacketTypeError,
                8u,
                123456007UL,
                HandshakePayloads.Error(ProtocolConstants.ErrorUnsupportedCodec, ProtocolConstants.ErrorSeverityRecoverable, "Unsupported codec"));

            CollectionAssert.AreEqual(ReadFixture("valid-error.bin"), packet);
        }

        private static byte[] ReadFixture(string name)
        {
            DirectoryInfo directory = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (directory != null)
            {
                string path = Path.Combine(directory.FullName, "testdata", "protocol", name);
                if (File.Exists(path))
                {
                    return File.ReadAllBytes(path);
                }

                directory = directory.Parent;
            }

            throw new FileNotFoundException("Fixture not found.", name);
        }
    }
}
```

- [ ] **Step 2: Run Windows tests and verify failure**

Run on Windows:

```powershell
dotnet test receiver-windows/OpenAudioLink.sln -c Release
```

Expected: FAIL because `PacketWriter`, `HandshakePayloads`, and new constants are not defined.

- [ ] **Step 3: Extend Windows constants**

Replace `receiver-windows/src/OpenAudioLink/Protocol/ProtocolConstants.cs` with:

```csharp
namespace OpenAudioLink
{
    public static class ProtocolConstants
    {
        public const int HeaderSize = 24;
        public const int AudioPayloadHeaderSize = 19;
        public const int MaxPacketSize = 65536;
        public const byte MajorVersion = 1;
        public const byte MinorVersion = 0;

        public const byte PacketTypeHello = 0x01;
        public const byte PacketTypeWelcome = 0x02;
        public const byte PacketTypeStartStream = 0x03;
        public const byte PacketTypeStreamReady = 0x04;
        public const byte PacketTypeAudio = 0x05;
        public const byte PacketTypeStopStream = 0x06;
        public const byte PacketTypePing = 0x07;
        public const byte PacketTypePong = 0x08;
        public const byte PacketTypeError = 0x09;

        public const byte ResultSuccess = 0;
        public const byte ResultUnsupportedProtocol = 1;
        public const byte ResultReceiverBusy = 2;
        public const byte ResultInternalError = 4;

        public const byte StreamResultSuccess = 0;
        public const byte StreamResultUnsupportedCodec = 1;
        public const byte StreamResultUnsupportedFormat = 2;
        public const byte StreamResultReceiverNotReady = 3;
        public const byte StreamResultInternalError = 4;

        public const ushort ErrorInvalidPacket = 1001;
        public const ushort ErrorUnsupportedProtocol = 1002;
        public const ushort ErrorUnsupportedCodec = 1003;
        public const ushort ErrorInvalidPayload = 1004;
        public const ushort ErrorReceiverBusy = 1005;
        public const ushort ErrorTimeout = 1008;
        public const byte ErrorSeverityRecoverable = 2;
        public const byte ErrorSeverityFatal = 3;

        public const byte CodecAacLc = 1;
        public const byte PlatformAndroid = 1;
        public const byte PlatformWindows = 2;
        public const uint CapabilityAacSupported = 1;
        public const int DefaultPort = 39888;

        public static readonly byte[] Magic = { (byte)'O', (byte)'A', (byte)'L', (byte)'P' };
    }
}
```

- [ ] **Step 4: Add Windows packet writer**

Create `receiver-windows/src/OpenAudioLink/Protocol/PacketWriter.cs`:

```csharp
using System;

namespace OpenAudioLink
{
    public static class PacketWriter
    {
        public static byte[] WritePacket(byte packetType, uint sequenceNumber, ulong timestamp, byte[] payload)
        {
            payload = payload ?? new byte[0];
            if (payload.Length > ProtocolConstants.MaxPacketSize)
            {
                throw new ArgumentOutOfRangeException(nameof(payload), "Payload is too large.");
            }

            byte[] packet = new byte[ProtocolConstants.HeaderSize + payload.Length];
            Buffer.BlockCopy(ProtocolConstants.Magic, 0, packet, 0, ProtocolConstants.Magic.Length);
            packet[4] = ProtocolConstants.MajorVersion;
            packet[5] = ProtocolConstants.MinorVersion;
            packet[6] = packetType;
            packet[7] = 0;
            WriteUInt32(packet, 8, sequenceNumber);
            WriteUInt64(packet, 12, timestamp);
            WriteUInt32(packet, 20, (uint)payload.Length);
            Buffer.BlockCopy(payload, 0, packet, ProtocolConstants.HeaderSize, payload.Length);
            return packet;
        }

        internal static void WriteUInt16(byte[] buffer, int offset, ushort value)
        {
            buffer[offset] = (byte)(value >> 8);
            buffer[offset + 1] = (byte)value;
        }

        internal static void WriteUInt32(byte[] buffer, int offset, uint value)
        {
            buffer[offset] = (byte)(value >> 24);
            buffer[offset + 1] = (byte)(value >> 16);
            buffer[offset + 2] = (byte)(value >> 8);
            buffer[offset + 3] = (byte)value;
        }

        internal static void WriteUInt64(byte[] buffer, int offset, ulong value)
        {
            WriteUInt32(buffer, offset, (uint)(value >> 32));
            WriteUInt32(buffer, offset + 4, (uint)value);
        }
    }
}
```

- [ ] **Step 5: Add Windows handshake payload helpers**

Create `receiver-windows/src/OpenAudioLink/Protocol/HandshakePayloads.cs`:

```csharp
using System;
using System.Text;

namespace OpenAudioLink
{
    public static class HandshakePayloads
    {
        public static byte[] Hello(string senderName, string senderVersion, byte platform, uint capabilities)
        {
            return Join(
                WriteString(senderName),
                WriteString(senderVersion),
                new[] { ProtocolConstants.MajorVersion, ProtocolConstants.MinorVersion, platform },
                WriteUInt32(capabilities));
        }

        public static byte[] Welcome(byte result, string receiverName, string receiverVersion, ulong sessionId)
        {
            return Join(new[] { result }, WriteString(receiverName), WriteString(receiverVersion), WriteUInt64(sessionId));
        }

        public static byte[] StartStream(byte codec, uint sampleRate, byte channels, uint bitrate, ushort frameDuration)
        {
            return Join(new[] { codec }, WriteUInt32(sampleRate), new[] { channels }, WriteUInt32(bitrate), WriteUInt16(frameDuration));
        }

        public static byte[] StreamReady(byte result, byte codec, uint sampleRate, byte channels)
        {
            return Join(new[] { result, codec }, WriteUInt32(sampleRate), new[] { channels });
        }

        public static byte[] Ping(uint sequence, ulong timestamp)
        {
            return Join(WriteUInt32(sequence), WriteUInt64(timestamp));
        }

        public static byte[] Error(ushort code, byte severity, string description)
        {
            return Join(WriteUInt16(code), new[] { severity }, WriteString(description));
        }

        public static byte ReadStartStreamCodec(byte[] payload)
        {
            if (payload == null || payload.Length < 12)
            {
                throw new PacketParseException("START_STREAM payload is too short.");
            }
            return payload[0];
        }

        private static byte[] WriteString(string value)
        {
            byte[] text = Encoding.UTF8.GetBytes(value ?? string.Empty);
            if (text.Length > ushort.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "String is too long.");
            }
            return Join(WriteUInt16((ushort)text.Length), text);
        }

        private static byte[] WriteUInt16(ushort value)
        {
            byte[] buffer = new byte[2];
            PacketWriter.WriteUInt16(buffer, 0, value);
            return buffer;
        }

        private static byte[] WriteUInt32(uint value)
        {
            byte[] buffer = new byte[4];
            PacketWriter.WriteUInt32(buffer, 0, value);
            return buffer;
        }

        private static byte[] WriteUInt64(ulong value)
        {
            byte[] buffer = new byte[8];
            PacketWriter.WriteUInt64(buffer, 0, value);
            return buffer;
        }

        private static byte[] Join(params byte[][] parts)
        {
            int length = 0;
            foreach (byte[] part in parts)
            {
                length += part.Length;
            }

            byte[] output = new byte[length];
            int offset = 0;
            foreach (byte[] part in parts)
            {
                Buffer.BlockCopy(part, 0, output, offset, part.Length);
                offset += part.Length;
            }
            return output;
        }
    }
}
```

- [ ] **Step 6: Add Windows packet stream reader**

Create `receiver-windows/src/OpenAudioLink/Protocol/PacketReader.cs`:

```csharp
using System;
using System.IO;

namespace OpenAudioLink
{
    public static class PacketReader
    {
        public static byte[] ReadPacket(Stream stream)
        {
            byte[] header = ReadExact(stream, ProtocolConstants.HeaderSize);
            uint payloadLength = PacketParser.ReadUInt32(header, 20);
            if (payloadLength > ProtocolConstants.MaxPacketSize)
            {
                throw new PacketParseException("Payload is too large.");
            }

            byte[] packet = new byte[ProtocolConstants.HeaderSize + (int)payloadLength];
            Buffer.BlockCopy(header, 0, packet, 0, header.Length);
            byte[] payload = ReadExact(stream, (int)payloadLength);
            Buffer.BlockCopy(payload, 0, packet, ProtocolConstants.HeaderSize, payload.Length);
            PacketParser.ParseHeader(packet);
            return packet;
        }

        private static byte[] ReadExact(Stream stream, int length)
        {
            byte[] buffer = new byte[length];
            int offset = 0;
            while (offset < length)
            {
                int read = stream.Read(buffer, offset, length - offset);
                if (read == 0)
                {
                    throw new EndOfStreamException("Connection closed while reading packet.");
                }
                offset += read;
            }
            return buffer;
        }
    }
}
```

- [ ] **Step 7: Extend Windows parser fixture tests for new packet types**

Append to `receiver-windows/tests/OpenAudioLink.Tests/Protocol/PacketParserTests.cs` before `ReadFixture`:

```csharp
        [TestMethod]
        public void ParseHeader_Phase1aFixtures_ReturnExpectedTypes()
        {
            Assert.AreEqual(ProtocolConstants.PacketTypeWelcome, PacketParser.ParseHeader(ReadFixture("valid-welcome.bin")).PacketType);
            Assert.AreEqual(ProtocolConstants.PacketTypeStartStream, PacketParser.ParseHeader(ReadFixture("valid-start-stream.bin")).PacketType);
            Assert.AreEqual(ProtocolConstants.PacketTypeStreamReady, PacketParser.ParseHeader(ReadFixture("valid-stream-ready.bin")).PacketType);
            Assert.AreEqual(ProtocolConstants.PacketTypePing, PacketParser.ParseHeader(ReadFixture("valid-ping.bin")).PacketType);
            Assert.AreEqual(ProtocolConstants.PacketTypePong, PacketParser.ParseHeader(ReadFixture("valid-pong.bin")).PacketType);
            Assert.AreEqual(ProtocolConstants.PacketTypeStopStream, PacketParser.ParseHeader(ReadFixture("valid-stop-stream.bin")).PacketType);
            Assert.AreEqual(ProtocolConstants.PacketTypeError, PacketParser.ParseHeader(ReadFixture("valid-error.bin")).PacketType);
        }
```

- [ ] **Step 8: Run Windows tests**

Run on Windows:

```powershell
dotnet test receiver-windows/OpenAudioLink.sln -c Release
```

Expected: PASS.

- [ ] **Step 9: Commit**

```bash
git add receiver-windows/src/OpenAudioLink/Protocol receiver-windows/tests/OpenAudioLink.Tests/Protocol
git commit -m "feat: add Windows protocol handshake serialization"
```

---

### Task 3: Add Windows Receiver Session Logic

**Files:**

- Create: `receiver-windows/src/OpenAudioLink/Receiver/ReceiverSession.cs`
- Create: `receiver-windows/tests/OpenAudioLink.Tests/Receiver/ReceiverSessionTests.cs`

- [ ] **Step 1: Write failing receiver session tests**

Create `receiver-windows/tests/OpenAudioLink.Tests/Receiver/ReceiverSessionTests.cs`:

```csharp
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenAudioLink;

namespace OpenAudioLink.Tests.Receiver
{
    [TestClass]
    public sealed class ReceiverSessionTests
    {
        [TestMethod]
        public void ProcessHello_ReturnsWelcome()
        {
            ReceiverSession session = new ReceiverSession(0x0102030405060708UL);
            byte[] hello = PacketWriter.WritePacket(
                ProtocolConstants.PacketTypeHello,
                1u,
                123456000UL,
                HandshakePayloads.Hello("Android Phone", "1.0.0", ProtocolConstants.PlatformAndroid, ProtocolConstants.CapabilityAacSupported));

            byte[] response = session.Process(hello);
            PacketHeader header = PacketParser.ParseHeader(response);

            Assert.AreEqual(ProtocolConstants.PacketTypeWelcome, header.PacketType);
            Assert.AreEqual(ReceiverSessionState.WaitingForStartStream, session.State);
        }

        [TestMethod]
        public void ProcessStartStream_ReturnsStreamReady()
        {
            ReceiverSession session = ReadySession();
            byte[] start = PacketWriter.WritePacket(
                ProtocolConstants.PacketTypeStartStream,
                3u,
                123456002UL,
                HandshakePayloads.StartStream(ProtocolConstants.CodecAacLc, 48000u, 2, 192000u, 20));

            byte[] response = session.Process(start);
            PacketHeader header = PacketParser.ParseHeader(response);
            byte[] payload = PacketParser.Payload(response);

            Assert.AreEqual(ProtocolConstants.PacketTypeStreamReady, header.PacketType);
            Assert.AreEqual(ProtocolConstants.StreamResultSuccess, payload[0]);
            Assert.AreEqual(ReceiverSessionState.Streaming, session.State);
        }

        [TestMethod]
        public void ProcessUnsupportedCodec_ReturnsStreamReadyFailure()
        {
            ReceiverSession session = ReadySession();
            byte[] start = PacketWriter.WritePacket(
                ProtocolConstants.PacketTypeStartStream,
                3u,
                123456002UL,
                HandshakePayloads.StartStream(2, 48000u, 2, 192000u, 20));

            byte[] response = session.Process(start);
            PacketHeader header = PacketParser.ParseHeader(response);
            byte[] payload = PacketParser.Payload(response);

            Assert.AreEqual(ProtocolConstants.PacketTypeStreamReady, header.PacketType);
            Assert.AreEqual(ProtocolConstants.StreamResultUnsupportedCodec, payload[0]);
            Assert.AreEqual(ReceiverSessionState.Stopped, session.State);
        }

        [TestMethod]
        public void ProcessPing_ReturnsPongWithSamePayload()
        {
            ReceiverSession session = StreamingSession();
            byte[] pingPayload = HandshakePayloads.Ping(5u, 123456005UL);
            byte[] ping = PacketWriter.WritePacket(ProtocolConstants.PacketTypePing, 5u, 123456004UL, pingPayload);

            byte[] response = session.Process(ping);

            Assert.AreEqual(ProtocolConstants.PacketTypePong, PacketParser.ParseHeader(response).PacketType);
            CollectionAssert.AreEqual(pingPayload, PacketParser.Payload(response));
        }

        [TestMethod]
        public void ProcessStopStream_StopsSession()
        {
            ReceiverSession session = StreamingSession();
            byte[] stop = PacketWriter.WritePacket(ProtocolConstants.PacketTypeStopStream, 7u, 123456006UL, new byte[0]);

            byte[] response = session.Process(stop);

            Assert.IsNull(response);
            Assert.AreEqual(ReceiverSessionState.Stopped, session.State);
        }

        [TestMethod]
        public void ProcessPingBeforeStartStream_Throws()
        {
            ReceiverSession session = ReadySession();
            byte[] ping = PacketWriter.WritePacket(ProtocolConstants.PacketTypePing, 5u, 123456004UL, HandshakePayloads.Ping(5u, 123456005UL));

            Assert.ThrowsException<PacketParseException>(() => session.Process(ping));
        }

        private static ReceiverSession ReadySession()
        {
            ReceiverSession session = new ReceiverSession(0x0102030405060708UL);
            session.Process(PacketWriter.WritePacket(
                ProtocolConstants.PacketTypeHello,
                1u,
                123456000UL,
                HandshakePayloads.Hello("Android Phone", "1.0.0", ProtocolConstants.PlatformAndroid, ProtocolConstants.CapabilityAacSupported)));
            return session;
        }

        private static ReceiverSession StreamingSession()
        {
            ReceiverSession session = ReadySession();
            session.Process(PacketWriter.WritePacket(
                ProtocolConstants.PacketTypeStartStream,
                3u,
                123456002UL,
                HandshakePayloads.StartStream(ProtocolConstants.CodecAacLc, 48000u, 2, 192000u, 20)));
            return session;
        }
    }
}
```

- [ ] **Step 2: Run Windows tests and verify failure**

Run on Windows:

```powershell
dotnet test receiver-windows/OpenAudioLink.sln -c Release
```

Expected: FAIL because `ReceiverSession` and `ReceiverSessionState` are not defined.

- [ ] **Step 3: Implement receiver session**

Create `receiver-windows/src/OpenAudioLink/Receiver/ReceiverSession.cs`:

```csharp
namespace OpenAudioLink
{
    public enum ReceiverSessionState
    {
        WaitingForHello,
        WaitingForStartStream,
        Streaming,
        Stopped
    }

    public sealed class ReceiverSession
    {
        private readonly ulong sessionId;
        private uint nextSequence = 1;

        public ReceiverSession(ulong sessionId)
        {
            this.sessionId = sessionId;
            State = ReceiverSessionState.WaitingForHello;
        }

        public ReceiverSessionState State { get; private set; }

        public byte[] Process(byte[] packet)
        {
            PacketHeader header = PacketParser.ParseHeader(packet);
            byte[] payload = PacketParser.Payload(packet);

            if (State == ReceiverSessionState.WaitingForHello)
            {
                if (header.PacketType != ProtocolConstants.PacketTypeHello)
                {
                    throw new PacketParseException("Expected HELLO.");
                }

                State = ReceiverSessionState.WaitingForStartStream;
                return PacketWriter.WritePacket(
                    ProtocolConstants.PacketTypeWelcome,
                    nextSequence++,
                    0,
                    HandshakePayloads.Welcome(ProtocolConstants.ResultSuccess, "Windows PC", "1.0.0", sessionId));
            }

            if (State == ReceiverSessionState.WaitingForStartStream)
            {
                if (header.PacketType == ProtocolConstants.PacketTypeStopStream)
                {
                    State = ReceiverSessionState.Stopped;
                    return null;
                }

                if (header.PacketType != ProtocolConstants.PacketTypeStartStream)
                {
                    throw new PacketParseException("Expected START_STREAM.");
                }

                byte codec = HandshakePayloads.ReadStartStreamCodec(payload);
                if (codec != ProtocolConstants.CodecAacLc)
                {
                    State = ReceiverSessionState.Stopped;
                    return PacketWriter.WritePacket(
                        ProtocolConstants.PacketTypeStreamReady,
                        nextSequence++,
                        0,
                        HandshakePayloads.StreamReady(ProtocolConstants.StreamResultUnsupportedCodec, 0, 0, 0));
                }

                State = ReceiverSessionState.Streaming;
                return PacketWriter.WritePacket(
                    ProtocolConstants.PacketTypeStreamReady,
                    nextSequence++,
                    0,
                    HandshakePayloads.StreamReady(ProtocolConstants.StreamResultSuccess, ProtocolConstants.CodecAacLc, 48000u, 2));
            }

            if (State == ReceiverSessionState.Streaming)
            {
                if (header.PacketType == ProtocolConstants.PacketTypePing)
                {
                    return PacketWriter.WritePacket(ProtocolConstants.PacketTypePong, nextSequence++, header.Timestamp, payload);
                }

                if (header.PacketType == ProtocolConstants.PacketTypeStopStream)
                {
                    State = ReceiverSessionState.Stopped;
                    return null;
                }

                throw new PacketParseException("Expected PING or STOP_STREAM.");
            }

            throw new PacketParseException("Session is stopped.");
        }

        public static byte[] BusyWelcome()
        {
            return PacketWriter.WritePacket(
                ProtocolConstants.PacketTypeWelcome,
                1u,
                0,
                HandshakePayloads.Welcome(ProtocolConstants.ResultReceiverBusy, "Windows PC", "1.0.0", 0));
        }
    }
}
```

- [ ] **Step 4: Run Windows tests**

Run on Windows:

```powershell
dotnet test receiver-windows/OpenAudioLink.sln -c Release
```

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add receiver-windows/src/OpenAudioLink/Receiver receiver-windows/tests/OpenAudioLink.Tests/Receiver
git commit -m "feat: add Windows receiver session state"
```

---

### Task 4: Add Windows TCP Loopback Receiver

**Files:**

- Create: `receiver-windows/src/OpenAudioLink/Receiver/TcpReceiver.cs`
- Create: `receiver-windows/tests/OpenAudioLink.Tests/Receiver/TcpReceiverTests.cs`

- [ ] **Step 1: Write failing TCP loopback tests**

Create `receiver-windows/tests/OpenAudioLink.Tests/Receiver/TcpReceiverTests.cs`:

```csharp
using System;
using System.Net;
using System.Net.Sockets;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenAudioLink;

namespace OpenAudioLink.Tests.Receiver
{
    [TestClass]
    public sealed class TcpReceiverTests
    {
        [TestMethod]
        public void LoopbackClient_CompletesHandshakePingAndStop()
        {
            using (TcpReceiver receiver = TcpReceiver.StartLoopback())
            using (TcpClient client = new TcpClient())
            {
                client.Connect(IPAddress.Loopback, receiver.Port);
                NetworkStream stream = client.GetStream();

                Write(stream, PacketWriter.WritePacket(
                    ProtocolConstants.PacketTypeHello,
                    1u,
                    123456000UL,
                    HandshakePayloads.Hello("Android Phone", "1.0.0", ProtocolConstants.PlatformAndroid, ProtocolConstants.CapabilityAacSupported)));
                Assert.AreEqual(ProtocolConstants.PacketTypeWelcome, PacketParser.ParseHeader(PacketReader.ReadPacket(stream)).PacketType);

                Write(stream, PacketWriter.WritePacket(
                    ProtocolConstants.PacketTypeStartStream,
                    3u,
                    123456002UL,
                    HandshakePayloads.StartStream(ProtocolConstants.CodecAacLc, 48000u, 2, 192000u, 20)));
                Assert.AreEqual(ProtocolConstants.PacketTypeStreamReady, PacketParser.ParseHeader(PacketReader.ReadPacket(stream)).PacketType);

                byte[] pingPayload = HandshakePayloads.Ping(5u, 123456005UL);
                Write(stream, PacketWriter.WritePacket(ProtocolConstants.PacketTypePing, 5u, 123456004UL, pingPayload));
                byte[] pong = PacketReader.ReadPacket(stream);
                Assert.AreEqual(ProtocolConstants.PacketTypePong, PacketParser.ParseHeader(pong).PacketType);
                CollectionAssert.AreEqual(pingPayload, PacketParser.Payload(pong));

                Write(stream, PacketWriter.WritePacket(ProtocolConstants.PacketTypeStopStream, 7u, 123456006UL, new byte[0]));
            }
        }

        [TestMethod]
        public void SecondClient_ReceivesBusyWelcome()
        {
            using (TcpReceiver receiver = TcpReceiver.StartLoopback())
            using (TcpClient first = new TcpClient())
            using (TcpClient second = new TcpClient())
            {
                first.Connect(IPAddress.Loopback, receiver.Port);
                Write(first.GetStream(), PacketWriter.WritePacket(
                    ProtocolConstants.PacketTypeHello,
                    1u,
                    123456000UL,
                    HandshakePayloads.Hello("Android Phone", "1.0.0", ProtocolConstants.PlatformAndroid, ProtocolConstants.CapabilityAacSupported)));
                PacketReader.ReadPacket(first.GetStream());

                second.Connect(IPAddress.Loopback, receiver.Port);
                Write(second.GetStream(), PacketWriter.WritePacket(
                    ProtocolConstants.PacketTypeHello,
                    2u,
                    123456001UL,
                    HandshakePayloads.Hello("Second Phone", "1.0.0", ProtocolConstants.PlatformAndroid, ProtocolConstants.CapabilityAacSupported)));

                byte[] response = PacketReader.ReadPacket(second.GetStream());
                byte[] payload = PacketParser.Payload(response);

                Assert.AreEqual(ProtocolConstants.PacketTypeWelcome, PacketParser.ParseHeader(response).PacketType);
                Assert.AreEqual(ProtocolConstants.ResultReceiverBusy, payload[0]);
            }
        }

        private static void Write(NetworkStream stream, byte[] packet)
        {
            stream.Write(packet, 0, packet.Length);
            stream.Flush();
        }
    }
}
```

- [ ] **Step 2: Run Windows tests and verify failure**

Run on Windows:

```powershell
dotnet test receiver-windows/OpenAudioLink.sln -c Release
```

Expected: FAIL because `TcpReceiver` is not defined.

- [ ] **Step 3: Implement TCP receiver**

Create `receiver-windows/src/OpenAudioLink/Receiver/TcpReceiver.cs`:

```csharp
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace OpenAudioLink
{
    public sealed class TcpReceiver : IDisposable
    {
        private readonly TcpListener listener;
        private readonly CancellationTokenSource stop = new CancellationTokenSource();
        private readonly Task acceptTask;
        private int active;
        private ulong nextSessionId = 1;

        private TcpReceiver(TcpListener listener)
        {
            this.listener = listener;
            Port = ((IPEndPoint)listener.LocalEndpoint).Port;
            acceptTask = Task.Run(() => AcceptLoop());
        }

        public int Port { get; }

        public static TcpReceiver StartLoopback()
        {
            TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            return new TcpReceiver(listener);
        }

        public void Dispose()
        {
            stop.Cancel();
            listener.Stop();
            try
            {
                acceptTask.Wait(TimeSpan.FromSeconds(2));
            }
            catch (AggregateException)
            {
            }
            stop.Dispose();
        }

        private void AcceptLoop()
        {
            while (!stop.IsCancellationRequested)
            {
                try
                {
                    TcpClient client = listener.AcceptTcpClient();
                    Task.Run(() => HandleClient(client));
                }
                catch (SocketException)
                {
                    if (!stop.IsCancellationRequested)
                    {
                        throw;
                    }
                }
            }
        }

        private void HandleClient(TcpClient client)
        {
            using (client)
            {
                NetworkStream stream = client.GetStream();
                if (Interlocked.CompareExchange(ref active, 1, 0) != 0)
                {
                    PacketReader.ReadPacket(stream);
                    Write(stream, ReceiverSession.BusyWelcome());
                    return;
                }

                try
                {
                    ReceiverSession session = new ReceiverSession(nextSessionId++);
                    while (session.State != ReceiverSessionState.Stopped)
                    {
                        byte[] packet = PacketReader.ReadPacket(stream);
                        byte[] response = session.Process(packet);
                        if (response != null)
                        {
                            Write(stream, response);
                        }
                    }
                }
                finally
                {
                    Interlocked.Exchange(ref active, 0);
                }
            }
        }

        private static void Write(NetworkStream stream, byte[] packet)
        {
            stream.Write(packet, 0, packet.Length);
            stream.Flush();
        }
    }
}
```

- [ ] **Step 4: Run Windows tests**

Run on Windows:

```powershell
dotnet test receiver-windows/OpenAudioLink.sln -c Release
```

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add receiver-windows/src/OpenAudioLink/Receiver/TcpReceiver.cs receiver-windows/tests/OpenAudioLink.Tests/Receiver/TcpReceiverTests.cs
git commit -m "feat: add Windows TCP handshake receiver"
```

---

### Task 5: Add Android Protocol Serialization

**Files:**

- Modify: `sender-android/app/src/main/java/com/openaudiolink/protocol/ProtocolConstants.kt`
- Create: `sender-android/app/src/main/java/com/openaudiolink/protocol/PacketWriter.kt`
- Create: `sender-android/app/src/main/java/com/openaudiolink/protocol/HandshakePayloads.kt`
- Create: `sender-android/app/src/main/java/com/openaudiolink/protocol/PacketReader.kt`
- Create: `sender-android/app/src/test/java/com/openaudiolink/protocol/PacketWriterTest.kt`
- Modify: `sender-android/app/src/test/java/com/openaudiolink/protocol/PacketParserTest.kt`

- [ ] **Step 1: Write failing Android exact-byte tests**

Create `sender-android/app/src/test/java/com/openaudiolink/protocol/PacketWriterTest.kt`:

```kotlin
package com.openaudiolink.protocol

import org.junit.Assert.assertArrayEquals
import org.junit.Test
import java.io.File

class PacketWriterTest {
    @Test
    fun writeWelcome_matchesGoldenPacket() {
        val packet = PacketWriter.writePacket(
            ProtocolConstants.PacketTypeWelcome,
            2L,
            123456001L,
            HandshakePayloads.welcome(ProtocolConstants.ResultSuccess, "Windows PC", "1.0.0", 0x0102030405060708L),
        )

        assertArrayEquals(readFixture("valid-welcome.bin"), packet)
    }

    @Test
    fun writeStartStream_matchesGoldenPacket() {
        val packet = PacketWriter.writePacket(
            ProtocolConstants.PacketTypeStartStream,
            3L,
            123456002L,
            HandshakePayloads.startStream(ProtocolConstants.CodecAacLc, 48000L, 2, 192000L, 20),
        )

        assertArrayEquals(readFixture("valid-start-stream.bin"), packet)
    }

    @Test
    fun writeStreamReady_matchesGoldenPacket() {
        val packet = PacketWriter.writePacket(
            ProtocolConstants.PacketTypeStreamReady,
            4L,
            123456003L,
            HandshakePayloads.streamReady(ProtocolConstants.StreamResultSuccess, ProtocolConstants.CodecAacLc, 48000L, 2),
        )

        assertArrayEquals(readFixture("valid-stream-ready.bin"), packet)
    }

    @Test
    fun writePingAndPong_matchGoldenPackets() {
        val payload = HandshakePayloads.ping(5L, 123456005L)

        assertArrayEquals(readFixture("valid-ping.bin"), PacketWriter.writePacket(ProtocolConstants.PacketTypePing, 5L, 123456004L, payload))
        assertArrayEquals(readFixture("valid-pong.bin"), PacketWriter.writePacket(ProtocolConstants.PacketTypePong, 6L, 123456004L, payload))
    }

    @Test
    fun writeStopStream_matchesGoldenPacket() {
        val packet = PacketWriter.writePacket(ProtocolConstants.PacketTypeStopStream, 7L, 123456006L, ByteArray(0))

        assertArrayEquals(readFixture("valid-stop-stream.bin"), packet)
    }

    @Test
    fun writeError_matchesGoldenPacket() {
        val packet = PacketWriter.writePacket(
            ProtocolConstants.PacketTypeError,
            8L,
            123456007L,
            HandshakePayloads.error(ProtocolConstants.ErrorUnsupportedCodec, ProtocolConstants.ErrorSeverityRecoverable, "Unsupported codec"),
        )

        assertArrayEquals(readFixture("valid-error.bin"), packet)
    }

    private fun readFixture(name: String): ByteArray {
        var directory: File? = File(System.getProperty("user.dir")).absoluteFile
        while (directory != null) {
            val candidates = listOf(File(directory, "testdata/protocol/$name"), File(directory, "../testdata/protocol/$name"))
            candidates.firstOrNull { it.isFile }?.let { return it.readBytes() }
            directory = directory.parentFile
        }
        throw java.io.FileNotFoundException("Fixture not found: $name")
    }
}
```

- [ ] **Step 2: Run Android tests and verify failure**

Run on an `x86-64` Android build host:

```bash
cd sender-android
./gradlew :app:testDebugUnitTest
```

Expected: FAIL because `PacketWriter`, `HandshakePayloads`, and new constants are not defined.

- [ ] **Step 3: Extend Android constants**

Replace `sender-android/app/src/main/java/com/openaudiolink/protocol/ProtocolConstants.kt` with:

```kotlin
package com.openaudiolink.protocol

object ProtocolConstants {
    const val HeaderSize = 24
    const val AudioPayloadHeaderSize = 19
    const val MaxPacketSize = 65536
    const val MajorVersion = 1
    const val MinorVersion = 0

    const val PacketTypeHello = 0x01
    const val PacketTypeWelcome = 0x02
    const val PacketTypeStartStream = 0x03
    const val PacketTypeStreamReady = 0x04
    const val PacketTypeAudio = 0x05
    const val PacketTypeStopStream = 0x06
    const val PacketTypePing = 0x07
    const val PacketTypePong = 0x08
    const val PacketTypeError = 0x09

    const val ResultSuccess = 0
    const val ResultUnsupportedProtocol = 1
    const val ResultReceiverBusy = 2
    const val ResultInternalError = 4

    const val StreamResultSuccess = 0
    const val StreamResultUnsupportedCodec = 1
    const val StreamResultUnsupportedFormat = 2
    const val StreamResultReceiverNotReady = 3
    const val StreamResultInternalError = 4

    const val ErrorInvalidPacket = 1001
    const val ErrorUnsupportedProtocol = 1002
    const val ErrorUnsupportedCodec = 1003
    const val ErrorInvalidPayload = 1004
    const val ErrorReceiverBusy = 1005
    const val ErrorTimeout = 1008
    const val ErrorSeverityRecoverable = 2
    const val ErrorSeverityFatal = 3

    const val CodecAacLc = 1
    const val PlatformAndroid = 1
    const val PlatformWindows = 2
    const val CapabilityAacSupported = 1L
    const val DefaultPort = 39888

    val Magic = byteArrayOf('O'.code.toByte(), 'A'.code.toByte(), 'L'.code.toByte(), 'P'.code.toByte())
}
```

- [ ] **Step 4: Add Android packet writer**

Create `sender-android/app/src/main/java/com/openaudiolink/protocol/PacketWriter.kt`:

```kotlin
package com.openaudiolink.protocol

object PacketWriter {
    fun writePacket(packetType: Int, sequenceNumber: Long, timestamp: Long, payload: ByteArray = ByteArray(0)): ByteArray {
        require(payload.size <= ProtocolConstants.MaxPacketSize) { "Payload is too large." }
        val packet = ByteArray(ProtocolConstants.HeaderSize + payload.size)
        ProtocolConstants.Magic.copyInto(packet, 0)
        packet[4] = ProtocolConstants.MajorVersion.toByte()
        packet[5] = ProtocolConstants.MinorVersion.toByte()
        packet[6] = packetType.toByte()
        packet[7] = 0
        writeUInt32(packet, 8, sequenceNumber)
        writeUInt64(packet, 12, timestamp)
        writeUInt32(packet, 20, payload.size.toLong())
        payload.copyInto(packet, ProtocolConstants.HeaderSize)
        return packet
    }

    internal fun writeUInt16(buffer: ByteArray, offset: Int, value: Int) {
        buffer[offset] = (value ushr 8).toByte()
        buffer[offset + 1] = value.toByte()
    }

    internal fun writeUInt32(buffer: ByteArray, offset: Int, value: Long) {
        buffer[offset] = (value ushr 24).toByte()
        buffer[offset + 1] = (value ushr 16).toByte()
        buffer[offset + 2] = (value ushr 8).toByte()
        buffer[offset + 3] = value.toByte()
    }

    internal fun writeUInt64(buffer: ByteArray, offset: Int, value: Long) {
        writeUInt32(buffer, offset, value ushr 32)
        writeUInt32(buffer, offset + 4, value)
    }
}
```

- [ ] **Step 5: Add Android handshake payload helpers**

Create `sender-android/app/src/main/java/com/openaudiolink/protocol/HandshakePayloads.kt`:

```kotlin
package com.openaudiolink.protocol

object HandshakePayloads {
    fun hello(senderName: String, senderVersion: String, platform: Int, capabilities: Long): ByteArray = concat(
        writeString(senderName),
        writeString(senderVersion),
        byteArrayOf(ProtocolConstants.MajorVersion.toByte(), ProtocolConstants.MinorVersion.toByte(), platform.toByte()),
        writeUInt32(capabilities),
    )

    fun welcome(result: Int, receiverName: String, receiverVersion: String, sessionId: Long): ByteArray =
        concat(byteArrayOf(result.toByte()), writeString(receiverName), writeString(receiverVersion), writeUInt64(sessionId))

    fun startStream(codec: Int, sampleRate: Long, channels: Int, bitrate: Long, frameDuration: Int): ByteArray =
        concat(byteArrayOf(codec.toByte()), writeUInt32(sampleRate), byteArrayOf(channels.toByte()), writeUInt32(bitrate), writeUInt16(frameDuration))

    fun streamReady(result: Int, codec: Int, sampleRate: Long, channels: Int): ByteArray =
        concat(byteArrayOf(result.toByte(), codec.toByte()), writeUInt32(sampleRate), byteArrayOf(channels.toByte()))

    fun ping(sequence: Long, timestamp: Long): ByteArray = concat(writeUInt32(sequence), writeUInt64(timestamp))

    fun error(code: Int, severity: Int, description: String): ByteArray = concat(writeUInt16(code), byteArrayOf(severity.toByte()), writeString(description))

    private fun writeString(value: String): ByteArray {
        val data = value.toByteArray(Charsets.UTF_8)
        require(data.size <= 0xffff) { "String is too long." }
        return concat(writeUInt16(data.size), data)
    }

    private fun writeUInt16(value: Int): ByteArray = ByteArray(2).also { PacketWriter.writeUInt16(it, 0, value) }
    private fun writeUInt32(value: Long): ByteArray = ByteArray(4).also { PacketWriter.writeUInt32(it, 0, value) }
    private fun writeUInt64(value: Long): ByteArray = ByteArray(8).also { PacketWriter.writeUInt64(it, 0, value) }

    private fun concat(vararg parts: ByteArray): ByteArray {
        val output = ByteArray(parts.sumOf { it.size })
        var offset = 0
        for (part in parts) {
            part.copyInto(output, offset)
            offset += part.size
        }
        return output
    }
}
```

- [ ] **Step 6: Add Android packet stream reader**

Create `sender-android/app/src/main/java/com/openaudiolink/protocol/PacketReader.kt`:

```kotlin
package com.openaudiolink.protocol

import java.io.EOFException
import java.io.InputStream

object PacketReader {
    fun readPacket(input: InputStream): ByteArray {
        val header = readExact(input, ProtocolConstants.HeaderSize)
        val payloadLength = PacketParser.readUInt32(header, 20).toInt()
        if (payloadLength > ProtocolConstants.MaxPacketSize) throw PacketParseException("Payload is too large.")
        val payload = readExact(input, payloadLength)
        val packet = header + payload
        PacketParser.parseHeader(packet)
        return packet
    }

    private fun readExact(input: InputStream, length: Int): ByteArray {
        val buffer = ByteArray(length)
        var offset = 0
        while (offset < length) {
            val read = input.read(buffer, offset, length - offset)
            if (read < 0) throw EOFException("Connection closed while reading packet.")
            offset += read
        }
        return buffer
    }
}
```

- [ ] **Step 7: Extend Android parser fixture tests for new packet types**

Append to `sender-android/app/src/test/java/com/openaudiolink/protocol/PacketParserTest.kt` before `readFixture`:

```kotlin
    @Test
    fun parseHeader_phase1aFixtures_returnExpectedTypes() {
        assertEquals(ProtocolConstants.PacketTypeWelcome, PacketParser.parseHeader(readFixture("valid-welcome.bin")).packetType)
        assertEquals(ProtocolConstants.PacketTypeStartStream, PacketParser.parseHeader(readFixture("valid-start-stream.bin")).packetType)
        assertEquals(ProtocolConstants.PacketTypeStreamReady, PacketParser.parseHeader(readFixture("valid-stream-ready.bin")).packetType)
        assertEquals(ProtocolConstants.PacketTypePing, PacketParser.parseHeader(readFixture("valid-ping.bin")).packetType)
        assertEquals(ProtocolConstants.PacketTypePong, PacketParser.parseHeader(readFixture("valid-pong.bin")).packetType)
        assertEquals(ProtocolConstants.PacketTypeStopStream, PacketParser.parseHeader(readFixture("valid-stop-stream.bin")).packetType)
        assertEquals(ProtocolConstants.PacketTypeError, PacketParser.parseHeader(readFixture("valid-error.bin")).packetType)
    }
```

- [ ] **Step 8: Run Android tests**

Run on an `x86-64` Android build host:

```bash
cd sender-android
./gradlew :app:testDebugUnitTest
```

Expected: PASS.

- [ ] **Step 9: Commit**

```bash
git add sender-android/app/src/main/java/com/openaudiolink/protocol sender-android/app/src/test/java/com/openaudiolink/protocol
git commit -m "feat: add Android protocol handshake serialization"
```

---

### Task 6: Add Android Handshake Client Skeleton

**Files:**

- Modify: `sender-android/app/src/main/AndroidManifest.xml`
- Create: `sender-android/app/src/main/java/com/openaudiolink/network/HandshakeClient.kt`
- Create: `sender-android/app/src/main/java/com/openaudiolink/network/TcpHandshakeClient.kt`
- Create: `sender-android/app/src/test/java/com/openaudiolink/network/HandshakeClientTest.kt`

- [ ] **Step 1: Write failing Android client tests**

Create `sender-android/app/src/test/java/com/openaudiolink/network/HandshakeClientTest.kt`:

```kotlin
package com.openaudiolink.network

import com.openaudiolink.protocol.HandshakePayloads
import com.openaudiolink.protocol.PacketParser
import com.openaudiolink.protocol.PacketReader
import com.openaudiolink.protocol.PacketWriter
import com.openaudiolink.protocol.ProtocolConstants
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import java.io.ByteArrayInputStream
import java.io.ByteArrayOutputStream

class HandshakeClientTest {
    @Test
    fun run_successfulHandshake_writesHelloStartPingStop() {
        val responses = ByteArrayOutputStream().apply {
            write(PacketWriter.writePacket(ProtocolConstants.PacketTypeWelcome, 2L, 123456001L, HandshakePayloads.welcome(ProtocolConstants.ResultSuccess, "Windows PC", "1.0.0", 1L)))
            write(PacketWriter.writePacket(ProtocolConstants.PacketTypeStreamReady, 4L, 123456003L, HandshakePayloads.streamReady(ProtocolConstants.StreamResultSuccess, ProtocolConstants.CodecAacLc, 48000L, 2)))
            val pingPayload = HandshakePayloads.ping(5L, 123456005L)
            write(PacketWriter.writePacket(ProtocolConstants.PacketTypePong, 6L, 123456004L, pingPayload))
        }.toByteArray()
        val output = ByteArrayOutputStream()

        val result = HandshakeClient().run(ByteArrayInputStream(responses), output)

        assertTrue(result)
        val sent = ByteArrayInputStream(output.toByteArray())
        assertEquals(ProtocolConstants.PacketTypeHello, PacketParser.parseHeader(PacketReader.readPacket(sent)).packetType)
        assertEquals(ProtocolConstants.PacketTypeStartStream, PacketParser.parseHeader(PacketReader.readPacket(sent)).packetType)
        assertEquals(ProtocolConstants.PacketTypePing, PacketParser.parseHeader(PacketReader.readPacket(sent)).packetType)
        assertEquals(ProtocolConstants.PacketTypeStopStream, PacketParser.parseHeader(PacketReader.readPacket(sent)).packetType)
    }

    @Test
    fun run_busyWelcome_returnsFalse() {
        val responses = PacketWriter.writePacket(
            ProtocolConstants.PacketTypeWelcome,
            2L,
            123456001L,
            HandshakePayloads.welcome(ProtocolConstants.ResultReceiverBusy, "Windows PC", "1.0.0", 0L),
        )

        val result = HandshakeClient().run(ByteArrayInputStream(responses), ByteArrayOutputStream())

        assertEquals(false, result)
    }
}
```

- [ ] **Step 2: Run Android tests and verify failure**

Run on an `x86-64` Android build host:

```bash
cd sender-android
./gradlew :app:testDebugUnitTest
```

Expected: FAIL because `HandshakeClient` is not defined.

- [ ] **Step 3: Add Android network permission**

Modify `sender-android/app/src/main/AndroidManifest.xml` so the top of the file is:

```xml
<manifest xmlns:android="http://schemas.android.com/apk/res/android">
    <uses-permission android:name="android.permission.INTERNET" />

    <application
```

- [ ] **Step 4: Implement stream-based handshake client**

Create `sender-android/app/src/main/java/com/openaudiolink/network/HandshakeClient.kt`:

```kotlin
package com.openaudiolink.network

import com.openaudiolink.protocol.HandshakePayloads
import com.openaudiolink.protocol.PacketParser
import com.openaudiolink.protocol.PacketReader
import com.openaudiolink.protocol.PacketWriter
import com.openaudiolink.protocol.ProtocolConstants
import java.io.InputStream
import java.io.OutputStream

class HandshakeClient {
    fun run(input: InputStream, output: OutputStream): Boolean {
        var sequence = 1L
        write(output, PacketWriter.writePacket(
            ProtocolConstants.PacketTypeHello,
            sequence++,
            123456000L,
            HandshakePayloads.hello("Android Phone", "1.0.0", ProtocolConstants.PlatformAndroid, ProtocolConstants.CapabilityAacSupported),
        ))

        val welcomePayload = PacketParser.payload(PacketReader.readPacket(input))
        if (welcomePayload.firstOrNull()?.toInt() != ProtocolConstants.ResultSuccess) return false

        write(output, PacketWriter.writePacket(
            ProtocolConstants.PacketTypeStartStream,
            sequence++,
            123456002L,
            HandshakePayloads.startStream(ProtocolConstants.CodecAacLc, 48000L, 2, 192000L, 20),
        ))

        val streamReadyPayload = PacketParser.payload(PacketReader.readPacket(input))
        if (streamReadyPayload.firstOrNull()?.toInt() != ProtocolConstants.StreamResultSuccess) return false

        val pingPayload = HandshakePayloads.ping(5L, 123456005L)
        write(output, PacketWriter.writePacket(ProtocolConstants.PacketTypePing, sequence++, 123456004L, pingPayload))
        val pong = PacketReader.readPacket(input)
        if (PacketParser.parseHeader(pong).packetType != ProtocolConstants.PacketTypePong) return false
        if (!PacketParser.payload(pong).contentEquals(pingPayload)) return false

        write(output, PacketWriter.writePacket(ProtocolConstants.PacketTypeStopStream, sequence, 123456006L, ByteArray(0)))
        return true
    }

    private fun write(output: OutputStream, packet: ByteArray) {
        output.write(packet)
        output.flush()
    }
}
```

- [ ] **Step 5: Implement TCP wrapper**

Create `sender-android/app/src/main/java/com/openaudiolink/network/TcpHandshakeClient.kt`:

```kotlin
package com.openaudiolink.network

import com.openaudiolink.protocol.ProtocolConstants
import java.net.InetSocketAddress
import java.net.Socket

class TcpHandshakeClient {
    fun connect(host: String, port: Int = ProtocolConstants.DefaultPort): Boolean {
        Socket().use { socket ->
            socket.connect(InetSocketAddress(host, port), 10_000)
            socket.soTimeout = 15_000
            return HandshakeClient().run(socket.getInputStream(), socket.getOutputStream())
        }
    }
}
```

- [ ] **Step 6: Run Android tests**

Run on an `x86-64` Android build host:

```bash
cd sender-android
./gradlew :app:testDebugUnitTest
```

Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add sender-android/app/src/main/AndroidManifest.xml sender-android/app/src/main/java/com/openaudiolink/network sender-android/app/src/test/java/com/openaudiolink/network
git commit -m "feat: add Android TCP handshake client"
```

---

### Task 7: Final Verification and CI Follow-up

**Files:**

- Read: `.github/workflows/docs.yml`
- Read: `.github/workflows/windows.yml`
- Read: `.github/workflows/android.yml`

- [ ] **Step 1: Run local checks**

Run:

```bash
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

- [ ] **Step 2: Run Windows tests on Windows**

Run on Windows:

```powershell
dotnet test receiver-windows/OpenAudioLink.sln -c Release
```

Expected: PASS.

- [ ] **Step 3: Run Android tests on x86-64 Android build host**

Run:

```bash
cd sender-android
./gradlew :app:testDebugUnitTest
```

Expected: PASS.

- [ ] **Step 4: Check Gitea Actions status**

Run from this repository:

```bash
python3 - <<'PY'
import json, urllib.request
url='http://192.168.3.20/api/v1/repos/hashqq/OpenAudioLink/actions/runs'
data=json.load(urllib.request.urlopen(url, timeout=20))
for r in data.get('workflow_runs', []):
    print(f"id={r.get('id')} path={r.get('path')} status={r.get('status')} conclusion={r.get('conclusion')} sha={r.get('head_sha','')[:7]}")
PY
```

Expected: runs for pushed commits are either `success` or still `queued` with runner labels visible in job details.

- [ ] **Step 5: Commit verification notes only if a workflow file changed**

No commit is needed when only commands were run. If a workflow file is changed to fix a verified CI failure, commit that workflow-only change:

```bash
git add .github/workflows
git commit -m "ci: fix phase 1a validation workflow"
```

---

## Self-Review Checklist

- Spec coverage:
  - Golden bytes for every Phase 1-A packet: Task 1, Task 2, Task 5.
  - Windows one-sender receiver and busy rejection: Task 3, Task 4.
  - Android explicit TCP client: Task 6.
  - `PING -> PONG` identical payload: Task 3, Task 4, Task 6.
  - `STOP_STREAM` release: Task 3, Task 4, Task 6.
- Type consistency:
  - Packet type names match `ProtocolConstants` in C# and Kotlin.
  - Result names match `docs/03-Protocol.md` and the Phase 1-A design spec.
  - Fixture names match `tools/protocol/generate_golden_packets.py` output.
- Verification limits:
  - Linux local checks are always runnable.
  - Windows and Android platform tests require supported hosts.
