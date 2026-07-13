# Phase 1-J Windows Default LAN Listener Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the WinForms receiver app start its fake receiver runtime on the protocol default TCP port instead of a loopback-only dynamic port.

**Architecture:** Keep `ReceiverRuntime` and protocol code unchanged. `MainForm` switches from `ReceiverRuntime.StartLoopback()` to `ReceiverRuntime.Start(IPAddress.Any, ProtocolConstants.DefaultPort)` and existing WinForms tests assert the default port behavior.

**Tech Stack:** C#/.NET Framework 4.8, WinForms, MSTest, existing `ReceiverRuntime`, existing protocol constants, Python docs/golden checks.

---

## Scope Check

This plan implements only `docs/superpowers/specs/2026-07-13-phase-1j-windows-default-lan-listener-design.md`.

In scope:

- `MainForm` default listener address/port change.
- WinForms tests updated to assert `ProtocolConstants.DefaultPort`.

Out of scope:

- Configuration files or configurable ports.
- Discovery, mDNS, UDP, pairing, or receiver identity.
- Android UI or Android sender changes.
- Real AAC decode.
- WASAPI, NAudio, Media Foundation, or speaker playback.
- Firewall rule management.
- Tray behavior, installer work, logs, diagnostics, or settings UI.
- Protocol wire-format changes.
- New dependencies.

---

## Files and Responsibilities

- Modify `receiver-windows/tests/OpenAudioLink.Tests/UI/MainFormTests.cs`
  - Assert normal `MainForm` startup uses `ProtocolConstants.DefaultPort`.
  - Continue proving `HELLO -> WELCOME` through the form-owned runtime.
  - Continue proving dispose closes the listener.

- Modify `receiver-windows/src/OpenAudioLink/UI/MainForm.cs`
  - Start `ReceiverRuntime` on `IPAddress.Any` and `ProtocolConstants.DefaultPort`.
  - Keep `ListeningPort` and label behavior.

No `.csproj`, protocol constant, runtime, Android, or workflow changes are needed.

---

### Task 1: Update WinForms tests for default-port expectation

**Files:**
- Modify: `receiver-windows/tests/OpenAudioLink.Tests/UI/MainFormTests.cs`

- [ ] **Step 1: Update failing test expectation**

In `receiver-windows/tests/OpenAudioLink.Tests/UI/MainFormTests.cs`, change the startup assertion from non-zero port to default port:

```csharp
Assert.AreEqual(ProtocolConstants.DefaultPort, form.ListeningPort);
StringAssert.Contains(VisibleText(form), "Listening on TCP port " + ProtocolConstants.DefaultPort);
```

The full `ConstructorStartsReceiverRuntimeAndShowsPort` test should be:

```csharp
[TestMethod]
public void ConstructorStartsReceiverRuntimeAndShowsPort()
{
    RunSta(() =>
    {
        using (MainForm form = new MainForm())
        {
            Assert.AreEqual(ProtocolConstants.DefaultPort, form.ListeningPort);
            StringAssert.Contains(VisibleText(form), "Listening on TCP port " + ProtocolConstants.DefaultPort);

            using (TcpClient client = Connect(form.ListeningPort))
            {
                NetworkStream stream = client.GetStream();
                Write(stream, ProtocolConstants.PacketTypeHello, 1u, HandshakePayloads.Hello("Android Phone", "1.0.0", ProtocolConstants.PlatformAndroid, ProtocolConstants.CapabilityAacSupported));
                AssertPacket(stream, ProtocolConstants.PacketTypeWelcome, HandshakePayloads.Welcome(ProtocolConstants.ResultSuccess, "Windows PC", "1.0.0", 1));
                Write(stream, ProtocolConstants.PacketTypeStopStream, 2u, new byte[0]);
            }
        }
    });
}
```

- [ ] **Step 2: Run tests and verify RED**

Run on a Windows/.NET build host:

```powershell
dotnet test receiver-windows/OpenAudioLink.sln -c Release --filter MainFormTests
```

Expected: FAIL because `MainForm` currently uses `ReceiverRuntime.StartLoopback()` and returns a dynamic port, not `ProtocolConstants.DefaultPort`.

On this Linux workspace, run:

```bash
if command -v dotnet >/dev/null 2>&1; then dotnet test receiver-windows/OpenAudioLink.sln -c Release --filter MainFormTests; else echo 'dotnet not found; Windows tests require CI'; fi
```

Expected locally if `dotnet` is absent: `dotnet not found; Windows tests require CI`.

- [ ] **Step 3: Commit failing test**

```bash
git add receiver-windows/tests/OpenAudioLink.Tests/UI/MainFormTests.cs
git commit -m "test: expect main form default listener port"
```

---

### Task 2: Start MainForm runtime on default LAN listener

**Files:**
- Modify: `receiver-windows/src/OpenAudioLink/UI/MainForm.cs`
- Test: `receiver-windows/tests/OpenAudioLink.Tests/UI/MainFormTests.cs`

- [ ] **Step 1: Update MainForm runtime start**

In `receiver-windows/src/OpenAudioLink/UI/MainForm.cs`, add imports:

```csharp
using System.Net;
using OpenAudioLink.Protocol;
```

Then replace:

```csharp
runtime = ReceiverRuntime.StartLoopback();
```

with:

```csharp
runtime = ReceiverRuntime.Start(IPAddress.Any, ProtocolConstants.DefaultPort);
```

The top of the final file should be:

```csharp
using System.Drawing;
using System.Net;
using System.Windows.Forms;
using OpenAudioLink.Protocol;
using OpenAudioLink.Receiver;
```

The constructor should be:

```csharp
public MainForm()
{
    runtime = ReceiverRuntime.Start(IPAddress.Any, ProtocolConstants.DefaultPort);
    ListeningPort = runtime.Port;

    Text = "OpenAudioLink Receiver";
    Size = new Size(480, 240);

    Controls.Add(new Label
    {
        AutoSize = true,
        Location = new Point(24, 24),
        Text = "Listening on TCP port " + ListeningPort
    });
}
```

- [ ] **Step 2: Run tests and verify GREEN**

Run on a Windows/.NET build host:

```powershell
dotnet test receiver-windows/OpenAudioLink.sln -c Release --filter MainFormTests
```

Expected: PASS.

On this Linux workspace, record the unavailable local runner if applicable:

```bash
if command -v dotnet >/dev/null 2>&1; then dotnet test receiver-windows/OpenAudioLink.sln -c Release --filter MainFormTests; else echo 'dotnet not found; Windows tests require CI'; fi
```

Expected locally if `dotnet` is absent: `dotnet not found; Windows tests require CI`.

- [ ] **Step 3: Commit implementation**

```bash
git add receiver-windows/src/OpenAudioLink/UI/MainForm.cs
git commit -m "feat: listen on default receiver port"
```

---

### Task 3: Final validation and branch push

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
git push -u origin phase-1j-windows-default-lan-listener
git fetch origin phase-1j-windows-default-lan-listener
printf 'local=%s\nremote=%s\n' "$(git rev-parse --short HEAD)" "$(git rev-parse --short origin/phase-1j-windows-default-lan-listener)"
git diff --exit-code HEAD origin/phase-1j-windows-default-lan-listener --stat
ip route del 192.168.3.20/32 via 192.168.4.1 dev eth0 || true
```

Expected: local and remote hashes match; diff command exits 0.
