package com.openaudiolink

import android.app.Activity
import android.os.Bundle
import android.view.inputmethod.EditorInfo
import android.widget.Button
import android.widget.EditText
import android.widget.LinearLayout
import android.widget.TextView
import com.openaudiolink.network.TcpHandshakeClient

class MainActivity : Activity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)

        val hostInput = EditText(this).apply {
            hint = "Windows receiver IP or host"
            setSingleLine(true)
            imeOptions = EditorInfo.IME_ACTION_DONE
        }
        val connectButton = Button(this).apply { text = "Connect Fake Stream" }
        val statusText = TextView(this).apply { text = ManualConnectStatus.Idle.name }
        val controller = ManualConnectController { host -> TcpHandshakeClient().connect(host) }

        connectButton.setOnClickListener {
            val host = hostInput.text.toString()
            if (host.trim().isEmpty()) {
                statusText.text = ManualConnectStatus.Failed.name
                return@setOnClickListener
            }

            statusText.text = ManualConnectStatus.Connecting.name
            connectButton.isEnabled = false

            Thread {
                val result = controller.connect(host)
                runOnUiThread {
                    statusText.text = result.name
                    connectButton.isEnabled = true
                }
            }.start()
        }

        setContentView(LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(32, 32, 32, 32)
            addView(TextView(this@MainActivity).apply { text = "OpenAudioLink Sender" })
            addView(hostInput)
            addView(connectButton)
            addView(statusText)
        })
    }
}
