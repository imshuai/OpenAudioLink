package com.openaudiolink.network

import com.openaudiolink.TestFixtures
import com.openaudiolink.protocol.HandshakePayloads
import com.openaudiolink.protocol.PacketParser
import com.openaudiolink.protocol.PacketReader
import com.openaudiolink.protocol.PacketWriter
import com.openaudiolink.protocol.ProtocolConstants
import java.io.ByteArrayInputStream
import java.io.ByteArrayOutputStream
import java.io.IOException
import org.junit.Assert.*
import org.junit.Test

class HandshakeClientTest {
    @Test
    fun runWritesHandshakePacketsOnSuccess() {
        val input = ByteArrayInputStream(successfulResponses())
        val output = ByteArrayOutputStream()
        val encoded = TestFixtures.read("testdata/audio/aac-lc-48k-stereo-1024.raw")

        assertTrue(HandshakeClient().run(input, output))

        val written = ByteArrayInputStream(output.toByteArray())
        assertPacket(written, ProtocolConstants.PacketTypeHello, 1)
        assertArrayEquals(
            HandshakePayloads.startStream(ProtocolConstants.CodecAacLc, 48000, 2, 192000, 21),
            assertPacket(written, ProtocolConstants.PacketTypeStartStream, 2),
        )
        val timestamps = longArrayOf(123456003, 123477336, 123498670)
        timestamps.forEachIndexed { index, timestamp ->
            assertArrayEquals(
                HandshakePayloads.audio(ProtocolConstants.CodecAacLc, (index + 1).toLong(), timestamp, 21, encoded),
                assertPacket(written, ProtocolConstants.PacketTypeAudio, (index + 3).toLong(), timestamp),
            )
        }
        assertArrayEquals(
            HandshakePayloads.ping(5, 123498671),
            assertPacket(written, ProtocolConstants.PacketTypePing, 6, 123498672),
        )
        assertPacket(written, ProtocolConstants.PacketTypeStopStream, 7, 123498673)
        assertEquals(0, written.available())
    }

    @Test
    fun fakeAacFrame_matchesCanonicalFixture() {
        assertArrayEquals(TestFixtures.read("testdata/audio/aac-lc-48k-stereo-1024.raw"), FakeAacFrameBytes)
    }

    @Test
    fun runReturnsFalseWhenReceiverSendsDataAfterStop() {
        val input = ByteArrayInputStream(successfulResponses() + byteArrayOf(0x01))

        assertFalse(HandshakeClient().run(input, ByteArrayOutputStream()))
    }

    @Test
    fun runReturnsFalseWhenReceiverCloseFailsAfterStop() {
        val input = IOExceptionAtEofInputStream(successfulResponses())

        assertFalse(HandshakeClient().run(input, ByteArrayOutputStream()))
    }

    @Test
    fun runReturnsFalseOnBusyWelcome() {
        val input = ByteArrayInputStream(
            PacketWriter.writePacket(ProtocolConstants.PacketTypeWelcome, 1, 1, HandshakePayloads.welcome(ProtocolConstants.ResultReceiverBusy, "receiver", "1.0", 7))
        )
        val output = ByteArrayOutputStream()

        assertFalse(HandshakeClient().run(input, output))
    }

    @Test
    fun runReturnsFalseOnTimeout() {
        assertFalse(HandshakeClient().run(ByteArrayInputStream(byteArrayOf()), ByteArrayOutputStream()))
    }

    @Test
    fun runReturnsFalseOnProtocolRejection() {
        val input = ByteArrayInputStream(
            PacketWriter.writePacket(ProtocolConstants.PacketTypeWelcome, 1, 1, HandshakePayloads.welcome(ProtocolConstants.ResultUnsupportedProtocol, "receiver", "1.0", 7))
        )

        assertFalse(HandshakeClient().run(input, ByteArrayOutputStream()))
    }

    @Test
    fun runReturnsFalseOnStreamReadyFailure() {
        val input = ByteArrayInputStream(
            PacketWriter.writePacket(ProtocolConstants.PacketTypeWelcome, 1, 1, HandshakePayloads.welcome(ProtocolConstants.ResultSuccess, "receiver", "1.0", 7)) +
                PacketWriter.writePacket(ProtocolConstants.PacketTypeStreamReady, 2, 2, HandshakePayloads.streamReady(ProtocolConstants.StreamResultUnsupportedCodec, ProtocolConstants.CodecAacLc, 48000, 2))
        )

        assertFalse(HandshakeClient().run(input, ByteArrayOutputStream()))
    }

    @Test
    fun runReturnsFalseOnPongPayloadMismatch() {
        val input = ByteArrayInputStream(
            PacketWriter.writePacket(ProtocolConstants.PacketTypeWelcome, 1, 1, HandshakePayloads.welcome(ProtocolConstants.ResultSuccess, "receiver", "1.0", 7)) +
                PacketWriter.writePacket(ProtocolConstants.PacketTypeStreamReady, 2, 2, HandshakePayloads.streamReady(ProtocolConstants.StreamResultSuccess, ProtocolConstants.CodecAacLc, 48000, 2)) +
                PacketWriter.writePacket(ProtocolConstants.PacketTypePong, 3, 3, HandshakePayloads.ping(6, 123498671))
        )

        assertFalse(HandshakeClient().run(input, ByteArrayOutputStream()))
    }

    private fun successfulResponses(): ByteArray =
        PacketWriter.writePacket(ProtocolConstants.PacketTypeWelcome, 1, 1, HandshakePayloads.welcome(ProtocolConstants.ResultSuccess, "receiver", "1.0", 7)) +
            PacketWriter.writePacket(ProtocolConstants.PacketTypeStreamReady, 2, 2, HandshakePayloads.streamReady(ProtocolConstants.StreamResultSuccess, ProtocolConstants.CodecAacLc, 48000, 2)) +
            PacketWriter.writePacket(ProtocolConstants.PacketTypePong, 6, 6, HandshakePayloads.ping(5, 123498671))

    private class IOExceptionAtEofInputStream(bytes: ByteArray) : ByteArrayInputStream(bytes) {
        override fun read(): Int = super.read().also {
            if (it == -1) throw IOException("Receiver did not close cleanly.")
        }
    }

    private fun assertPacket(
        input: ByteArrayInputStream,
        packetType: Int,
        sequenceNumber: Long,
        timestamp: Long? = null,
    ): ByteArray {
        val packet = PacketReader.readPacket(input)
        val header = PacketParser.parseHeader(packet)
        assertEquals(packetType, header.packetType)
        assertEquals(sequenceNumber, header.sequenceNumber)
        if (timestamp != null) assertEquals(timestamp, header.timestamp)
        return PacketParser.payload(packet)
    }
}
