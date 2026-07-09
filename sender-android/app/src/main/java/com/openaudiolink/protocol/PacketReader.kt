package com.openaudiolink.protocol

import java.io.EOFException
import java.io.InputStream

object PacketReader {
    fun readPacket(input: InputStream): ByteArray {
        val headerBytes = input.readFully(ProtocolConstants.HeaderSize)
        val payloadLength = PacketParser.readUInt32(headerBytes, 20)
        if (payloadLength > ProtocolConstants.MaxPacketSize) throw PacketParseException("Payload length exceeds max packet size.")
        val payload = input.readFully(payloadLength.toInt())
        return (headerBytes + payload).also { PacketParser.parseHeader(it) }
    }

    private fun InputStream.readFully(size: Int): ByteArray {
        val buffer = ByteArray(size)
        var offset = 0
        while (offset < size) {
            val read = read(buffer, offset, size - offset)
            if (read < 0) throw EOFException("Premature end of stream.")
            offset += read
        }
        return buffer
    }
}
