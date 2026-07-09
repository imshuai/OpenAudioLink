package com.openaudiolink.protocol

import java.io.ByteArrayOutputStream

object HandshakePayloads {
    fun hello(senderName: String, senderVersion: String, platform: Int, capabilities: Long): ByteArray = bytes {
        string(senderName); string(senderVersion); byte(platform); uint64(capabilities)
    }

    fun welcome(result: Int, receiverName: String, appVersion: String, sessionId: Long): ByteArray = bytes {
        byte(result); string(receiverName); string(appVersion); uint64(sessionId)
    }

    fun startStream(codec: Int, sampleRate: Long, channels: Int, bitrate: Long, frameDurationMs: Int): ByteArray = bytes {
        byte(codec); uint32(sampleRate); byte(channels); uint32(bitrate); uint16(frameDurationMs)
    }

    fun streamReady(result: Int, codec: Int, sampleRate: Long, channels: Int): ByteArray = bytes {
        byte(result); byte(codec); uint32(sampleRate); byte(channels)
    }

    fun ping(sequenceNumber: Long, timestamp: Long): ByteArray = bytes { uint32(sequenceNumber); uint64(timestamp) }
    fun error(code: Int, severity: Int, message: String): ByteArray = bytes { uint16(code); byte(severity); string(message) }

    private fun bytes(block: Writer.() -> Unit): ByteArray = Writer().apply(block).out.toByteArray()

    private class Writer {
        val out = ByteArrayOutputStream()
        fun byte(value: Int) = out.write(value)
        fun uint16(value: Int) = out.write(byteArrayOf((value ushr 8).toByte(), value.toByte()))
        fun uint32(value: Long) = out.write(ByteArray(4).also { PacketWriter.writeUInt32(it, 0, value) })
        fun uint64(value: Long) = out.write(ByteArray(8).also { PacketWriter.writeUInt64(it, 0, value) })
        fun string(value: String) {
            val data = value.toByteArray(Charsets.UTF_8)
            require(data.size <= 0xffff) { "String exceeds uint16 length." }
            uint16(data.size)
            out.write(data)
        }
    }
}
