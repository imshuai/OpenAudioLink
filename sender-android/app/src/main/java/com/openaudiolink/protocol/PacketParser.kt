package com.openaudiolink.protocol

object PacketParser {
    fun parseHeader(packet: ByteArray?): PacketHeader {
        if (packet == null) throw PacketParseException("Packet is null.")
        if (packet.size < ProtocolConstants.HeaderSize) throw PacketParseException("Packet is shorter than header.")
        if (!ProtocolConstants.Magic.indices.all { packet[it] == ProtocolConstants.Magic[it] }) {
            throw PacketParseException("Invalid magic.")
        }

        val majorVersion = packet[4].u8()
        if (majorVersion != ProtocolConstants.MajorVersion) throw PacketParseException("Unsupported major version.")

        val payloadLength = readUInt32(packet, 20)
        if (payloadLength > ProtocolConstants.MaxPacketSize) throw PacketParseException("Payload is too large.")
        if (packet.size.toLong() < ProtocolConstants.HeaderSize.toLong() + payloadLength) {
            throw PacketParseException("Packet is shorter than payload length.")
        }

        return PacketHeader(
            majorVersion = majorVersion,
            minorVersion = packet[5].u8(),
            packetType = packet[6].u8(),
            flags = packet[7].u8(),
            sequenceNumber = readUInt32(packet, 8),
            timestamp = readUInt64(packet, 12),
            payloadLength = payloadLength.toInt(),
        )
    }

    fun payload(packet: ByteArray?): ByteArray {
        val header = parseHeader(packet)
        return packet!!.copyOfRange(ProtocolConstants.HeaderSize, ProtocolConstants.HeaderSize + header.payloadLength)
    }

    internal fun readUInt32(buffer: ByteArray, offset: Int): Long =
        (buffer[offset].u8().toLong() shl 24) or
            (buffer[offset + 1].u8().toLong() shl 16) or
            (buffer[offset + 2].u8().toLong() shl 8) or
            buffer[offset + 3].u8().toLong()

    private fun readUInt64(buffer: ByteArray, offset: Int): Long =
        (readUInt32(buffer, offset) shl 32) or readUInt32(buffer, offset + 4)

    private fun Byte.u8(): Int = toInt() and 0xff
}
