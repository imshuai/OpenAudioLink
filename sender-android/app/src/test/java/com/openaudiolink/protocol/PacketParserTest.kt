package com.openaudiolink.protocol

import com.openaudiolink.TestFixtures
import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertThrows
import org.junit.Test

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
    fun validateAacPayload_validAudioPayload_exposesCanonicalRawFrame() {
        val packet = TestFixtures.read("testdata/protocol/valid-audio-aac.bin")
        val payload = PacketParser.payload(packet)
        val encoded = TestFixtures.read("testdata/audio/aac-lc-48k-stereo-1024.raw")

        assertEquals(ProtocolConstants.PacketTypeAudio, PacketParser.parseHeader(packet).packetType)
        assertEquals(ProtocolConstants.AudioPayloadHeaderSize + encoded.size, payload.size)
        assertArrayEquals(
            HandshakePayloads.audio(
                ProtocolConstants.CodecAacLc,
                1,
                123456789,
                21,
                encoded,
            ),
            payload,
        )
        assertArrayEquals(
            encoded,
            payload.copyOfRange(ProtocolConstants.AudioPayloadHeaderSize, payload.size),
        )
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
            Triple("valid-start-stream.bin", ProtocolConstants.PacketTypeStartStream, "010000bb80020002ee000015"),
            Triple("valid-stream-ready.bin", ProtocolConstants.PacketTypeStreamReady, "00010000bb8002"),
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

    private fun readFixture(name: String) = TestFixtures.read("testdata/protocol/$name")
}
