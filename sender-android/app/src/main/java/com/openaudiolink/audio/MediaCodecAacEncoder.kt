package com.openaudiolink.audio

import android.media.AudioFormat
import android.media.MediaCodec
import android.media.MediaCodecInfo
import android.media.MediaFormat
import android.os.SystemClock
import java.io.Closeable
import java.nio.ByteBuffer
import java.util.concurrent.TimeoutException

private val CANONICAL_AUDIO_SPECIFIC_CONFIG =
    byteArrayOf(0x11, 0x90.toByte())

internal fun validateCanonicalAudioSpecificConfig(bytes: ByteArray) {
    if (!bytes.contentEquals(CANONICAL_AUDIO_SPECIFIC_CONFIG)) {
        val actual = bytes.joinToString(" ") { byte ->
            "%02X".format(byte.toInt() and 0xff)
        }
        throw IllegalStateException(
            "Expected AudioSpecificConfig 11 90, got $actual.",
        )
    }
}

class EncodedAccessUnit(
    val bytes: ByteArray,
    val presentationTimeUs: Long,
)

class MediaCodecAacEncoder : Closeable {
    val codecName: String

    private val ownerThreadId = Thread.currentThread().id
    private val codec: MediaCodec
    private var started = false
    private var state = State.Faulted
    private var hasInput = false
    private var lastInputPresentationTimeUs = -1L
    private var inputEosQueued = false
    private var outputEosSeen = false
    private var configValidated = false
    private var submittedFrameCount = 0
    private var completedCandidateCount = 0
    private var partialOutput = ByteArray(0)
    private var partialPresentationTimeUs = 0L

    init {
        var created: MediaCodec? = null
        var createdName = ""
        var didStart = false
        try {
            created = MediaCodec.createEncoderByType(MediaFormat.MIMETYPE_AUDIO_AAC)
            createdName = created.name
            val format = MediaFormat.createAudioFormat(
                MediaFormat.MIMETYPE_AUDIO_AAC,
                SAMPLE_RATE,
                CHANNEL_COUNT,
            )
            format.setInteger(
                MediaFormat.KEY_AAC_PROFILE,
                MediaCodecInfo.CodecProfileLevel.AACObjectLC,
            )
            format.setInteger(MediaFormat.KEY_BIT_RATE, BIT_RATE)
            format.setInteger(
                MediaFormat.KEY_PCM_ENCODING,
                AudioFormat.ENCODING_PCM_16BIT,
            )
            created.configure(
                format,
                null,
                null,
                MediaCodec.CONFIGURE_FLAG_ENCODE,
            )
            created.start()
            didStart = true
            validateInputFormat(created.inputFormat)
        } catch (failure: Exception) {
            cleanupConstruction(created, didStart, failure)
            throw IllegalStateException(
                "MediaCodec AAC encoder construction failed.",
                failure,
            )
        }
        codec = checkNotNull(created)
        codecName = createdName
        started = true
        state = State.Active
    }

    fun submit(
        pcm: ByteArray,
        presentationTimeUs: Long,
    ): List<EncodedAccessUnit> {
        checkOwnerThread()
        requireActive("submit")
        require(pcm.size == PCM_BYTES_PER_FRAME) {
            "PCM frame must contain exactly $PCM_BYTES_PER_FRAME bytes."
        }
        require(presentationTimeUs >= 0) {
            "Input presentation timestamp must be non-negative."
        }
        if (hasInput) {
            require(presentationTimeUs > lastInputPresentationTimeUs) {
                "Input presentation timestamps must be strictly increasing."
            }
        }

        val output = mutableListOf<EncodedAccessUnit>()
        try {
            val deadline = deadlineNanos()
            val inputIndex = acquireInputBuffer(output, deadline)
            val inputBuffer = codec.getInputBuffer(inputIndex)
                ?: throw IllegalStateException("MediaCodec returned no input buffer.")
            inputBuffer.clear()
            if (inputBuffer.remaining() < pcm.size) {
                throw IllegalStateException(
                    "MediaCodec input buffer is smaller than $PCM_BYTES_PER_FRAME bytes.",
                )
            }
            inputBuffer.put(pcm)
            codec.queueInputBuffer(
                inputIndex,
                0,
                pcm.size,
                presentationTimeUs,
                0,
            )
            hasInput = true
            lastInputPresentationTimeUs = presentationTimeUs
            submittedFrameCount++
            collectOutput(output, waitForEos = false, deadlineNanos = deadline)
            return output
        } catch (failure: Exception) {
            fault("submit", failure)
        }
    }

    fun drain(): List<EncodedAccessUnit> {
        checkOwnerThread()
        when (state) {
            State.Closed -> throw IllegalStateException("Encoder is closed.")
            State.Faulted -> throw IllegalStateException("Encoder is faulted.")
            State.Drained -> return emptyList()
            State.Active -> Unit
        }

        val output = mutableListOf<EncodedAccessUnit>()
        try {
            val deadline = deadlineNanos()
            val inputIndex = acquireInputBuffer(output, deadline)
            codec.queueInputBuffer(
                inputIndex,
                0,
                0,
                0,
                MediaCodec.BUFFER_FLAG_END_OF_STREAM,
            )
            inputEosQueued = true
            collectOutput(output, waitForEos = true, deadlineNanos = deadline)
            if (!outputEosSeen) {
                throw IllegalStateException("MediaCodec drain ended without output EOS.")
            }
            if (hasInput && !configValidated) {
                throw IllegalStateException("MediaCodec emitted no canonical AAC config.")
            }
            if (partialOutput.isNotEmpty()) {
                throw IllegalStateException("MediaCodec ended with a partial access unit.")
            }
            val expectedCandidateCount = Math.addExact(
                submittedFrameCount,
                CODEC_ADDED_CANDIDATE_COUNT,
            )
            if (completedCandidateCount != expectedCandidateCount) {
                throw IllegalStateException(
                    "MediaCodec produced $completedCandidateCount output candidates " +
                        "for $submittedFrameCount input frames; expected " +
                        "$expectedCandidateCount.",
                )
            }
            state = State.Drained
            return output
        } catch (failure: Exception) {
            fault("drain", failure)
        }
    }

    override fun close() {
        checkOwnerThread()
        if (state == State.Closed) {
            return
        }

        var failure: Throwable? = null
        if (started) {
            try {
                codec.stop()
            } catch (stopFailure: Throwable) {
                failure = stopFailure
            }
        }
        try {
            codec.release()
        } catch (releaseFailure: Throwable) {
            if (failure == null) {
                failure = releaseFailure
            } else {
                failure.addSuppressed(releaseFailure)
            }
        }
        started = false
        state = State.Closed
        if (failure != null) {
            throw IllegalStateException("MediaCodec close failed.", failure)
        }
    }

    private fun acquireInputBuffer(
        output: MutableList<EncodedAccessUnit>,
        deadlineNanos: Long,
    ): Int {
        while (true) {
            requireBeforeDeadline(deadlineNanos, "input buffer")
            val index = codec.dequeueInputBuffer(DEQUEUE_TIMEOUT_US)
            requireBeforeDeadline(deadlineNanos, "input buffer")
            if (index >= 0) {
                return index
            }
            if (index != MediaCodec.INFO_TRY_AGAIN_LATER) {
                throw IllegalStateException("Unexpected input result $index.")
            }
            collectOutput(output, waitForEos = false, deadlineNanos = deadlineNanos)
            requireBeforeDeadline(deadlineNanos, "input buffer")
        }
    }

    private fun collectOutput(
        output: MutableList<EncodedAccessUnit>,
        waitForEos: Boolean,
        deadlineNanos: Long,
    ) {
        val info = MediaCodec.BufferInfo()
        while (true) {
            requireBeforeDeadline(
                deadlineNanos,
                if (waitForEos) "output EOS" else "available output",
            )
            val timeoutUs = if (waitForEos) DEQUEUE_TIMEOUT_US else 0
            val index = codec.dequeueOutputBuffer(info, timeoutUs)
            requireBeforeDeadline(
                deadlineNanos,
                if (waitForEos) "output EOS" else "available output",
            )
            when {
                index >= 0 -> processOutputBuffer(index, info, output)
                index == MediaCodec.INFO_TRY_AGAIN_LATER -> {
                    if (!waitForEos) {
                        return
                    }
                    requireBeforeDeadline(deadlineNanos, "output EOS")
                }
                index == MediaCodec.INFO_OUTPUT_FORMAT_CHANGED ->
                    validateOutputFormat(codec.outputFormat)
                index == MediaCodec.INFO_OUTPUT_BUFFERS_CHANGED -> Unit
                else -> throw IllegalStateException("Unexpected output result $index.")
            }
            if (outputEosSeen) {
                return
            }
        }
    }

    private fun processOutputBuffer(
        index: Int,
        info: MediaCodec.BufferInfo,
        output: MutableList<EncodedAccessUnit>,
    ) {
        try {
            val flags = info.flags
            val isConfig = flags and MediaCodec.BUFFER_FLAG_CODEC_CONFIG != 0
            val isPartial = flags and MediaCodec.BUFFER_FLAG_PARTIAL_FRAME != 0
            val isEos = flags and MediaCodec.BUFFER_FLAG_END_OF_STREAM != 0
            val bytes = if (info.size == 0) {
                ByteArray(0)
            } else {
                val buffer = codec.getOutputBuffer(index)
                    ?: throw IllegalStateException("MediaCodec returned no output buffer.")
                copyRange(
                    buffer,
                    info.offset,
                    info.size,
                    if (isConfig) CANONICAL_CONFIG_BYTES else MAX_ACCESS_UNIT_BYTES,
                )
            }

            if (isConfig) {
                if (bytes.isNotEmpty()) {
                    validateCanonicalAudioSpecificConfig(bytes)
                    configValidated = true
                }
            } else if (bytes.isNotEmpty()) {
                if (!configValidated) {
                    throw IllegalStateException(
                        "MediaCodec emitted audio before canonical codec config.",
                    )
                }
                appendMediaBytes(bytes, info.presentationTimeUs, isPartial, output)
            }

            if (isEos) {
                if (partialOutput.isNotEmpty()) {
                    throw IllegalStateException(
                        "MediaCodec signaled EOS with an incomplete access unit.",
                    )
                }
                if (!inputEosQueued) {
                    throw IllegalStateException("MediaCodec emitted EOS before input EOS.")
                }
                outputEosSeen = true
            }
        } finally {
            codec.releaseOutputBuffer(index, false)
        }
    }

    private fun appendMediaBytes(
        bytes: ByteArray,
        presentationTimeUs: Long,
        isPartial: Boolean,
        output: MutableList<EncodedAccessUnit>,
    ) {
        if (partialOutput.isEmpty() && !isPartial) {
            if (bytes.size > MAX_ACCESS_UNIT_BYTES) {
                throw IllegalStateException("AAC access unit exceeds size limit.")
            }
            output += EncodedAccessUnit(bytes, presentationTimeUs)
            completedCandidateCount++
            return
        }

        if (partialOutput.isEmpty()) {
            partialPresentationTimeUs = presentationTimeUs
        }
        if (bytes.size > MAX_ACCESS_UNIT_BYTES - partialOutput.size) {
            throw IllegalStateException("AAC access unit exceeds size limit.")
        }
        partialOutput += bytes
        if (!isPartial) {
            output += EncodedAccessUnit(
                partialOutput,
                partialPresentationTimeUs,
            )
            completedCandidateCount++
            partialOutput = ByteArray(0)
        }
    }

    private fun validateInputFormat(format: MediaFormat) {
        if (format.getInteger(MediaFormat.KEY_SAMPLE_RATE) != SAMPLE_RATE) {
            throw IllegalStateException("MediaCodec input sample rate is not 48000 Hz.")
        }
        if (format.getInteger(MediaFormat.KEY_CHANNEL_COUNT) != CHANNEL_COUNT) {
            throw IllegalStateException("MediaCodec input is not stereo.")
        }
        if (
            format.getInteger(
                MediaFormat.KEY_PCM_ENCODING,
                AudioFormat.ENCODING_PCM_16BIT,
            ) != AudioFormat.ENCODING_PCM_16BIT
        ) {
            throw IllegalStateException("MediaCodec input is not PCM16.")
        }
    }

    private fun validateOutputFormat(format: MediaFormat) {
        if (format.getString(MediaFormat.KEY_MIME) != MediaFormat.MIMETYPE_AUDIO_AAC) {
            throw IllegalStateException("MediaCodec output MIME is not AAC.")
        }
        if (format.getInteger(MediaFormat.KEY_SAMPLE_RATE) != SAMPLE_RATE) {
            throw IllegalStateException("MediaCodec output sample rate is not 48000 Hz.")
        }
        if (format.getInteger(MediaFormat.KEY_CHANNEL_COUNT) != CHANNEL_COUNT) {
            throw IllegalStateException("MediaCodec output is not stereo.")
        }
        format.getByteBuffer("csd-0")?.let { config ->
            validateCanonicalAudioSpecificConfig(
                copyRemaining(config, CANONICAL_CONFIG_BYTES),
            )
            configValidated = true
        }
    }

    private fun requireActive(operation: String) {
        when (state) {
            State.Active -> Unit
            State.Drained -> throw IllegalStateException("Cannot $operation after drain.")
            State.Faulted -> throw IllegalStateException("Encoder is faulted.")
            State.Closed -> throw IllegalStateException("Encoder is closed.")
        }
    }

    private fun checkOwnerThread() {
        if (Thread.currentThread().id != ownerThreadId) {
            throw IllegalStateException("MediaCodec encoder is owner-thread-only.")
        }
    }

    private fun requireBeforeDeadline(deadlineNanos: Long, operation: String) {
        if (SystemClock.elapsedRealtimeNanos() >= deadlineNanos) {
            throw TimeoutException("Timed out waiting for MediaCodec $operation.")
        }
    }

    private fun deadlineNanos(): Long =
        SystemClock.elapsedRealtimeNanos() + OPERATION_TIMEOUT_NANOS

    private fun fault(operation: String, failure: Exception): Nothing {
        state = State.Faulted
        throw IllegalStateException("MediaCodec $operation failed.", failure)
    }

    private enum class State {
        Active,
        Drained,
        Faulted,
        Closed,
    }

    private companion object {
        const val SAMPLE_RATE = 48_000
        const val CHANNEL_COUNT = 2
        const val BIT_RATE = 192_000
        const val PCM_BYTES_PER_FRAME = 4096
        const val CODEC_ADDED_CANDIDATE_COUNT = 1
        const val MAX_ACCESS_UNIT_BYTES = 65_536
        const val CANONICAL_CONFIG_BYTES = 2
        const val DEQUEUE_TIMEOUT_US = 10_000L
        const val OPERATION_TIMEOUT_NANOS = 5_000_000_000L

        fun copyRange(
            buffer: ByteBuffer,
            offset: Int,
            size: Int,
            maximumSize: Int,
        ): ByteArray {
            val copy = buffer.duplicate()
            if (offset < 0 || size < 0 || offset > copy.limit() - size) {
                throw IllegalStateException("Invalid MediaCodec output buffer bounds.")
            }
            if (size > maximumSize) {
                throw IllegalStateException("MediaCodec output exceeds size limit.")
            }
            copy.position(offset)
            copy.limit(offset + size)
            return ByteArray(size).also { copy.get(it) }
        }

        fun copyRemaining(buffer: ByteBuffer, maximumSize: Int): ByteArray {
            val copy = buffer.duplicate()
            if (copy.remaining() > maximumSize) {
                throw IllegalStateException("MediaCodec output exceeds size limit.")
            }
            return ByteArray(copy.remaining()).also { copy.get(it) }
        }

        fun cleanupConstruction(
            codec: MediaCodec?,
            started: Boolean,
            failure: Throwable,
        ) {
            if (codec == null) {
                return
            }
            if (started) {
                try {
                    codec.stop()
                } catch (stopFailure: Throwable) {
                    failure.addSuppressed(stopFailure)
                }
            }
            try {
                codec.release()
            } catch (releaseFailure: Throwable) {
                failure.addSuppressed(releaseFailure)
            }
        }
    }
}
