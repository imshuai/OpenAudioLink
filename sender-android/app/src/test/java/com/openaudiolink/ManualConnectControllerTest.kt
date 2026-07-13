package com.openaudiolink

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Test
import java.io.IOException

class ManualConnectControllerTest {
    @Test
    fun emptyHostFailsWithoutCallingConnector() {
        var called = false
        val status = ManualConnectController {
            called = true
            true
        }.connect("   ")

        assertEquals(ManualConnectStatus.Failed, status)
        assertFalse(called)
    }

    @Test
    fun trimsHostBeforeConnecting() {
        var connectedHost = ""
        val status = ManualConnectController { host ->
            connectedHost = host
            true
        }.connect("  192.168.3.20  ")

        assertEquals(ManualConnectStatus.Success, status)
        assertEquals("192.168.3.20", connectedHost)
    }

    @Test
    fun successfulConnectorReturnsSuccess() {
        val status = ManualConnectController { true }.connect("receiver.local")

        assertEquals(ManualConnectStatus.Success, status)
    }

    @Test
    fun failedConnectorReturnsFailed() {
        val status = ManualConnectController { false }.connect("receiver.local")

        assertEquals(ManualConnectStatus.Failed, status)
    }

    @Test
    fun connectorExceptionReturnsFailed() {
        val status = ManualConnectController { throw IOException("boom") }.connect("receiver.local")

        assertEquals(ManualConnectStatus.Failed, status)
    }
}
