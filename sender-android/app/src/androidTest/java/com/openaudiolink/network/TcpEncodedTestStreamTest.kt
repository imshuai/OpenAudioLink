package com.openaudiolink.network

import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import com.openaudiolink.protocol.AudioPayloadValidator
import com.openaudiolink.protocol.HandshakePayloads
import com.openaudiolink.protocol.PacketParser
import com.openaudiolink.protocol.PacketReader
import com.openaudiolink.protocol.PacketWriter
import com.openaudiolink.protocol.ProtocolConstants
import java.io.File
import java.io.FileOutputStream
import java.io.InputStream
import java.io.OutputStream
import java.net.ServerSocket
import java.util.concurrent.atomic.AtomicReference
import kotlin.concurrent.thread
import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test
import org.junit.runner.RunWith

@RunWith(AndroidJUnit4::class)
class TcpEncodedTestStreamTest {
    @Test
    fun executablePathSendsExactEncodedTestStreamAndExportsWire() {
        val packets = mutableListOf<ByteArray>()
        val failure = AtomicReference<Throwable?>()
        ServerSocket(0).use { listener ->
            listener.soTimeout = SOCKET_TIMEOUT_MS
            val server = thread(isDaemon = true) {
                try {
                    listener.accept().use { client ->
                        client.soTimeout = SOCKET_TIMEOUT_MS
                        val input = client.getInputStream()
                        val output = client.getOutputStream()
                        packets += readType(input, ProtocolConstants.PacketTypeHello)
                        writeWelcome(output)
                        packets += readType(input, ProtocolConstants.PacketTypeStartStream)
                        writeReady(output)
                        repeat(4) {
                            packets += readType(input, ProtocolConstants.PacketTypeAudio)
                        }
                        val ping = readType(input, ProtocolConstants.PacketTypePing)
                        packets += ping
                        output.write(PacketWriter.writePacket(
                            ProtocolConstants.PacketTypePong,
                            7,
                            7,
                            PacketParser.payload(ping),
                        ))
                        packets += readType(input, ProtocolConstants.PacketTypeStopStream)
                    }
                } catch (error: Throwable) {
                    failure.set(error)
                }
            }

            assertTrue(TcpHandshakeClient().connect("127.0.0.1", listener.localPort))
            server.join(JOIN_TIMEOUT_MS)
            assertFalse("server thread timed out", server.isAlive)
            failure.get()?.let { throw AssertionError("server failed", it) }
        }

        assertExactPackets(packets)
        writeArtifact(packets)
    }

    private fun readType(input: InputStream, expected: Int): ByteArray =
        PacketReader.readPacket(input).also {
            assertEquals(expected, PacketParser.parseHeader(it).packetType)
        }

    private fun writeWelcome(output: OutputStream) {
        output.write(PacketWriter.writePacket(
            ProtocolConstants.PacketTypeWelcome,
            1,
            1,
            HandshakePayloads.welcome(
                ProtocolConstants.ResultSuccess,
                "receiver",
                "1.0",
                7,
            ),
        ))
    }

    private fun writeReady(output: OutputStream) {
        output.write(PacketWriter.writePacket(
            ProtocolConstants.PacketTypeStreamReady,
            2,
            2,
            HandshakePayloads.streamReady(
                ProtocolConstants.StreamResultSuccess,
                ProtocolConstants.CodecAacLc,
                48000,
                2,
            ),
        ))
    }

    private fun writeArtifact(packets: List<ByteArray>) {
        val context = InstrumentationRegistry.getInstrumentation().targetContext
        FileOutputStream(File(context.filesDir, ARTIFACT_NAME), false).use { output ->
            packets.forEach { packet -> output.write(packet) }
        }
    }

    private fun assertExactPackets(packets: List<ByteArray>) {
        assertEquals(8, packets.size)
        val types = intArrayOf(
            ProtocolConstants.PacketTypeHello,
            ProtocolConstants.PacketTypeStartStream,
            ProtocolConstants.PacketTypeAudio,
            ProtocolConstants.PacketTypeAudio,
            ProtocolConstants.PacketTypeAudio,
            ProtocolConstants.PacketTypeAudio,
            ProtocolConstants.PacketTypePing,
            ProtocolConstants.PacketTypeStopStream,
        )
        val sequences = longArrayOf(1, 2, 3, 4, 5, 6, 7, 8)
        val timestamps = longArrayOf(
            123456000,
            123456002,
            123456003,
            123477336,
            123498670,
            123520003,
            123520005,
            123520006,
        )
        packets.indices.forEach { index ->
            val header = PacketParser.parseHeader(packets[index])
            assertEquals(types[index], header.packetType)
            assertEquals(sequences[index], header.sequenceNumber)
            assertEquals(timestamps[index], header.timestamp)
        }
        assertArrayEquals(
            HandshakePayloads.hello(
                "Android Phone",
                "1.0.0",
                ProtocolConstants.PlatformAndroid,
                ProtocolConstants.CapabilityAacSupported,
            ),
            PacketParser.payload(packets[0]),
        )
        assertArrayEquals(
            HandshakePayloads.startStream(
                ProtocolConstants.CodecAacLc,
                48000,
                2,
                192000,
                21,
            ),
            PacketParser.payload(packets[1]),
        )
        repeat(4) { index ->
            val payload = PacketParser.payload(packets[index + 2])
            AudioPayloadValidator.validateAacPayload(payload)
            assertEquals(index + 1L, readUInt32(payload, 1))
            assertEquals(timestamps[index + 2], readUInt64(payload, 5))
            assertEquals(21, readUInt16(payload, 13))
            assertTrue(payload.size > ProtocolConstants.AudioPayloadHeaderSize)
        }
        assertArrayEquals(
            HandshakePayloads.ping(6, 123520004),
            PacketParser.payload(packets[6]),
        )
        assertEquals(0, PacketParser.payload(packets[7]).size)
    }

    private fun readUInt16(bytes: ByteArray, offset: Int): Int =
        ((bytes[offset].toInt() and 0xff) shl 8) or
            (bytes[offset + 1].toInt() and 0xff)

    private fun readUInt32(bytes: ByteArray, offset: Int): Long =
        ((bytes[offset].toLong() and 0xff) shl 24) or
            ((bytes[offset + 1].toLong() and 0xff) shl 16) or
            ((bytes[offset + 2].toLong() and 0xff) shl 8) or
            (bytes[offset + 3].toLong() and 0xff)

    private fun readUInt64(bytes: ByteArray, offset: Int): Long =
        (readUInt32(bytes, offset) shl 32) or readUInt32(bytes, offset + 4)

    private companion object {
        const val SOCKET_TIMEOUT_MS = 15_000
        const val JOIN_TIMEOUT_MS = 30_000L
        const val ARTIFACT_NAME = "mediacodec-runtime-wire.bin"
    }
}
