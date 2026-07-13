# Phase 1-I Windows App Runtime Lifecycle Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Wire the WinForms `MainForm` to start and dispose the existing fake `ReceiverRuntime`.

**Architecture:** Keep `Program` and receiver internals unchanged. `MainForm` owns one `ReceiverRuntime`, exposes its bound port through `ListeningPort`, displays that port in a label, and disposes the runtime from `Dispose(bool)`.

**Tech Stack:** C#/.NET Framework 4.8, WinForms, MSTest, existing `ReceiverRuntime`, existing Phase 1-A protocol helpers, Python docs/golden checks.

---

## Scope Check

This plan implements only `docs/superpowers/specs/2026-07-13-phase-1i-windows-app-runtime-lifecycle-design.md`.

In scope:

- `MainForm` runtime ownership.
- `MainForm.ListeningPort`.
- Minimal listening-port label.
- WinForms tests for startup handshake and disposal.

Out of scope:

- Real AAC decode.
- WASAPI, NAudio, Media Foundation, or speaker playback.
- Android sender changes.
- Protocol wire-format changes.
- Discovery, mDNS, UDP, pairing, or receiver identity.
- Configuration files, fixed ports, settings UI, persisted receiver names.
- Tray behavior, installer work, logs, diagnostics, UI timers, live counters, or new dependencies.

---

## Files and Responsibilities

- Modify `receiver-windows/src/OpenAudioLink/UI/MainForm.cs`
  - Owns one `ReceiverRuntime`.
  - Exposes `ListeningPort`.
  - Displays `Listening on TCP port <ListeningPort>`.
  - Disposes the runtime from `Dispose(bool)`.

- Create `receiver-windows/tests/OpenAudioLink.Tests/UI/MainFormTests.cs`
  - Runs WinForms construction/disposal on an STA thread.
  - Proves the form-owned runtime accepts a loopback `HELLO -> WELCOME` exchange.
  - Proves disposing the form closes the listener.

- Modify `receiver-windows/tests/OpenAudioLink.Tests/OpenAudioLink.Tests.csproj`
  - Adds an explicit framework reference to `System.Windows.Forms` for the UI test helper.

---

### Task 1: Add failing WinForms lifecycle tests

**Files:**
- Create: `receiver-windows/tests/OpenAudioLink.Tests/UI/MainFormTests.cs`
- Modify: `receiver-windows/tests/OpenAudioLink.Tests/OpenAudioLink.Tests.csproj`

- [ ] **Step 1: Create the failing tests**

Create `receiver-windows/tests/OpenAudioLink.Tests/UI/MainFormTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenAudioLink.Protocol;
using OpenAudioLink;

namespace OpenAudioLink.Tests.UI
{
    [TestClass]
    public sealed class MainFormTests
    {
        private const int SocketTimeoutMilliseconds = 5000;

        [TestMethod]
        public void ConstructorStartsReceiverRuntimeAndShowsPort()
        {
            RunSta(() =>
            {
                using (MainForm form = new MainForm())
                {
                    Assert.AreNotEqual(0, form.ListeningPort);
                    StringAssert.Contains(VisibleText(form), "Listening on TCP port " + form.ListeningPort);

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

        [TestMethod]
        public void DisposeStopsReceiverRuntime()
        {
            int port = 0;

            RunSta(() =>
            {
                MainForm form = new MainForm();
                port = form.ListeningPort;
                form.Dispose();
            });

            AssertConnectFails(port);
        }

        private static void RunSta(Action action)
        {
            Exception error = null;
            Thread thread = new Thread(() =>
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    error = ex;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();

            if (error != null)
            {
                ExceptionDispatchInfo.Capture(error).Throw();
            }
        }

        private static string VisibleText(Control parent)
        {
            List<string> parts = new List<string>();
            foreach (Control control in parent.Controls)
            {
                if (!string.IsNullOrEmpty(control.Text))
                {
                    parts.Add(control.Text);
                }
            }

            return string.Join("\n", parts.ToArray());
        }

        private static TcpClient Connect(int port)
        {
            TcpClient client = new TcpClient();
            client.ReceiveTimeout = SocketTimeoutMilliseconds;
            client.SendTimeout = SocketTimeoutMilliseconds;
            client.Connect(IPAddress.Loopback, port);
            return client;
        }

        private static void AssertConnectFails(int port)
        {
            using (TcpClient client = new TcpClient())
            {
                IAsyncResult result = client.BeginConnect(IPAddress.Loopback, port, null, null);
                if (!result.AsyncWaitHandle.WaitOne(SocketTimeoutMilliseconds))
                {
                    client.Close();
                    Assert.Fail("Timed out waiting for connection failure after MainForm.Dispose.");
                }

                try
                {
                    client.EndConnect(result);
                    Assert.Fail("Expected connection to fail after MainForm.Dispose.");
                }
                catch (SocketException)
                {
                }
            }
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

- [ ] **Step 2: Add WinForms framework reference for tests**

Add this item group to `receiver-windows/tests/OpenAudioLink.Tests/OpenAudioLink.Tests.csproj`:

```xml
  <ItemGroup>
    <Reference Include="System.Windows.Forms" />
  </ItemGroup>
```

- [ ] **Step 3: Run tests and verify RED**

Run on a Windows/.NET build host:

```powershell
dotnet test receiver-windows/OpenAudioLink.sln -c Release --filter MainFormTests
```

Expected: FAIL because `MainForm.ListeningPort` does not exist and the form still shows the skeleton label.

On this Linux workspace, run:

```bash
if command -v dotnet >/dev/null 2>&1; then dotnet test receiver-windows/OpenAudioLink.sln -c Release --filter MainFormTests; else echo 'dotnet not found; Windows tests require CI'; fi
```

Expected locally if `dotnet` is absent: `dotnet not found; Windows tests require CI`.

- [ ] **Step 4: Commit failing tests**

```bash
git add receiver-windows/tests/OpenAudioLink.Tests/UI/MainFormTests.cs receiver-windows/tests/OpenAudioLink.Tests/OpenAudioLink.Tests.csproj
git commit -m "test: cover main form runtime lifecycle"
```

---

### Task 2: Wire `MainForm` to `ReceiverRuntime`

**Files:**
- Modify: `receiver-windows/src/OpenAudioLink/UI/MainForm.cs`
- Test: `receiver-windows/tests/OpenAudioLink.Tests/UI/MainFormTests.cs`

- [ ] **Step 1: Update `MainForm` minimally**

Replace `receiver-windows/src/OpenAudioLink/UI/MainForm.cs` with:

```csharp
using System.Drawing;
using System.Windows.Forms;
using OpenAudioLink.Receiver;

namespace OpenAudioLink
{
    public sealed class MainForm : Form
    {
        private readonly ReceiverRuntime runtime;

        public MainForm()
        {
            runtime = ReceiverRuntime.StartLoopback();
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

        public int ListeningPort { get; }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                runtime.Dispose();
            }

            base.Dispose(disposing);
        }
    }
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
git commit -m "feat: start receiver runtime from main form"
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
git push -u origin phase-1i-windows-app-runtime-lifecycle
git fetch origin phase-1i-windows-app-runtime-lifecycle
printf 'local=%s\nremote=%s\n' "$(git rev-parse --short HEAD)" "$(git rev-parse --short origin/phase-1i-windows-app-runtime-lifecycle)"
git diff --exit-code HEAD origin/phase-1i-windows-app-runtime-lifecycle --stat
ip route del 192.168.3.20/32 via 192.168.4.1 dev eth0 || true
```

Expected: local and remote hashes match; diff command exits 0.
