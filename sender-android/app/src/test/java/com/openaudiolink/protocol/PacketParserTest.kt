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

    @Test
    fun validateAacPayload_tooShort_throws() {
        assertThrows(PacketParseException::class.java) {
            AudioPayloadValidator.validateAacPayload(ByteArray(ProtocolConstants.AudioPayloadHeaderSize - 1))
        }
    }

    @Test
    fun validateAacPayload_unsupportedCodec_throws() {
        val payload = validAudioPayload()
        payload[0] = ProtocolConstants.CodecOpus.toByte()

        assertThrows(PacketParseException::class.java) {
            AudioPayloadValidator.validateAacPayload(payload)
        }
    }

    @Test
    fun validateAacPayload_encodedSizeMismatch_throws() {
        val payload = validAudioPayload()
        payload[18] = 0x05

        assertThrows(PacketParseException::class.java) {
            AudioPayloadValidator.validateAacPayload(payload)
        }
    }

    @Test
    fun parseHeader_phase1aPacketTypesAndPayloads_matchFixtures() {
        val cases = listOf(
            Triple("valid-hello.bin", ProtocolConstants.PacketTypeHello, "000d416e64726f69642050686f6e650005312e302e3001000100000001"),
            Triple("valid-welcome.bin", ProtocolConstants.PacketTypeWelcome, "00000a57696e646f77732050430005312e302e300102030405060708"),
            Triple("valid-start-stream.bin", ProtocolConstants.PacketTypeStartStream, "010000bb80020002ee000014"),
            Triple("valid-stream-ready.bin", ProtocolConstants.PacketTypeStreamReady, "00010000bb8002"),
            Triple("valid-audio-aac.bin", ProtocolConstants.PacketTypeAudio, "010000000100000000075bcd1500140000000411223344"),
            Triple("valid-stop-stream.bin", ProtocolConstants.PacketTypeStopStream, ""),
            Triple("valid-ping.bin", ProtocolConstants.PacketTypePing, "0000000500000000075bca05"),
            Triple("valid-pong.bin", ProtocolConstants.PacketTypePong, "0000000500000000075bca05"),
            Triple("valid-error.bin", ProtocolConstants.PacketTypeError, "03eb020011556e737570706f7274656420636f646563"),
        )

        cases.forEach { (fixture, packetType, payloadHex) ->
            val packet = readFixture(fixture)
            assertEquals(packetType, PacketParser.parseHeader(packet).packetType)
            assertArrayEquals(hex(payloadHex), PacketParser.payload(packet))
        }
    }

    @Test
    fun payload_invalidDeclaredLength_throws() {
        assertThrows(PacketParseException::class.java) {
            PacketParser.payload(readFixture("invalid-length.bin"))
        }
    }

    private fun validAudioPayload(): ByteArray = PacketParser.payload(readFixture("valid-audio-aac.bin"))

    private fun hex(value: String): ByteArray = value.chunked(2).map { it.toInt(16).toByte() }.toByteArray()

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
