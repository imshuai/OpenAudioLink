# Phase 1-B Fake Audio Transport Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Send deterministic fake `AUDIO` packets over the existing Phase 1-A TCP handshake and validate them on the Windows receiver.

**Architecture:** Reuse the existing packet writer/parser and `AudioPayloadValidator`. Add one `AUDIO` payload helper to the existing `HandshakePayloads` helpers on each platform, record accepted audio frames in `ReceiverSession`, and insert one fake `AUDIO` packet into the Android handshake client after `STREAM_READY`.

**Tech Stack:** C#/.NET Framework 4.8 MSTest, Kotlin/JUnit4 Android unit tests, Python golden packet generator, OpenAudioLink Version 1 TCP protocol.

---

## Reference Spec

Implement exactly this design:

- `docs/superpowers/specs/2026-07-09-phase-1b-fake-audio-transport-design.md`

Phase 1-B must not add real capture, encoding, decoding, playback, discovery, foreground service, or UI work.

## Known Local Verification Limits

- Current Linux host has no `dotnet`; Windows tests run on Windows or GitHub Actions `windows-latest`.
- Current Linux host is `aarch64`; Android Gradle resource processing uses an `x86-64` `aapt2` binary. Android tests run on an `x86-64` Android build host or GitHub Actions `ubuntu-latest`.
- Local Linux can run docs checks, golden packet checks, and whitespace checks.

---

## File Structure

Modify existing files:

```text
receiver-windows/src/OpenAudioLink/Protocol/HandshakePayloads.cs
receiver-windows/tests/OpenAudioLink.Tests/Protocol/PacketWriterTests.cs
receiver-windows/tests/OpenAudioLink.Tests/Protocol/PacketParserTests.cs
receiver-windows/src/OpenAudioLink/Receiver/ReceiverSession.cs
receiver-windows/tests/OpenAudioLink.Tests/Receiver/ReceiverSessionTests.cs
receiver-windows/tests/OpenAudioLink.Tests/Receiver/TcpReceiverTests.cs
sender-android/app/src/main/java/com/openaudiolink/protocol/HandshakePayloads.kt
sender-android/app/src/test/java/com/openaudiolink/protocol/PacketWriterTest.kt
sender-android/app/src/test/java/com/openaudiolink/protocol/PacketParserTest.kt
sender-android/app/src/main/java/com/openaudiolink/network/HandshakeClient.kt
sender-android/app/src/test/java/com/openaudiolink/network/HandshakeClientTest.kt
```

Do not create new production files for Phase 1-B. The existing helper files are enough.

---

## Task 1: Add Cross-Platform AUDIO Payload Builders

**Files:**

- Modify: `receiver-windows/src/OpenAudioLink/Protocol/HandshakePayloads.cs`
- Modify: `receiver-windows/tests/OpenAudioLink.Tests/Protocol/PacketWriterTests.cs`
- Modify: `sender-android/app/src/main/java/com/openaudiolink/protocol/HandshakePayloads.kt`
- Modify: `sender-android/app/src/test/java/com/openaudiolink/protocol/PacketWriterTest.kt`

- [ ] **Step 1: Add failing C# exact-byte test for AUDIO**

Append this test to `receiver-windows/tests/OpenAudioLink.Tests/Protocol/PacketWriterTests.cs` after `WriteStreamReady_MatchesGoldenPacket`:

```csharp
        [TestMethod]
        public void WriteAudio_MatchesGoldenPacket()
        {
            byte[] packet = PacketWriter.WritePacket(
                ProtocolConstants.PacketTypeAudio,
                2u,
                123456789UL,
                HandshakePayloads.Audio(
                    ProtocolConstants.CodecAacLc,
                    1u,
                    123456789UL,
                    20,
                    new byte[] { 0x11, 0x22, 0x33, 0x44 }));

            CollectionAssert.AreEqual(ReadFixture("valid-audio-aac.bin"), packet);
        }
```

- [ ] **Step 2: Add failing Android exact-byte test for AUDIO**

Append this one-line test to `sender-android/app/src/test/java/com/openaudiolink/protocol/PacketWriterTest.kt` after `write_streamReady_matchesFixture`:

```kotlin
    @Test fun write_audio_matchesFixture() = assertPacket("valid-audio-aac.bin", ProtocolConstants.PacketTypeAudio, 2, 123456789, HandshakePayloads.audio(ProtocolConstants.CodecAacLc, 1, 123456789, 20, byteArrayOf(0x11, 0x22, 0x33, 0x44)))
```

- [ ] **Step 3: Run targeted tests to verify they fail on supported hosts**

On Windows:

```powershell
dotnet test receiver-windows/OpenAudioLink.sln -c Release --filter PacketWriterTests
```

Expected: FAIL because `HandshakePayloads.Audio` does not exist.

On an `x86-64` Android build host:

```bash
cd sender-android
./gradlew :app:testDebugUnitTest --tests com.openaudiolink.protocol.PacketWriterTest
```

Expected: FAIL because `HandshakePayloads.audio` does not exist.

- [ ] **Step 4: Implement C# AUDIO payload builder**

Add this method to `receiver-windows/src/OpenAudioLink/Protocol/HandshakePayloads.cs` after `StreamReady`:

```csharp
        public static byte[] Audio(byte codec, uint frameNumber, ulong captureTimestamp, ushort frameDuration, byte[] encodedData)
        {
            encodedData = encodedData ?? new byte[0];
            return Join(
                new[] { codec },
                WriteUInt32(frameNumber),
                WriteUInt64(captureTimestamp),
                WriteUInt16(frameDuration),
                WriteUInt32((uint)encodedData.Length),
                encodedData);
        }
```

- [ ] **Step 5: Implement Android AUDIO payload builder**

Modify `sender-android/app/src/main/java/com/openaudiolink/protocol/HandshakePayloads.kt`.

Add this function after `streamReady`:

```kotlin
    fun audio(codec: Int, frameNumber: Long, captureTimestamp: Long, frameDurationMs: Int, encodedData: ByteArray): ByteArray = bytes {
        byte(codec); uint32(frameNumber); uint64(captureTimestamp); uint16(frameDurationMs); uint32(encodedData.size.toLong()); data(encodedData)
    }
```

Add this helper method inside `private class Writer`, after `uint64`:

```kotlin
        fun data(value: ByteArray) = out.write(value)
```

- [ ] **Step 6: Run targeted tests to verify they pass on supported hosts**

On Windows:

```powershell
dotnet test receiver-windows/OpenAudioLink.sln -c Release --filter PacketWriterTests
```

Expected: PASS.

On an `x86-64` Android build host:

```bash
cd sender-android
./gradlew :app:testDebugUnitTest --tests com.openaudiolink.protocol.PacketWriterTest
```

Expected: PASS.

- [ ] **Step 7: Commit Task 1**

```bash
git add receiver-windows/src/OpenAudioLink/Protocol/HandshakePayloads.cs \
        receiver-windows/tests/OpenAudioLink.Tests/Protocol/PacketWriterTests.cs \
        sender-android/app/src/main/java/com/openaudiolink/protocol/HandshakePayloads.kt \
        sender-android/app/src/test/java/com/openaudiolink/protocol/PacketWriterTest.kt
git commit -m "feat: add fake audio payload builders"
```

---

## Task 2: Add AUDIO Validator Edge Tests

**Files:**

- Modify: `receiver-windows/tests/OpenAudioLink.Tests/Protocol/PacketParserTests.cs`
- Modify: `sender-android/app/src/test/java/com/openaudiolink/protocol/PacketParserTest.kt`

- [ ] **Step 1: Add C# validator edge tests**

Append these tests to `receiver-windows/tests/OpenAudioLink.Tests/Protocol/PacketParserTests.cs` after `ValidateAacPayload_ValidAudioPayload_DoesNotThrow`:

```csharp
        [TestMethod]
        public void ValidateAacPayload_TooShort_Throws()
        {
            Assert.ThrowsException<PacketParseException>(() => AudioPayloadValidator.ValidateAacPayload(new byte[ProtocolConstants.AudioPayloadHeaderSize - 1]));
        }

        [TestMethod]
        public void ValidateAacPayload_UnsupportedCodec_Throws()
        {
            byte[] payload = ValidAudioPayload();
            payload[0] = ProtocolConstants.CodecOpus;

            Assert.ThrowsException<PacketParseException>(() => AudioPayloadValidator.ValidateAacPayload(payload));
        }

        [TestMethod]
        public void ValidateAacPayload_EncodedSizeMismatch_Throws()
        {
            byte[] payload = ValidAudioPayload();
            payload[18] = 0x05;

            Assert.ThrowsException<PacketParseException>(() => AudioPayloadValidator.ValidateAacPayload(payload));
        }
```

Add this helper before `AssertFixture`:

```csharp
        private static byte[] ValidAudioPayload()
        {
            return PacketParser.Payload(ReadFixture("valid-audio-aac.bin"));
        }
```

- [ ] **Step 2: Add Android validator edge tests**

Append these tests to `sender-android/app/src/test/java/com/openaudiolink/protocol/PacketParserTest.kt` after `validateAacPayload_validAudioPayload_doesNotThrow`:

```kotlin
    @Test
    fun validateAacPayload_tooShort_throws() {
        assertThrows(PacketParseException::class.java) {
            AudioPayloadValidator.validateAacPayload(ByteArray(ProtocolConstants.AudioPayloadHeaderSize - 1))
        }
    }

    @Test
    fun validateAacPayload_unsupportedCodec_throws() {
        val payload = validAudioPayload()
        payload[0] = ProtocolConstants.CodecOpus.toByte()

        assertThrows(PacketParseException::class.java) {
            AudioPayloadValidator.validateAacPayload(payload)
        }
    }

    @Test
    fun validateAacPayload_encodedSizeMismatch_throws() {
        val payload = validAudioPayload()
        payload[18] = 0x05

        assertThrows(PacketParseException::class.java) {
            AudioPayloadValidator.validateAacPayload(payload)
        }
    }
```

Add this helper before `hex`:

```kotlin
    private fun validAudioPayload(): ByteArray = PacketParser.payload(readFixture("valid-audio-aac.bin"))
```

- [ ] **Step 3: Run targeted validator tests on supported hosts**

On Windows:

```powershell
dotnet test receiver-windows/OpenAudioLink.sln -c Release --filter PacketParserTests
```

Expected: PASS.

On an `x86-64` Android build host:

```bash
cd sender-android
./gradlew :app:testDebugUnitTest --tests com.openaudiolink.protocol.PacketParserTest
```

Expected: PASS.

- [ ] **Step 4: Commit Task 2**

```bash
git add receiver-windows/tests/OpenAudioLink.Tests/Protocol/PacketParserTests.cs \
        sender-android/app/src/test/java/com/openaudiolink/protocol/PacketParserTest.kt
git commit -m "test: cover fake audio payload validation"
```

---

## Task 3: Validate and Record AUDIO in Windows ReceiverSession

**Files:**

- Modify: `receiver-windows/src/OpenAudioLink/Receiver/ReceiverSession.cs`
- Modify: `receiver-windows/tests/OpenAudioLink.Tests/Receiver/ReceiverSessionTests.cs`

- [ ] **Step 1: Replace the existing streaming AUDIO test**

In `receiver-windows/tests/OpenAudioLink.Tests/Receiver/ReceiverSessionTests.cs`, replace `ProcessAudioWhileStreaming_ReturnsNullAndStaysStreaming` with:

```csharp
        [TestMethod]
        public void ProcessAudioWhileStreaming_RecordsFrameAndReturnsNull()
        {
            ReceiverSession session = StreamingSession();
            byte[] payload = ValidAudioPayload();

            byte[] response = session.Process(PacketWriter.WritePacket(ProtocolConstants.PacketTypeAudio, 5u, 123456789UL, payload));

            Assert.IsNull(response);
            Assert.AreEqual(ReceiverSessionState.Streaming, session.State);
            Assert.AreEqual(1, session.AudioFramesReceived);
            CollectionAssert.AreEqual(payload, session.LastAudioPayload);
        }
```

- [ ] **Step 2: Add AUDIO order and invalid payload tests**

Append these tests after `ProcessAudioWhileStreaming_RecordsFrameAndReturnsNull`:

```csharp
        [TestMethod]
        public void ProcessAudioBeforeStartStream_Throws()
        {
            ReceiverSession session = ReadySession();
            byte[] audio = PacketWriter.WritePacket(ProtocolConstants.PacketTypeAudio, 5u, 123456789UL, ValidAudioPayload());

            Assert.ThrowsException<PacketParseException>(() => session.Process(audio));
        }

        [TestMethod]
        public void ProcessInvalidAudioWhileStreaming_Throws()
        {
            ReceiverSession session = StreamingSession();
            byte[] audio = PacketWriter.WritePacket(ProtocolConstants.PacketTypeAudio, 5u, 123456789UL, new byte[] { ProtocolConstants.CodecAacLc });

            Assert.ThrowsException<PacketParseException>(() => session.Process(audio));
            Assert.AreEqual(0, session.AudioFramesReceived);
        }
```

Add this helper before `ReadySession`:

```csharp
        private static byte[] ValidAudioPayload()
        {
            return HandshakePayloads.Audio(
                ProtocolConstants.CodecAacLc,
                1u,
                123456789UL,
                20,
                new byte[] { 0x11, 0x22, 0x33, 0x44 });
        }
```

- [ ] **Step 3: Run receiver session tests to verify failure**

On Windows:

```powershell
dotnet test receiver-windows/OpenAudioLink.sln -c Release --filter ReceiverSessionTests
```

Expected: FAIL because `AudioFramesReceived` and `LastAudioPayload` do not exist, and invalid `AUDIO` is not validated.

- [ ] **Step 4: Implement receiver session audio recording**

Modify `receiver-windows/src/OpenAudioLink/Receiver/ReceiverSession.cs`.

Add these properties after `State`:

```csharp
        public int AudioFramesReceived { get; private set; }

        public byte[] LastAudioPayload { get; private set; }
```

Replace the `PacketTypeAudio` branch in `ProcessStreaming` with:

```csharp
            if (header.PacketType == ProtocolConstants.PacketTypeAudio)
            {
                AudioPayloadValidator.ValidateAacPayload(payload);
                AudioFramesReceived++;
                LastAudioPayload = (byte[])payload.Clone();
                return null;
            }
```

- [ ] **Step 5: Run receiver session tests to verify pass**

On Windows:

```powershell
dotnet test receiver-windows/OpenAudioLink.sln -c Release --filter ReceiverSessionTests
```

Expected: PASS.

- [ ] **Step 6: Commit Task 3**

```bash
git add receiver-windows/src/OpenAudioLink/Receiver/ReceiverSession.cs \
        receiver-windows/tests/OpenAudioLink.Tests/Receiver/ReceiverSessionTests.cs
git commit -m "feat: record validated audio frames in receiver session"
```

---

## Task 4: Prove AUDIO Survives Windows TCP Loopback

**Files:**

- Modify: `receiver-windows/tests/OpenAudioLink.Tests/Receiver/TcpReceiverTests.cs`

- [ ] **Step 1: Insert one valid AUDIO packet into the successful loopback test**

In `ClientCompletesPhase1aHandshake`, insert this block after the `STREAM_READY` assertion and before the existing `PING` block:

```csharp
                Write(stream, ProtocolConstants.PacketTypeAudio, 3u, HandshakePayloads.Audio(
                    ProtocolConstants.CodecAacLc,
                    1u,
                    123456789UL,
                    20,
                    new byte[] { 0x11, 0x22, 0x33, 0x44 }));
```

Then change the existing `PING` and `STOP_STREAM` sequence numbers in this test:

```csharp
                byte[] ping = HandshakePayloads.Ping(4u, 123UL);
                Write(stream, ProtocolConstants.PacketTypePing, 4u, ping);
                AssertPacket(stream, ProtocolConstants.PacketTypePong, ping);

                Write(stream, ProtocolConstants.PacketTypeStopStream, 5u, new byte[0]);
```

- [ ] **Step 2: Run TCP receiver tests on Windows**

```powershell
dotnet test receiver-windows/OpenAudioLink.sln -c Release --filter TcpReceiverTests
```

Expected: PASS. If it fails after the `AUDIO` write, the receiver is closing the connection instead of accepting valid audio while streaming.

- [ ] **Step 3: Commit Task 4**

```bash
git add receiver-windows/tests/OpenAudioLink.Tests/Receiver/TcpReceiverTests.cs
git commit -m "test: cover audio packet in TCP loopback"
```

---

## Task 5: Send One Fake AUDIO Packet from Android HandshakeClient

**Files:**

- Modify: `sender-android/app/src/main/java/com/openaudiolink/network/HandshakeClient.kt`
- Modify: `sender-android/app/src/test/java/com/openaudiolink/network/HandshakeClientTest.kt`

- [ ] **Step 1: Update Android success test to expect AUDIO**

In `sender-android/app/src/test/java/com/openaudiolink/network/HandshakeClientTest.kt`, replace the packet assertions in `runWritesHandshakePacketsOnSuccess` with:

```kotlin
        val written = ByteArrayInputStream(output.toByteArray())
        assertEquals(ProtocolConstants.PacketTypeHello, PacketParser.parseHeader(PacketReader.readPacket(written)).packetType)
        assertEquals(ProtocolConstants.PacketTypeStartStream, PacketParser.parseHeader(PacketReader.readPacket(written)).packetType)
        val audio = PacketReader.readPacket(written)
        assertEquals(ProtocolConstants.PacketTypeAudio, PacketParser.parseHeader(audio).packetType)
        assertArrayEquals(
            HandshakePayloads.audio(ProtocolConstants.CodecAacLc, 1, 123456789, 20, byteArrayOf(0x11, 0x22, 0x33, 0x44)),
            PacketParser.payload(audio)
        )
        assertEquals(ProtocolConstants.PacketTypePing, PacketParser.parseHeader(PacketReader.readPacket(written)).packetType)
        assertEquals(ProtocolConstants.PacketTypeStopStream, PacketParser.parseHeader(PacketReader.readPacket(written)).packetType)
```

`org.junit.Assert.*` is already imported, so `assertArrayEquals` is available.

- [ ] **Step 2: Run Android handshake client test to verify failure**

On an `x86-64` Android build host:

```bash
cd sender-android
./gradlew :app:testDebugUnitTest --tests com.openaudiolink.network.HandshakeClientTest
```

Expected: FAIL because `HandshakeClient` has not written an `AUDIO` packet.

- [ ] **Step 3: Add fake audio payload to HandshakeClient**

In `sender-android/app/src/main/java/com/openaudiolink/network/HandshakeClient.kt`, add this property after `pingPayload`:

```kotlin
    private val fakeAudioPayload = HandshakePayloads.audio(ProtocolConstants.CodecAacLc, 1, 123456789, 20, byteArrayOf(0x11, 0x22, 0x33, 0x44))
```

Insert this block after the `STREAM_READY` success check and before the `PING` write:

```kotlin
            output.write(PacketWriter.writePacket(ProtocolConstants.PacketTypeAudio, 3, 123456789, fakeAudioPayload))
            output.flush()
```

Change the `PING` and `STOP_STREAM` sequence numbers in `run`:

```kotlin
            output.write(PacketWriter.writePacket(ProtocolConstants.PacketTypePing, 4, 123456004, pingPayload))
```

```kotlin
            output.write(PacketWriter.writePacket(ProtocolConstants.PacketTypeStopStream, 5, 123456006))
```

- [ ] **Step 4: Run Android handshake client test to verify pass**

On an `x86-64` Android build host:

```bash
cd sender-android
./gradlew :app:testDebugUnitTest --tests com.openaudiolink.network.HandshakeClientTest
```

Expected: PASS.

- [ ] **Step 5: Commit Task 5**

```bash
git add sender-android/app/src/main/java/com/openaudiolink/network/HandshakeClient.kt \
        sender-android/app/src/test/java/com/openaudiolink/network/HandshakeClientTest.kt
git commit -m "feat: send fake audio frame from Android handshake client"
```

---

## Task 6: Final Verification and CI

**Files:**

- No source files should change unless a verified CI failure requires a workflow-only fix.

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

- [ ] **Step 2: Run Windows tests on Windows or GitHub Actions**

```powershell
dotnet test receiver-windows/OpenAudioLink.sln -c Release
```

Expected: PASS.

- [ ] **Step 3: Run Android tests on an x86-64 Android build host or GitHub Actions**

```bash
cd sender-android
./gradlew :app:testDebugUnitTest
```

Expected: PASS.

- [ ] **Step 4: Push branch and create CI PR**

```bash
git push -u origin phase-1b-fake-audio-transport
```

If GitHub is mirror-only for CI, create the GitHub PR from the mirrored branch to run GitHub Actions. Do not merge the GitHub PR if Gitea remains the source of truth; merge the branch into Gitea `main` after CI passes.

- [ ] **Step 5: Commit workflow fixes only if CI proves they are needed**

If GitHub Actions fails because of workflow configuration, make the smallest workflow-only change, then commit:

```bash
git add .github/workflows
git commit -m "ci: fix phase 1b validation workflow"
```

---

## Self-Review Checklist

- Spec coverage:
  - Fake `AUDIO` payload builder: Task 1.
  - Validator edge cases: Task 2.
  - Windows streaming acceptance and recording: Task 3.
  - Windows TCP loopback with audio before ping/stop: Task 4.
  - Android fake audio send path: Task 5.
  - Final checks and CI: Task 6.
- Scope control:
  - No real capture.
  - No AAC encoding.
  - No AAC decoding.
  - No PCM playback.
  - No discovery.
  - No foreground service.
  - No UI changes.
- Type consistency:
  - C# helper name: `HandshakePayloads.Audio`.
  - Kotlin helper name: `HandshakePayloads.audio`.
  - Fake frame values match `valid-audio-aac.bin`.
  - AUDIO sequence is inserted before PING in Android and TCP tests.
