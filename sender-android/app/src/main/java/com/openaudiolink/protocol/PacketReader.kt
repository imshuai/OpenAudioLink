package com.openaudiolink.protocol

import java.io.EOFException
import java.io.InputStream

object PacketReader {
    fun readPacket(input: InputStream): ByteArray {
        val headerBytes = input.readFully(ProtocolConstants.HeaderSize)
        if (!ProtocolConstants.Magic.indices.all { headerBytes[it] == ProtocolConstants.Magic[it] }) throw PacketParseException("Invalid magic.")
        if ((headerBytes[4].toInt() and 0xff) != ProtocolConstants.MajorVersion) throw PacketParseException("Unsupported major version.")
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
