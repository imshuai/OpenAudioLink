package com.openaudiolink

enum class ManualConnectStatus {
    Idle,
    Connecting,
    Success,
    Failed,
}

class ManualConnectController(
    private val connector: (String) -> Boolean,
) {
    fun connect(host: String): ManualConnectStatus {
        val trimmed = host.trim()
        if (trimmed.isEmpty()) return ManualConnectStatus.Failed

        return try {
            if (connector(trimmed)) ManualConnectStatus.Success else ManualConnectStatus.Failed
        } catch (_: Exception) {
            ManualConnectStatus.Failed
        }
    }
}
