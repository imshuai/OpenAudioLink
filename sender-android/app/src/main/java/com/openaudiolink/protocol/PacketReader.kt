package com.openaudiolink.protocol

import java.io.EOFException
import java.io.InputStream

data class Packet(val header: PacketHeader, val payload: ByteArray)

object PacketReader {
    fun read(input: InputStream): Packet {
        val headerBytes = input.readFully(ProtocolConstants.HeaderSize)
        val payloadLength = PacketParser.readUInt32(headerBytes, 20)
        if (payloadLength > ProtocolConstants.MaxPacketSize) PacketParser.parseHeader(headerBytes)
        val payload = input.readFully(payloadLength.toInt())
        val packet = headerBytes + payload
        return Packet(PacketParser.parseHeader(packet), payload)
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
