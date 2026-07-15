# Phase 1-P Android MediaCodec AAC Encoder Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Prove on exact-head CI that a fixed Android `MediaCodec` AAC-LC encoder emits canonical raw access units which the existing Windows Media Foundation decoder consumes, without connecting either codec to the fake runtimes.

**Architecture:** Add one synchronous owner-thread `MediaCodecAacEncoder` with fixed Version 1 media attributes, exact `11 90` codec-config validation, partial-output assembly, explicit EOS drain, and deterministic cleanup. API 29 run `29392806222` observed selected `OMX.google.aac.encoder` produce two candidates for one input and thirteen for twelve inputs. The selected-codec gate is therefore zero-input/zero-output and non-empty `N` inputs/`N + 1` candidates, retaining one codec-added priming/padding candidate without audio-content or clock semantics. Every one of the thirteen candidates must decode independently to one PCM frame on Windows. An API 29 x86_64 emulator exports the thirteen boundaries in a test-only ADTS artifact; a dependent `windows-2022` job strips those headers and verifies both independent and continuous decode.

**Tech Stack:** Kotlin 2.0.21, Android API 29/35, `android.media.MediaCodec`, AndroidX Test/JUnit 4, Gradle 8.7/AGP 8.5.2, GitHub Actions, C#/.NET Framework 4.8, MSTest, Markdown.

---

## Scope Check

This plan implements only `docs/superpowers/specs/2026-07-15-phase-1p-android-mediacodec-aac-encoder-design.md`.

In scope:

- One production `MediaCodecAacEncoder` and `EncodedAccessUnit` in one Kotlin file.
- Fixed AAC-LC, 48 kHz, stereo, PCM16, 192 kbps behavior.
- Exact 4096-byte PCM input frames and caller-owned monotonic timestamps.
- Exact `AudioSpecificConfig = 11 90` validation from either native representation.
- Synchronous submit/output/drain, partial-frame assembly, state, owner-thread, timeout, and cleanup behavior.
- Full-cycle zero-input/zero-output and non-empty `N`-input/`N + 1`-candidate
  relation; the retained codec-added priming/padding candidate has no inferred
  audio-content or clock semantics, while batched, missing, or other-count
  output fails the selected codec.
- API 29 emulator instrumentation and repeated native lifecycle proof.
- Ephemeral test-only ADTS export.
- Independent one-frame and continuous Windows Media Foundation decode of the
  same thirteen generated raw candidates.
- Android workflow unit, emulator, and Windows interop jobs.
- Focused active-doc and completed-spec status corrections.

Out of scope:

- MediaProjection, playback capture, AudioRecord, queues, foreground service, UI, discovery, settings, runtime logging, and process lifecycle.
- Fake-frame replacement, packetization, TCP, heartbeat, reconnect, or protocol production changes.
- Windows runtime/renderer integration or audible playback.
- Hardware encoder guarantees, selection policy, real-device matrix, bitrate configuration, performance, endurance, or latency gates.
- Raw AAC parsing or support for codecs that batch multiple access units into
  one synchronous output buffer.
- Encoder interfaces, factories, DI, coroutines, callback mode, Robolectric, third-party codec libraries, or checked-in MediaCodec output.

---

## Files And Responsibilities

Create:

- `sender-android/app/src/main/java/com/openaudiolink/audio/MediaCodecAacEncoder.kt` — fixed synchronous codec wrapper, access-unit value, state, config validator, and cleanup.
- `sender-android/app/src/test/java/com/openaudiolink/audio/MediaCodecAacEncoderContractTest.kt` — JVM exact-ASC contract.
- `sender-android/app/src/androidTest/java/com/openaudiolink/audio/MediaCodecAacEncoderTest.kt` — emulator native behavior, misuse, and ephemeral ADTS writer.

Modify:

- `sender-android/gradle.properties` — enable AndroidX for the existing AndroidX instrumentation runner and test-only dependencies.
- `sender-android/app/build.gradle.kts` — only AndroidX instrumentation dependencies.
- `.github/workflows/android.yml` — unit, emulator, artifact, and Windows interop jobs.
- `receiver-windows/tests/OpenAudioLink.Tests/Receiver/MediaFoundationAacDecoderTests.cs` — dynamic artifact decode oracle and strict reusable ADTS parser.
- `docs/04-Android.md`, `docs/06-Audio.md`, `docs/10-Testing.md`, `docs/11-Roadmap.md` — active truth.
- `docs/superpowers/specs/2026-07-14-phase-1n-aac-lc-wire-contract-design.md`, `docs/superpowers/specs/2026-07-15-phase-1o-windows-media-foundation-aac-decoder-design.md`, and the Phase 1-P spec — completed status and focused stale-language correction.

Do not modify any Android fake sender/runtime/protocol source, Android manifest, Windows production source/project, protocol golden, or checked-in audio fixture.

---

## Execution Conventions

Work only in:

```text
/root/.config/superpowers/worktrees/OpenAudioLink/phase-1p-android-mediacodec-aac-encoder
```

Branch:

```text
phase-1p-android-mediacodec-aac-encoder
```

Base:

```text
cd6f42cb7bec567dd9e4270ff8e3636504972f63
```

This plan and its corrected design spec must be committed before Task 1. They
remain part of the exact phase diff; no later task is responsible for rescuing
an untracked plan file.

Local Android commands use the existing ARM64 `aapt2` override:

```bash
ANDROID_HOME=/root/Android/Sdk \
ANDROID_SDK_ROOT=/root/Android/Sdk \
./sender-android/gradlew \
  -p sender-android \
  -Pandroid.aapt2FromMavenOverride=/root/.cache/openaudiolink/android-sdk-tools-35.0.2-aarch64/bin/aapt2 \
  :app:testDebugUnitTest :app:assembleDebugAndroidTest
```

The ARM64 host cannot execute its installed x64 `adb` or emulator. Native truth comes only from exact-head GitHub Actions. Do not weaken, skip, or replace native tests after a failure.

Push to Gitea with the known route workaround and verify both source and mirror tips:

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

git push origin phase-1p-android-mediacodec-aac-encoder
HEAD_SHA=$(git rev-parse HEAD)
GITEA_SHA=$(git ls-remote origin refs/heads/phase-1p-android-mediacodec-aac-encoder | cut -f1)
test "$GITEA_SHA" = "$HEAD_SHA"

for attempt in $(seq 1 60); do
  GITHUB_SHA=$(gh api \
    repos/imshuai/OpenAudioLink/commits/phase-1p-android-mediacodec-aac-encoder \
    --jq .sha 2>/dev/null || true)
  [ "$GITHUB_SHA" = "$HEAD_SHA" ] && break
  sleep 5
done
test "$GITHUB_SHA" = "$HEAD_SHA"
```

Use REST for exact-head runs:

```bash
HEAD_SHA=$(git rev-parse HEAD)
gh api \
  'repos/imshuai/OpenAudioLink/actions/runs?branch=phase-1p-android-mediacodec-aac-encoder&per_page=30' \
  --jq ".workflow_runs[] | select(.head_sha == \"$HEAD_SHA\") | [.id,.name,.status,.conclusion] | @tsv"
```

Every implementation task ends with specification review, then code-quality review. Fix and re-review every Critical or Important finding before advancing.

---

### Task 1: Establish the API 29 instrumentation lane

**Files:**
- Modify: `sender-android/gradle.properties`
- Modify: `sender-android/app/build.gradle.kts`
- Create: `sender-android/app/src/androidTest/java/com/openaudiolink/audio/MediaCodecAacEncoderTest.kt`
- Modify: `.github/workflows/android.yml`

- [ ] **Step 1: Enable AndroidX for instrumentation tests**

In `sender-android/gradle.properties`, change:

```properties
android.useAndroidX=false
```

to:

```properties
android.useAndroidX=true
```

The project already names `androidx.test.runner.AndroidJUnitRunner`; this setting is required before resolving the AndroidX test dependencies below.

- [ ] **Step 2: Add only the required instrumentation dependencies**

Append to the existing `dependencies` block:

```kotlin
androidTestImplementation("androidx.test.ext:junit:1.2.1")
androidTestImplementation("androidx.test:runner:1.6.2")
```

The complete block becomes:

```kotlin
dependencies {
    testImplementation("junit:junit:4.13.2")
    androidTestImplementation("androidx.test.ext:junit:1.2.1")
    androidTestImplementation("androidx.test:runner:1.6.2")
}
```

Do not add Robolectric, coroutine-test, codec, or assertion libraries.

- [ ] **Step 3: Add one framework-boundary baseline test**

Create `MediaCodecAacEncoderTest.kt`:

```kotlin
package com.openaudiolink.audio

import android.os.Build
import androidx.test.ext.junit.runners.AndroidJUnit4
import org.junit.Assert.assertTrue
import org.junit.Test
import org.junit.runner.RunWith

@RunWith(AndroidJUnit4::class)
class MediaCodecAacEncoderTest {
    @Test
    fun instrumentationRunsOnSupportedAndroid() {
        assertTrue(Build.VERSION.SDK_INT >= 29)
    }
}
```

This proves only runner/emulator availability. It makes no codec claim.

- [ ] **Step 4: Split Android CI into unit and emulator jobs**

Replace `.github/workflows/android.yml` with:

```yaml
name: android

on:
  pull_request:
  push:
    branches: ['phase-*']

jobs:
  unit:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-java@v4
        with:
          distribution: temurin
          java-version: '17'
      - uses: android-actions/setup-android@v3
      - run: ./gradlew :app:testDebugUnitTest
        working-directory: sender-android

  media-codec:
    runs-on: ubuntu-22.04
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-java@v4
        with:
          distribution: temurin
          java-version: '17'
      - uses: android-actions/setup-android@v3
      - name: Enable KVM
        run: |
          echo 'KERNEL=="kvm", GROUP="kvm", MODE="0666", OPTIONS+="static_node=kvm"' | sudo tee /etc/udev/rules.d/99-kvm4all.rules
          sudo udevadm control --reload-rules
          sudo udevadm trigger --name-match=kvm
      - name: Run API 29 instrumentation tests
        uses: reactivecircus/android-emulator-runner@v2
        with:
          api-level: 29
          target: default
          arch: x86_64
          working-directory: ./sender-android
          disable-animations: true
          script: |
            test "$(adb shell getprop ro.build.version.sdk | tr -d '\r')" = 29 && echo "Android API level: 29"
            adb logcat -c
            ./gradlew -Pandroid.injected.androidTest.leaveApksInstalledAfterRun=true :app:connectedDebugAndroidTest; TEST_STATUS=$?; adb logcat -d -s MediaCodecAacTest:I TestRunner:E AndroidJUnitRunner:E '*:S'; exit "$TEST_STATUS"
            adb shell pm path com.openaudiolink | grep -F 'package:'
```

Each physical script line runs as an independent shell command, so the test
status and failure log dump must stay on the same physical line; the dump runs
on both test outcomes and the original test exit code is preserved. Do not set
`profile`; device shape is irrelevant. Do not add a `main` push trigger.
The injected AGP property is required because AGP 8.5.2 otherwise uninstalls
the tested package before a later CI line can export app-private artifacts.

- [ ] **Step 5: Compile every Android test source locally**

Run:

```bash
ANDROID_HOME=/root/Android/Sdk \
ANDROID_SDK_ROOT=/root/Android/Sdk \
./sender-android/gradlew \
  -p sender-android \
  -Pandroid.aapt2FromMavenOverride=/root/.cache/openaudiolink/android-sdk-tools-35.0.2-aarch64/bin/aapt2 \
  :app:testDebugUnitTest :app:assembleDebug :app:assembleDebugAndroidTest
```

Expected: `BUILD SUCCESSFUL`. This compiles instrumentation code but does not claim it ran.

- [ ] **Step 6: Commit the infrastructure baseline**

```bash
git add sender-android/gradle.properties \
  sender-android/app/build.gradle.kts \
  sender-android/app/src/androidTest/java/com/openaudiolink/audio/MediaCodecAacEncoderTest.kt \
  .github/workflows/android.yml
git commit -m "ci: add android emulator test lane"
```

- [ ] **Step 7: Push and prove the real emulator baseline**

Push with the execution-convention snippet. Require exact-head `docs`, `windows`, and `android` workflows to complete successfully. Inspect the Android jobs:

```bash
RUN_ID=$(gh api \
  'repos/imshuai/OpenAudioLink/actions/runs?branch=phase-1p-android-mediacodec-aac-encoder&per_page=30' \
  --jq ".workflow_runs[] | select(.head_sha == \"$(git rev-parse HEAD)\" and .event == \"push\" and .name == \"android\") | .id" \
  | head -n 1)
test -n "$RUN_ID"
gh run watch "$RUN_ID" --repo imshuai/OpenAudioLink --exit-status
gh api "repos/imshuai/OpenAudioLink/actions/runs/$RUN_ID/jobs?per_page=20" \
  --jq '.jobs[] | [.name,.status,.conclusion] | @tsv'
```

Expected jobs: `unit` and `media-codec`, both `completed success`. If emulator provisioning fails, use systematic debugging on that exact log before adding feature tests.

---

### Task 2: Specify the canonical ASC validator

**Files:**
- Create: `sender-android/app/src/test/java/com/openaudiolink/audio/MediaCodecAacEncoderContractTest.kt`

- [ ] **Step 1: Write the JVM RED contract**

Create:

```kotlin
package com.openaudiolink.audio

import org.junit.Assert.assertThrows
import org.junit.Assert.assertTrue
import org.junit.Test

class MediaCodecAacEncoderContractTest {
    @Test
    fun acceptsOnlyCanonicalAudioSpecificConfig() {
        validateCanonicalAudioSpecificConfig(byteArrayOf(0x11, 0x90.toByte()))

        listOf(
            byteArrayOf(),
            byteArrayOf(0x11),
            byteArrayOf(0x11, 0x90.toByte(), 0x00),
            byteArrayOf(0x29, 0x90.toByte()),
            byteArrayOf(0x12, 0x10),
            byteArrayOf(0x11, 0x88.toByte()),
        ).forEach { invalid ->
            val error = assertThrows(IllegalStateException::class.java) {
                validateCanonicalAudioSpecificConfig(invalid)
            }
            assertTrue(error.message.orEmpty().contains("11 90"))
        }
    }
}
```

- [ ] **Step 2: Run RED locally**

Run only the new test:

```bash
ANDROID_HOME=/root/Android/Sdk \
ANDROID_SDK_ROOT=/root/Android/Sdk \
./sender-android/gradlew \
  -p sender-android \
  -Pandroid.aapt2FromMavenOverride=/root/.cache/openaudiolink/android-sdk-tools-35.0.2-aarch64/bin/aapt2 \
  :app:testDebugUnitTest --tests '*MediaCodecAacEncoderContractTest'
```

Expected: Kotlin compilation fails with unresolved reference `validateCanonicalAudioSpecificConfig`. A dependency, syntax, or unrelated test failure is not the intended RED.

- [ ] **Step 3: Commit and push the intentional RED**

```bash
git add sender-android/app/src/test/java/com/openaudiolink/audio/MediaCodecAacEncoderContractTest.kt
git commit -m "test: specify canonical Android AAC config"
```

Push exact HEAD. Require `docs` and `windows` success. Require `android` failure in the `unit` job for the unresolved validator and no other reason. Record the run ID and decisive compiler line.

---

### Task 3: Implement the exact ASC validator

**Files:**
- Create: `sender-android/app/src/main/java/com/openaudiolink/audio/MediaCodecAacEncoder.kt`
- Test: `sender-android/app/src/test/java/com/openaudiolink/audio/MediaCodecAacEncoderContractTest.kt`

- [ ] **Step 1: Add the minimum production validator**

Create `MediaCodecAacEncoder.kt` with only:

```kotlin
package com.openaudiolink.audio

private val CANONICAL_AUDIO_SPECIFIC_CONFIG =
    byteArrayOf(0x11, 0x90.toByte())

internal fun validateCanonicalAudioSpecificConfig(bytes: ByteArray) {
    if (!bytes.contentEquals(CANONICAL_AUDIO_SPECIFIC_CONFIG)) {
        val actual = bytes.joinToString(" ") { byte ->
            "%02X".format(byte.toInt() and 0xff)
        }
        throw IllegalStateException(
            "Expected AudioSpecificConfig 11 90, got $actual.",
        )
    }
}
```

Do not add the encoder class before the native RED test exists.

- [ ] **Step 2: Run focused and regression GREEN tests**

```bash
ANDROID_HOME=/root/Android/Sdk \
ANDROID_SDK_ROOT=/root/Android/Sdk \
./sender-android/gradlew \
  -p sender-android \
  -Pandroid.aapt2FromMavenOverride=/root/.cache/openaudiolink/android-sdk-tools-35.0.2-aarch64/bin/aapt2 \
  :app:testDebugUnitTest :app:assembleDebugAndroidTest
```

Expected: `BUILD SUCCESSFUL` and the complete JVM suite passes.

- [ ] **Step 3: Commit and push GREEN**

```bash
git add sender-android/app/src/main/java/com/openaudiolink/audio/MediaCodecAacEncoder.kt
git commit -m "feat: validate Android AAC config"
```

Push exact HEAD. Require `docs`, `windows`, and both Android jobs to return to success.

---
### Task 4: Specify the native encoder contract

**Files:**
- Modify: `sender-android/app/src/androidTest/java/com/openaudiolink/audio/MediaCodecAacEncoderTest.kt`

- [ ] **Step 1: Replace the baseline with the complete native RED contract**

Replace `MediaCodecAacEncoderTest.kt` with:

```kotlin
package com.openaudiolink.audio

import android.os.Build
import android.util.Log
import androidx.test.ext.junit.runners.AndroidJUnit4
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertThrows
import org.junit.Assert.assertTrue
import org.junit.Test
import org.junit.runner.RunWith
import java.util.concurrent.atomic.AtomicReference
import kotlin.math.PI
import kotlin.math.roundToInt
import kotlin.math.sin

@RunWith(AndroidJUnit4::class)
class MediaCodecAacEncoderTest {
    @Test
    fun instrumentationRunsOnSupportedAndroid() {
        assertTrue(Build.VERSION.SDK_INT >= 29)
    }

    @Test
    fun encodesAndDrainsTwiceInOneProcess() {
        val first = encodeOnce()
        val second = encodeOnce()

        assertEquals(EXPECTED_OUTPUT_CANDIDATE_COUNT, first.size)
        assertEquals(EXPECTED_OUTPUT_CANDIDATE_COUNT, second.size)
    }

    @Test
    fun emptyInputDrainsToNoCandidates() {
        MediaCodecAacEncoder().use { encoder ->
            assertTrue(encoder.drain().isEmpty())
        }
    }

    @Test
    fun rejectsWrongPcmLengthAndInputTimestamps() {
        MediaCodecAacEncoder().use { encoder ->
            assertThrows(IllegalArgumentException::class.java) {
                encoder.submit(ByteArray(PCM_BYTES_PER_FRAME - 1), 0)
            }
            assertThrows(IllegalArgumentException::class.java) {
                encoder.submit(ByteArray(PCM_BYTES_PER_FRAME + 1), 0)
            }
            assertThrows(IllegalArgumentException::class.java) {
                encoder.submit(ByteArray(PCM_BYTES_PER_FRAME), -1)
            }

            encoder.submit(pcmFrame(0), 0)
            assertThrows(IllegalArgumentException::class.java) {
                encoder.submit(pcmFrame(1), 0)
            }
        }
    }

    @Test
    fun drainAndCloseAreIdempotentAndCloseInput() {
        val encoder = MediaCodecAacEncoder()
        encoder.submit(pcmFrame(0), 0)
        encoder.drain()
        assertTrue(encoder.drain().isEmpty())
        assertThrows(IllegalStateException::class.java) {
            encoder.submit(pcmFrame(1), sampleTimeUs(1))
        }

        encoder.close()
        encoder.close()
        assertThrows(IllegalStateException::class.java) {
            encoder.drain()
        }
    }

    @Test
    fun callsFromAnotherThreadAreRejected() {
        val encoder = MediaCodecAacEncoder()
        try {
            assertWrongThread {
                encoder.submit(pcmFrame(0), 0)
            }
            assertWrongThread {
                encoder.drain()
            }
            assertWrongThread {
                encoder.close()
            }
        } finally {
            encoder.close()
        }
    }

    private fun encodeOnce(): List<EncodedAccessUnit> {
        val output = mutableListOf<EncodedAccessUnit>()
        MediaCodecAacEncoder().use { encoder ->
            Log.i(TAG, "AAC encoder: ${encoder.codecName}")
            repeat(INPUT_FRAME_COUNT) { frameIndex ->
                output += encoder.submit(
                    pcmFrame(frameIndex),
                    sampleTimeUs(frameIndex.toLong()),
                )
            }
            output += encoder.drain()
            Log.i(
                TAG,
                "AAC output: ${output.size} AU(s), PTS(us)=" +
                    output.joinToString(prefix = "[", postfix = "]") { unit ->
                        unit.presentationTimeUs.toString()
                    },
            )
            assertTrue(encoder.drain().isEmpty())
        }

        assertEquals(EXPECTED_OUTPUT_CANDIDATE_COUNT, output.size)
        output.forEach { unit ->
            assertTrue(unit.bytes.isNotEmpty())
            assertTrue(unit.bytes.size <= MAX_ACCESS_UNIT_BYTES)
        }
        return output
    }

    private fun pcmFrame(frameIndex: Int): ByteArray {
        val pcm = ByteArray(PCM_BYTES_PER_FRAME)
        repeat(SAMPLES_PER_FRAME) { sampleInFrame ->
            val sampleIndex = frameIndex * SAMPLES_PER_FRAME + sampleInFrame
            val left = sineSample(sampleIndex, LEFT_FREQUENCY_HZ)
            val right = sineSample(sampleIndex, RIGHT_FREQUENCY_HZ)
            val offset = sampleInFrame * BYTES_PER_STEREO_SAMPLE
            putLittleEndian(pcm, offset, left)
            putLittleEndian(pcm, offset + 2, right)
        }
        return pcm
    }

    private fun sineSample(sampleIndex: Int, frequencyHz: Double): Short =
        (sin(2.0 * PI * frequencyHz * sampleIndex / SAMPLE_RATE) * AMPLITUDE)
            .roundToInt()
            .toShort()

    private fun putLittleEndian(destination: ByteArray, offset: Int, value: Short) {
        val integer = value.toInt()
        destination[offset] = integer.toByte()
        destination[offset + 1] = (integer ushr 8).toByte()
    }

    private fun sampleTimeUs(frameIndex: Long): Long =
        Math.addExact(Math.multiplyExact(frameIndex, 64_000L), 1L) / 3L

    private fun assertWrongThread(action: () -> Unit) {
        val error = AtomicReference<Throwable?>()
        val thread = Thread {
            try {
                action()
            } catch (failure: Throwable) {
                error.set(failure)
            }
        }
        thread.isDaemon = true
        thread.start()
        thread.join(30_000)
        assertFalse("worker thread did not stop", thread.isAlive)
        assertTrue(error.get() is IllegalStateException)
    }

    private companion object {
        const val TAG = "MediaCodecAacTest"
        const val SAMPLE_RATE = 48_000.0
        const val SAMPLES_PER_FRAME = 1024
        const val INPUT_FRAME_COUNT = 12
        const val CODEC_ADDED_CANDIDATE_COUNT = 1
        const val EXPECTED_OUTPUT_CANDIDATE_COUNT =
            INPUT_FRAME_COUNT + CODEC_ADDED_CANDIDATE_COUNT
        const val PCM_BYTES_PER_FRAME = 4096
        const val BYTES_PER_STEREO_SAMPLE = 4
        const val MAX_ACCESS_UNIT_BYTES = 65_536
        const val LEFT_FREQUENCY_HZ = 440.0
        const val RIGHT_FREQUENCY_HZ = 660.0
        const val AMPLITUDE = 12_000.0
    }
}
```

Exact output count is a deliberate Version 1 compatibility gate for the
selected codec, not an Android scheduling guarantee. Live API 29 evidence from
run `29392806222` observed `OMX.google.aac.encoder` produce two candidates for
one input and thirteen for twelve inputs. Zero input requires zero candidates;
non-empty `N` input frames require `N + 1`, retaining the codec-added
priming/padding candidate without audio-content or clock semantics. Batching,
loss, or any other count fails. Output PTS remains diagnostic and is not
required to equal input PTS.

- [ ] **Step 2: Compile locally and confirm the intended RED**

Run:

```bash
ANDROID_HOME=/root/Android/Sdk \
ANDROID_SDK_ROOT=/root/Android/Sdk \
./sender-android/gradlew \
  -p sender-android \
  -Pandroid.aapt2FromMavenOverride=/root/.cache/openaudiolink/android-sdk-tools-35.0.2-aarch64/bin/aapt2 \
  :app:assembleDebugAndroidTest
```

Expected: Kotlin compilation fails only because `MediaCodecAacEncoder` and `EncodedAccessUnit` do not exist. Fix test syntax before committing if any other error appears.

- [ ] **Step 3: Commit and push native RED**

```bash
git add sender-android/app/src/androidTest/java/com/openaudiolink/audio/MediaCodecAacEncoderTest.kt
git commit -m "test: specify Android MediaCodec AAC encoder"
```

Push exact HEAD. Require:

- `docs`: success.
- `windows`: success.
- Android `unit`: success.
- Android `media-codec`: failure for unresolved `MediaCodecAacEncoder`/`EncodedAccessUnit`, after the API 29 emulator starts.

Do not accept an emulator, dependency, Gradle, or unrelated source failure as the feature RED.

---

### Task 5: Implement the standalone MediaCodec encoder

**Files:**
- Modify: `sender-android/app/src/main/java/com/openaudiolink/audio/MediaCodecAacEncoder.kt`
- Test: `sender-android/app/src/test/java/com/openaudiolink/audio/MediaCodecAacEncoderContractTest.kt`
- Test: `sender-android/app/src/androidTest/java/com/openaudiolink/audio/MediaCodecAacEncoderTest.kt`

- [ ] **Step 1: Replace the validator-only file with the fixed encoder**

Replace `MediaCodecAacEncoder.kt` with:

```kotlin
package com.openaudiolink.audio

import android.media.AudioFormat
import android.media.MediaCodec
import android.media.MediaCodecInfo
import android.media.MediaFormat
import android.os.SystemClock
import java.io.Closeable
import java.nio.ByteBuffer
import java.util.concurrent.TimeoutException

private val CANONICAL_AUDIO_SPECIFIC_CONFIG =
    byteArrayOf(0x11, 0x90.toByte())

internal fun validateCanonicalAudioSpecificConfig(bytes: ByteArray) {
    if (!bytes.contentEquals(CANONICAL_AUDIO_SPECIFIC_CONFIG)) {
        val actual = bytes.joinToString(" ") { byte ->
            "%02X".format(byte.toInt() and 0xff)
        }
        throw IllegalStateException(
            "Expected AudioSpecificConfig 11 90, got $actual.",
        )
    }
}

class EncodedAccessUnit(
    val bytes: ByteArray,
    val presentationTimeUs: Long,
)

class MediaCodecAacEncoder : Closeable {
    val codecName: String

    private val ownerThreadId = Thread.currentThread().id
    private val codec: MediaCodec
    private var started = false
    private var state = State.Faulted
    private var hasInput = false
    private var lastInputPresentationTimeUs = -1L
    private var inputEosQueued = false
    private var outputEosSeen = false
    private var configValidated = false
    private var submittedFrameCount = 0
    private var completedCandidateCount = 0
    private var partialOutput = ByteArray(0)
    private var partialPresentationTimeUs = 0L

    init {
        var created: MediaCodec? = null
        var createdName = ""
        var didStart = false
        try {
            created = MediaCodec.createEncoderByType(MediaFormat.MIMETYPE_AUDIO_AAC)
            createdName = created.name
            val format = MediaFormat.createAudioFormat(
                MediaFormat.MIMETYPE_AUDIO_AAC,
                SAMPLE_RATE,
                CHANNEL_COUNT,
            )
            format.setInteger(
                MediaFormat.KEY_AAC_PROFILE,
                MediaCodecInfo.CodecProfileLevel.AACObjectLC,
            )
            format.setInteger(MediaFormat.KEY_BIT_RATE, BIT_RATE)
            format.setInteger(
                MediaFormat.KEY_PCM_ENCODING,
                AudioFormat.ENCODING_PCM_16BIT,
            )
            created.configure(
                format,
                null,
                null,
                MediaCodec.CONFIGURE_FLAG_ENCODE,
            )
            created.start()
            didStart = true
            validateInputFormat(created.inputFormat)
        } catch (failure: Exception) {
            cleanupConstruction(created, didStart, failure)
            throw IllegalStateException(
                "MediaCodec AAC encoder construction failed.",
                failure,
            )
        }
        codec = checkNotNull(created)
        codecName = createdName
        started = true
        state = State.Active
    }

    fun submit(
        pcm: ByteArray,
        presentationTimeUs: Long,
    ): List<EncodedAccessUnit> {
        checkOwnerThread()
        requireActive("submit")
        require(pcm.size == PCM_BYTES_PER_FRAME) {
            "PCM frame must contain exactly $PCM_BYTES_PER_FRAME bytes."
        }
        require(presentationTimeUs >= 0) {
            "Input presentation timestamp must be non-negative."
        }
        if (hasInput) {
            require(presentationTimeUs > lastInputPresentationTimeUs) {
                "Input presentation timestamps must be strictly increasing."
            }
        }

        val output = mutableListOf<EncodedAccessUnit>()
        try {
            val deadline = deadlineNanos()
            val inputIndex = acquireInputBuffer(output, deadline)
            val inputBuffer = codec.getInputBuffer(inputIndex)
                ?: throw IllegalStateException("MediaCodec returned no input buffer.")
            inputBuffer.clear()
            if (inputBuffer.remaining() < pcm.size) {
                throw IllegalStateException(
                    "MediaCodec input buffer is smaller than $PCM_BYTES_PER_FRAME bytes.",
                )
            }
            inputBuffer.put(pcm)
            codec.queueInputBuffer(
                inputIndex,
                0,
                pcm.size,
                presentationTimeUs,
                0,
            )
            hasInput = true
            lastInputPresentationTimeUs = presentationTimeUs
            submittedFrameCount++
            collectOutput(output, waitForEos = false, deadlineNanos = deadline)
            return output
        } catch (failure: Exception) {
            fault("submit", failure)
        }
    }

    fun drain(): List<EncodedAccessUnit> {
        checkOwnerThread()
        when (state) {
            State.Closed -> throw IllegalStateException("Encoder is closed.")
            State.Faulted -> throw IllegalStateException("Encoder is faulted.")
            State.Drained -> return emptyList()
            State.Active -> Unit
        }

        val output = mutableListOf<EncodedAccessUnit>()
        try {
            val deadline = deadlineNanos()
            val inputIndex = acquireInputBuffer(output, deadline)
            codec.queueInputBuffer(
                inputIndex,
                0,
                0,
                0,
                MediaCodec.BUFFER_FLAG_END_OF_STREAM,
            )
            inputEosQueued = true
            collectOutput(output, waitForEos = true, deadlineNanos = deadline)
            if (!outputEosSeen) {
                throw IllegalStateException("MediaCodec drain ended without output EOS.")
            }
            if (hasInput && !configValidated) {
                throw IllegalStateException("MediaCodec emitted no canonical AAC config.")
            }
            if (partialOutput.isNotEmpty()) {
                throw IllegalStateException("MediaCodec ended with a partial access unit.")
            }
            val expectedCandidateCount = if (submittedFrameCount == 0) {
                0
            } else {
                Math.addExact(
                    submittedFrameCount,
                    CODEC_ADDED_CANDIDATE_COUNT,
                )
            }
            if (completedCandidateCount != expectedCandidateCount) {
                throw IllegalStateException(
                    "MediaCodec produced $completedCandidateCount output candidates " +
                        "for $submittedFrameCount input frames; expected " +
                        "$expectedCandidateCount.",
                )
            }
            state = State.Drained
            return output
        } catch (failure: Exception) {
            fault("drain", failure)
        }
    }

    override fun close() {
        checkOwnerThread()
        if (state == State.Closed) {
            return
        }

        var failure: Throwable? = null
        if (started) {
            try {
                codec.stop()
            } catch (stopFailure: Throwable) {
                failure = stopFailure
            }
        }
        try {
            codec.release()
        } catch (releaseFailure: Throwable) {
            if (failure == null) {
                failure = releaseFailure
            } else {
                failure.addSuppressed(releaseFailure)
            }
        }
        started = false
        state = State.Closed
        if (failure != null) {
            throw IllegalStateException("MediaCodec close failed.", failure)
        }
    }

    private fun acquireInputBuffer(
        output: MutableList<EncodedAccessUnit>,
        deadlineNanos: Long,
    ): Int {
        while (true) {
            requireBeforeDeadline(deadlineNanos, "input buffer")
            val index = codec.dequeueInputBuffer(DEQUEUE_TIMEOUT_US)
            requireBeforeDeadline(deadlineNanos, "input buffer")
            if (index >= 0) {
                return index
            }
            if (index != MediaCodec.INFO_TRY_AGAIN_LATER) {
                throw IllegalStateException("Unexpected input result $index.")
            }
            collectOutput(output, waitForEos = false, deadlineNanos = deadlineNanos)
            requireBeforeDeadline(deadlineNanos, "input buffer")
        }
    }

    private fun collectOutput(
        output: MutableList<EncodedAccessUnit>,
        waitForEos: Boolean,
        deadlineNanos: Long,
    ) {
        val info = MediaCodec.BufferInfo()
        while (true) {
            requireBeforeDeadline(
                deadlineNanos,
                if (waitForEos) "output EOS" else "available output",
            )
            val timeoutUs = if (waitForEos) DEQUEUE_TIMEOUT_US else 0
            val index = codec.dequeueOutputBuffer(info, timeoutUs)
            requireBeforeDeadline(
                deadlineNanos,
                if (waitForEos) "output EOS" else "available output",
            )
            when {
                index >= 0 -> processOutputBuffer(index, info, output)
                index == MediaCodec.INFO_TRY_AGAIN_LATER -> {
                    if (!waitForEos) {
                        return
                    }
                    requireBeforeDeadline(deadlineNanos, "output EOS")
                }
                index == MediaCodec.INFO_OUTPUT_FORMAT_CHANGED ->
                    validateOutputFormat(codec.outputFormat)
                index == MediaCodec.INFO_OUTPUT_BUFFERS_CHANGED -> Unit
                else -> throw IllegalStateException("Unexpected output result $index.")
            }
            if (outputEosSeen) {
                return
            }
        }
    }

    private fun processOutputBuffer(
        index: Int,
        info: MediaCodec.BufferInfo,
        output: MutableList<EncodedAccessUnit>,
    ) {
        try {
            val flags = info.flags
            val isConfig = flags and MediaCodec.BUFFER_FLAG_CODEC_CONFIG != 0
            val isPartial = flags and MediaCodec.BUFFER_FLAG_PARTIAL_FRAME != 0
            val isEos = flags and MediaCodec.BUFFER_FLAG_END_OF_STREAM != 0
            val bytes = if (info.size == 0) {
                ByteArray(0)
            } else {
                val buffer = codec.getOutputBuffer(index)
                    ?: throw IllegalStateException("MediaCodec returned no output buffer.")
                copyRange(
                    buffer,
                    info.offset,
                    info.size,
                    if (isConfig) CANONICAL_CONFIG_BYTES else MAX_ACCESS_UNIT_BYTES,
                )
            }

            if (isConfig) {
                if (bytes.isNotEmpty()) {
                    validateCanonicalAudioSpecificConfig(bytes)
                    configValidated = true
                }
            } else if (bytes.isNotEmpty()) {
                if (!configValidated) {
                    throw IllegalStateException(
                        "MediaCodec emitted audio before canonical codec config.",
                    )
                }
                appendMediaBytes(bytes, info.presentationTimeUs, isPartial, output)
            }

            if (isEos) {
                if (partialOutput.isNotEmpty()) {
                    throw IllegalStateException(
                        "MediaCodec signaled EOS with an incomplete access unit.",
                    )
                }
                if (!inputEosQueued) {
                    throw IllegalStateException("MediaCodec emitted EOS before input EOS.")
                }
                outputEosSeen = true
            }
        } finally {
            codec.releaseOutputBuffer(index, false)
        }
    }

    private fun appendMediaBytes(
        bytes: ByteArray,
        presentationTimeUs: Long,
        isPartial: Boolean,
        output: MutableList<EncodedAccessUnit>,
    ) {
        if (partialOutput.isEmpty() && !isPartial) {
            if (bytes.size > MAX_ACCESS_UNIT_BYTES) {
                throw IllegalStateException("AAC access unit exceeds size limit.")
            }
            output += EncodedAccessUnit(bytes, presentationTimeUs)
            completedCandidateCount++
            return
        }

        if (partialOutput.isEmpty()) {
            partialPresentationTimeUs = presentationTimeUs
        }
        if (bytes.size > MAX_ACCESS_UNIT_BYTES - partialOutput.size) {
            throw IllegalStateException("AAC access unit exceeds size limit.")
        }
        partialOutput += bytes
        if (!isPartial) {
            output += EncodedAccessUnit(
                partialOutput,
                partialPresentationTimeUs,
            )
            completedCandidateCount++
            partialOutput = ByteArray(0)
        }
    }

    private fun validateInputFormat(format: MediaFormat) {
        if (format.getInteger(MediaFormat.KEY_SAMPLE_RATE) != SAMPLE_RATE) {
            throw IllegalStateException("MediaCodec input sample rate is not 48000 Hz.")
        }
        if (format.getInteger(MediaFormat.KEY_CHANNEL_COUNT) != CHANNEL_COUNT) {
            throw IllegalStateException("MediaCodec input is not stereo.")
        }
        if (
            format.getInteger(
                MediaFormat.KEY_PCM_ENCODING,
                AudioFormat.ENCODING_PCM_16BIT,
            ) != AudioFormat.ENCODING_PCM_16BIT
        ) {
            throw IllegalStateException("MediaCodec input is not PCM16.")
        }
    }

    private fun validateOutputFormat(format: MediaFormat) {
        if (format.getString(MediaFormat.KEY_MIME) != MediaFormat.MIMETYPE_AUDIO_AAC) {
            throw IllegalStateException("MediaCodec output MIME is not AAC.")
        }
        if (format.getInteger(MediaFormat.KEY_SAMPLE_RATE) != SAMPLE_RATE) {
            throw IllegalStateException("MediaCodec output sample rate is not 48000 Hz.")
        }
        if (format.getInteger(MediaFormat.KEY_CHANNEL_COUNT) != CHANNEL_COUNT) {
            throw IllegalStateException("MediaCodec output is not stereo.")
        }
        format.getByteBuffer("csd-0")?.let { config ->
            validateCanonicalAudioSpecificConfig(
                copyRemaining(config, CANONICAL_CONFIG_BYTES),
            )
            configValidated = true
        }
    }

    private fun requireActive(operation: String) {
        when (state) {
            State.Active -> Unit
            State.Drained -> throw IllegalStateException("Cannot $operation after drain.")
            State.Faulted -> throw IllegalStateException("Encoder is faulted.")
            State.Closed -> throw IllegalStateException("Encoder is closed.")
        }
    }

    private fun checkOwnerThread() {
        if (Thread.currentThread().id != ownerThreadId) {
            throw IllegalStateException("MediaCodec encoder is owner-thread-only.")
        }
    }

    private fun requireBeforeDeadline(deadlineNanos: Long, operation: String) {
        if (SystemClock.elapsedRealtimeNanos() >= deadlineNanos) {
            throw TimeoutException("Timed out waiting for MediaCodec $operation.")
        }
    }

    private fun deadlineNanos(): Long =
        SystemClock.elapsedRealtimeNanos() + OPERATION_TIMEOUT_NANOS

    private fun fault(operation: String, failure: Exception): Nothing {
        state = State.Faulted
        throw IllegalStateException("MediaCodec $operation failed.", failure)
    }

    private enum class State {
        Active,
        Drained,
        Faulted,
        Closed,
    }

    private companion object {
        const val SAMPLE_RATE = 48_000
        const val CHANNEL_COUNT = 2
        const val BIT_RATE = 192_000
        const val PCM_BYTES_PER_FRAME = 4096
        const val CODEC_ADDED_CANDIDATE_COUNT = 1
        const val MAX_ACCESS_UNIT_BYTES = 65_536
        const val CANONICAL_CONFIG_BYTES = 2
        const val DEQUEUE_TIMEOUT_US = 10_000L
        const val OPERATION_TIMEOUT_NANOS = 5_000_000_000L

        fun copyRange(
            buffer: ByteBuffer,
            offset: Int,
            size: Int,
            maximumSize: Int,
        ): ByteArray {
            val copy = buffer.duplicate()
            if (offset < 0 || size < 0 || offset > copy.limit() - size) {
                throw IllegalStateException("Invalid MediaCodec output buffer bounds.")
            }
            if (size > maximumSize) {
                throw IllegalStateException("MediaCodec output exceeds size limit.")
            }
            copy.position(offset)
            copy.limit(offset + size)
            return ByteArray(size).also { copy.get(it) }
        }

        fun copyRemaining(buffer: ByteBuffer, maximumSize: Int): ByteArray {
            val copy = buffer.duplicate()
            if (copy.remaining() > maximumSize) {
                throw IllegalStateException("MediaCodec output exceeds size limit.")
            }
            return ByteArray(copy.remaining()).also { copy.get(it) }
        }

        fun cleanupConstruction(
            codec: MediaCodec?,
            started: Boolean,
            failure: Throwable,
        ) {
            if (codec == null) {
                return
            }
            if (started) {
                try {
                    codec.stop()
                } catch (stopFailure: Throwable) {
                    failure.addSuppressed(stopFailure)
                }
            }
            try {
                codec.release()
            } catch (releaseFailure: Throwable) {
                failure.addSuppressed(releaseFailure)
            }
        }
    }
}
```

Do not add output PTS rewriting. The full-cycle gate requires zero candidates
for zero input and `N + 1` candidates for non-empty `N` input, retaining the
codec-added priming/padding candidate without semantic inference. It is a
strict Version 1 gate for the selected codec, not an Android scheduling
guarantee. `KEY_MAX_INPUT_SIZE` remains intentionally absent.

- [ ] **Step 2: Run every locally available GREEN gate**

```bash
ANDROID_HOME=/root/Android/Sdk \
ANDROID_SDK_ROOT=/root/Android/Sdk \
./sender-android/gradlew \
  -p sender-android \
  -Pandroid.aapt2FromMavenOverride=/root/.cache/openaudiolink/android-sdk-tools-35.0.2-aarch64/bin/aapt2 \
  :app:testDebugUnitTest :app:assembleDebug :app:assembleDebugAndroidTest
python3 tools/check_docs_consistency.py
python3 tools/audio/validate_aac_fixture.py
python3 tools/protocol/generate_golden_packets.py --check
git diff --check
```

Expected: all local gates pass. This proves compilation and pure contracts, not native execution.

- [ ] **Step 3: Commit implementation**

```bash
git add sender-android/app/src/main/java/com/openaudiolink/audio/MediaCodecAacEncoder.kt
git commit -m "feat: encode AAC with Android MediaCodec"
```

- [ ] **Step 4: Push and debug exact native behavior**

Push exact HEAD and watch the Android run. Require `unit` and `media-codec`
success. Inspect instrumentation logs for two codec names, two successful
lifecycle runs, exact `13 AU(s)` for each run, and each platform output
timestamp list.

If native CI fails, invoke `superpowers:systematic-debugging`. Change only one
observed MediaCodec assumption at a time. Do not weaken exact `11 90`, the
non-empty `N + 1` candidate boundary gate, raw-AU, EOS, owner-thread, or cleanup
requirements. If the platform demonstrates behavior contradicting the design,
update the design with evidence before changing the contract.

---
### Task 6: Specify Android-to-Windows artifact interoperability

**Files:**
- Modify: `receiver-windows/tests/OpenAudioLink.Tests/Receiver/MediaFoundationAacDecoderTests.cs`
- Modify: `.github/workflows/android.yml`

- [ ] **Step 1: Add the conditional Windows artifact oracle**

Add constants beside `ContinuousFixture`:

```csharp
private const string MediaCodecInteropEnabled =
    "OAL_MEDIACODEC_INTEROP";
private const string MediaCodecInteropPath =
    "OAL_MEDIACODEC_ADTS_PATH";
private const int MediaCodecInputFrameCount = 12;
private const int MediaCodecAddedCandidateCount = 1;
private const int MediaCodecExpectedOutputCandidateCount =
    MediaCodecInputFrameCount + MediaCodecAddedCandidateCount;
```

Add this test after `CanonicalFixtureDecodesTwiceToCompleteStereoPcm`:

```csharp
[TestMethod]
public void AndroidMediaCodecArtifactDecodesToCompleteStereoPcm()
{
    string enabled = Environment.GetEnvironmentVariable(MediaCodecInteropEnabled);
    if (string.IsNullOrEmpty(enabled))
    {
        return;
    }
    Assert.AreEqual("1", enabled);

    string path = Environment.GetEnvironmentVariable(MediaCodecInteropPath);
    Assert.IsFalse(string.IsNullOrEmpty(path), "interop artifact path is missing");
    Assert.IsTrue(File.Exists(path), "interop artifact does not exist: " + path);

    IReadOnlyList<byte[]> frames = SplitAdts(
        File.ReadAllBytes(path),
        MediaCodecExpectedOutputCandidateCount);
    RunMta(() =>
    {
        foreach (byte[] frame in frames)
        {
            Assert.AreEqual(
                4096,
                DecodeFrames(new[] { frame }).Length,
                "MediaCodec output candidate is not exactly one AAC access unit");
        }
        byte[] pcm = DecodeFrames(frames);
        AssertPcm(
            pcm,
            checked(MediaCodecExpectedOutputCandidateCount * 4096));
        Console.WriteLine(
            "MediaCodec interop decoded " + frames.Count + " access units to "
            + pcm.Length + " PCM bytes.");
    });
}
```

Replace `DecodeFixture`, and add the reusable helper:

```csharp
private static byte[] DecodeFixture()
{
    return DecodeFrames(SplitAdts(TestFixtures.Read(ContinuousFixture)));
}

private static byte[] DecodeFrames(IReadOnlyList<byte[]> frames)
{
    List<byte> pcm = new List<byte>();
    using (MediaFoundationAacDecoder decoder = new MediaFoundationAacDecoder())
    {
        foreach (byte[] frame in frames)
        {
            AddChunks(pcm, decoder.Submit(frame));
        }
        AddChunks(pcm, decoder.Drain());
    }
    return pcm.ToArray();
}
```

Change `AssertPcm` to accept an exact expected size:

```csharp
private static void AssertPcm(byte[] pcm, int expectedLength = 24576)
{
    Assert.AreEqual(expectedLength, pcm.Length);
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

Replace the existing ADTS rejection test so every newly strict header field has
a regression check:

```csharp
[TestMethod]
public void AdtsSplitterRejectsInvalidHeaderTrailingByteAndWrongFrameCount()
{
    Assert.ThrowsException<InvalidDataException>(() => SplitAdts(
        new byte[] { 0x00, 0xf0, 0x00, 0x00, 0x00, 0x00, 0x00 }));
    Assert.ThrowsException<InvalidDataException>(() => SplitAdts(
        new byte[] { 0xff, 0xf0, 0x00, 0x00, 0x00, 0x00, 0x00 }));

    byte[] wrongMpegVersion = TestFixtures.Read(ContinuousFixture);
    wrongMpegVersion[1] = (byte)(wrongMpegVersion[1] | 0x08);
    Assert.ThrowsException<InvalidDataException>(() => SplitAdts(wrongMpegVersion));

    byte[] wrongProfile = TestFixtures.Read(ContinuousFixture);
    wrongProfile[2] = (byte)(wrongProfile[2] & 0x3f);
    Assert.ThrowsException<InvalidDataException>(() => SplitAdts(wrongProfile));

    byte[] wrongRate = TestFixtures.Read(ContinuousFixture);
    wrongRate[2] = (byte)((wrongRate[2] & 0xc3) | (4 << 2));
    Assert.ThrowsException<InvalidDataException>(() => SplitAdts(wrongRate));

    byte[] wrongChannels = TestFixtures.Read(ContinuousFixture);
    wrongChannels[2] = (byte)(wrongChannels[2] & 0xfe);
    wrongChannels[3] = (byte)((wrongChannels[3] & 0x3f) | (1 << 6));
    Assert.ThrowsException<InvalidDataException>(() => SplitAdts(wrongChannels));

    byte[] multipleRawBlocks = TestFixtures.Read(ContinuousFixture);
    multipleRawBlocks[6] = (byte)(multipleRawBlocks[6] | 1);
    Assert.ThrowsException<InvalidDataException>(() => SplitAdts(multipleRawBlocks));

    byte[] trailing = TestFixtures.Read(ContinuousFixture);
    Array.Resize(ref trailing, trailing.Length + 1);
    Assert.ThrowsException<InvalidDataException>(() => SplitAdts(trailing));
    Assert.ThrowsException<InvalidDataException>(() => SplitAdts(new byte[0]));
}
```

Replace `SplitAdts` with a strict reusable parser:

```csharp
private static IReadOnlyList<byte[]> SplitAdts(
    byte[] data,
    int? expectedFrameCount = 6)
{
    List<byte[]> frames = new List<byte[]>();
    int offset = 0;
    while (offset < data.Length)
    {
        if (data.Length - offset < 7)
        {
            throw new InvalidDataException("truncated ADTS header");
        }
        if (data[offset] != 0xff || (data[offset + 1] & 0xf0) != 0xf0)
        {
            throw new InvalidDataException("invalid ADTS sync");
        }
        if ((data[offset + 1] & 0x0e) != 0)
        {
            throw new InvalidDataException("ADTS must use MPEG-4 layer 0");
        }
        if ((data[offset + 1] & 1) != 1)
        {
            throw new InvalidDataException("CRC-bearing ADTS is unsupported");
        }
        int profile = (data[offset + 2] >> 6) & 3;
        int sampleRateIndex = (data[offset + 2] >> 2) & 15;
        int channelConfiguration =
            ((data[offset + 2] & 1) << 2)
            | ((data[offset + 3] >> 6) & 3);
        if (profile != 1 || sampleRateIndex != 3 || channelConfiguration != 2)
        {
            throw new InvalidDataException(
                "ADTS must be AAC-LC, 48 kHz, stereo");
        }
        if ((data[offset + 6] & 3) != 0)
        {
            throw new InvalidDataException("ADTS frame must contain one raw block");
        }
        int length =
            ((data[offset + 3] & 3) << 11)
            | (data[offset + 4] << 3)
            | ((data[offset + 5] >> 5) & 7);
        if (length <= 7 || offset + length > data.Length)
        {
            throw new InvalidDataException("truncated ADTS frame");
        }
        byte[] raw = new byte[length - 7];
        Buffer.BlockCopy(data, offset + 7, raw, 0, raw.Length);
        frames.Add(raw);
        offset += length;
    }
    if (expectedFrameCount.HasValue && frames.Count != expectedFrameCount.Value)
    {
        throw new InvalidDataException(
            "ADTS frame count must be " + expectedFrameCount.Value);
    }
    return frames;
}
```

Existing splitter tests continue to prove the six-frame canonical fixture. The
interop gate deliberately requires twelve input frames plus one codec-added
priming/padding candidate: thirteen boundaries. It independently decodes each
candidate to exactly 4096 bytes, then verifies the continuous 53,248-byte
decode. The selected codec fails for batching, loss, or any count other than
non-empty `N + 1`; the extra candidate is retained without audio-content or
clock semantics.

- [ ] **Step 2: Add artifact extraction and the dependent Windows job**

Replace `.github/workflows/android.yml` with:

```yaml
name: android

on:
  pull_request:
  push:
    branches: ['phase-*']

jobs:
  unit:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-java@v4
        with:
          distribution: temurin
          java-version: '17'
      - uses: android-actions/setup-android@v3
      - run: ./gradlew :app:testDebugUnitTest
        working-directory: sender-android

  media-codec:
    runs-on: ubuntu-22.04
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-java@v4
        with:
          distribution: temurin
          java-version: '17'
      - uses: android-actions/setup-android@v3
      - name: Enable KVM
        run: |
          echo 'KERNEL=="kvm", GROUP="kvm", MODE="0666", OPTIONS+="static_node=kvm"' | sudo tee /etc/udev/rules.d/99-kvm4all.rules
          sudo udevadm control --reload-rules
          sudo udevadm trigger --name-match=kvm
      - name: Run API 29 MediaCodec tests and export AAC
        uses: reactivecircus/android-emulator-runner@v2
        with:
          api-level: 29
          target: default
          arch: x86_64
          working-directory: ./sender-android
          disable-animations: true
          script: |
            test "$(adb shell getprop ro.build.version.sdk | tr -d '\r')" = 29 && echo "Android API level: 29"
            adb logcat -c
            ./gradlew -Pandroid.injected.androidTest.leaveApksInstalledAfterRun=true :app:connectedDebugAndroidTest; TEST_STATUS=$?; adb logcat -d -s MediaCodecAacTest:I TestRunner:E AndroidJUnitRunner:E '*:S'; exit "$TEST_STATUS"
            adb shell pm path com.openaudiolink | grep -F 'package:'
            adb exec-out run-as com.openaudiolink cat files/mediacodec-aac-interop.adts > "$GITHUB_WORKSPACE/mediacodec-aac-interop.adts"
            test -s "$GITHUB_WORKSPACE/mediacodec-aac-interop.adts"
      - uses: actions/upload-artifact@v4
        with:
          name: mediacodec-aac-interop
          path: mediacodec-aac-interop.adts
          if-no-files-found: error
          retention-days: 1

  windows-interop:
    needs: media-codec
    runs-on: windows-2022
    steps:
      - uses: actions/checkout@v4
      - uses: microsoft/setup-msbuild@v2
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: 8.0.x
      - uses: actions/download-artifact@v4
        with:
          name: mediacodec-aac-interop
          path: interop
      - name: Decode Android MediaCodec output
        shell: pwsh
        env:
          OAL_TEST_ARCHITECTURE: x64
          OAL_MEDIACODEC_INTEROP: '1'
          OAL_MEDIACODEC_ADTS_PATH: ${{ github.workspace }}\interop\mediacodec-aac-interop.adts
        run: |
          if (!(Test-Path -LiteralPath $env:OAL_MEDIACODEC_ADTS_PATH)) {
            throw "MediaCodec AAC artifact is missing"
          }
          dotnet test receiver-windows/OpenAudioLink.sln -c Release --logger "console;verbosity=detailed" -- RunConfiguration.TargetPlatform=x64
```

The emulator action executes each physical `script` line independently; no line depends on a prior shell-local variable or `cd`.

- [ ] **Step 3: Run local non-native gates**

```bash
ANDROID_HOME=/root/Android/Sdk \
ANDROID_SDK_ROOT=/root/Android/Sdk \
./sender-android/gradlew \
  -p sender-android \
  -Pandroid.aapt2FromMavenOverride=/root/.cache/openaudiolink/android-sdk-tools-35.0.2-aarch64/bin/aapt2 \
  :app:testDebugUnitTest :app:assembleDebugAndroidTest
python3 tools/check_docs_consistency.py
git diff --check
```

The Linux host cannot compile the changed C# test; Windows CI remains authoritative.

- [ ] **Step 4: Commit and push the intended interop RED**

```bash
git add receiver-windows/tests/OpenAudioLink.Tests/Receiver/MediaFoundationAacDecoderTests.cs \
  .github/workflows/android.yml
git commit -m "test: specify Android Windows AAC interop"
```

Push exact HEAD. Expected:

- `docs`: success.
- standalone `windows` matrix: compiles and succeeds because the interop gate is absent there.
- Android `unit`: success.
- Android `media-codec`: native tests pass, then the `adb ... cat files/mediacodec-aac-interop.adts` line fails because no artifact is written yet.
- Android `windows-interop`: skipped because its dependency failed.

Require that exact missing-file failure; unrelated C#, emulator, codec, or workflow errors are not the intended RED.

---

### Task 7: Export exact native output and make interop GREEN

**Files:**
- Modify: `sender-android/app/src/androidTest/java/com/openaudiolink/audio/MediaCodecAacEncoderTest.kt`

- [ ] **Step 1: Write and validate ADTS only in instrumentation code**

Add imports:

```kotlin
import androidx.test.platform.app.InstrumentationRegistry
import org.junit.Assert.assertArrayEquals
import java.io.File
import java.io.FileOutputStream
```

Change the success test to export only the first completed run:

```kotlin
@Test
fun encodesAndDrainsTwiceInOneProcess() {
    val first = encodeOnce()
    val second = encodeOnce()

    assertTrue(first.isNotEmpty())
    assertTrue(second.isNotEmpty())
    writeAndValidateArtifact(first)
}
```

Add these helpers before the companion object:

```kotlin
private fun writeAndValidateArtifact(units: List<EncodedAccessUnit>) {
    val context = InstrumentationRegistry.getInstrumentation().targetContext
    val artifact = File(context.filesDir, ARTIFACT_NAME)
    FileOutputStream(artifact, false).use { stream ->
        units.forEach { unit ->
            stream.write(adtsHeader(unit.bytes.size))
            stream.write(unit.bytes)
        }
    }

    val stored = artifact.readBytes()
    var offset = 0
    units.forEach { unit ->
        assertTrue(stored.size - offset >= ADTS_HEADER_BYTES)
        assertEquals(0xff, stored[offset].toInt() and 0xff)
        assertEquals(0xf1, stored[offset + 1].toInt() and 0xff)
        val profile = (stored[offset + 2].toInt() ushr 6) and 3
        val sampleRateIndex = (stored[offset + 2].toInt() ushr 2) and 15
        val channelConfiguration =
            ((stored[offset + 2].toInt() and 1) shl 2) or
                ((stored[offset + 3].toInt() ushr 6) and 3)
        assertEquals(1, profile)
        assertEquals(3, sampleRateIndex)
        assertEquals(2, channelConfiguration)
        val frameLength =
            ((stored[offset + 3].toInt() and 3) shl 11) or
                ((stored[offset + 4].toInt() and 0xff) shl 3) or
                ((stored[offset + 5].toInt() ushr 5) and 7)
        assertEquals(ADTS_HEADER_BYTES + unit.bytes.size, frameLength)
        val raw = stored.copyOfRange(
            offset + ADTS_HEADER_BYTES,
            offset + frameLength,
        )
        assertArrayEquals(unit.bytes, raw)
        offset += frameLength
    }
    assertEquals(stored.size, offset)
}

private fun adtsHeader(rawSize: Int): ByteArray {
    val frameLength = Math.addExact(ADTS_HEADER_BYTES, rawSize)
    require(frameLength <= MAX_ADTS_FRAME_BYTES) {
        "AAC access unit is too large for ADTS."
    }
    return byteArrayOf(
        0xff.toByte(),
        0xf1.toByte(),
        ((AAC_LC_ADTS_PROFILE shl 6) or
            (SAMPLE_RATE_INDEX shl 2) or
            (CHANNEL_CONFIGURATION ushr 2)).toByte(),
        (((CHANNEL_CONFIGURATION and 3) shl 6) or
            (frameLength ushr 11)).toByte(),
        (frameLength ushr 3).toByte(),
        (((frameLength and 7) shl 5) or 0x1f).toByte(),
        0xfc.toByte(),
    )
}
```

Add constants to the companion object:

```kotlin
const val ARTIFACT_NAME = "mediacodec-aac-interop.adts"
const val ADTS_HEADER_BYTES = 7
const val MAX_ADTS_FRAME_BYTES = 8191
const val AAC_LC_ADTS_PROFILE = 1
const val SAMPLE_RATE_INDEX = 3
const val CHANNEL_CONFIGURATION = 2
```

No ADTS helper enters production source.

- [ ] **Step 2: Compile all Android sources locally**

```bash
ANDROID_HOME=/root/Android/Sdk \
ANDROID_SDK_ROOT=/root/Android/Sdk \
./sender-android/gradlew \
  -p sender-android \
  -Pandroid.aapt2FromMavenOverride=/root/.cache/openaudiolink/android-sdk-tools-35.0.2-aarch64/bin/aapt2 \
  :app:testDebugUnitTest :app:assembleDebug :app:assembleDebugAndroidTest
python3 tools/check_docs_consistency.py
python3 tools/audio/validate_aac_fixture.py
python3 tools/protocol/generate_golden_packets.py --check
git diff --check
```

Expected: all local gates pass.

- [ ] **Step 3: Commit and push interop GREEN**

```bash
git add sender-android/app/src/androidTest/java/com/openaudiolink/audio/MediaCodecAacEncoderTest.kt
git commit -m "test: export MediaCodec AAC interop artifact"
```

Push exact HEAD. Require all three workflows success. For the Android workflow, inspect jobs and logs:

```bash
HEAD_SHA=$(git rev-parse HEAD)
RUN_ID=$(gh api \
  'repos/imshuai/OpenAudioLink/actions/runs?branch=phase-1p-android-mediacodec-aac-encoder&per_page=30' \
  --jq ".workflow_runs[] | select(.head_sha == \"$HEAD_SHA\" and .event == \"push\" and .name == \"android\") | .id" \
  | head -n 1)
test -n "$RUN_ID"
gh run watch "$RUN_ID" --repo imshuai/OpenAudioLink --exit-status
gh api "repos/imshuai/OpenAudioLink/actions/runs/$RUN_ID/jobs?per_page=20" \
  --jq '.jobs[] | [.name,.status,.conclusion] | @tsv'
LOG_FILE=$(mktemp)
trap 'rm -f "$LOG_FILE"' EXIT
gh run view "$RUN_ID" --repo imshuai/OpenAudioLink --log > "$LOG_FILE"
grep -F 'Android API level: 29' "$LOG_FILE"
test "$(grep -cF 'AAC encoder:' "$LOG_FILE")" -ge 2
test "$(grep -cF 'AAC output: 13 AU(s)' "$LOG_FILE")" -ge 2
grep -F 'MediaCodec interop decoded 13 access units to 53248 PCM bytes.' "$LOG_FILE"
grep -F 'Passed!' "$LOG_FILE"
```

Expected Android jobs:

```text
unit             completed success
media-codec      completed success
windows-interop  completed success
```

The separate assertions prevent an unrelated success line from satisfying the
gate. They prove the API level, both native lifecycles, exact candidate count,
53,248-byte Windows decode, and successful x64 test run.

---
### Task 8: Align active codec, testing, and roadmap documentation

**Files:**
- Modify: `docs/04-Android.md`
- Modify: `docs/06-Audio.md`
- Modify: `docs/10-Testing.md`
- Modify: `docs/11-Roadmap.md`
- Modify: `docs/superpowers/specs/2026-07-14-phase-1n-aac-lc-wire-contract-design.md`
- Modify: `docs/superpowers/specs/2026-07-15-phase-1o-windows-media-foundation-aac-decoder-design.md`
- Modify: `docs/superpowers/specs/2026-07-15-phase-1p-android-mediacodec-aac-encoder-design.md`

- [ ] **Step 1: Correct Android encoder status and behavior**

In `docs/04-Android.md`, add this status paragraph immediately after “The encoder receives PCM frames and produces encoded AAC frames.”:

```text
Phase 1-P proves this boundary in a standalone fixed-format
MediaCodecAacEncoder. It is not connected to AudioRecord, queues, packetization,
HandshakeClient, or the sender runtime; those remain later phases.
```

Replace the hardware guarantee under `# Codec Technology` with:

```text
Version 1 uses Android's platform MediaCodec API with MIME type
`audio/mp4a-latm`. Phase 1-P accepts the default encoder selected by Android and
makes no hardware-acceleration guarantee. Hardware preference, software
fallback policy, and device coverage are later runtime and release decisions.
```

Replace the encoder lifecycle diagram with:

```text
Create MediaCodec

↓

Configure

↓

Start

↓

Submit PCM / Collect Available Output

↓

Queue Input EOS

↓

Drain Through Output EOS

↓

Stop

↓

Release
```

Replace the stale MediaCodec-output statements with:

```text
MediaCodec output may be buffered or fragmented. One submit can make zero, one,
or many complete raw AAC access units available. Codec-config output is
validated as `AudioSpecificConfig = 11 90` and never exposed as audio. Partial
buffers are assembled before an access unit is returned, and delayed output is
collected by draining through output EOS.

Android permits one audio output buffer to batch multiple access units without
exposing their internal boundaries. Version 1 rejects that behavior: a complete
encode-and-drain cycle must produce zero candidates for zero input and `N + 1`
candidates for non-empty `N` submitted 1024-sample PCM frames. The extra
candidate is retained only as codec-added priming/padding, without inferred
audio-content or clock semantics. This is a compatibility gate for the selected
codec, not a general MediaCodec guarantee.

Phase 1-P returns raw access units to its caller. A later runtime-integration
phase assigns wire frame metadata and wraps each complete access unit in one
AUDIO packet. Production output has no ADTS, LATM/LOAS, container, or
codec-config bytes.
```

Remove `No additional buffering occurs inside the encoder` and any statement that Phase 1-P immediately packetizes output.

- [ ] **Step 2: Correct future decoder-loop pseudocode**

In `docs/06-Audio.md`, replace the stale one-input/one-output decoder loop with:

```text
while running:

    frame = AACQueue.Take()

    for each pcm chunk in Decoder.Submit(frame):

        PCMQueue.Push(chunk)

on end of stream:

    for each delayed pcm chunk in Decoder.Drain():

        PCMQueue.Push(chunk)
```

State immediately below it:

```text
This remains future runtime-integration pseudocode. Submit may return zero, one,
or many chunks; Drain is required before shutdown.
```

- [ ] **Step 3: Record the exact native and interop gates**

In `docs/10-Testing.md`, replace the instrumentation bullet `MediaCodec AAC encoder availability` with:

```text
- MediaCodec AAC-LC encode, config, EOS/drain and repeated lifecycle on the
  fixed API 29 x86_64 emulator
```

Add this focused Phase 1-P subsection after the Android instrumentation section:

```markdown
## Phase 1-P MediaCodec Gate

GitHub Android CI runs the standalone `MediaCodecAacEncoder` on an API 29
x86_64 emulator. The test submits deterministic 48 kHz stereo PCM16 frames with
sample-derived timestamps, requires exact `AudioSpecificConfig = 11 90`, drains
through output EOS, repeats create/encode/drain/close, and rejects state,
thread, size and timestamp misuse. Each twelve-frame run must produce exactly
thirteen candidates: one retained codec-added priming/padding candidate beyond
the input count. The test records the selected codec name and platform output
timestamps without equating them to input timestamps, but does not require
hardware acceleration or a particular implementation.

The encoder may buffer input and fragment output, but the selected codec fails
for batching, loss, or a non-empty count other than `N + 1`. Test code assembles
thirteen candidates and adds ADTS headers only to preserve the proven boundaries
in an ephemeral CI artifact. The codec-added priming/padding candidate is
retained without inferred audio-content or clock semantics. A dependent
`windows-2022` x64 job removes those headers, decodes every candidate
independently to exactly 4096 PCM bytes, then decodes all thirteen through
`MediaFoundationAacDecoder`. The continuous oracle is exactly 53,248 PCM bytes
with non-zero energy in both channels, not an AAC or PCM hash.

This proves standalone Android-to-Windows codec compatibility only. It does not
prove MediaProjection, AudioRecord, queues, sender/receiver runtime integration,
hardware encoding, OEM compatibility, audible playback, latency, power, or
endurance.
```

- [ ] **Step 4: Make roadmap status truthful without claiming completion**

Replace the `# Current Status` table and following focus sentence in `docs/11-Roadmap.md` with:

```markdown
# Current Status

当前项目状态：

| Area | Status |
|------|--------|
| Product direction | Defined |
| Architecture documents | Active |
| Protocol specification | Version 1 implemented and tested |
| Android Sender | Phase 1 in progress; fake transport and standalone AAC encoder |
| Windows Receiver | Phase 1 in progress; fake runtime and standalone AAC decoder |
| Testing strategy | Active in CI |
| Public release | Not started |

Phase 0 已完成。当前重点是逐步把 Phase 1 的 standalone 组件接入真实采集、
传输、解码和播放主链路；Version 1.0 尚未完成。
```

Do not mark discovery, capture, foreground service, runtime codec integration, renderer, installer, or manual end-to-end playback complete.

- [ ] **Step 5: Correct completed design status and stale ownership wording**

In the Phase 1-N and Phase 1-O specs, change:

```text
**Status:** Draft for implementation
```

to:

```text
**Status:** Implemented
```

In Phase 1-N, replace:

```text
The Android MediaCodec phase later replaces the embedded development frame with live encoded output.
```

with:

```text
A later sender runtime-integration phase replaces the embedded development frame with live encoded output.
```

Only after the native emulator and Windows interop jobs are green, change the Phase 1-P spec status to:

```text
**Status:** Implemented
```

- [ ] **Step 6: Run focused stale-language and global docs checks**

```bash
python3 - <<'PY'
from pathlib import Path

android = Path('docs/04-Android.md').read_text()
audio = Path('docs/06-Audio.md').read_text()
testing = Path('docs/10-Testing.md').read_text()
roadmap = Path('docs/11-Roadmap.md').read_text()
phase_n = Path(
    'docs/superpowers/specs/2026-07-14-phase-1n-aac-lc-wire-contract-design.md'
).read_text()
phase_o = Path(
    'docs/superpowers/specs/2026-07-15-phase-1o-windows-media-foundation-aac-decoder-design.md'
).read_text()
phase_p = Path(
    'docs/superpowers/specs/2026-07-15-phase-1p-android-mediacodec-aac-encoder-design.md'
).read_text()
checks = {
    'standalone encoder boundary': 'standalone fixed-format' in android,
    'no hardware guarantee': 'makes no hardware-acceleration guarantee' in android,
    'eos drain lifecycle': 'Drain Through Output EOS' in android,
    'buffered output': 'zero, one,\nor many complete raw AAC' in android,
    'packetization later': 'later runtime-integration\nphase' in android,
    'future decoder drain': 'Decoder.Drain()' in audio,
    'api 29 gate': 'API 29 x86_64 emulator' in testing,
    'windows oracle': 'exactly 53,248' in testing,
    'roadmap in progress': 'Phase 1 in progress' in roadmap,
    'roadmap not complete': 'Version 1.0 尚未完成' in roadmap,
    'phase n implemented': '**Status:** Implemented' in phase_n,
    'phase o implemented': '**Status:** Implemented' in phase_o,
    'phase p implemented': '**Status:** Implemented' in phase_p,
    'runtime owns fake replacement':
        'sender runtime-integration phase replaces' in phase_n,
    'old hardware wording gone':
        "hardware-accelerated MediaCodec API" not in android,
    'old flush lifecycle gone': '\nFlush\n\n↓\n\nStop' not in android,
    'old no-buffer claim gone':
        'No additional buffering occurs inside the encoder' not in android,
}
failed = [name for name, ok in checks.items() if not ok]
if failed:
    raise SystemExit('stale Phase 1-P documentation: ' + ', '.join(failed))
print('focused Phase 1-P documentation ok')
PY
python3 tools/check_docs_consistency.py
git diff --check
```

Expected: focused and global checks pass.

- [ ] **Step 7: Commit documentation truth**

```bash
git add docs/04-Android.md docs/06-Audio.md docs/10-Testing.md docs/11-Roadmap.md \
  docs/superpowers/specs/2026-07-14-phase-1n-aac-lc-wire-contract-design.md \
  docs/superpowers/specs/2026-07-15-phase-1o-windows-media-foundation-aac-decoder-design.md \
  docs/superpowers/specs/2026-07-15-phase-1p-android-mediacodec-aac-encoder-design.md
git commit -m "docs: record Android MediaCodec interop"
```

Push exact HEAD and require `docs`, `windows`, and all Android jobs success before final review.

---

### Task 9: Verify, review, and integrate Phase 1-P

**Files:**
- Verify: every Phase 1-P file
- No new implementation files

- [ ] **Step 1: Run complete local verification**

```bash
python3 tools/check_docs_consistency.py
python3 -m unittest discover -s tools/audio -p 'test_*.py'
python3 tools/audio/validate_aac_fixture.py
python3 tools/protocol/generate_golden_packets.py --check
ANDROID_HOME=/root/Android/Sdk \
ANDROID_SDK_ROOT=/root/Android/Sdk \
./sender-android/gradlew \
  -p sender-android \
  -Pandroid.aapt2FromMavenOverride=/root/.cache/openaudiolink/android-sdk-tools-35.0.2-aarch64/bin/aapt2 \
  :app:testDebugUnitTest :app:assembleDebug :app:assembleDebugAndroidTest
git diff --check HEAD
git status --short --branch
```

Expected: docs/audio/protocol/Android compilation and JVM tests pass. Native Android and Windows runtime behavior remains exact-head CI-only.

- [ ] **Step 2: Audit the exact phase diff**

```bash
BASE_SHA=cd6f42cb7bec567dd9e4270ff8e3636504972f63
git diff --stat "$BASE_SHA"..HEAD
git diff --check "$BASE_SHA"..HEAD
git log --oneline --reverse "$BASE_SHA"..HEAD
```

Require the changed set to be exactly these files:

```text
.github/workflows/android.yml
docs/04-Android.md
docs/06-Audio.md
docs/10-Testing.md
docs/11-Roadmap.md
docs/superpowers/plans/2026-07-15-phase-1p-android-mediacodec-aac-encoder.md
docs/superpowers/specs/2026-07-14-phase-1n-aac-lc-wire-contract-design.md
docs/superpowers/specs/2026-07-15-phase-1o-windows-media-foundation-aac-decoder-design.md
docs/superpowers/specs/2026-07-15-phase-1p-android-mediacodec-aac-encoder-design.md
receiver-windows/tests/OpenAudioLink.Tests/Receiver/MediaFoundationAacDecoderTests.cs
sender-android/app/build.gradle.kts
sender-android/gradle.properties
sender-android/app/src/androidTest/java/com/openaudiolink/audio/MediaCodecAacEncoderTest.kt
sender-android/app/src/main/java/com/openaudiolink/audio/MediaCodecAacEncoder.kt
sender-android/app/src/test/java/com/openaudiolink/audio/MediaCodecAacEncoderContractTest.kt
```

Any Android fake runtime/UI/protocol, Windows production/runtime/renderer, manifest, golden packet, or checked-in audio binary change is scope drift and must be removed.

- [ ] **Step 3: Verify source and mirror tips**

Push with the execution-convention snippet. Require:

```text
HEAD == Gitea phase branch == GitHub mirror phase branch
```

- [ ] **Step 4: Require exact-head workflows and concrete job evidence**

Require exactly one **push-triggered** `docs`, `windows`, and `android`
workflow on HEAD. An open PR may legitimately add one `pull_request` run for
the same SHA; require it to succeed but do not misclassify it as a duplicate:

```bash
HEAD_SHA=$(git rev-parse HEAD)
RUNS_URL='repos/imshuai/OpenAudioLink/actions/runs?branch=phase-1p-android-mediacodec-aac-encoder&per_page=100'
for NAME in docs windows android; do
  COUNT=$(gh api "$RUNS_URL" \
    --jq "[.workflow_runs[] | select(.head_sha == \"$HEAD_SHA\" and .event == \"push\" and .name == \"$NAME\")] | length")
  test "$COUNT" = 1
  RUN_ID=$(gh api "$RUNS_URL" \
    --jq ".workflow_runs[] | select(.head_sha == \"$HEAD_SHA\" and .event == \"push\" and .name == \"$NAME\") | .id")
  gh run watch "$RUN_ID" --repo imshuai/OpenAudioLink --exit-status
done
PR_RUN_IDS=$(gh api "$RUNS_URL" \
  --jq ".workflow_runs[] | select(.head_sha == \"$HEAD_SHA\" and .event == \"pull_request\") | .id")
for RUN_ID in $PR_RUN_IDS; do
  gh run watch "$RUN_ID" --repo imshuai/OpenAudioLink --exit-status
done
```

Inspect Windows jobs and require `test (x86)` and `test (x64)` success.

Inspect Android jobs and require:

```text
unit
media-codec
windows-interop
```

all successful. Inspect logs and require:

- API property assertion equals `29`.
- instrumentation suite executes without skipped codec tests.
- two codec names/lifecycle runs and exact `13 AU(s)`/platform PTS lists are logged.
- artifact extraction and upload succeed.
- Windows independently decodes every candidate to 4096 bytes and the log
  prints `13 access units` and exactly `53248 PCM bytes`.
- Windows interop testhost is x64.

Do not treat workflow or matrix labels as runtime proof.

- [ ] **Step 5: Request final two-stage review**

Use `superpowers:requesting-code-review` with:

```text
BASE_SHA=cd6f42cb7bec567dd9e4270ff8e3636504972f63
HEAD_SHA=$(git rev-parse HEAD)
REQUIREMENTS=docs/superpowers/specs/2026-07-15-phase-1p-android-mediacodec-aac-encoder-design.md
PLAN=docs/superpowers/plans/2026-07-15-phase-1p-android-mediacodec-aac-encoder.md
```

First review full specification compliance. Only after approval, review code quality with special attention to:

- MediaCodec state and buffer ownership;
- actual PCM16 input-format confirmation;
- codec-config validation before output;
- partial-frame assembly and pre-allocation memory bound;
- zero-input/zero-candidate and non-empty `N`-input/`N + 1`-candidate
  batching/loss/other-count rejection;
- EOS-with-data ordering and drain deadline;
- constructor/close cleanup;
- emulator artifact provenance;
- C# parser/oracle false positives;
- CI environment gates;
- unnecessary abstractions or dependencies.

Fix every Critical and Important issue, rerun all checks, repush, and reverify the corrected exact HEAD.

- [ ] **Step 6: Finish and merge**

Invoke `superpowers:finishing-a-development-branch`. The previously established project workflow selects local fast-forward integration after exact-head branch CI is green:

1. Fetch and prove `origin/main` remains the phase base or an ancestor of HEAD.
2. Fast-forward local `main` to the phase branch.
3. Rerun complete local verification on merged `main`.
4. Push `main` to Gitea.
5. Wait until GitHub `main` mirrors the same SHA.
6. Prove the `main` push created no duplicate Actions runs.
7. Remove the owned worktree and delete the merged local phase branch; retain the remote phase branch unless explicitly requested otherwise.

---

## Execution Order And Review Gates

For every task:

1. Follow RED → minimum GREEN; infrastructure baseline is the only configuration exception.
2. Commit only that task's files.
3. Run specification-compliance review.
4. Run code-quality review.
5. Fix and re-review every Critical or Important finding.
6. Push and inspect exact-head CI when the task requires native evidence.

The expected failing heads are only:

- Task 2: unresolved canonical ASC validator.
- Task 4: unresolved native encoder class.
- Task 6: missing emulator-produced ADTS artifact.

Every later head must return to green before advancing. Emulator or Windows-native truth must come from live exact-head behavior, not source inference.
