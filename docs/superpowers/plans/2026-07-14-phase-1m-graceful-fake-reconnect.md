# Phase 1-M Graceful Fake Reconnect Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make Android report fake-stream success only after Windows closes the stopped session, then prove the same receiver accepts a second manual fake stream.

**Architecture:** Keep the existing protocol and receiver lifecycle. Use the protocol-defined TCP EOF after `STOP_STREAM` as Android's completion signal, characterize Windows sequential-session reuse with a loopback test, and document the two-run fake end-to-end smoke path.

**Tech Stack:** Kotlin/JVM, Java streams and sockets, JUnit 4, C#/.NET Framework 4.8, MSTest, TCP loopback, Markdown, existing repository checks.

---

## Scope Check

This plan implements only `docs/superpowers/specs/2026-07-14-phase-1m-graceful-fake-reconnect-design.md`.

In scope:

- Android waits for TCP EOF after sending `STOP_STREAM`.
- Android rejects trailing data and final-read I/O failure.
- Windows loopback coverage for two sequential sessions on one receiver.
- Exact fake end-to-end manual steps with rendered counts `0`, `3`, and `6`.
- Android and Windows default-port constants aligned with canonical TCP `39888`.
- A docs CI check that rejects implementation default-port drift.

Out of scope:

- Automatic retry, backoff, receiver-restart recovery, and network-change handling.
- New protocol packets or wire-format changes.
- Discovery, capture, codecs, real playback, foreground service, and saved receivers.
- Android or Windows UI changes.
- New dependencies.

---

## Files and Responsibilities

- Modify `sender-android/app/src/test/java/com/openaudiolink/network/HandshakeClientTest.kt`
  - Add final-read regression tests and one shared successful-response fixture helper.

- Modify `sender-android/app/src/main/java/com/openaudiolink/network/HandshakeClient.kt`
  - Require EOF after flushing `STOP_STREAM`.

- Modify `receiver-windows/tests/OpenAudioLink.Tests/Receiver/TcpReceiverTests.cs`
  - Prove one receiver accepts session 2 after session 1 stops and closes.

- Modify `docs/10-Testing.md`
  - Add the development fake end-to-end smoke procedure.

- Modify `tools/check_docs_consistency.py`
  - Parse both implementation `DefaultPort` values and require canonical TCP `39888`.

- Modify `sender-android/app/src/main/java/com/openaudiolink/protocol/ProtocolConstants.kt`
  - Correct Android `DefaultPort` to `39888`.

- Modify `receiver-windows/src/OpenAudioLink/Protocol/ProtocolConstants.cs`
  - Correct Windows `DefaultPort` to `39888`.

- Modify historical phase documents that incorrectly name `37373`
  - Align Phase 1-A, Phase 1-J, and Phase 1-K with the frozen protocol documentation.

No UI, project, workflow, dependency, or packet wire-format changes are needed.

---

### Task 1: Add Android final-read regression tests

**Files:**
- Modify: `sender-android/app/src/test/java/com/openaudiolink/network/HandshakeClientTest.kt`

- [ ] **Step 1: Add the I/O import and shared successful responses**

Add this import beside the existing Java stream imports:

```kotlin
import java.io.IOException
```

Replace the inline response construction in `runWritesHandshakePacketsOnSuccess` with:

```kotlin
val input = ByteArrayInputStream(successfulResponses())
```

Add these helpers before `assertPacket(...)`:

```kotlin
private fun successfulResponses(): ByteArray =
    PacketWriter.writePacket(ProtocolConstants.PacketTypeWelcome, 1, 1, HandshakePayloads.welcome(ProtocolConstants.ResultSuccess, "receiver", "1.0", 7)) +
        PacketWriter.writePacket(ProtocolConstants.PacketTypeStreamReady, 2, 2, HandshakePayloads.streamReady(ProtocolConstants.StreamResultSuccess, ProtocolConstants.CodecAacLc, 48000, 2)) +
        PacketWriter.writePacket(ProtocolConstants.PacketTypePong, 6, 6, HandshakePayloads.ping(5, 123456005))

private class IOExceptionAtEofInputStream(bytes: ByteArray) : ByteArrayInputStream(bytes) {
    override fun read(): Int {
        val value = super.read()
        if (value == -1) throw IOException("Receiver did not close cleanly.")
        return value
    }
}
```

- [ ] **Step 2: Add the two failing tests**

Add these tests before `assertPacket(...)`:

```kotlin
@Test
fun runReturnsFalseWhenReceiverSendsDataAfterStop() {
    val input = ByteArrayInputStream(successfulResponses() + byteArrayOf(0x01))

    assertFalse(HandshakeClient().run(input, ByteArrayOutputStream()))
}

@Test
fun runReturnsFalseWhenReceiverCloseFailsAfterStop() {
    val input = IOExceptionAtEofInputStream(successfulResponses())

    assertFalse(HandshakeClient().run(input, ByteArrayOutputStream()))
}
```

- [ ] **Step 3: Verify RED on an Android build host**

Run:

```bash
cd sender-android
./gradlew :app:testDebugUnitTest --tests com.openaudiolink.network.HandshakeClientTest
```

Expected: the two new tests fail because `HandshakeClient.run(...)` currently returns `true` immediately after writing `STOP_STREAM`.

On this Linux workspace, record the unavailable Android runner:

```bash
if [ -n "${ANDROID_HOME:-}" ]; then (cd sender-android && ./gradlew :app:testDebugUnitTest --tests com.openaudiolink.network.HandshakeClientTest); else echo 'ANDROID_HOME not set; Android tests require CI'; fi
```

Expected locally: `ANDROID_HOME not set; Android tests require CI`.

- [ ] **Step 4: Commit the failing tests**

```bash
git add sender-android/app/src/test/java/com/openaudiolink/network/HandshakeClientTest.kt
git commit -m "test: require receiver close after stop"
```

---

### Task 2: Wait for receiver EOF after STOP_STREAM

**Files:**
- Modify: `sender-android/app/src/main/java/com/openaudiolink/network/HandshakeClient.kt`
- Test: `sender-android/app/src/test/java/com/openaudiolink/network/HandshakeClientTest.kt`

- [ ] **Step 1: Replace immediate success with EOF validation**

In `HandshakeClient.run(...)`, replace:

```kotlin
output.write(PacketWriter.writePacket(ProtocolConstants.PacketTypeStopStream, 7, 123456007))
output.flush()
return true
```

with:

```kotlin
output.write(PacketWriter.writePacket(ProtocolConstants.PacketTypeStopStream, 7, 123456007))
output.flush()
return input.read() == -1
```

The existing `IOException` catch maps socket timeout and reset during this final read to `false`.

- [ ] **Step 2: Verify GREEN on an Android build host**

Run:

```bash
cd sender-android
./gradlew :app:testDebugUnitTest --tests com.openaudiolink.network.HandshakeClientTest
```

Expected: all `HandshakeClientTest` cases pass.

On this Linux workspace:

```bash
if [ -n "${ANDROID_HOME:-}" ]; then (cd sender-android && ./gradlew :app:testDebugUnitTest --tests com.openaudiolink.network.HandshakeClientTest); else echo 'ANDROID_HOME not set; Android tests require CI'; fi
```

Expected locally: `ANDROID_HOME not set; Android tests require CI`.

- [ ] **Step 3: Commit the minimal implementation**

```bash
git add sender-android/app/src/main/java/com/openaudiolink/network/HandshakeClient.kt
git commit -m "fix: wait for receiver close after stop"
```

---

### Task 3: Characterize Windows sequential session reuse

**Files:**
- Modify: `receiver-windows/tests/OpenAudioLink.Tests/Receiver/TcpReceiverTests.cs`

- [ ] **Step 1: Add the reconnect integration test**

Add this test after `SecondClientReceivesBusyWelcomeWhileFirstActive`:

```csharp
[TestMethod]
public void ClientCanReconnectAfterStopStream()
{
    using (TcpReceiver receiver = TcpReceiver.StartLoopback())
    {
        CompleteAndStop(receiver, 1UL);
        CompleteAndStop(receiver, 2UL);
    }
}
```

Add this helper before `Connect(...)`:

```csharp
private static void CompleteAndStop(TcpReceiver receiver, ulong sessionId)
{
    using (TcpClient client = Connect(receiver))
    {
        NetworkStream stream = client.GetStream();
        Write(stream, ProtocolConstants.PacketTypeHello, 1u, HandshakePayloads.Hello("Android Phone", "1.0.0", ProtocolConstants.PlatformAndroid, ProtocolConstants.CapabilityAacSupported));
        AssertPacket(stream, ProtocolConstants.PacketTypeWelcome, HandshakePayloads.Welcome(ProtocolConstants.ResultSuccess, "Windows PC", "1.0.0", sessionId));

        Write(stream, ProtocolConstants.PacketTypeStartStream, 2u, HandshakePayloads.StartStream(ProtocolConstants.CodecAacLc, 48000u, 2, 192000u, 20));
        AssertPacket(stream, ProtocolConstants.PacketTypeStreamReady, HandshakePayloads.StreamReady(ProtocolConstants.StreamResultSuccess, ProtocolConstants.CodecAacLc, 48000u, 2));

        Write(stream, ProtocolConstants.PacketTypeStopStream, 3u, new byte[0]);
        Assert.AreEqual(0, stream.Read(new byte[1], 0, 1));
    }
}
```

The EOF assertion synchronizes with receiver session cleanup. No sleep or private-state polling is allowed.

- [ ] **Step 2: Run the Windows characterization test**

Run on Windows/.NET:

```powershell
dotnet test receiver-windows/OpenAudioLink.sln -c Release --filter TcpReceiverTests
```

Expected: PASS. This locks in the existing Windows lifecycle; no Windows production change is required.

On this Linux workspace:

```bash
if command -v dotnet >/dev/null 2>&1; then dotnet test receiver-windows/OpenAudioLink.sln -c Release --filter TcpReceiverTests; else echo 'dotnet not found; Windows tests require CI'; fi
```

Expected locally: `dotnet not found; Windows tests require CI`.

- [ ] **Step 3: Commit the Windows coverage**

```bash
git add receiver-windows/tests/OpenAudioLink.Tests/Receiver/TcpReceiverTests.cs
git commit -m "test: cover reconnect after stop stream"
```

---

### Task 4: Document the fake end-to-end smoke path

**Files:**
- Modify: `docs/10-Testing.md`

- [ ] **Step 1: Add the development smoke section**

After the existing `End-to-End Validation` section and before `Platform Test Matrix`, add:

```markdown
# Development Fake End-to-End Smoke Test

The current development scaffold provides a fake transport smoke test before real capture, codec and playback work is available. This smoke test does not replace release end-to-end validation.

Prerequisites:

- Android and Windows are on the same reachable network.
- TCP port `39888` is reachable from Android.
- The current Windows receiver and Android sender builds are installed.

Steps and expected results:

| Step | Action | Expected Result |
|-----:|--------|-----------------|
| 1 | Start the Windows receiver. | The window shows `Listening on TCP port 39888` and `Rendered frames: 0`. |
| 2 | Enter the Windows IP address or host in Android and press `Connect Fake Stream`. | Android shows `Connecting`, then `Success`. |
| 3 | Check the Windows receiver. | The window shows `Rendered frames: 3`. |
| 4 | Press `Connect Fake Stream` again without restarting either application. | Android again shows `Connecting`, then `Success`; the previous session must no longer cause a busy failure. |
| 5 | Check the Windows receiver again. | The window shows `Rendered frames: 6`. |

Passing this smoke test proves the current fake packet flow, graceful `STOP_STREAM` close and manual reconnect path. It does not prove discovery, MediaProjection, real AAC processing, audible playback or the latency target.

---
```

- [ ] **Step 2: Run documentation checks**

```bash
python3 tools/check_docs_consistency.py
python3 tools/protocol/generate_golden_packets.py --check
git diff --check
```

Expected:

```text
docs consistency ok: 12 markdown files checked
protocol golden packets ok
```

`git diff --check` prints no output.

- [ ] **Step 3: Commit the smoke procedure**

```bash
git add docs/10-Testing.md
git commit -m "docs: add fake reconnect smoke test"
```

---

### Task 5: Enforce and restore the canonical default TCP port

**Files:**
- Modify: `tools/check_docs_consistency.py`
- Modify: `sender-android/app/src/main/java/com/openaudiolink/protocol/ProtocolConstants.kt`
- Modify: `receiver-windows/src/OpenAudioLink/Protocol/ProtocolConstants.cs`
- Modify: `docs/superpowers/plans/2026-07-08-phase-1a-network-handshake.md`
- Modify: `docs/superpowers/specs/2026-07-13-phase-1j-windows-default-lan-listener-design.md`
- Modify: `docs/superpowers/specs/2026-07-13-phase-1k-android-manual-fake-connect-ui-design.md`

- [ ] **Step 1: Add a failing cross-platform default-port check**

In `tools/check_docs_consistency.py`, add these constants after `ROOT`:

```python
CANONICAL_DEFAULT_PORT = 39888
DEFAULT_PORT_SOURCES = [
    (
        "Android",
        ROOT / "sender-android/app/src/main/java/com/openaudiolink/protocol/ProtocolConstants.kt",
        re.compile(r"\bconst val DefaultPort = (\d+)\b"),
    ),
    (
        "Windows",
        ROOT / "receiver-windows/src/OpenAudioLink/Protocol/ProtocolConstants.cs",
        re.compile(r"\bpublic const int DefaultPort = (\d+);\b"),
    ),
]
```

Add this function before `main()`:

```python
def check_default_ports() -> list[str]:
    errors: list[str] = []
    for platform, path, pattern in DEFAULT_PORT_SOURCES:
        match = pattern.search(read(path))
        if match is None:
            errors.append(f"missing {platform} DefaultPort: {path.relative_to(ROOT)}")
        elif int(match.group(1)) != CANONICAL_DEFAULT_PORT:
            errors.append(
                f"default port mismatch in {path.relative_to(ROOT)}: "
                f"{match.group(1)} != {CANONICAL_DEFAULT_PORT}"
            )

    return errors
```

In `main()`, add:

```python
errors.extend(check_default_ports())
```

- [ ] **Step 2: Verify RED**

Run:

```bash
python3 tools/check_docs_consistency.py
```

Expected: exit `1`, reporting both implementation constants as mismatches against canonical port `39888`.

- [ ] **Step 3: Commit the failing check**

```bash
git add tools/check_docs_consistency.py
git commit -m "test: enforce canonical default tcp port"
```

- [ ] **Step 4: Correct both constants and stale phase documents**

Change:

```kotlin
const val DefaultPort = 37373
```

to:

```kotlin
const val DefaultPort = 39888
```

Change:

```csharp
public const int DefaultPort = 37373;
```

to:

```csharp
public const int DefaultPort = 39888;
```

Replace every `37373` with `39888` in exactly these documents:

```text
docs/superpowers/plans/2026-07-08-phase-1a-network-handshake.md
docs/superpowers/specs/2026-07-13-phase-1j-windows-default-lan-listener-design.md
docs/superpowers/specs/2026-07-13-phase-1k-android-manual-fake-connect-ui-design.md
```

- [ ] **Step 5: Verify GREEN**

Run:

```bash
python3 tools/check_docs_consistency.py
python3 tools/protocol/generate_golden_packets.py --check
rg -n "37373" sender-android/app/src/main/java/com/openaudiolink/protocol/ProtocolConstants.kt receiver-windows/src/OpenAudioLink/Protocol/ProtocolConstants.cs docs/superpowers/plans/2026-07-08-phase-1a-network-handshake.md docs/superpowers/specs/2026-07-13-phase-1j-windows-default-lan-listener-design.md docs/superpowers/specs/2026-07-13-phase-1k-android-manual-fake-connect-ui-design.md && exit 1 || true
git diff --check
```

Expected:

```text
docs consistency ok: 12 markdown files checked
protocol golden packets ok
```

The `rg` command finds no stale port in the corrected implementation or historical phase files, and `git diff --check` prints no output.

- [ ] **Step 6: Commit the correction**

```bash
git add sender-android/app/src/main/java/com/openaudiolink/protocol/ProtocolConstants.kt receiver-windows/src/OpenAudioLink/Protocol/ProtocolConstants.cs docs/superpowers/plans/2026-07-08-phase-1a-network-handshake.md docs/superpowers/specs/2026-07-13-phase-1j-windows-default-lan-listener-design.md docs/superpowers/specs/2026-07-13-phase-1k-android-manual-fake-connect-ui-design.md
git commit -m "fix: align default tcp port"
```

---

### Task 6: Final validation, push and CI

**Files:**
- Validate repository state only.

- [ ] **Step 1: Run local repository checks**

```bash
python3 tools/check_docs_consistency.py
python3 tools/protocol/generate_golden_packets.py --check
git diff --check HEAD
git status --short --branch
```

Expected:

```text
docs consistency ok: 12 markdown files checked
protocol golden packets ok
```

The branch is clean and `git diff --check HEAD` prints no output.

- [ ] **Step 2: Record platform-test handoff**

```bash
if command -v dotnet >/dev/null 2>&1; then dotnet test receiver-windows/OpenAudioLink.sln -c Release; else echo 'dotnet not found; Windows tests require CI'; fi
if [ -n "${ANDROID_HOME:-}" ]; then (cd sender-android && ./gradlew :app:testDebugUnitTest); else echo 'ANDROID_HOME not set; Android tests require CI'; fi
```

Expected locally in this workspace:

```text
dotnet not found; Windows tests require CI
ANDROID_HOME not set; Android tests require CI
```

- [ ] **Step 3: Push the phase branch**

Use the known Gitea MTU workaround:

```bash
ip route add 192.168.3.20/32 via 192.168.4.1 dev eth0 mtu 1200 || true
git push -u origin phase-1m-graceful-fake-reconnect
git fetch origin phase-1m-graceful-fake-reconnect
printf 'local=%s\nremote=%s\n' "$(git rev-parse --short HEAD)" "$(git rev-parse --short origin/phase-1m-graceful-fake-reconnect)"
git diff --exit-code HEAD origin/phase-1m-graceful-fake-reconnect --stat
ip route del 192.168.3.20/32 via 192.168.4.1 dev eth0 || true
```

Expected: local and remote hashes match and the diff exits `0`.

- [ ] **Step 4: Check GitHub Actions**

```bash
git rev-parse HEAD
gh run list -R imshuai/OpenAudioLink -L 10 --json databaseId,name,headBranch,headSha,status,conclusion
```

Expected: exactly one `docs`, one `android`, and one `windows` run for `phase-1m-graceful-fake-reconnect` have `status: completed`, `conclusion: success`, and `headSha` equal to the full hash printed by `git rev-parse HEAD`.
