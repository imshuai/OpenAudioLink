package com.openaudiolink.audio

import android.os.Build
import androidx.test.ext.junit.runners.AndroidJUnit4
import org.junit.Assert.assertTrue
import org.junit.Test
import org.junit.runner.RunWith

@RunWith(AndroidJUnit4::class)
class MediaCodecAacEncoderTest {
    @Test
    fun instrumentationRunsOnSupportedAndroid() {
        assertTrue(Build.VERSION.SDK_INT >= 29)
    }
}
