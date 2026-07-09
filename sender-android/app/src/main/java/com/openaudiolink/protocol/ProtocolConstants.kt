package com.openaudiolink.protocol

object ProtocolConstants {
    const val HeaderSize = 24
    const val AudioPayloadHeaderSize = 19
    const val MaxPacketSize = 65536
    const val MajorVersion = 1
    const val MinorVersion = 0
    const val PacketTypeHello = 0x01
    const val PacketTypeWelcome = 0x02
    const val PacketTypeStartStream = 0x03
    const val PacketTypeStreamReady = 0x04
    const val PacketTypeAudio = 0x05
    const val PacketTypeStopStream = 0x06
    const val PacketTypePing = 0x07
    const val PacketTypePong = 0x08
    const val PacketTypeError = 0x09
    const val ResultSuccess = 0
    const val ResultUnsupportedProtocol = 1
    const val ResultReceiverBusy = 2
    const val ResultInternalError = 4
    const val StreamResultSuccess = 0
    const val StreamResultUnsupportedCodec = 1
    const val StreamResultUnsupportedFormat = 2
    const val StreamResultReceiverNotReady = 3
    const val StreamResultInternalError = 4
    const val ErrorInvalidPacket = 1001
    const val ErrorUnsupportedProtocol = 1002
    const val ErrorUnsupportedCodec = 1003
    const val ErrorInvalidPayload = 1004
    const val ErrorReceiverBusy = 1005
    const val ErrorTimeout = 1008
    const val ErrorSeverityRecoverable = 2
    const val ErrorSeverityFatal = 3
    const val CodecAacLc = 1
    const val PlatformAndroid = 1
    const val PlatformWindows = 2
    const val CapabilityAacSupported = 1L
    const val DefaultPort = 37373
    val Magic = byteArrayOf('O'.code.toByte(), 'A'.code.toByte(), 'L'.code.toByte(), 'P'.code.toByte())
}
