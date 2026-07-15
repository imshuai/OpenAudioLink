package com.openaudiolink.network

import com.openaudiolink.ManualConnectController
import com.openaudiolink.ManualConnectStatus
import com.openaudiolink.protocol.HandshakePayloads
import com.openaudiolink.protocol.PacketParser
import com.openaudiolink.protocol.PacketReader
import com.openaudiolink.protocol.PacketWriter
import com.openaudiolink.protocol.ProtocolConstants
import java.net.ServerSocket
import java.util.concurrent.atomic.AtomicLong
import java.util.concurrent.atomic.AtomicReference
import kotlin.concurrent.thread
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Test

class TcpHandshakeClientTest {
    @Test
    fun producerFailureClosesSocketOnOwnerThreadAndReportsFailed() {
        val failure = AtomicReference<Throwable?>()
        val supplierThread = AtomicLong(-1L)
        ServerSocket(0).use { listener ->
            listener.soTimeout = SOCKET_TIMEOUT_MS
            val server = thread(isDaemon = true) {
                try {
                    listener.accept().use { client ->
                        client.soTimeout = SOCKET_TIMEOUT_MS
                        val input = client.getInputStream()
                        val output = client.getOutputStream()
                        assertType(PacketReader.readPacket(input), ProtocolConstants.PacketTypeHello)
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
                        assertType(PacketReader.readPacket(input), ProtocolConstants.PacketTypeStartStream)
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
                        assertEquals(-1, input.read())
                    }
                } catch (error: Throwable) {
                    failure.set(error)
                }
            }

            val owner = Thread.currentThread().id
            val status = ManualConnectController { host ->
                TcpHandshakeClient {
                    supplierThread.set(Thread.currentThread().id)
                    throw IllegalStateException("producer failed")
                }.connect(host, listener.localPort)
            }.connect("127.0.0.1")

            server.join(JOIN_TIMEOUT_MS)
            assertFalse("server thread timed out", server.isAlive)
            assertNull(failure.get())
            assertEquals(owner, supplierThread.get())
            assertEquals(ManualConnectStatus.Failed, status)
        }
    }

    private fun assertType(packet: ByteArray, expected: Int) {
        assertEquals(expected, PacketParser.parseHeader(packet).packetType)
    }

    private companion object {
        const val SOCKET_TIMEOUT_MS = 15_000
        const val JOIN_TIMEOUT_MS = 30_000L
    }
}
