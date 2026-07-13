# Phase 1-L Windows Rendered Frame Status Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Show the fake rendered frame count in the Windows receiver UI so manual Android-to-Windows fake stream validation is visible on the receiver.

**Architecture:** Keep the fake receiver pipeline unchanged. Add one optional rendered-count callback to `ReceiverRuntime.Start(...)`, then use it from `MainForm` to update a second WinForms label.

**Tech Stack:** C#/.NET Framework 4.8, WinForms, MSTest, existing `ReceiverRuntime`, existing fake AAC decode/render pipeline, Python docs/golden checks.

---

## Scope Check

This plan implements only `docs/superpowers/specs/2026-07-13-phase-1l-windows-rendered-frame-status-design.md`.

In scope:

- WinForms visible text for `Rendered frames: 0` and later `Rendered frames: 3`.
- Optional `ReceiverRuntime` callback after fake render drain.
- WinForms tests for startup text and fake stream status update.

Out of scope:

- Real audio playback, WASAPI, Media Foundation, NAudio, device selection, or volume controls.
- Discovery, mDNS, pairing, reconnect policy, or session history.
- Logging subsystem, diagnostics panel, settings UI, tray behavior, or installer work.
- Android sender changes.
- Protocol wire-format changes.
- New external dependencies.

---

## Files and Responsibilities

- Modify `receiver-windows/tests/OpenAudioLink.Tests/UI/MainFormTests.cs`
  - Assert startup includes `Rendered frames: 0`.
  - Send a full fake stream through `MainForm` and assert visible text includes `Rendered frames: 3`.

- Modify `receiver-windows/src/OpenAudioLink/Receiver/ReceiverRuntime.cs`
  - Add optional `Action<int> renderedCountChanged` to `Start(...)`.
  - Invoke the callback after `renderer.Drain(...)`.
  - Keep `StartLoopback(...)` source-compatible.

- Modify `receiver-windows/src/OpenAudioLink/UI/MainForm.cs`
  - Add a second label for rendered frame count.
  - Pass a rendered-count callback into `ReceiverRuntime.Start(...)`.
  - Marshal updates via `BeginInvoke` when the label has a WinForms handle and requires it.

No project file, protocol, Android, workflow, or dependency changes are needed.

---

### Task 1: Add WinForms tests for rendered frame status

**Files:**
- Modify: `receiver-windows/tests/OpenAudioLink.Tests/UI/MainFormTests.cs`

- [ ] **Step 1: Update startup assertion and add fake stream test**

In `receiver-windows/tests/OpenAudioLink.Tests/UI/MainFormTests.cs`, update `ConstructorStartsReceiverRuntimeAndShowsPort` to include:

```csharp
StringAssert.Contains(VisibleText(form), "Rendered frames: 0");
```

Then add this test method after `ConstructorStartsReceiverRuntimeAndShowsPort`:

```csharp
[TestMethod]
public void FakeStreamUpdatesRenderedFrameStatus()
{
    RunSta(() =>
    {
        using (MainForm form = new MainForm())
        using (TcpClient client = Connect(form.ListeningPort))
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

            byte[] ping = HandshakePayloads.Ping(5u, 123456005UL);
            Write(stream, ProtocolConstants.PacketTypePing, 6u, ping);
            AssertPacket(stream, ProtocolConstants.PacketTypePong, ping);

            StringAssert.Contains(VisibleText(form), "Rendered frames: 3");
            Write(stream, ProtocolConstants.PacketTypeStopStream, 7u, new byte[0]);
        }
    });
}
```

- [ ] **Step 2: Verify RED**

Run on a Windows/.NET build host:

```powershell
dotnet test receiver-windows/OpenAudioLink.sln -c Release --filter MainFormTests
```

Expected: FAIL because `MainForm` does not yet display `Rendered frames: 0` or update to `Rendered frames: 3`.

On this Linux workspace, record the unavailable runner:

```bash
if command -v dotnet >/dev/null 2>&1; then dotnet test receiver-windows/OpenAudioLink.sln -c Release --filter MainFormTests; else echo 'dotnet not found; Windows tests require CI'; fi
```

Expected locally: `dotnet not found; Windows tests require CI`.

- [ ] **Step 3: Commit the failing tests**

```bash
git add receiver-windows/tests/OpenAudioLink.Tests/UI/MainFormTests.cs
git commit -m "test: expect rendered frame status in main form"
```

---

### Task 2: Add rendered-count callback to ReceiverRuntime

**Files:**
- Modify: `receiver-windows/src/OpenAudioLink/Receiver/ReceiverRuntime.cs`
- Test: `receiver-windows/tests/OpenAudioLink.Tests/UI/MainFormTests.cs`

- [ ] **Step 1: Update ReceiverRuntime.Start**

In `receiver-windows/src/OpenAudioLink/Receiver/ReceiverRuntime.cs`, replace the `Start` method with:

```csharp
public static ReceiverRuntime Start(IPAddress address, int port, int queueCapacity = 8, Action<int> renderedCountChanged = null)
{
    if (address == null)
    {
        throw new ArgumentNullException(nameof(address));
    }

    renderedCountChanged = renderedCountChanged ?? (_ => { });
    AudioFrameQueue queue = new AudioFrameQueue(queueCapacity);
    FakeAacDecoder decoder = new FakeAacDecoder();
    FakeAudioRenderer renderer = new FakeAudioRenderer();
    TcpReceiver receiver = TcpReceiver.Start(address, port, payload =>
    {
        queue.Enqueue(payload);
        renderer.Drain(queue, decoder);
        renderedCountChanged(renderer.RenderedCount);
    });

    return new ReceiverRuntime(queue, renderer, receiver);
}
```

Keep `StartLoopback` unchanged:

```csharp
public static ReceiverRuntime StartLoopback(int queueCapacity = 8)
{
    return Start(IPAddress.Loopback, 0, queueCapacity);
}
```

- [ ] **Step 2: Run targeted tests**

Run on Windows/.NET:

```powershell
dotnet test receiver-windows/OpenAudioLink.sln -c Release --filter MainFormTests
```

Expected: still FAIL because `MainForm` has not wired the callback into a visible label yet.

On this Linux workspace:

```bash
if command -v dotnet >/dev/null 2>&1; then dotnet test receiver-windows/OpenAudioLink.sln -c Release --filter MainFormTests; else echo 'dotnet not found; Windows tests require CI'; fi
```

Expected locally: `dotnet not found; Windows tests require CI`.

- [ ] **Step 3: Commit runtime callback**

```bash
git add receiver-windows/src/OpenAudioLink/Receiver/ReceiverRuntime.cs
git commit -m "feat: expose rendered frame count callback"
```

---

### Task 3: Wire MainForm rendered frame label

**Files:**
- Modify: `receiver-windows/src/OpenAudioLink/UI/MainForm.cs`
- Test: `receiver-windows/tests/OpenAudioLink.Tests/UI/MainFormTests.cs`

- [ ] **Step 1: Replace MainForm with rendered status UI**

Replace `receiver-windows/src/OpenAudioLink/UI/MainForm.cs` with:

```csharp
using System;
using System.Drawing;
using System.Net;
using System.Windows.Forms;
using OpenAudioLink.Protocol;
using OpenAudioLink.Receiver;

namespace OpenAudioLink
{
    public sealed class MainForm : Form
    {
        private readonly Label renderedFramesLabel;
        private readonly ReceiverRuntime runtime;

        public MainForm()
        {
            Text = "OpenAudioLink Receiver";
            Size = new Size(480, 240);

            Label portLabel = new Label
            {
                AutoSize = true,
                Location = new Point(24, 24),
                Text = "Listening on TCP port ..."
            };
            renderedFramesLabel = new Label
            {
                AutoSize = true,
                Location = new Point(24, 56),
                Text = RenderedFramesText(0)
            };

            Controls.Add(portLabel);
            Controls.Add(renderedFramesLabel);

            runtime = ReceiverRuntime.Start(IPAddress.Any, ProtocolConstants.DefaultPort, renderedCountChanged: UpdateRenderedFrames);
            ListeningPort = runtime.Port;
            portLabel.Text = "Listening on TCP port " + ListeningPort;
        }

        public int ListeningPort { get; }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                runtime.Dispose();
            }

            base.Dispose(disposing);
        }

        private void UpdateRenderedFrames(int renderedCount)
        {
            if (IsDisposed || renderedFramesLabel.IsDisposed)
            {
                return;
            }

            if (renderedFramesLabel.InvokeRequired && renderedFramesLabel.IsHandleCreated)
            {
                try
                {
                    renderedFramesLabel.BeginInvoke(new Action(() => UpdateRenderedFrames(renderedCount)));
                }
                catch (InvalidOperationException)
                {
                }

                return;
            }

            renderedFramesLabel.Text = RenderedFramesText(renderedCount);
        }

        private static string RenderedFramesText(int renderedCount)
        {
            return "Rendered frames: " + renderedCount;
        }
    }
}
```

- [ ] **Step 2: Verify GREEN**

Run on Windows/.NET:

```powershell
dotnet test receiver-windows/OpenAudioLink.sln -c Release --filter MainFormTests
```

Expected: PASS for `MainFormTests`.

On this Linux workspace:

```bash
if command -v dotnet >/dev/null 2>&1; then dotnet test receiver-windows/OpenAudioLink.sln -c Release --filter MainFormTests; else echo 'dotnet not found; Windows tests require CI'; fi
```

Expected locally: `dotnet not found; Windows tests require CI`.

- [ ] **Step 3: Commit UI wiring**

```bash
git add receiver-windows/src/OpenAudioLink/UI/MainForm.cs
git commit -m "feat: show rendered frame count in receiver ui"
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
git push -u origin phase-1l-windows-rendered-frame-status
git fetch origin phase-1l-windows-rendered-frame-status
printf 'local=%s\nremote=%s\n' "$(git rev-parse --short HEAD)" "$(git rev-parse --short origin/phase-1l-windows-rendered-frame-status)"
git diff --exit-code HEAD origin/phase-1l-windows-rendered-frame-status --stat
ip route del 192.168.3.20/32 via 192.168.4.1 dev eth0 || true
```

Expected: local and remote commit hashes match; diff command exits 0.

- [ ] **Step 4: Check GitHub Actions**

```bash
gh run list -R imshuai/OpenAudioLink -L 10
```

Expected: `docs`, `android`, and `windows` runs for branch `phase-1l-windows-rendered-frame-status` complete with `success`.
