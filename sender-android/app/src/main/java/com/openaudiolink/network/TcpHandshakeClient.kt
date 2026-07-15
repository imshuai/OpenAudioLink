package com.openaudiolink.network

import com.openaudiolink.protocol.ProtocolConstants
import java.net.InetSocketAddress
import java.net.Socket

class TcpHandshakeClient(
    private val audioFrameSupplier: () -> List<ByteArray> = {
        List(3) { FakeAacFrameBytes.clone() }
    },
) {
    fun connect(host: String, port: Int = ProtocolConstants.DefaultPort): Boolean = Socket().use { socket ->
        socket.connect(InetSocketAddress(host, port), 10_000)
        socket.soTimeout = 15_000
        HandshakeClient(audioFrameSupplier).run(socket.getInputStream(), socket.getOutputStream())
    }
}
