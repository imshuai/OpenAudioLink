# OpenAudioLink Roadmap

本文档描述 OpenAudioLink 的后续规划。

Roadmap 不是承诺清单。

它用于说明优先级、阶段边界和暂不实现的内容。

遵循原则：

> Keep Core Small.

任何大型能力都必须满足：

- 不破坏 Version 1 的 Android → Windows 主链路
- 不改变已发布协议的兼容性承诺
- 可以独立测试
- 可以延后实现

---

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

---

# Version 1.0 Goal

Version 1.0 的目标非常简单：

> Make any Windows computer behave like a wireless speaker for Android devices.

Version 1.0 只覆盖一个主流程：

```text
Android playback audio

↓

Android AudioPlaybackCapture

↓

AAC encode

↓

OpenAudioLink Protocol over TCP

↓

Windows receiver

↓

AAC decode

↓

Windows playback device
```

---

# Phase 0

目标：稳定规格和工程骨架。

重点：

- 文档一致性
- 协议常量冻结
- 项目目录创建
- 最小 CI
- 测试样例准备

主要工作：

- Finalize `docs/03-Protocol.md`
- Finalize `docs/10-Testing.md`
- Create Android project skeleton
- Create Windows solution skeleton
- Add protocol golden packet tests
- Add configuration schema examples
- Add basic build workflows

完成标准：

- 所有核心文档互相一致
- 协议 header 和 AUDIO payload 有 golden tests
- Android 和 Windows 空项目可以构建
- CI 可以运行基础检查

---

# Phase 1

目标：实现 Version 1.0 最小可用产品。

重点：

- 能发现
- 能连接
- 能传输
- 能播放
- 能停止

主要功能：

## Android Sender

- MediaProjection permission flow
- AudioPlaybackCapture
- AAC-LC encoding
- Receiver discovery
- TCP transport
- Foreground service
- Basic settings
- Runtime logs

---

## Windows Receiver

- WinForms receiver application
- TCP listener
- Protocol handshake
- AAC-LC decoder
- Audio playback output
- mDNS advertisement
- Configuration file
- Runtime logs
- Installer

---

## Protocol

- Version 1 handshake
- AUDIO packets
- PING / PONG heartbeat
- STOP_STREAM
- ERROR handling
- Receiver busy handling

---

## Testing

- Protocol unit tests
- Android unit tests
- Windows unit tests
- TCP loopback integration test
- Basic end-to-end manual validation

完成标准：

- Android can discover Windows receiver.
- Android can connect to Windows receiver.
- Windows can play received audio.
- Stop/reconnect works.
- Normal LAN latency is below 150 ms.

---

# Phase 1.1

目标：提升稳定性和可诊断性。

重点：

- 重连
- 设备变化
- 网络变化
- 日志诊断
- 首次运行体验

主要工作：

- Better reconnection policy
- Receiver offline detection
- Audio device hot-plug handling
- Discovery diagnostics
- Firewall diagnostics
- Latency display
- Queue depth display
- Export diagnostics bundle
- Configuration migration tests

完成标准：

- Wi-Fi reconnect can recover.
- Receiver restart can recover.
- Audio device change reports clear status.
- Diagnostics explain common failures.

---

# Phase 1.5

目标：发布硬化。

重点：

- 性能
- 长时间运行
- 安装升级
- 兼容性

主要工作：

- 24-hour playback validation
- Windows 7 SP1 validation
- Windows 10 / 11 validation
- Android vendor device matrix
- Installer upgrade testing
- APK signing workflow
- Release notes workflow
- Checksums for release artifacts

完成标准：

- Release checklist passes.
- 24-hour receiver playback test passes.
- Installer and APK validation pass.
- Known limitations are documented.

---

# Phase 2

目标：在不破坏 Version 1 的前提下改善传输和音频体验。

候选功能：

- Optional UDP audio transport
- Adaptive jitter buffer
- Optional Opus codec
- Better latency measurement
- Sender-side bitrate adaptation
- Pairing / trusted receivers
- Basic stream encryption

这些功能只有在 Version 1 稳定后才进入实现。

---

# Phase 3

目标：扩展平台和场景。

候选方向：

- Linux receiver
- macOS receiver
- Headless receiver
- Multiple receiver profiles
- Multi-room research prototype
- Protocol compliance test suite

Phase 3 不应牺牲 Version 1 的简单性。

---

# Non-Goals

以下内容不属于近期目标：

- DLNA renderer
- Chromecast receiver
- AirPlay receiver
- Bluetooth audio stack
- Media library
- Screen mirroring
- Cloud relay
- WAN-first streaming

OpenAudioLink 可以借鉴这些产品的体验，但不实现它们的协议。

---

# Documentation Policy

每个重要模块必须同步维护文档。

新增或修改功能时，至少检查：

- Architecture impact
- Protocol impact
- Configuration impact
- Testing impact
- Deployment impact

文档冲突必须优先修复。

规格不清晰时，不应先写实现绕过去。

---

# Release Policy

公开发布前必须满足：

- Build passes
- Protocol tests pass
- Unit tests pass
- End-to-end playback passes
- Release checklist passes
- Known limitations are documented

版本号遵循 Semantic Versioning。

---

# Long-Term Vision

OpenAudioLink 的长期目标是成为一个小而清晰的开放无线音频传输协议。

它应保持：

- 本地网络优先
- 开放协议
- 低延迟
- 易实现
- 易测试
- 不依赖云服务
- 不依赖专有生态

最终目标不是做一个大而全的媒体平台。

最终目标是把 Android 当前播放的声音可靠地送到 Windows 扬声器。

---

# End
