package com.openaudiolink.protocol

import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertThrows
import org.junit.Test
import java.io.ByteArrayInputStream
import java.io.EOFException
import java.io.FilterInputStream
import java.io.InputStream

class PacketReaderTest {
    @Test
    fun readPacket_prematureClose_throwsEof() {
        assertThrows(EOFException::class.java) {
            PacketReader.readPacket(ByteArrayInputStream(byteArrayOf(0x4f, 0x41)))
        }
    }

    @Test
    fun readPacket_invalidHeader_throwsBeforePayloadRead() {
        val header = packetHeader(1).also { it[0] = 0 }

        assertThrows(PacketParseException::class.java) {
            PacketReader.readPacket(FailAfterBytesInputStream(header + byteArrayOf(1), ProtocolConstants.HeaderSize))
        }
    }

    @Test
    fun readPacket_oversizeLength_throwsBeforePayloadRead() {
        val header = packetHeader(ProtocolConstants.MaxPacketSize + 1L)

        assertThrows(PacketParseException::class.java) {
            PacketReader.readPacket(FailAfterBytesInputStream(header + byteArrayOf(1), ProtocolConstants.HeaderSize))
        }
    }

    @Test
    fun readPacket_concatenatedPackets_readsOneAtATime() {
        val first = PacketWriter.writePacket(ProtocolConstants.PacketTypePing, 1, 2, byteArrayOf(3))
        val second = PacketWriter.writePacket(ProtocolConstants.PacketTypePong, 4, 5, byteArrayOf(6, 7))
        val input = ByteArrayInputStream(first + second)

        assertArrayEquals(first, PacketReader.readPacket(input))
        assertArrayEquals(second, PacketReader.readPacket(input))
        assertEquals(0, input.available())
    }

    private fun packetHeader(payloadLength: Long) =
        PacketWriter.writePacket(ProtocolConstants.PacketTypePing, 1, 2, ByteArray(payloadLength.coerceAtMost(1).toInt()))
            .copyOf(ProtocolConstants.HeaderSize)
            .also {
                it[20] = (payloadLength ushr 24).toByte()
                it[21] = (payloadLength ushr 16).toByte()
                it[22] = (payloadLength ushr 8).toByte()
                it[23] = payloadLength.toByte()
            }

    private class FailAfterBytesInputStream(bytes: ByteArray, private val allowed: Int) : FilterInputStream(ByteArrayInputStream(bytes)) {
        private var read = 0

        override fun read(b: ByteArray, off: Int, len: Int): Int {
            if (read >= allowed) error("payload read attempted")
            val count = super.read(b, off, minOf(len, allowed - read))
            if (count > 0) read += count
            return count
        }

        override fun read(): Int {
            if (read >= allowed) error("payload read attempted")
            val value = super.read()
            if (value >= 0) read++
            return value
        }
    }
}
