# Phase 1-K Android Manual Fake Connect UI Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a minimal Android sender screen that lets a user enter a Windows receiver host and run the existing fake TCP stream.

**Architecture:** Keep network/protocol code unchanged. Add a tiny JVM-testable `ManualConnectController` for host trimming and result mapping, then wire `MainActivity` with native Android Views and a background `Thread` that calls `TcpHandshakeClient().connect(host)`.

**Tech Stack:** Kotlin, Android SDK native Views, JUnit 4 JVM tests, existing `TcpHandshakeClient`, existing GitHub Actions Android/Windows/docs workflows.

---

## Scope Check

This plan implements only `docs/superpowers/specs/2026-07-13-phase-1k-android-manual-fake-connect-ui-design.md`.

In scope:

- Manual host/IP input in `MainActivity`.
- A connect button that triggers the existing fake stream client.
- UI status values: `Idle`, `Connecting`, `Success`, `Failed`.
- JVM unit tests for manual connection decision logic.

Out of scope:

- Discovery, mDNS, QR codes, pairing, or cached receivers.
- MediaProjection, AudioPlaybackCapture, foreground service, AAC encoder, or real audio capture.
- Compose, ViewModel, coroutines, AndroidX, Robolectric, instrumentation tests, or new dependencies.
- Saved settings, host history, richer validation, or detailed error messages.
- Windows receiver changes.
- Protocol wire-format changes.

---

## Files and Responsibilities

- Create `sender-android/app/src/test/java/com/openaudiolink/ManualConnectControllerTest.kt`
  - Proves empty input does not call the connector.
  - Proves host trimming.
  - Proves `true`, `false`, and exception results map to status.

- Create `sender-android/app/src/main/java/com/openaudiolink/ManualConnectController.kt`
  - Contains `ManualConnectStatus`.
  - Maps a host string and connector lambda to final status.
  - Has no Android framework dependency.

- Modify `sender-android/app/src/main/java/com/openaudiolink/MainActivity.kt`
  - Replaces skeleton `TextView` with native View layout.
  - Runs `TcpHandshakeClient().connect(host)` on a background `Thread`.
  - Updates status/button state on the UI thread.

No Gradle, manifest, protocol, network client, Windows, or workflow changes are needed.

---

### Task 1: Add manual connect controller tests

**Files:**
- Create: `sender-android/app/src/test/java/com/openaudiolink/ManualConnectControllerTest.kt`

- [ ] **Step 1: Write the failing JVM tests**

Create `sender-android/app/src/test/java/com/openaudiolink/ManualConnectControllerTest.kt`:

```kotlin
package com.openaudiolink

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Test
import java.io.IOException

class ManualConnectControllerTest {
    @Test
    fun emptyHostFailsWithoutCallingConnector() {
        var called = false
        val status = ManualConnectController {
            called = true
            true
        }.connect("   ")

        assertEquals(ManualConnectStatus.Failed, status)
        assertFalse(called)
    }

    @Test
    fun trimsHostBeforeConnecting() {
        var connectedHost = ""
        val status = ManualConnectController { host ->
            connectedHost = host
            true
        }.connect("  192.168.3.20  ")

        assertEquals(ManualConnectStatus.Success, status)
        assertEquals("192.168.3.20", connectedHost)
    }

    @Test
    fun successfulConnectorReturnsSuccess() {
        val status = ManualConnectController { true }.connect("receiver.local")

        assertEquals(ManualConnectStatus.Success, status)
    }

    @Test
    fun failedConnectorReturnsFailed() {
        val status = ManualConnectController { false }.connect("receiver.local")

        assertEquals(ManualConnectStatus.Failed, status)
    }

    @Test
    fun connectorExceptionReturnsFailed() {
        val status = ManualConnectController { throw IOException("boom") }.connect("receiver.local")

        assertEquals(ManualConnectStatus.Failed, status)
    }
}
```

- [ ] **Step 2: Verify RED**

Run where Android SDK is available:

```bash
cd sender-android
./gradlew :app:testDebugUnitTest --tests com.openaudiolink.ManualConnectControllerTest
```

Expected: FAIL because `ManualConnectController` and `ManualConnectStatus` do not exist.

On this Linux workspace without Android SDK, record the local runner gap:

```bash
if [ -n "${ANDROID_HOME:-}" ]; then cd sender-android && ./gradlew :app:testDebugUnitTest --tests com.openaudiolink.ManualConnectControllerTest; else echo 'ANDROID_HOME not set; Android tests require CI'; fi
```

Expected locally: `ANDROID_HOME not set; Android tests require CI`.

- [ ] **Step 3: Commit the failing tests**

```bash
git add sender-android/app/src/test/java/com/openaudiolink/ManualConnectControllerTest.kt
git commit -m "test: cover manual connect status mapping"
```

---

### Task 2: Add the minimal controller

**Files:**
- Create: `sender-android/app/src/main/java/com/openaudiolink/ManualConnectController.kt`
- Test: `sender-android/app/src/test/java/com/openaudiolink/ManualConnectControllerTest.kt`

- [ ] **Step 1: Implement the minimal controller**

Create `sender-android/app/src/main/java/com/openaudiolink/ManualConnectController.kt`:

```kotlin
package com.openaudiolink

enum class ManualConnectStatus {
    Idle,
    Connecting,
    Success,
    Failed,
}

class ManualConnectController(
    private val connector: (String) -> Boolean,
) {
    fun connect(host: String): ManualConnectStatus {
        val trimmed = host.trim()
        if (trimmed.isEmpty()) return ManualConnectStatus.Failed

        return try {
            if (connector(trimmed)) ManualConnectStatus.Success else ManualConnectStatus.Failed
        } catch (_: Exception) {
            ManualConnectStatus.Failed
        }
    }
}
```

- [ ] **Step 2: Verify GREEN**

Run where Android SDK is available:

```bash
cd sender-android
./gradlew :app:testDebugUnitTest --tests com.openaudiolink.ManualConnectControllerTest
```

Expected: PASS for `ManualConnectControllerTest`.

On this Linux workspace without Android SDK, record the local runner gap:

```bash
if [ -n "${ANDROID_HOME:-}" ]; then cd sender-android && ./gradlew :app:testDebugUnitTest --tests com.openaudiolink.ManualConnectControllerTest; else echo 'ANDROID_HOME not set; Android tests require CI'; fi
```

Expected locally: `ANDROID_HOME not set; Android tests require CI`.

- [ ] **Step 3: Commit the controller**

```bash
git add sender-android/app/src/main/java/com/openaudiolink/ManualConnectController.kt
git commit -m "feat: add manual connect controller"
```

---

### Task 3: Wire MainActivity manual connect UI

**Files:**
- Modify: `sender-android/app/src/main/java/com/openaudiolink/MainActivity.kt`

- [ ] **Step 1: Replace the skeleton Activity UI**

Replace `sender-android/app/src/main/java/com/openaudiolink/MainActivity.kt` with:

```kotlin
package com.openaudiolink

import android.app.Activity
import android.os.Bundle
import android.view.inputmethod.EditorInfo
import android.widget.Button
import android.widget.EditText
import android.widget.LinearLayout
import android.widget.TextView
import com.openaudiolink.network.TcpHandshakeClient

class MainActivity : Activity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)

        val hostInput = EditText(this).apply {
            hint = "Windows receiver IP or host"
            singleLine = true
            imeOptions = EditorInfo.IME_ACTION_DONE
        }
        val connectButton = Button(this).apply { text = "Connect Fake Stream" }
        val statusText = TextView(this).apply { text = ManualConnectStatus.Idle.name }
        val controller = ManualConnectController { host -> TcpHandshakeClient().connect(host) }

        connectButton.setOnClickListener {
            val host = hostInput.text.toString()
            if (host.trim().isEmpty()) {
                statusText.text = ManualConnectStatus.Failed.name
                return@setOnClickListener
            }

            statusText.text = ManualConnectStatus.Connecting.name
            connectButton.isEnabled = false

            Thread {
                val result = controller.connect(host)
                runOnUiThread {
                    statusText.text = result.name
                    connectButton.isEnabled = true
                }
            }.start()
        }

        setContentView(LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(32, 32, 32, 32)
            addView(TextView(this@MainActivity).apply { text = "OpenAudioLink Sender" })
            addView(hostInput)
            addView(connectButton)
            addView(statusText)
        })
    }
}
```

- [ ] **Step 2: Verify Android unit tests still pass**

Run where Android SDK is available:

```bash
cd sender-android
./gradlew :app:testDebugUnitTest
```

Expected: PASS.

On this Linux workspace without Android SDK:

```bash
if [ -n "${ANDROID_HOME:-}" ]; then cd sender-android && ./gradlew :app:testDebugUnitTest; else echo 'ANDROID_HOME not set; Android tests require CI'; fi
```

Expected locally: `ANDROID_HOME not set; Android tests require CI`.

- [ ] **Step 3: Commit the Activity wiring**

```bash
git add sender-android/app/src/main/java/com/openaudiolink/MainActivity.kt
git commit -m "feat: add android manual fake connect ui"
```

---

### Task 4: Final validation and branch push

**Files:**
- Validate repository state only.

- [ ] **Step 1: Run local checks**

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

`git diff --check HEAD` should print no output.

- [ ] **Step 2: Record platform-test handoff**

```bash
if command -v dotnet >/dev/null 2>&1; then dotnet test receiver-windows/OpenAudioLink.sln -c Release; else echo 'dotnet not found; Windows tests require CI'; fi
if [ -n "${ANDROID_HOME:-}" ]; then cd sender-android && ./gradlew :app:testDebugUnitTest; else echo 'ANDROID_HOME not set; Android tests require CI'; fi
```

Expected locally in this workspace:

```text
dotnet not found; Windows tests require CI
ANDROID_HOME not set; Android tests require CI
```

- [ ] **Step 3: Push the phase branch**

Use the known MTU workaround for the Gitea host:

```bash
ip route add 192.168.3.20/32 via 192.168.4.1 dev eth0 mtu 1200 || true
git push -u origin phase-1k-android-manual-fake-connect-ui
git fetch origin phase-1k-android-manual-fake-connect-ui
printf 'local=%s\nremote=%s\n' "$(git rev-parse --short HEAD)" "$(git rev-parse --short origin/phase-1k-android-manual-fake-connect-ui)"
git diff --exit-code HEAD origin/phase-1k-android-manual-fake-connect-ui --stat
ip route del 192.168.3.20/32 via 192.168.4.1 dev eth0 || true
```

Expected: local and remote commit hashes match; diff command exits 0.

- [ ] **Step 4: Check GitHub Actions**

```bash
gh run list -R imshuai/OpenAudioLink -L 10
```

Expected: `docs`, `android`, and `windows` runs for branch `phase-1k-android-manual-fake-connect-ui` complete with `success`.
