package com.openaudiolink.protocol

import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertThrows
import org.junit.Test
import java.io.File
import java.io.FileNotFoundException

class PacketParserTest {
    @Test
    fun parseHeader_validHello_returnsHeaderAndDeclaredPayload() {
        val packet = readFixture("valid-hello.bin")

        val header = PacketParser.parseHeader(packet)
        val payload = PacketParser.payload(packet)

        assertEquals(ProtocolConstants.MajorVersion, header.majorVersion)
        assertEquals(ProtocolConstants.MinorVersion, header.minorVersion)
        assertEquals(ProtocolConstants.PacketTypeHello, header.packetType)
        assertEquals(1L, header.sequenceNumber)
        assertEquals(123456000L, header.timestamp)
        assertEquals(payload.size, header.payloadLength)
        assertArrayEquals(packet.copyOfRange(ProtocolConstants.HeaderSize, ProtocolConstants.HeaderSize + payload.size), payload)
    }

    @Test
    fun parseHeader_invalidMagic_throws() {
        assertThrows(PacketParseException::class.java) {
            PacketParser.parseHeader(readFixture("invalid-magic.bin"))
        }
    }

    @Test
    fun parseHeader_invalidLength_throws() {
        assertThrows(PacketParseException::class.java) {
            PacketParser.parseHeader(readFixture("invalid-length.bin"))
        }
    }

    @Test
    fun validateAacPayload_validAudioPayload_doesNotThrow() {
        val packet = readFixture("valid-audio-aac.bin")

        val header = PacketParser.parseHeader(packet)
        val payload = PacketParser.payload(packet)

        assertEquals(ProtocolConstants.PacketTypeAudio, header.packetType)
        assertEquals(ProtocolConstants.CodecAacLc, payload[0].toInt() and 0xff)
        assertEquals(23, payload.size)
        AudioPayloadValidator.validateAacPayload(payload)
    }

    private fun readFixture(name: String): ByteArray {
        var directory: File? = File(System.getProperty("user.dir")).absoluteFile
        while (directory != null) {
            val candidates = listOf(
                File(directory, "testdata/protocol/$name"),
                File(directory, "../testdata/protocol/$name"),
            )
            candidates.firstOrNull { it.isFile }?.let { return it.readBytes() }
            directory = directory.parentFile
        }
        throw FileNotFoundException("Fixture not found: $name")
    }
}
