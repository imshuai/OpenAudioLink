package com.openaudiolink.protocol

object ProtocolConstants {
    const val HeaderSize = 24
    const val AudioPayloadHeaderSize = 19
    const val MaxPacketSize = 65536
    const val MajorVersion = 1
    const val MinorVersion = 0
    const val PacketTypeHello = 0x01
    const val PacketTypeAudio = 0x05
    const val CodecAacLc = 1
    val Magic = byteArrayOf('O'.code.toByte(), 'A'.code.toByte(), 'L'.code.toByte(), 'P'.code.toByte())
}
