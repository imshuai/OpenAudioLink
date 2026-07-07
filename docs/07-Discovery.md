# docs/07-Discovery.md

# OpenAudioLink Discovery System Design

Version: 1.0

---

# Overview

The Discovery System allows Android senders to automatically find OpenAudioLink receivers on the local network.

The discovery system must provide:

- Zero configuration discovery
- Fast receiver detection
- Network change handling
- Multiple receiver support
- Compatibility with complex networks

---

# Design Goals

The discovery system should:

- Work without a central server
- Work on normal home networks
- Support Windows firewall environments
- Support future multi-room scenarios
- Allow manual connection fallback

---

# Discovery Architecture

OpenAudioLink uses a multi-layer discovery strategy.

```text
                 Android App

                     |

        +------------+------------+

        |                         |

        ▼                         ▼

      mDNS                  Direct Discovery

        |                         |

        ▼                         ▼

   Local Receivers        Known Receivers

```

---

# Discovery Methods

Version 1 supports three discovery mechanisms.

## Method 1

mDNS / DNS-SD

Primary method.

Used for:

- Home networks
- Office networks
- Standard WiFi

---

## Method 2

UDP Broadcast Discovery

Fallback method.

Used when:

- mDNS blocked
- Router does not forward multicast
- Enterprise network restrictions

---

## Method 3

Direct Unicast Discovery

Advanced method.

Used for:

- WireGuard networks
- VPN networks
- Cross-subnet communication

---

# Discovery Priority

Android sender searches in this order.

```text
1. mDNS

↓

2. UDP Broadcast

↓

3. Saved Receiver IP

```

---

# Why Multiple Discovery Methods

Different networks behave differently.

Example:

Home WiFi:

```
mDNS works perfectly
```

Enterprise WiFi:

```
Multicast blocked
```

WireGuard:

```
Multicast unavailable
```

A single discovery method cannot cover all environments.

---

# mDNS Discovery

Technology:

```
DNS Service Discovery

+

Multicast DNS
```

---

# Service Type

OpenAudioLink service:

```
_openaudiolink._tcp.local
```

---

# Service Advertisement

Receiver publishes:

```
Service Name

+

Port

+

TXT Metadata
```

Example:

```
Office-PC._openaudiolink._tcp.local
```

---

# TXT Record Design

Example:

```
version=1

id=7f2c91ab

name=Office-PC

codec=aac

protocol=1

status=ready
```

---

# Required TXT Fields

| Field | Description |
|-|-|
| version | Protocol version |
| id | Receiver UUID |
| name | Display name |
| codec | Supported codec |
| protocol | Protocol version |
| status | Current state |

---

# Optional TXT Fields

| Field | Description |
|-|-|
| platform | windows |
| model | PC |
| latency | default latency |
| features | supported features |

---

# Receiver Advertisement Lifecycle

Startup:

```text
Application Start

↓

Initialize Network

↓

Create mDNS Service

↓

Publish Advertisement

↓

Wait For Queries
```

---

# Shutdown

Normal shutdown:

```text
Stop Service

↓

Withdraw Advertisement

↓

Close Socket
```

---

# Network Change

Example:

```
Ethernet

↓

WiFi
```

Flow:

```text
Network Changed

↓

Remove Old Advertisement

↓

Detect Interfaces

↓

Publish New Advertisement
```

---

# Multiple Receivers

Example:

```
Living Room PC

Bedroom PC

Office PC
```

Android displays:

```
Living Room PC

Bedroom PC

Office PC
```

Each receiver has:

```
Unique UUID
```

---

# Receiver Identity

Receiver identity is permanent.

Generated:

```
UUID v4
```

Stored locally.

Example:

```
d91b9c54-a6c4-4f35-bc3d-9fd8a7b0a012
```

---

# Name vs Identity

Important distinction:

Display Name:

```
Office-PC
```

can change.

Identity:

```
UUID
```

never changes.

---

# Discovery Result Model

Android receives:

```json
{
 "id":
 "d91b9c54-a6c4-4f35-bc3d-9fd8a7b0a012",

 "name":
 "Office-PC",

 "address":
 "192.168.1.20",

 "port":
 39888,

 "status":
 "ready"
}
```

---

# Discovery Cache

Android should cache discovered receivers.

Purpose:

- Faster startup
- Show previous devices
- Support direct connection

Cache lifetime:

```
24 hours
```

---

# Cache Validation

Cached receiver:

```
Found previously

↓

Try connection

↓

Success

↓

Update information
```

Failure:

```
Remove or mark offline
```

---

# Discovery Timeout

Recommended:

mDNS:

```
3 seconds
```

UDP:

```
2 seconds
```

Unicast:

```
1 second
```

---

# Discovery Security

Discovery data is not trusted.

Validate:

- Port range
- UUID format
- Protocol version
- Packet size

Discovery only provides connection information.

Authentication happens later.

---

# UDP Broadcast Discovery

UDP Broadcast Discovery provides a fallback mechanism when mDNS is unavailable.

It is designed for:

- Home networks with multicast issues
- Enterprise networks
- Simple LAN environments

---

# Broadcast Overview

Communication model:

```text
Android Sender

      |

      | UDP Broadcast

      ▼

All OpenAudioLink Receivers

      |

      ▼

Matching Receiver Response
```

---

# Broadcast Address

IPv4 broadcast:

```
255.255.255.255
```

or subnet broadcast:

Example:

```
192.168.1.255
```

---

# Broadcast Port

Discovery port:

```
39887/UDP
```

Audio transport:

```
39888/TCP
```

Separating ports avoids conflicts.

---

# Broadcast Packet Header

Every discovery packet starts with:

```
Magic

Version

Message Type

Length
```

Example:

```
OAL1

01

01

0048
```

---

# Packet Format

Binary layout:

```
+----------------+

| Magic 4 bytes |

+----------------+

| Version 1     |

+----------------+

| Type 1        |

+----------------+

| Length 2      |

+----------------+

| Payload       |

+----------------+
```

---

# Message Types

Defined values:

| Type | Name |
|-|-|
| 0x01 | DISCOVER_REQUEST |
| 0x02 | DISCOVER_RESPONSE |
| 0x03 | PING |
| 0x04 | PONG |

---

# DISCOVER_REQUEST

Sender broadcasts:

```json
{
 "version":1,
 "protocol":1,
 "requestId":"uuid"
}
```

Purpose:

Ask all receivers:

"Who is available?"

---

# DISCOVER_RESPONSE

Receiver response:

```json
{
 "id":
 "uuid",

 "name":
 "Office-PC",

 "port":
 39888,

 "codec":
 "aac",

 "status":
 "ready"
}
```

---

# Response Rules

Receiver should:

Respond immediately if:

```
Service Available
```

Do not respond if:

```
Application Disabled
```

---

# Response Delay

To avoid network collision:

Random delay:

```
0-100ms
```

before responding.

---

# Duplicate Handling

Android may receive:

```
mDNS result

+

Broadcast result
```

for the same receiver.

Merge by:

```
Receiver UUID
```

---

# Broadcast Limitations

Broadcast cannot cross:

- Routers
- NAT
- Most VPN tunnels

Therefore it is only a fallback.

---

# Direct Unicast Discovery

Direct discovery solves networks where:

- mDNS unavailable
- Broadcast unavailable

Examples:

- WireGuard
- Tailscale
- Routed LAN
- Manual IP connection

---

# Unicast Model

```text
Android

    |

    | UDP Request

    ▼

Known IP Address

    |

    ▼

Receiver Response
```

---

# Unicast Discovery Port

Same as broadcast:

```
39887/UDP
```

---

# Request Packet

Example:

```json
{
 "type":
 "DISCOVER_REQUEST",

 "client":
 "android",

 "version":
 1
}
```

---

# Response Packet

Example:

```json
{
 "type":
 "DISCOVER_RESPONSE",

 "receiverId":
 "uuid",

 "name":
 "Office-PC",

 "tcpPort":
 39888
}
```

---

# Known Receiver List

Android stores:

```
Receiver UUID

Last IP

Last Port

Last Seen Time
```

---

# Direct Connection Workflow

Example:

```text
Open App

↓

Load Known Receivers

↓

Send UDP Probe

↓

Receive Response

↓

Update Device List
```

---

# WireGuard Scenario

Example topology:

```
Phone

10.0.0.2


        WireGuard


PC

10.0.0.10
```

mDNS:

```
Unavailable
```

Broadcast:

```
Unavailable
```

Unicast:

```
Works
```

---

# Manual IP Support

The application should allow:

```
Add Receiver Manually
```

Input:

```
IP Address

Port
```

Example:

```
10.0.0.10:39887
```

---

# Discovery Manager

Android component:

```
DiscoveryManager
```

Responsibilities:

- Start discovery
- Stop discovery
- Merge results
- Cache receivers
- Notify UI

---

# Discovery State Machine

States:

```
Idle

↓

Searching

↓

Found

↓

Connecting

↓

Connected

```

---

# Search Sequence

Recommended:

```
Start Search

↓

mDNS Query

↓

Broadcast Request

↓

Cached Receiver Probe

↓

Merge Results

↓

Display List
```

---

# Discovery Timeout Handling

If no result:

```
Searching

↓

Timeout

↓

Show Empty Result

↓

Keep Background Scan
```

---

# Discovery Frequency

Foreground:

```
Continuous
```

Background:

```
30 seconds
```

---

# Battery Consideration

Android discovery should avoid:

- Continuous broadcast
- High-frequency scans
- Keeping sockets alive unnecessarily

---

# mDNS Implementation Design

mDNS is the primary discovery mechanism.

It provides automatic receiver discovery without requiring:

- Server infrastructure
- Manual configuration
- User input

---

# Windows Implementation

Recommended library:

```
Makaretu.Dns.Multicast
```

Reason:

- Pure .NET implementation
- Supports DNS-SD
- Works on Windows 7+
- No native dependency

---

# Windows Discovery Service

Namespace:

```
OpenAudioLink.Discovery
```

Main class:

```
MdnsPublisher
```

---

# MdnsPublisher Responsibilities

Handles:

- Service registration
- TXT record publishing
- Service removal
- Network changes

---

# Class Design

```csharp
public interface IDiscoveryService
{
    Task StartAsync();

    Task StopAsync();

    IReadOnlyList<ReceiverInfo> GetReceivers();
}
```

---

# Windows Advertisement Flow

Startup:

```text
Application Start

↓

Create Service Profile

↓

Register DNS-SD Service

↓

Publish TXT Records

↓

Wait
```

---

# Service Profile

Example:

```text
Name:

Office-PC


Type:

_openaudiolink._tcp


Port:

39888
```

---

# TXT Record Generation

Example:

```text
version=1

id=uuid

name=Office-PC

codec=aac

status=ready
```

---

# TXT Record Update

Some values change dynamically:

```
status

latency

features
```

Example:

Before:

```
status=ready
```

After:

```
status=busy
```

---

# Windows Service Discovery Client

Although the Windows receiver mainly advertises, future versions may also discover other receivers.

Class:

```
MdnsBrowser
```

Responsibilities:

- Browse services
- Resolve addresses
- Parse TXT records

---

# Android mDNS Implementation

Android uses:

```
NsdManager
```

provided by Android framework.

Advantages:

- No external dependency
- Battery optimized
- System integrated

---

# Android Discovery Flow

```text
Create NsdManager

↓

Discover Services

↓

Resolve Service

↓

Read TXT Records

↓

Create ReceiverInfo

```

---

# Android Service Type

Same as Windows:

```
_openaudiolink._tcp.
```

Important:

Both sides must use exactly the same service type.

---

# Android DiscoveryManager

Structure:

```
DiscoveryManager

├── MdnsDiscovery

├── BroadcastDiscovery

├── UnicastDiscovery

├── ReceiverRepository

└── DiscoveryState
```

---

# ReceiverRepository

Stores discovered devices.

Responsibilities:

- Add receiver
- Update receiver
- Remove receiver
- Merge duplicates

---

# Receiver Model

Recommended:

```kotlin
data class ReceiverInfo(

    val id: String,

    val name: String,

    val address: String,

    val port: Int,

    val status: String

)
```

---

# Duplicate Merge Algorithm

Same receiver may appear through:

```
mDNS

+

Broadcast

+

Cache
```

Merge key:

```
receiverId
```

---

# Merge Rules

Example:

Existing:

```
Office-PC

192.168.1.10
```

New:

```
Office-PC

10.0.0.5
```

Result:

```
Same Receiver

Multiple Addresses
```

---

# Address Priority

Preferred order:

```
Same LAN subnet

↓

Ethernet

↓

WiFi

↓

VPN

```

---

# IPv4 / IPv6 Handling

Discovery should collect:

```
IPv4

IPv6
```

Connection priority:

```
IPv4

↓

IPv6
```

Reason:

Most home networks still prefer IPv4.

---

# Network Interface Changes

Android example:

```
WiFi disconnected

↓

Mobile network active

↓

WiFi restored
```

Discovery should:

```
Stop current scan

↓

Restart discovery

↓

Refresh receiver list
```

---

# Windows Network Changes

Possible events:

- Ethernet connected
- WiFi connected
- IP changed
- VPN enabled

Flow:

```text
Network Event

↓

Stop Advertisement

↓

Rebind Interfaces

↓

Restart Advertisement
```

---

# Interface Filtering

Avoid advertising through:

- Loopback
- Disabled adapters
- Virtual-only adapters

Examples:

```
127.0.0.1

VMware adapter

Hyper-V internal switch
```

---

# VPN Consideration

VPN adapters are special.

Default:

```
Do not advertise
```

Exception:

User enables:

```
Allow VPN Discovery
```

---

# Discovery Diagnostics

The application should expose:

```
Discovery Enabled

Current Interfaces

Advertised Address

Service Name

TXT Records
```

---

# Logging Example

Startup:

```
mDNS started

Service:

Office-PC._openaudiolink._tcp

Port:

39888
```

Network change:

```
Interface changed

Restarting advertisement
```

---

# Discovery Security Boundary

Discovery provides:

- Device information
- Connection address

It does not provide:

- Authentication
- Authorization
- Encryption

These belong to the connection protocol.

---

# Discovery and Connection Integration

Discovery only provides receiver information.

The actual audio session requires a separate connection process.

The relationship:

```text
Discovery

↓

Receiver Information

↓

TCP Connection

↓

Authentication

↓

Audio Streaming
```

---

# Discovery vs Connection

Discovery answers:

```
"What receivers exist?"
```

Connection answers:

```
"Can I start playback?"
```

They are intentionally separated.

---

# Receiver Availability Model

A receiver has two states:

Discovery state:

```
Online

Offline

Unknown
```

Connection state:

```
Disconnected

Connecting

Connected

Streaming
```

These states are independent.

---

# Example

Receiver discovered:

```
Office-PC

192.168.1.20

status=ready
```

But connection fails:

```
Discovery:

Online


Connection:

Failed
```

The receiver remains visible.

---

# Receiver Status Synchronization

Discovery TXT status is only a hint.

Example:

```
status=ready
```

does not guarantee:

- TCP port available
- User permission
- Audio device available

The sender must confirm through TCP handshake.

---

# Connection Workflow

Complete process:

```text
Android App

↓

Discovery Scan

↓

Select Receiver

↓

TCP Connect

↓

HELLO

↓

CAPABILITY

↓

START_STREAM

↓

AUDIO DATA

```

---

# TCP Handshake Dependency

The receiver should not accept audio immediately.

Required sequence:

```
CONNECT

↓

HELLO

↓

SESSION_CREATE

↓

READY

↓

AUDIO
```

---

# Discovery Information Usage

ReceiverInfo:

```json
{
 "id":"uuid",

 "name":"Office-PC",

 "address":"192.168.1.20",

 "port":39888
}
```

is only used to establish the TCP connection.

---

# Online Detection

A discovered receiver is considered online when:

One of the following succeeds:

```
mDNS advertisement exists

OR

UDP response received

OR

TCP handshake succeeds
```

---

# Offline Detection

A receiver becomes offline when:

```
mDNS removed

AND

UDP timeout

AND

TCP unavailable
```

---

# Offline Timeout

Recommended:

```
30 seconds
```

Reason:

Network changes can temporarily interrupt discovery.

---

# Receiver Cache Lifecycle

Android cache:

```
New Receiver

↓

Active

↓

Inactive

↓

Expired
```

---

# Cache Expiration

Recommended:

```
24 hours
```

Expired entries are removed silently.

---

# Background Discovery

When application is backgrounded:

Do not continuously scan.

Instead:

```
Keep cache

+

Refresh when app returns
```

---

# Foreground Discovery

When user opens receiver selection:

```text
Start Scan

↓

Show Existing Cache

↓

Update Live Results

↓

Stop Scan After Timeout
```

---

# Discovery UI Model

Recommended Android state:

```kotlin
sealed class DiscoveryState {

    object Idle

    object Searching

    data class Found(
        val receivers: List<ReceiverInfo>
    )

    data class Error(
        val message:String
    )
}
```

---

# UI Update Flow

```text
DiscoveryManager

↓

Repository

↓

ViewModel

↓

Compose UI
```

The UI should never directly access network discovery.

---

# Multiple Receiver Selection

Example:

```
Office-PC

LivingRoom-PC

Bedroom-PC
```

User selects:

```
LivingRoom-PC
```

Only that receiver starts a session.

---

# Multiple Sender Handling

Possible scenario:

```
Phone A

        ↓

    Receiver

        ↑

Phone B
```

Receiver policy:

Version 1:

```
One active sender only
```

---

# Busy Receiver

When already streaming:

Discovery:

```
status=busy
```

Connection:

```
BUSY response
```

---

# Busy Handling

Android UI:

Example:

```
Office-PC

Currently playing

[Connect anyway]
```

---

# Receiver Handoff

Future feature:

```
Phone A

↓

Stop

↓

Phone B

↓

Start
```

Requires:

- Session ownership
- Authentication

---

# Discovery Reliability

The system should tolerate:

- WiFi roaming
- IP changes
- Sleep/wake
- Router restart

---

# Sleep/Wake Handling

Windows example:

```
PC Sleep

↓

Network Lost

↓

Wake

↓

New IP

↓

Restart Advertisement
```

---

# Discovery Logging

Recommended events:

```
Receiver found

Receiver lost

Address changed

Service restarted

Connection requested
```

---

# Discovery Security Model

Discovery is designed to locate receivers.

It is **not** responsible for authentication or authorization.

The security boundary begins only after a TCP connection is established.

---

# Trust Model

Version 1 assumes:

```
Trusted Local Network
```

Examples:

- Home LAN
- Office LAN
- WireGuard private network

Public Wi-Fi is not considered a trusted environment.

---

# Discovery Threats

Possible threats include:

- Fake receiver advertisements
- Receiver impersonation
- Discovery packet flooding
- Replay of discovery responses
- Network scanning

Discovery should minimize exposure while remaining lightweight.

---

# Receiver Identity

Each receiver owns a permanent identifier.

Requirements:

- Generated once
- Persisted locally
- Never changes automatically

Format:

```
UUID v4
```

Example:

```
d91b9c54-a6c4-4f35-bc3d-9fd8a7b0a012
```

The UUID is the canonical identity of the receiver.

---

# Receiver Name

Display names are user-friendly only.

Example:

```
Office-PC
```

Names may change at any time.

Applications must never use the display name as the unique identifier.

---

# Duplicate Detection

Receivers are considered identical when:

```
Receiver UUID matches
```

Even if:

- IP changes
- Hostname changes
- Display name changes

---

# Discovery Packet Validation

Every received discovery packet must validate:

- Magic value
- Protocol version
- Packet length
- Message type
- Maximum payload size

Invalid packets are discarded immediately.

---

# Payload Limits

Recommended limits.

| Field | Maximum |
|--------|---------:|
| Receiver Name | 64 bytes |
| UUID | 36 characters |
| TXT Record Value | 256 bytes |
| Discovery Packet | 1024 bytes |

Large packets should be rejected before parsing.

---

# Rate Limiting

Receivers should limit discovery responses.

Example:

```
Maximum:

20 responses / second
```

Excess requests are silently ignored.

This reduces amplification risks.

---

# Broadcast Protection

A receiver should ignore repeated identical requests from the same source for a short interval.

Suggested suppression window:

```
500 ms
```

This prevents unnecessary network traffic.

---

# Replay Considerations

Replay protection is not required during discovery.

Reason:

Discovery packets contain only public receiver information.

Replay protection becomes relevant during session establishment.

---

# Authentication Boundary

Authentication starts after TCP connection.

Workflow:

```text
Discovery

↓

TCP Connect

↓

HELLO

↓

Authentication

↓

Session Ready

↓

Audio Streaming
```

The discovery layer remains intentionally stateless.

---

# Pairing (Future)

Version 2 may introduce optional pairing.

Example workflow:

```text
Android

↓

Request Pair

↓

Receiver Displays PIN

↓

User Confirms

↓

Shared Trust Established
```

Pairing is not required for Version 1.

---

# Trusted Receivers

Future receiver database.

Example:

```json
[
  {
    "id":"d91b9c54...",
    "trusted":true,
    "lastSeen":"2026-07-01"
  }
]
```

Trusted receivers may skip additional confirmation dialogs.

---

# Secure Transport

Discovery remains unencrypted.

Audio sessions may later support:

- TLS
- Mutual authentication
- Encrypted control channel
- Encrypted audio transport

These enhancements are independent of discovery.

---

# Discovery and Firewall

The installer should create firewall rules for:

```
UDP 5353

UDP 39887

TCP 39888
```

If firewall configuration fails, the application should notify the user.

---

# Privacy

Discovery packets should contain only information necessary to establish a connection.

Recommended fields:

- Receiver UUID
- Display Name
- Protocol Version
- Listening Port
- Supported Codec
- Status

Avoid exposing:

- Windows username
- Computer description
- Installed hardware
- Operating system build details

---

# Logging Security

Discovery logs should never include:

- Raw packet dumps by default
- Sensitive local configuration
- Authentication tokens

Debug mode may include packet payloads for troubleshooting.

---

# Failure Handling

Invalid discovery packets:

```text
Receive Packet

↓

Validation Failed

↓

Discard

↓

Continue Listening
```

Malformed packets must never terminate the discovery service.

---

# Future Security Extensions

Reserved capabilities:

- Receiver certificates
- Signed advertisements
- Challenge-response authentication
- Encrypted discovery payloads
- Enterprise device enrollment

The protocol version field enables future compatibility.

---

# Discovery Design Principles

The discovery subsystem follows these principles:

1. Discovery locates devices, not users.
2. UUID defines identity.
3. Display names are cosmetic.
4. Authentication belongs to the session layer.
5. Discovery remains lightweight.
6. Invalid input must never compromise stability.

---

# Discovery Subsystem Architecture

The discovery subsystem is responsible for locating receivers and maintaining an up-to-date receiver list.

Recommended namespace:

```
OpenAudioLink.Discovery
```

---

# Directory Structure

```
Discovery/

├── DiscoveryManager.cs
├── DiscoveryService.cs
├── DiscoveryOptions.cs
├── ReceiverRepository.cs
├── ReceiverInfo.cs
├── DiscoveryStatistics.cs
│
├── Mdns/
│   ├── MdnsPublisher.cs
│   ├── MdnsBrowser.cs
│   └── TxtRecordParser.cs
│
├── Broadcast/
│   ├── BroadcastServer.cs
│   ├── BroadcastClient.cs
│   ├── BroadcastPacket.cs
│   └── BroadcastSerializer.cs
│
├── Unicast/
│   ├── UnicastProbe.cs
│   ├── ReceiverCache.cs
│   └── KnownReceiverStore.cs
│
└── Network/
    ├── NetworkMonitor.cs
    ├── InterfaceHelper.cs
    └── AddressSelector.cs
```

---

# DiscoveryManager

DiscoveryManager coordinates all discovery mechanisms.

Responsibilities:

- Start discovery
- Stop discovery
- Merge receiver information
- Notify subscribers
- Maintain cache
- Handle network changes

---

# DiscoveryManager Interface

```csharp
public interface IDiscoveryManager
{
    Task StartAsync();

    Task StopAsync();

    IReadOnlyList<ReceiverInfo> GetReceivers();

    event EventHandler<ReceiverChangedEventArgs>
        ReceiverChanged;
}
```

---

# ReceiverRepository

ReceiverRepository is the single source of truth.

It owns:

- Active receivers
- Cached receivers
- Receiver state
- Last seen timestamp

No UI component should store independent receiver lists.

---

# ReceiverInfo

Recommended model:

```csharp
public sealed class ReceiverInfo
{
    public Guid Id { get; init; }

    public string Name { get; init; }

    public IPAddress Address { get; init; }

    public int Port { get; init; }

    public ReceiverStatus Status { get; set; }

    public DateTime LastSeen { get; set; }

    public IReadOnlyList<string> Capabilities { get; init; }
}
```

---

# ReceiverStatus

Suggested values:

```text
Unknown

Online

Busy

Offline

Connecting

Connected
```

---

# Discovery Lifecycle

```text
Application Start

↓

Load Cached Receivers

↓

Start mDNS

↓

Start UDP Discovery

↓

Start Network Monitor

↓

Merge Results

↓

Notify UI
```

---

# NetworkMonitor

Responsibilities:

- Detect adapter changes
- Detect IP changes
- Detect VPN interfaces
- Restart discovery services

Supported events:

- Adapter added
- Adapter removed
- Address changed
- Network available
- Network unavailable

---

# Receiver Merge Algorithm

When a receiver is discovered:

```text
Find UUID

↓

Exists?

↓

Yes

↓

Update Existing

↓

No

↓

Create Receiver
```

Only UUID determines identity.

---

# Address Selection

A receiver may expose multiple addresses.

Priority order:

1. Same subnet IPv4
2. Wired IPv4
3. Wi-Fi IPv4
4. VPN IPv4
5. IPv6

The selected address is used for the next connection attempt.

---

# Offline Detection

Every receiver maintains:

```
LastSeen
```

A periodic task checks:

```text
Now - LastSeen
```

If exceeded timeout:

```
Receiver Status

↓

Offline
```

Default timeout:

```
30 seconds
```

---

# Background Maintenance

Maintenance interval:

```
10 seconds
```

Tasks:

- Remove expired receivers
- Refresh cache
- Update statistics
- Validate network state

---

# Statistics

Discovery subsystem should expose:

```csharp
public sealed class DiscoveryStatistics
{
    public int OnlineReceivers;

    public int OfflineReceivers;

    public int CachedReceivers;

    public long MdnsPackets;

    public long BroadcastPackets;

    public long UnicastPackets;

    public long DiscoveryErrors;
}
```

---

# Logging

Startup example:

```
Discovery subsystem initialized

mDNS enabled

Broadcast enabled

Interfaces: 2
```

Receiver found:

```
Receiver discovered

Name: Office-PC

Address: 192.168.1.20

Method: mDNS
```

Receiver updated:

```
Receiver address changed

Old: 192.168.1.20

New: 192.168.50.15
```

---

# Error Recovery

If one discovery mechanism fails:

```text
mDNS Failure

↓

Log Error

↓

Disable mDNS

↓

Continue Broadcast

↓

Continue Unicast
```

The remaining discovery mechanisms continue operating.

---

# Configuration Options

Recommended configuration:

```json
{
  "discovery": {
    "enableMdns": true,
    "enableBroadcast": true,
    "enableUnicast": true,
    "broadcastPort": 39887,
    "tcpPort": 39888,
    "timeout": 3000,
    "offlineTimeout": 30000,
    "allowVpn": false
  }
}
```

---

# Thread Model

Recommended worker threads:

```text
Main Thread

↓

DiscoveryManager

    |

    +------ mDNS Thread

    |

    +------ Broadcast Thread

    |

    +------ Network Monitor

    |

    +------ Maintenance Timer
```

All workers communicate through thread-safe collections.

---

# Performance Targets

| Metric | Target |
|--------|--------:|
| Discovery Startup | < 1 s |
| Receiver Detection | < 3 s |
| Address Update | < 2 s |
| Memory Usage | < 5 MB |
| CPU Usage | < 1 % |

---

# Testing Checklist

## mDNS

- [ ] Receiver published
- [ ] Receiver discovered
- [ ] TXT record parsed
- [ ] Multiple receivers supported

---

## Broadcast

- [ ] Broadcast request sent
- [ ] Receiver responds
- [ ] Duplicate responses merged

---

## Unicast

- [ ] Manual IP probe
- [ ] WireGuard connectivity
- [ ] Cached receiver reconnect

---

## Network Changes

- [ ] Wi-Fi reconnect
- [ ] Ethernet reconnect
- [ ] DHCP address change
- [ ] VPN enable/disable

---

## Stability

- [ ] 24-hour discovery run
- [ ] Multiple receivers
- [ ] Multiple network adapters
- [ ] Firewall enabled
- [ ] Sleep/Wake recovery

---

# Discovery Design Principles

The subsystem follows these principles:

1. Zero configuration by default.
2. Multiple discovery methods for compatibility.
3. UUID is the only persistent identity.
4. Discovery is independent of connection.
5. Discovery failures must not affect streaming sessions.
6. Network changes are handled automatically.
7. Discovery remains lightweight and always-on.

---

# End of Document

docs/07-Discovery.md complete.