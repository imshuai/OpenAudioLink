# docs/06-Audio.md

# OpenAudioLink Audio Subsystem Design

Version: 1.0

Platform:

- Windows Receiver
- Future cross-platform receivers

---

# Overview

The Audio Subsystem is responsible for converting network audio packets into continuous speaker output.

The subsystem must provide:

- Low latency
- Stable playback
- Network jitter tolerance
- Device switching
- Long-duration reliability

The Audio Subsystem must operate independently from:

- Network transport
- Protocol parsing
- User interface

---

# Design Goals

Primary goals.

## Low Latency

Target:

```
End-to-end latency < 150 ms
```

Ideal:

```
80-120 ms
```

---

## Stability

The receiver should continue playback under:

- Variable network delay
- Packet arrival jitter
- Temporary CPU load
- Audio device changes

---

## Predictable Memory Usage

The audio pipeline must use bounded buffers.

Unlimited queues are prohibited.

---

## Thread Isolation

Each stage should have independent execution.

A slow decoder must not block:

- Network reception
- Protocol parsing
- UI updates

---

# Audio Pipeline Overview

The complete pipeline:

```text
                Network

                   │

                   ▼

          TCP Receive Thread

                   │

                   ▼

          Network Ring Buffer

                   │

                   ▼

        Protocol Processing Thread

                   │

                   ▼

            AAC Frame Queue

                   │

                   ▼

            Decoder Thread

                   │

                   ▼

            PCM Ring Buffer

                   │

                   ▼

          Adaptive Jitter Buffer

                   │

                   ▼

             Audio Clock

                   │

                   ▼

          Renderer Thread

                   │

                   ▼

              WASAPI

                   │

                   ▼

              Speaker
```

Each stage has a clear responsibility.

---

# Component Overview

```text
AudioManager

├── AudioPipeline

│
├── Decoder

│
├── BufferManager

│
├── JitterController

│
├── ClockManager

│
└── Renderer
```

---

# AudioManager

AudioManager is the top-level coordinator.

Responsibilities:

- Create pipeline
- Start pipeline
- Stop pipeline
- Restart pipeline
- Manage audio device
- Expose statistics

AudioManager does not decode or render directly.

---

# AudioPipeline

AudioPipeline connects all audio stages.

Responsibilities:

- Create queues
- Start worker threads
- Stop worker threads
- Monitor pipeline health

Lifecycle:

```text
Create

↓

Initialize

↓

Running

↓

Stop

↓

Dispose
```

---

# Thread Model

The recommended execution model:

```text
Thread 1

TCP Receive

        │

        ▼


Thread 2

Protocol Parser


        │

        ▼


Thread 3

AAC Decoder


        │

        ▼


Thread 4

Audio Renderer
```

Each thread communicates using lock-free or bounded queues.

---

# Why Separate Threads

Incorrect design:

```text
TCP Receive

↓

Decode

↓

Play
```

Problem:

A slow decode operation blocks network reception.

Result:

- Packet loss
- Increased latency
- Audio glitches

---

Recommended design:

```text
Receive

↓

Queue

↓

Decode

↓

Queue

↓

Play
```

Each stage can absorb temporary speed differences.

---

# Queue Architecture

The system contains three major buffers.

## Network Buffer

Purpose:

Absorb TCP scheduling variance.

Contains:

```
Raw packets
```

---

## AAC Buffer

Purpose:

Decouple network from decoder.

Contains:

```
Compressed audio frames
```

---

## PCM Buffer

Purpose:

Decouple decoder from playback.

Contains:

```
Uncompressed samples
```

---

# Buffer Principle

Every buffer must have:

- Maximum size
- Overflow strategy
- Underflow strategy

Example:

```text
Buffer Full

↓

Discard old data


Buffer Empty

↓

Insert silence
```

Real-time playback has priority over perfect data preservation.

---

# Buffer Sizes

Default configuration.

| Buffer | Size |
|-|-:|
| Network | 256 KB |
| AAC | 500 ms |
| PCM | 100 ms |
| Jitter | 100 ms |

---

# Latency Budget

Target latency:

```
100 ms
```

Suggested distribution:

```
Network

20 ms


AAC Queue

20 ms


Decode

5 ms


PCM Buffer

20 ms


Jitter Buffer

35 ms
```

Total:

```
≈100 ms
```

---

# Adaptive Latency

The system should dynamically adjust buffering.

Example:

Stable network:

```
80 ms
```

Unstable network:

```
150 ms
```

The goal is:

- Minimum delay
- Maximum stability

---

# Audio Format

Version 1 standard format:

```
Codec:

AAC-LC


Sample Rate:

48000 Hz


Channels:

2


Bit Depth:

16-bit PCM
```

All receivers must support this format.

---

# Future Codec Support

The architecture should allow:

```
AAC

↓

Opus

↓

FLAC

↓

PCM
```

Codec selection belongs to protocol negotiation.

---

# Decoder Subsystem

The Decoder subsystem converts compressed audio frames into PCM samples.

Version 1 uses:

```
AAC-LC
```

decoded through:

```
Windows Media Foundation
```

---

# Decoder Responsibilities

The decoder is responsible for:

- Initialize AAC decoder
- Accept compressed frames
- Decode frames
- Produce PCM samples
- Handle decoder errors
- Recover from temporary failures

The decoder does not:

- Receive network packets
- Manage playback devices
- Control latency

---

# Decoder Interface

Recommended interface.

```csharp
public interface IAudioDecoder
{
    void Initialize(AudioFormat format);

    bool Decode(
        ReadOnlyMemory<byte> input,
        out ReadOnlyMemory<byte> pcm
    );

    void Flush();

    void Dispose();
}
```

The interface intentionally hides the underlying decoder implementation.

---

# Decoder Implementation

Version 1:

```text
IAudioDecoder

        │

        ▼

MediaFoundationDecoder
```

Future implementations:

```text
IAudioDecoder

        │

        ├── MediaFoundationDecoder

        ├── FFmpegDecoder

        └── OpusDecoder
```

---

# Media Foundation Architecture

Internal pipeline.

```text
AAC Frame

↓

IMFSourceReader

↓

AAC Decoder MFT

↓

PCM Media Buffer

↓

PCM Output
```

---

# Decoder Initialization

Initialization sequence.

```text
Create Media Foundation

↓

Create Attributes

↓

Configure Decoder

↓

Set Input Type

↓

Set Output Type

↓

Ready
```

---

# Required Media Type

Input:

```
Audio AAC

MIME:

audio/aac

Subtype:

MFAudioFormat_AAC
```

---

Output:

```
Audio PCM

Subtype:

MFAudioFormat_PCM

Sample Rate:

48000

Channels:

2

Bits:

16
```

---

# Decoder Thread

The decoder runs independently.

Pipeline:

```text
AAC Queue

↓

Decoder Thread

↓

PCM Queue
```

The decoder thread:

- Waits for frames
- Decodes
- Pushes PCM
- Reports errors

---

# Decoder Loop

Pseudo code:

```text
while running:

    frame = AACQueue.Take()

    pcm = Decode(frame)

    if pcm available:

        PCMQueue.Push(pcm)

```

The decoder must block efficiently when no data exists.

Busy polling is prohibited.

---

# Decoder Timing

The decoder does not control playback speed.

Incorrect:

```text
Decode faster

↓

Sleep

↓

Maintain timing
```

Correct:

```text
Decode immediately

↓

Renderer controls clock
```

The audio device clock is authoritative.

---

# Decoder Error Handling

Possible errors:

- Invalid AAC frame
- Corrupted packet
- Unsupported format
- Media Foundation failure

Handling:

```text
Decode Error

↓

Log Error

↓

Discard Frame

↓

Continue
```

A single damaged frame must not terminate playback.

---

# Decoder Recovery

Fatal decoder failures:

Example:

```
Media Foundation unavailable
```

Recovery:

```text
Stop Decoder

↓

Release Resources

↓

Recreate Decoder

↓

Resume
```

---

# Frame Loss Handling

AAC frames may be lost because of:

- Network problems
- Buffer overflow
- Invalid packets

The decoder should:

```
Skip damaged frame

↓

Continue decoding
```

The renderer may briefly insert silence.

---

# PCM Output Format

All decoder output must be normalized.

Format:

```
Sample Rate:

48000 Hz


Channels:

2


Encoding:

Signed PCM


Bit Depth:

16-bit


Endian:

Little Endian
```

---

# PCM Conversion

If Media Foundation outputs another format:

Example:

```
Float PCM
```

Convert:

```
Float

↓

16-bit Integer

↓

PCM Queue
```

Conversion must happen before buffering.

---

# Decoder Buffer Management

The decoder should avoid allocations.

Recommended:

- Reuse buffers
- Pool memory
- Avoid copying when possible

Large continuous allocations cause:

- GC pressure
- Latency spikes

---

# Decoder Statistics

Expose:

```text
Decoded Frames

Decode Errors

Average Decode Time

Maximum Decode Time

Current Queue Depth
```

These values are useful for diagnostics.

---

# Performance Targets

Decoder requirements.

| Metric | Target |
|-|-:|
| Initialization | <500 ms |
| Decode Frame | <5 ms |
| Memory Growth | 0 MB/hour |
| CPU Usage | <2% |

---

# Windows Compatibility

Media Foundation availability:

| OS | Support |
|-|-|
| Windows 7 SP1 | Yes |
| Windows 8.1 | Yes |
| Windows 10 | Yes |
| Windows 11 | Yes |

Required:

```
Platform Update

Media Foundation components
```

---

# Fallback Strategy

If Media Foundation initialization fails:

```text
Try Media Foundation

↓

Failure

↓

Report Error

↓

Session Closed
```

Version 1 does not automatically fallback to FFmpeg.

Reason:

- Avoid additional native dependencies
- Simplify deployment
- Maintain Windows compatibility

---

# Decoder Security

Decoder input originates from network data.

Validation required:

- Maximum frame size
- Valid AAC headers
- Resource limits

Malformed input must not crash the process.

---

# Buffer Architecture

The buffering system is the core component responsible for maintaining smooth playback under network instability.

The receiver must handle:

- Packet arrival jitter
- Variable network latency
- Temporary CPU scheduling delays
- Decoder timing variation
- Audio device scheduling variation

---

# Buffer Design Goals

The buffer system must provide:

- Bounded memory usage
- Low latency
- Stable playback
- Predictable behavior
- Runtime monitoring

The system must never allow unlimited buffering.

---

# Buffer Pipeline

Complete buffering architecture.

```text
TCP Receive

    |

    ▼

Network Ring Buffer

    |

    ▼

AAC Frame Queue

    |

    ▼

Decoder

    |

    ▼

PCM Ring Buffer

    |

    ▼

Adaptive Jitter Buffer

    |

    ▼

Renderer
```

---

# Network Ring Buffer

The network buffer stores raw incoming packets.

Purpose:

- Absorb TCP receive bursts
- Separate socket timing from processing timing

Contents:

```
Protocol packets
```

Not decoded audio.

---

# Network Buffer Properties

Recommended:

```
Capacity:

256 KB
```

Overflow strategy:

```
Disconnect session
```

Reason:

If the receiver cannot process network input, continuing creates increasing latency.

---

# AAC Frame Queue

The AAC queue stores validated audio frames.

Contents:

```
AAC encoded frames
```

Producer:

```
Protocol Thread
```

Consumer:

```
Decoder Thread
```

---

# AAC Queue Interface

Recommended abstraction.

```csharp
public interface IAudioFrameQueue
{
    void Push(AudioFrame frame);

    bool TryPop(
        out AudioFrame frame
    );

    int Count { get; }
}
```

---

# AAC Queue Capacity

Recommended:

```
500 ms audio
```

Example:

```
48 kHz stereo AAC

≈20-25 frames
```

---

# AAC Queue Overflow

When full:

```
Remove oldest frame

↓

Insert newest frame
```

Reason:

Real-time playback prefers current audio.

Old audio increases latency.

---

# PCM Ring Buffer

The PCM buffer stores decoded samples.

Contents:

```
Raw PCM samples
```

Producer:

```
Decoder Thread
```

Consumer:

```
Renderer Thread
```

---

# PCM Buffer Size

Default:

```
100 ms
```

Configurable:

```
50-250 ms
```

---

# PCM Buffer Interface

Example:

```csharp
public interface IPcmBuffer
{
    void Write(
        ReadOnlySpan<byte> data
    );

    int Read(
        Span<byte> buffer
    );

    int AvailableBytes { get; }
}
```

---

# Ring Buffer Implementation

Recommended:

```
Circular Buffer
```

Structure:

```text
+-----------------------+

| Used | Free           |

+-----------------------+

        ↑

     Write

        ↑

   Read Position

```

Advantages:

- Constant memory
- No allocation
- Fast operations

---

# Locking Strategy

Possible implementations:

## Option 1

ConcurrentQueue

Advantages:

- Simple
- Safe

Disadvantages:

- Additional allocations


## Option 2

BlockingCollection

Advantages:

- Built-in synchronization

Disadvantages:

- Less control


## Option 3

Custom RingBuffer

Advantages:

- Highest performance
- Predictable memory

Disadvantages:

- More code

---

# Recommended Choice

Version 1:

```
Custom bounded RingBuffer
```

Reason:

The application is a real-time audio system.

Predictability is more important than development speed.

---

# Thread Synchronization

Recommended:

```text
Producer

↓

Signal

↓

Consumer
```

Avoid:

```text
while(true)

check buffer

sleep

check again
```

Busy waiting wastes CPU.

---

# Jitter Buffer

The jitter buffer compensates for network timing variation.

Example:

Packets arrive:

```
20ms

18ms

45ms

12ms

30ms
```

Playback requires:

```
20ms

20ms

20ms

20ms

20ms
```

The jitter buffer smooths these differences.

---

# Jitter Buffer Position

Location:

```text
PCM Buffer

↓

Jitter Buffer

↓

Renderer
```

It operates on decoded PCM.

Reason:

PCM timing directly represents playback time.

---

# Jitter Buffer Target

Default:

```
100 ms
```

Adaptive range:

```
50-200 ms
```

---

# Jitter Measurement

Measure:

```
Packet arrival interval

-

Expected interval
```

Example:

Expected:

```
20 ms
```

Actual:

```
35 ms
```

Jitter:

```
15 ms
```

---

# Adaptive Algorithm

The buffer target changes according to network quality.

Stable:

```
Reduce buffer
```

Unstable:

```
Increase buffer
```

---

# Adjustment Rules

Example:

```text
Jitter < 5ms

↓

Decrease target by 5ms
```


```text
Jitter > 20ms

↓

Increase target by 10ms
```

Limits:

```
50ms <= Buffer <= 200ms
```

---

# Startup Buffer

When playback starts:

```text
Receive packets

↓

Fill jitter buffer

↓

Start renderer
```

Recommended startup delay:

```
100 ms
```

---

# Buffer Underrun

Occurs when:

```
Renderer requests PCM

↓

No data available
```

Response:

```text
Insert silence

↓

Continue playback
```

Do not stop the audio device.

---

# Buffer Overrun

Occurs when:

```
PCM arrives faster than playback
```

Response:

```text
Drop oldest samples
```

Keeping latency low is more important than preserving all samples.

---

# Buffer Monitoring

Expose:

```
Network Buffer Size

AAC Queue Length

PCM Buffer Fill

Jitter Target

Underrun Count

Overrun Count
```

Update interval:

```
1 second
```

---

# Audio Renderer

The Audio Renderer is responsible for sending PCM samples to the Windows audio subsystem.

The renderer is the final stage of the audio pipeline.

Pipeline:

```text
PCM Buffer

↓

Audio Renderer

↓

Windows Audio Engine

↓

Playback Device
```

---

# Renderer Responsibilities

The renderer handles:

- Audio device initialization
- PCM playback
- Buffer submission
- Playback timing
- Device changes
- Output recovery

The renderer does not:

- Decode audio
- Manage network packets
- Control session state

---

# Renderer Interface

Recommended abstraction.

```csharp
public interface IAudioRenderer
{
    void Initialize(
        AudioFormat format
    );

    void Start();

    void Stop();

    void Write(
        ReadOnlySpan<byte> pcm
    );

    void Flush();

    void Dispose();
}
```

---

# Renderer Implementations

Version 1 provides:

```text
IAudioRenderer

        |

        ├── WasapiRenderer

        |

        └── WaveOutRenderer
```

---

# Renderer Selection

Startup sequence:

```text
Initialize WASAPI

        |

        Success

        |

        Use WASAPI


        |

        Failure

        |

        Use WaveOut
```

---

# WASAPI Renderer

Windows Audio Session API.

Preferred playback backend.

Advantages:

- Low latency
- Native Windows API
- Stable timing
- Better device integration

---

# WASAPI Mode

Version 1 uses:

```
Shared Mode
```

Reason:

- Compatible with all applications
- Allows system mixing
- Supports Windows 7+

---

# Exclusive Mode

Not used in Version 1.

Reasons:

- Device ownership conflicts
- More complex recovery
- User experience issues

Future versions may support optional exclusive mode.

---

# WASAPI Pipeline

```text
PCM Buffer

↓

WasapiOut

↓

Audio Client

↓

Audio Render Client

↓

Windows Audio Engine

↓

Device Driver

↓

Speaker
```

---

# Playback Thread

Renderer owns a dedicated playback thread.

Responsibilities:

```text
Wait for audio request

↓

Read PCM Buffer

↓

Fill WASAPI Buffer

↓

Wait for next callback

```

The playback thread should never block network or decoder threads.

---

# Event Driven Rendering

Preferred approach:

```
Event Callback
```

instead of:

```
Timer Loop
```

Reason:

The audio device clock should drive playback.

---

# Audio Clock

The playback device owns the final clock.

The renderer determines:

- When samples are consumed
- Current playback position
- Buffer availability

Other components should adapt to this clock.

---

# Clock Principle

Incorrect:

```text
Network Clock

controls

Playback
```

Correct:

```text
Audio Device Clock

controls

Playback
```

Network timing is only used to manage buffering.

---

# Playback Latency

Latency consists of:

```text
Network Delay

+

Decode Delay

+

PCM Buffer

+

WASAPI Buffer

+

Hardware Buffer
```

---

# Target Latency

Recommended:

```
80-150 ms
```

Typical:

```
100 ms
```

The system should prefer stability over extreme low latency.

---

# WASAPI Buffer Size

The renderer should request:

```
20-50 ms
```

hardware buffer.

The remaining latency is controlled by:

- Network buffer
- Jitter buffer
- PCM buffer

---

# WaveOut Renderer

Fallback implementation.

Technology:

```
NAudio WaveOutEvent
```

Advantages:

- Mature
- Stable
- Windows 7 compatible
- Simple recovery

---

# WaveOut Limitations

Compared with WASAPI:

- Higher latency
- Less precise timing
- Less device control

It exists mainly as compatibility fallback.

---

# Audio Device Manager

Responsible for:

- Enumerating devices
- Selecting output device
- Monitoring changes

---

# Device Enumeration

Use:

```
MMDeviceEnumerator
```

Collect:

```text
Device ID

Friendly Name

State

Default Status
```

---

# Device Selection Rules

Priority:

```
User Selected Device

↓

Previous Active Device

↓

Windows Default Device

↓

First Available Device
```

---

# Device Hot Plug

Examples:

- USB sound card inserted
- HDMI monitor connected
- Bluetooth headset connected

Flow:

```text
Device Change Event

↓

Pause Renderer

↓

Reinitialize Device

↓

Restore Playback

```

---

# Renderer Recovery

Possible failures:

- Device removed
- Driver reset
- Audio service restart

Recovery:

```text
Stop Renderer

↓

Release WASAPI

↓

Create New Renderer

↓

Resume
```

---

# Default Device Change

Windows users often change output devices.

Example:

```
Speaker

↓

Bluetooth Headset
```

Behavior:

```text
Detect Change

↓

Recreate Renderer

↓

Continue Stream
```

---

# Audio Device Notification

Recommended API:

```
IMMNotificationClient
```

Events:

- Default device changed
- Device added
- Device removed
- Device state changed

---

# Playback Silence Handling

When PCM data is unavailable:

```text
No Samples

↓

Generate Silence

↓

Submit To Device
```

Do not stop the renderer.

Stopping causes:

- Click sounds
- Reinitialization delay
- Increased latency

---

# Renderer Statistics

Expose:

```text
Current Device

Buffer Size

Playback Position

Underrun Count

Restart Count

Latency Estimate
```

---

# Performance Targets

| Metric | Target |
|-|-:|
| Renderer Startup | <500ms |
| Callback Processing | <2ms |
| CPU Usage | <1% |
| Device Recovery | <2s |

---

# Renderer Error Policy

Recoverable:

- Temporary underrun
- Device restart
- Audio service restart

Fatal:

- No available playback device
- Unsupported PCM format

Fatal errors terminate only playback.

The application remains running.

---

# Audio Clock and Synchronization

Real-time audio streaming requires a stable timing model.

The system must avoid:

- Playback speed variation
- Buffer oscillation
- Long-term clock drift
- Increasing latency

---

# Clock Ownership

The receiver playback device owns the final clock.

Architecture:

```text
Windows Audio Device Clock

            |

            ▼

       Renderer

            |

            ▼

     Jitter Controller

            |

            ▼

       PCM Buffer

            |

            ▼

       Decoder

            |

            ▼

       Network
```

---

# Why Network Cannot Be The Clock

Network timing is unstable.

Example:

Packet arrival:

```
20ms

22ms

18ms

40ms

15ms
```

If playback follows network timing:

Result:

- Speed changes
- Buffer oscillation
- Audio artifacts

---

# Why Sender Cannot Be The Clock

The sender device has its own oscillator.

Example:

Android:

```
48000Hz
```

Receiver:

```
47998Hz
```

Difference:

```
2 samples / second
```

After several minutes:

- Buffer slowly fills
- Buffer slowly empties

---

# Clock Drift

Every audio device has clock error.

Typical:

```
±50 ppm
```

Example:

48000 Hz device:

```
48000 × 50ppm

≈2.4 samples/sec
```

Small but cumulative.

---

# Timestamp Requirement

Every audio frame should contain timing information.

Recommended packet metadata:

```text
sequence

timestamp

sampleCount

presentationTime
```

---

# Audio Timestamp

Timestamp unit:

Recommended:

```
microseconds
```

Example:

```json
{
  "timestamp": 1234567890,
  "samples": 960
}
```

Meaning:

```
20ms audio frame
```

---

# Sequence Number

Every AUDIO packet contains:

```
uint64 sequence
```

Purpose:

Detect:

- Lost packets
- Duplicate packets
- Reordering

Example:

```
100

101

103
```

Missing:

```
102
```

---

# Receiver Timing Model

The receiver maintains:

```
Expected Playback Time
```

Compared against:

```
Received Audio Timestamp
```

Difference:

```
Timing Error
```

---

# Timing Error

Example:

Expected:

```
500ms
```

Received:

```
520ms
```

Error:

```
+20ms
```

Action:

Increase buffer temporarily.

---

# Drift Correction

The receiver should not frequently change playback speed.

Incorrect:

```
Speed up

Slow down

Speed up

Slow down
```

This creates audible artifacts.

---

# Recommended Correction

Use extremely small adjustments.

Methods:

## Method 1

Sample Drop / Duplicate

Example:

Remove:

```
1 sample
```

every few seconds.

---

## Method 2

Resampling

Adjust:

```
48000Hz

↓

47999Hz
```

for a short period.

---

# Version 1 Drift Strategy

Use:

```
Buffer Level Control
```

Only.

Rules:

```text
Buffer Too Full

↓

Drop Small PCM Segment
```

```text
Buffer Too Empty

↓

Insert Silence
```

---

# Future Drift Strategy

Version 2 may introduce:

```
Adaptive Sample Rate Converter
```

Pipeline:

```text
PCM

↓

ASRC

↓

Renderer
```

This allows smooth clock synchronization.

---

# Sender Synchronization

Version 1 does not require synchronized clocks.

Reason:

The system is:

```
Single Receiver

Single Stream
```

Only local playback stability matters.

---

# Multi-Room Future Design

For future multi-device playback:

Required:

- Common time source
- Clock synchronization
- Network latency measurement
- Sample rate adjustment

Architecture:

```text
Master Clock

      |

      +---------+

      |         |

Receiver A  Receiver B

```

---

# Latency Measurement

The receiver should estimate:

```
Network Delay

+

Decode Delay

+

Playback Delay
```

---

# Latency Reporting

The receiver periodically reports:

```text
currentBuffer

estimatedLatency

clockOffset
```

to the sender.

---

# Feedback Channel

Recommended protocol extension:

```text
STATUS_REPORT
```

Example:

```json
{
 "buffer": 92,
 "latency": 110,
 "drift": 3
}
```

---

# Sender Adjustment

Future versions may allow sender adaptation.

Examples:

Increase bitrate:

```
Stable network
```

Decrease bitrate:

```
High packet loss
```

Increase latency:

```
Unstable network
```

---

# Audio Quality Monitoring

Monitor:

```
Packet Loss

Buffer Underrun

Decoder Error

Device Restart

Latency Variation
```

---

# Quality Levels

Suggested profiles.

## Low Latency

```
Buffer:

50ms
```

Usage:

Gaming

---

## Balanced

```
Buffer:

100ms
```

Default.

---

## Stable

```
Buffer:

200ms
```

Usage:

Weak WiFi

---

# Configuration Example

```json
{
 "latencyMode":"balanced",
 "bufferMs":100
}
```

---

# Audio Synchronization Summary

The system follows these principles:

1. Network provides data, not timing.
2. Decoder produces samples, not playback timing.
3. Renderer owns the final clock.
4. Buffers absorb timing differences.
5. Corrections are gradual.
6. Stability is preferred over absolute minimum latency.

---

# Audio Subsystem Implementation Design

This section defines the complete implementation structure for the Audio Subsystem.

The goal is to provide enough detail for direct implementation.

---

# Audio Subsystem Structure

Recommended namespace:

```
OpenAudioLink.Audio
```

Directory:

```
Audio/

├── AudioManager.cs

├── AudioPipeline.cs

├── AudioSession.cs

├── AudioFormat.cs

├── AudioFrame.cs

├── AudioStatistics.cs


├── Decoder/

│   ├── IAudioDecoder.cs
│   ├── MediaFoundationDecoder.cs
│   └── DecoderStatistics.cs


├── Buffer/

│   ├── RingBuffer.cs
│   ├── AudioFrameQueue.cs
│   ├── PcmBuffer.cs
│   └── JitterBuffer.cs


├── Renderer/

│   ├── IAudioRenderer.cs
│   ├── WasapiRenderer.cs
│   ├── WaveOutRenderer.cs
│   └── AudioDeviceManager.cs


└── Clock/

    ├── AudioClock.cs
    └── DriftController.cs
```

---

# AudioManager

AudioManager is the external entry point.

Responsibilities:

- Create audio pipeline
- Control playback lifecycle
- Manage audio device
- Expose statistics

---

Interface:

```csharp
public interface IAudioManager
{
    Task StartAsync();

    Task StopAsync();

    Task PlayAsync();

    Task PauseAsync();

    AudioStatistics GetStatistics();
}
```

---

# AudioManager State Machine

States:

```
Stopped

↓

Initializing

↓

Ready

↓

Buffering

↓

Playing

↓

Stopping

↓

Stopped
```

---

# State Transitions

Startup:

```
Stopped

↓

Initializing

↓

Ready
```

Receive audio:

```
Ready

↓

Buffering

↓

Playing
```

Disconnect:

```
Playing

↓

Stopping

↓

Stopped
```

---

# AudioPipeline

The pipeline owns all processing components.

Structure:

```text
AudioPipeline

├── AAC Queue

├── Decoder Thread

├── PCM Buffer

├── Jitter Buffer

└── Renderer Thread
```

---

# Pipeline Startup

Order:

```
Create Buffers

↓

Initialize Decoder

↓

Initialize Renderer

↓

Start Decoder Thread

↓

Start Renderer Thread

↓

Accept Audio Frames
```

---

# Pipeline Shutdown

Order:

```
Stop Input

↓

Complete Queues

↓

Stop Decoder

↓

Flush PCM

↓

Stop Renderer

↓

Release Devices

```

This prevents:

- Deadlocks
- Resource leaks
- Audio device lock

---

# Decoder Thread Lifecycle

Startup:

```
Create Thread

↓

Wait For AAC Frames

↓

Decode

↓

Push PCM

```

Shutdown:

```
Receive Stop Signal

↓

Exit Loop

↓

Dispose Decoder
```

---

# Renderer Thread Lifecycle

Startup:

```
Initialize Device

↓

Start Audio Client

↓

Wait For PCM

↓

Submit Samples
```

Shutdown:

```
Stop Callback

↓

Flush Device

↓

Release Audio Client
```

---

# Thread Communication

Recommended primitives:

```
CancellationToken

+

Blocking Collection

+

Custom RingBuffer
```

---

# Thread Rules

Forbidden:

```
Thread.Sleep()
```

for timing control.

Forbidden:

```
while(true)
{
    Check Queue
}
```

because it wastes CPU.

---

# Exception Handling

Each worker thread must contain a top-level exception boundary.

Example:

```text
Thread Exception

↓

Capture Error

↓

Log

↓

Notify Manager

↓

Attempt Recovery
```

A worker thread crash must not terminate the application.

---

# Audio Statistics

Recommended model:

```csharp
public class AudioStatistics
{
    public long ReceivedFrames;

    public long DecodedFrames;

    public long DroppedFrames;

    public long Underruns;

    public long Overruns;

    public int BufferLevel;

    public int LatencyMs;

    public double DecodeTimeMs;
}
```

---

# Statistics Collection

Update interval:

```
1 second
```

Statistics should be:

- Thread safe
- Lightweight
- Lock free where possible

---

# Logging

Audio subsystem logs should include:

Startup:

```
Audio initialized

Device:

Speakers

Format:

AAC 48k Stereo
```

Runtime:

```
Buffer:

85%

Latency:

105ms
```

Errors:

```
Renderer restart requested
```

---

# Debug Mode

When enabled:

Additional information:

```
Packet timestamps

Buffer history

Decoder timing

Renderer callbacks

Device events
```

---

# Audio Trace

Optional diagnostic file:

```
audio_trace.log
```

Example:

```
12:01:10 Buffer=90ms

12:01:11 Buffer=85ms

12:01:12 Underrun

12:01:13 Renderer Restart
```

Useful for analyzing WiFi instability.

---

# Performance Monitoring

Recommended counters:

## CPU

```
Decoder CPU %

Renderer CPU %
```

---

## Memory

```
Audio Buffer Memory

GC Allocations
```

---

## Timing

```
Decode Time

Render Callback Time

Queue Delay
```

---

# Resource Limits

The audio subsystem must enforce:

```
Maximum AAC Queue

Maximum PCM Buffer

Maximum Memory Usage
```

Example:

```
Audio memory < 10MB
```

---

# Security Considerations

Network audio is untrusted input.

Validate:

- Frame size
- Sample count
- Timestamp range
- Buffer allocation size

Never allocate memory directly from network-controlled values.

---

# Future Extension Points

The architecture supports:

## Additional Codecs

```
AAC

Opus

FLAC
```

---

## Multiple Receivers

```
One Sender

↓

Many Receivers
```

---

## Synchronised Playback

Requires:

- Shared clock
- Drift correction
- Latency calibration

---

# Final Audio Architecture

Complete system:

```text
                Network

                   |

                   ▼

             AAC Queue

                   |

                   ▼

              Decoder

                   |

                   ▼

             PCM Buffer

                   |

                   ▼

          Adaptive Jitter Buffer

                   |

                   ▼

              Audio Clock

                   |

                   ▼

              Renderer

                   |

                   ▼

             Windows Audio
```

---

# Implementation Checklist

Before considering Audio Subsystem complete:

## Decoder

- [ ] AAC decoder works
- [ ] Invalid frames handled
- [ ] Decoder recovery works


## Buffer

- [ ] Ring buffer implemented
- [ ] Overflow handled
- [ ] Underflow handled


## Renderer

- [ ] WASAPI works
- [ ] WaveOut fallback works
- [ ] Device switching works


## Stability

- [ ] 24-hour playback test
- [ ] Network interruption test
- [ ] Audio device reconnect test


## Diagnostics

- [ ] Statistics available
- [ ] Logs available
- [ ] Debug mode available
