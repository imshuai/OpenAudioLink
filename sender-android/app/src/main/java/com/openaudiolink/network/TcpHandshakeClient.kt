package com.openaudiolink.network

import com.openaudiolink.protocol.ProtocolConstants
import java.net.InetSocketAddress
import java.net.Socket

object TcpHandshakeClient {
    fun connect(host: String, port: Int = ProtocolConstants.DefaultPort): Boolean = Socket().use { socket ->
        socket.connect(InetSocketAddress(host, port), 10_000)
        socket.soTimeout = 15_000
        HandshakeClient.run(socket.getInputStream(), socket.getOutputStream())
    }
}
