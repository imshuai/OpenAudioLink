package com.openaudiolink.protocol

object PacketWriter {
    fun write(packetType: Int, sequenceNumber: Long, timestamp: Long, payload: ByteArray = byteArrayOf()): ByteArray =
        ByteArray(ProtocolConstants.HeaderSize + payload.size).also { packet ->
            ProtocolConstants.Magic.copyInto(packet)
            packet[4] = ProtocolConstants.MajorVersion.toByte()
            packet[5] = ProtocolConstants.MinorVersion.toByte()
            packet[6] = packetType.toByte()
            packet[7] = 0
            writeUInt32(packet, 8, sequenceNumber)
            writeUInt64(packet, 12, timestamp)
            writeUInt32(packet, 20, payload.size.toLong())
            payload.copyInto(packet, ProtocolConstants.HeaderSize)
        }

    internal fun writeUInt16(buffer: ByteArray, offset: Int, value: Int) {
        buffer[offset] = (value ushr 8).toByte()
        buffer[offset + 1] = value.toByte()
    }

    internal fun writeUInt32(buffer: ByteArray, offset: Int, value: Long) {
        buffer[offset] = (value ushr 24).toByte()
        buffer[offset + 1] = (value ushr 16).toByte()
        buffer[offset + 2] = (value ushr 8).toByte()
        buffer[offset + 3] = value.toByte()
    }

    internal fun writeUInt64(buffer: ByteArray, offset: Int, value: Long) {
        writeUInt32(buffer, offset, value ushr 32)
        writeUInt32(buffer, offset + 4, value)
    }
}
