# DLNA Player Roadmap

本文档描述项目未来规划。

Roadmap 分为四个阶段：

- Phase 1
- Phase 2
- Phase 3
- Phase 4

并不是所有功能都会立即实现。

遵循：

> Keep Core Small.

任何大型功能都必须：

- 可插拔
- 不影响主流程
- 可以独立关闭

---

# Current Status

目前已经完成：

✓ UPnP SSDP

✓ Device Discovery

✓ Device Description

✓ ContentDirectory Browse

✓ DIDL Parser

✓ HTTP Streaming

✓ Audio Decoder

✓ Video Decoder

✓ Image Decoder

✓ Subtitle Parser

✓ Renderer Pipeline

✓ Playback Controller

✓ Playlist

✓ EventBus

✓ Repository

✓ Cache

✓ Thumbnail Loader

✓ Background Tasks

✓ Coroutine Framework

✓ Lifecycle Integration

✓ Error Recovery

✓ Retry Strategy

✓ Logging

✓ Settings

✓ Theme

✓ Android TV 基础支持

已经可以作为一个完整播放器使用。

---

# Phase 1

目标：

打造稳定播放器。

重点：

- 稳定
- 快
- 易维护

主要工作：

## Bug Fix

持续修复：

- Renderer Bug
- Decoder Bug
- Subtitle Bug
- Network Bug
- UI Bug

所有 Bug：

必须可复现。

每个 Bug：

必须拥有：

- Root Cause
- Fix
- Regression Test

---

## Performance

持续优化：

### Startup

减少：

Application Startup

Activity Startup

Renderer Startup

Media Open

目标：

首次播放：

< 2 秒

重新播放：

< 500 ms

---

### Memory

优化：

Bitmap

Buffer

Coroutine

Cache

减少：

GC

对象分配

临时对象

避免：

OOM

---

### CPU

优化：

Decoder

Renderer

Subtitle

Network

避免：

Busy Loop

减少：

Wakeup

降低：

Battery Usage

---

### Disk

减少：

Cache Miss

重复读取

Metadata IO

Thumbnail IO

目标：

随机浏览目录：

几乎不产生明显卡顿。

---

# UI Polish

继续优化：

动画：

- Transition
- Shared Element
- Cross Fade

滚动：

- RecyclerView
- Compose LazyList

播放器：

- Gesture
- Brightness
- Volume

TV：

- Focus
- D-Pad
- Remote

---

# Accessibility

增加：

TalkBack

Keyboard

Large Font

High Contrast

Content Description

保证：

所有核心页面：

均可无障碍访问。

---

# Internationalization

支持：

- 中文
- English

未来：

支持：

- 日本语
- 한국어
- Français
- Deutsch
- Español

所有字符串：

禁止硬编码。

统一：

strings.xml

---

# Phase 2

目标：

提升播放器能力。

不仅能播放。

还要：

播放得更好。

---

# Advanced Playback

增加更多播放器能力。

## Gapless Playback

支持：

无缝播放。

适用于：

- Album
- Live Concert
- Classical Music

要求：

下一首：

提前准备 Decoder。

播放结束：

立即切换。

中间：

不能出现：

- Click
- Pop
- Silence

---

## Crossfade

支持：

歌曲之间：

Crossfade。

配置：

- Disabled
- 1 s
- 2 s
- 3 s
- 5 s
- 10 s

未来：

支持：

Auto Crossfade。

---

## ReplayGain

支持：

读取：

ReplayGain Metadata。

包括：

- Track Gain
- Album Gain
- Peak

播放器：

自动：

调整：

Output Volume。

避免：

不同歌曲：

音量忽大忽小。

---

## Equalizer

提供：

内置 EQ。

支持：

- 5 Band
- 10 Band

预设：

- Flat
- Pop
- Rock
- Jazz
- Classical
- Vocal

未来：

支持：

自定义 Preset。

---

## Audio Effects

增加：

Audio Effect Pipeline。

支持：

- Bass Boost
- Virtualizer
- Loudness
- Compressor
- Limiter

所有效果：

可独立：

启用。

---

## Playback Speed

支持：

播放速度：

- 0.5x
- 0.75x
- 1.0x
- 1.25x
- 1.5x
- 2.0x

要求：

保持：

Pitch。

---

## Sleep Timer

支持：

Sleep Timer。

例如：

15 min

30 min

60 min

90 min

结束：

自动停止播放。

---

# Video

进一步提升：

视频能力。

支持：

HDR Metadata。

支持：

更多 Codec。

支持：

更好的：

Hardware Decoder。

未来：

支持：

Dolby Vision。

---

# Subtitle

支持：

更多字幕格式：

- ASS
- SSA
- WebVTT
- TTML

增加：

字幕：

样式设置。

包括：

- Font
- Size
- Color
- Outline
- Shadow
- Position

支持：

Subtitle Delay。

---

# Picture in Picture

支持：

Android PiP。

要求：

Home 键：

自动进入：

PiP。

恢复：

继续播放。

支持：

Remote Control。

---

# Audio Focus

完善：

Audio Focus。

包括：

Duck

Pause

Resume

Bluetooth

Headset

Phone Call

全部：

符合 Android Audio Focus Policy。

---

# Media Session

完善：

MediaSession。

支持：

Lock Screen

Notification

Bluetooth Headset

Wear OS

Android Auto

Assistant

保证：

所有控制：

统一入口。

---

# Phase 3

目标：

打造完整的家庭媒体中心。

不仅支持 DLNA。

还支持更多媒体来源。

---

# Network Storage

增加更多网络协议支持。

## SMB

支持：

SMB 2.x

SMB 3.x

功能：

- 浏览共享目录
- 文件播放
- 文件搜索
- 图片浏览

支持：

用户名密码认证。

未来：

支持：

Guest Login。

---

## WebDAV

支持：

WebDAV Server。

包括：

- Nextcloud
- ownCloud
- NAS

支持：

HTTPS。

支持：

Digest Authentication。

---

## FTP / FTPS

支持：

FTP

FTPS

SFTP（规划）

包括：

- 浏览
- 播放
- 搜索

支持：

断点读取。

---

# Cloud Storage

增加：

云盘支持。

规划：

- Google Drive
- Dropbox
- OneDrive

未来：

支持：

用户自行扩展 Provider。

统一：

Storage API。

---

# Offline Cache

支持：

离线缓存。

可以缓存：

- 视频
- 音乐
- 图片
- 字幕

支持：

LRU 自动清理。

支持：

缓存大小限制。

支持：

仅 Wi-Fi 下载。

---

# Download Manager

提供：

下载管理器。

支持：

- 暂停
- 恢复
- 重试
- 删除

支持：

多个下载任务。

后台：

持续下载。

---

# Search

增加：

全局搜索。

可搜索：

- 标题
- Album
- Artist
- Folder
- Metadata

未来：

支持：

全文索引。

---

# Library

建立：

媒体库。

自动：

扫描：

所有媒体。

分类：

Music

Movie

TV Show

Photo

支持：

最近播放。

支持：

收藏。

支持：

继续播放。

---

# Recommendation

未来：

增加：

推荐系统。

根据：

- 播放历史
- 收藏
- 最近播放

生成：

推荐列表。

所有推荐：

仅本地计算。

不上传：

任何数据。

---

# DLNA Renderer

增加：

Renderer 模式。

本应用：

既可以：

Control Point。

也可以：

Renderer。

支持：

Push 播放。

支持：

Remote Control。

支持：

Play

Pause

Seek

Stop

Volume。

---

# Casting

增加：

投屏能力。

包括：

DLNA

Google Cast

AirPlay（兼容层）

未来：

统一：

Casting API。

---

# Multi-room

规划：

多房间播放。

支持：

多个 Renderer。

统一：

播放控制。

未来：

支持：

同步播放。

---

# Phase 4

目标：

建立一个可持续演进的播放器生态。

核心保持轻量，扩展能力通过插件提供。

---

# Plugin System

引入插件机制。

所有大型功能均应支持独立安装或启用。

插件类型包括：

- Storage Provider
- Metadata Provider
- Subtitle Provider
- Decoder Extension
- Audio Effect
- Renderer Extension
- Theme
- Visualization

插件生命周期：

- Install
- Enable
- Disable
- Update
- Remove

要求：

插件崩溃：

不能影响主程序。

所有插件：

运行于明确的接口边界。

---

# Metadata Provider

支持多个元数据来源。

例如：

- Album
- Artist
- Genre
- Release Year
- Cover
- Biography

支持：

Provider Priority。

支持：

本地缓存。

支持：

离线模式。

---

# Thumbnail Service

建立统一缩略图服务。

负责：

- Decode
- Resize
- Crop
- Cache

支持：

多级缓存：

- Memory
- Disk

支持：

后台预生成。

---

# Auto Update

对于可支持的平台：

增加：

自动检查更新。

支持：

- Stable
- Beta
- Nightly

支持：

增量更新（规划）。

---

# Crash Reporting

建立统一异常收集。

记录：

- Stack Trace
- Device Info
- Android Version
- App Version

默认：

仅保存在本地。

用户确认后：

才允许导出或上传。

---

# Telemetry

遥测功能默认关闭。

如用户主动开启，仅收集匿名统计信息。

例如：

- App Startup Time
- Playback Success Rate
- Crash Count
- Decoder Usage
- Feature Usage

不会收集：

- 媒体内容
- 文件名
- 播放历史
- 用户身份信息

所有数据项均应公开说明。

---

# CI / CD

持续集成目标：

每次提交：

自动执行：

- Build
- Lint
- Unit Test
- Integration Test
- Static Analysis

Release Branch：

自动生成：

- APK
- AAB
- Mapping
- Changelog

所有 Release：

必须可追溯。

---

# Version Strategy

采用 Semantic Versioning。

格式：

MAJOR.MINOR.PATCH

例如：

1.0.0

1.1.0

1.1.1

规则：

MAJOR：

不兼容变更。

MINOR：

新增功能。

PATCH：

Bug Fix。

---

# Long-term Support

提供：

LTS 版本。

原则：

仅修复：

- Security
- Crash
- Critical Bug

不增加：

新功能。

保证：

长期稳定运行。

---

# Release Strategy

建议采用如下发布流程：

Nightly

↓

Beta

↓

Release Candidate

↓

Stable

每个阶段均需满足对应质量要求。

发布前：

必须完成：

- Regression Test
- Compatibility Test
- Performance Check

---

# Documentation

所有新增模块：

必须同步更新文档。

至少包括：

- Architecture
- API
- Sequence Diagram
- State Machine
- Configuration

文档与代码保持一致。

---

# Vision

长期目标：

构建一个现代化、高性能、模块化的 Android 媒体播放器。

项目应具备以下特点：

- 架构清晰
- 易于维护
- 易于扩展
- 高性能
- 高稳定性
- 支持多种媒体协议
- 支持多种存储方式
- 支持移动端与 Android TV
- 尊重用户隐私
- 不依赖任何特定服务

最终，希望项目能够成为一个：

- 优秀的 DLNA Control Point
- 优秀的 DLNA Renderer
- 优秀的本地媒体播放器
- 优秀的家庭媒体中心

---

# End

Roadmap 将根据项目实际进展持续更新。

新增功能应优先遵循以下原则：

1. 保持核心模块简单。
2. 优先保证稳定性。
3. 优先保证兼容性。
4. 避免过度设计。
5. 能够独立测试。
6. 能够独立维护。
7. 能够逐步演进。

> Simple, Stable, Extensible.