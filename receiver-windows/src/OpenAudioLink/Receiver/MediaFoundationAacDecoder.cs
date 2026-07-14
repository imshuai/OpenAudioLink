using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace OpenAudioLink.Receiver
{
    public sealed class MediaFoundationAacDecoder : IDisposable
    {
        private const uint InputStreamId = 0;
        private const uint OutputStreamId = 0;
        private const uint PcmBlockAlignment = 4;

        private readonly int ownerThreadId;
        private IMFTransform transform;
        private bool comInitialized;
        private bool mediaFoundationStarted;
        private DecoderState state;
        private long nextInputIndex;
        private uint outputStreamFlags;
        private uint outputBufferSize;
        private uint outputBufferAlignment;

        private enum DecoderState
        {
            Active,
            Drained,
            Faulted,
            Disposed
        }

        public MediaFoundationAacDecoder()
        {
            ownerThreadId = Environment.CurrentManagedThreadId;
            try
            {
                Initialize();
                state = DecoderState.Active;
            }
            catch (DllNotFoundException ex)
            {
                Cleanup();
                throw PlatformUnavailable("Media Foundation", ex);
            }
            catch (EntryPointNotFoundException ex)
            {
                Cleanup();
                throw PlatformUnavailable("Media Foundation", ex);
            }
            catch
            {
                Cleanup();
                throw;
            }
        }

        public IReadOnlyList<byte[]> Submit(byte[] rawAccessUnit)
        {
            CheckOwnerThread();
            CheckNotDisposed();
            if (state == DecoderState.Faulted)
            {
                throw new InvalidOperationException("The decoder is faulted.");
            }
            if (state == DecoderState.Drained)
            {
                throw new InvalidOperationException("The decoder has been drained.");
            }
            if (rawAccessUnit == null)
            {
                throw new ArgumentNullException(nameof(rawAccessUnit));
            }
            if (rawAccessUnit.Length == 0)
            {
                throw new ArgumentException("The AAC access unit must not be empty.", nameof(rawAccessUnit));
            }

            try
            {
                return SubmitCore(rawAccessUnit);
            }
            catch (DllNotFoundException ex)
            {
                state = DecoderState.Faulted;
                throw PlatformUnavailable("Media Foundation", ex);
            }
            catch (EntryPointNotFoundException ex)
            {
                state = DecoderState.Faulted;
                throw PlatformUnavailable("Media Foundation", ex);
            }
            catch
            {
                state = DecoderState.Faulted;
                throw;
            }
        }

        public IReadOnlyList<byte[]> Drain()
        {
            CheckOwnerThread();
            CheckNotDisposed();
            if (state == DecoderState.Faulted)
            {
                throw new InvalidOperationException("The decoder is faulted.");
            }
            if (state == DecoderState.Drained)
            {
                return Array.Empty<byte[]>();
            }

            try
            {
                List<byte[]> chunks = new List<byte[]>();
                CheckHr(
                    "IMFTransform.ProcessMessage(MFT_MESSAGE_NOTIFY_END_OF_STREAM)",
                    transform.ProcessMessage(
                        MediaFoundationInterop.MftMessageNotifyEndOfStream,
                        UIntPtr.Zero));
                CheckHr(
                    "IMFTransform.ProcessMessage(MFT_MESSAGE_COMMAND_DRAIN)",
                    transform.ProcessMessage(
                        MediaFoundationInterop.MftMessageCommandDrain,
                        UIntPtr.Zero));
                CollectOutput(chunks);
                CheckHr(
                    "IMFTransform.ProcessMessage(MFT_MESSAGE_NOTIFY_END_STREAMING)",
                    transform.ProcessMessage(
                        MediaFoundationInterop.MftMessageNotifyEndStreaming,
                        UIntPtr.Zero));
                state = DecoderState.Drained;
                return chunks;
            }
            catch (DllNotFoundException ex)
            {
                state = DecoderState.Faulted;
                throw PlatformUnavailable("Media Foundation", ex);
            }
            catch (EntryPointNotFoundException ex)
            {
                state = DecoderState.Faulted;
                throw PlatformUnavailable("Media Foundation", ex);
            }
            catch
            {
                state = DecoderState.Faulted;
                throw;
            }
        }

        public void Dispose()
        {
            CheckOwnerThread();
            if (state == DecoderState.Disposed)
            {
                return;
            }

            state = DecoderState.Disposed;
            Exception cleanupError = Cleanup();
            if (cleanupError != null)
            {
                throw cleanupError;
            }
        }

        private void Initialize()
        {
            int hr = MediaFoundationInterop.CoInitializeEx(
                IntPtr.Zero,
                MediaFoundationInterop.CoinitMultithreaded);
            CheckHr("CoInitializeEx", hr);
            comInitialized = true;

            CheckHr(
                "MFStartup",
                MediaFoundationInterop.MFStartup(
                    MediaFoundationInterop.MfVersion,
                    MediaFoundationInterop.MfStartupFull));
            mediaFoundationStarted = true;

            Guid classId = MediaFoundationInterop.ClsidCmsAacDecMft;
            Guid interfaceId = MediaFoundationInterop.IidImfTransform;
            hr = MediaFoundationInterop.CoCreateInstance(
                ref classId,
                IntPtr.Zero,
                MediaFoundationInterop.ClsctxInprocServer,
                ref interfaceId,
                out transform);
            if (hr == MediaFoundationInterop.RegdbEClassNotReg)
            {
                throw new PlatformNotSupportedException(
                    "The Microsoft AAC Decoder MFT is not registered.",
                    Marshal.GetExceptionForHR(hr));
            }
            CheckHr("CoCreateInstance(CLSID_CMSAACDecMFT)", hr);
            if (transform == null)
            {
                throw new InvalidOperationException("CoCreateInstance returned no IMFTransform.");
            }

            RequireDefaultStreams();
            RequireSynchronousTransform();
            ConfigureInputType();
            ConfigureOutputType();
            ReadOutputStreamInfo();

            CheckHr(
                "IMFTransform.ProcessMessage(MFT_MESSAGE_NOTIFY_BEGIN_STREAMING)",
                transform.ProcessMessage(
                    MediaFoundationInterop.MftMessageNotifyBeginStreaming,
                    UIntPtr.Zero));
            CheckHr(
                "IMFTransform.ProcessMessage(MFT_MESSAGE_NOTIFY_START_OF_STREAM)",
                transform.ProcessMessage(
                    MediaFoundationInterop.MftMessageNotifyStartOfStream,
                    UIntPtr.Zero));
        }

        private void RequireDefaultStreams()
        {
            uint inputCount;
            uint outputCount;
            CheckHr(
                "IMFTransform.GetStreamCount",
                transform.GetStreamCount(out inputCount, out outputCount));
            if (inputCount != 1 || outputCount != 1)
            {
                throw new InvalidOperationException(
                    "The Microsoft AAC Decoder MFT must expose exactly one input and one output stream.");
            }

            uint[] inputIds = new uint[1];
            uint[] outputIds = new uint[1];
            int hr = transform.GetStreamIDs(1, inputIds, 1, outputIds);
            if (hr == MediaFoundationInterop.ENotImpl)
            {
                return;
            }
            CheckHr("IMFTransform.GetStreamIDs", hr);
            if (inputIds[0] != InputStreamId || outputIds[0] != OutputStreamId)
            {
                throw new InvalidOperationException(
                    "The Microsoft AAC Decoder MFT must use stream IDs 0 and 0.");
            }
        }

        private void RequireSynchronousTransform()
        {
            IMFAttributes attributes = null;
            try
            {
                int hr = transform.GetAttributes(out attributes);
                if (hr == MediaFoundationInterop.ENotImpl)
                {
                    return;
                }
                CheckHr("IMFTransform.GetAttributes", hr);
                if (attributes == null)
                {
                    throw new InvalidOperationException(
                        "IMFTransform.GetAttributes returned no attributes.");
                }

                Guid key = MediaFoundationInterop.MfTransformAsync;
                uint async;
                hr = attributes.GetUINT32(ref key, out async);
                if (hr == MediaFoundationInterop.MfEAttributeNotFound)
                {
                    return;
                }
                CheckHr("IMFAttributes.GetUINT32(MF_TRANSFORM_ASYNC)", hr);
                if (async != 0)
                {
                    throw new PlatformNotSupportedException(
                        "Asynchronous Media Foundation transforms are not supported.");
                }
            }
            finally
            {
                ReleaseComObject(attributes);
            }
        }

        private void ConfigureInputType()
        {
            IMFMediaType mediaType = null;
            try
            {
                CheckHr(
                    "MFCreateMediaType(input)",
                    MediaFoundationInterop.MFCreateMediaType(out mediaType));
                if (mediaType == null)
                {
                    throw new InvalidOperationException(
                        "MFCreateMediaType(input) returned no media type.");
                }

                SetGuid(mediaType, "MF_MT_MAJOR_TYPE", MediaFoundationInterop.MfMtMajorType,
                    MediaFoundationInterop.MfMediaTypeAudio);
                SetGuid(mediaType, "MF_MT_SUBTYPE", MediaFoundationInterop.MfMtSubtype,
                    MediaFoundationInterop.MfAudioFormatAac);
                SetUInt32(mediaType, "MF_MT_AUDIO_SAMPLES_PER_SECOND",
                    MediaFoundationInterop.MfMtAudioSamplesPerSecond, 48000);
                SetUInt32(mediaType, "MF_MT_AUDIO_NUM_CHANNELS",
                    MediaFoundationInterop.MfMtAudioNumChannels, 2);
                SetUInt32(mediaType, "MF_MT_AAC_PAYLOAD_TYPE",
                    MediaFoundationInterop.MfMtAacPayloadType, 0);
                SetUInt32(mediaType, "MF_MT_AAC_AUDIO_PROFILE_LEVEL_INDICATION",
                    MediaFoundationInterop.MfMtAacAudioProfileLevelIndication, 0xFE);

                byte[] userData =
                {
                    0x00, 0x00, 0xFE, 0x00, 0x00, 0x00, 0x00,
                    0x00, 0x00, 0x00, 0x00, 0x00, 0x11, 0x90
                };
                Guid userDataKey = MediaFoundationInterop.MfMtUserData;
                CheckHr(
                    "IMFMediaType.SetBlob(MF_MT_USER_DATA)",
                    mediaType.SetBlob(ref userDataKey, userData, (uint)userData.Length));

                CheckHr(
                    "IMFTransform.SetInputType",
                    transform.SetInputType(InputStreamId, mediaType, 0));
            }
            finally
            {
                ReleaseComObject(mediaType);
            }
        }

        private void ConfigureOutputType()
        {
            IMFMediaType mediaType = null;
            try
            {
                CheckHr(
                    "MFCreateMediaType(output)",
                    MediaFoundationInterop.MFCreateMediaType(out mediaType));
                if (mediaType == null)
                {
                    throw new InvalidOperationException(
                        "MFCreateMediaType(output) returned no media type.");
                }

                SetGuid(mediaType, "MF_MT_MAJOR_TYPE", MediaFoundationInterop.MfMtMajorType,
                    MediaFoundationInterop.MfMediaTypeAudio);
                SetGuid(mediaType, "MF_MT_SUBTYPE", MediaFoundationInterop.MfMtSubtype,
                    MediaFoundationInterop.MfAudioFormatPcm);
                SetUInt32(mediaType, "MF_MT_AUDIO_SAMPLES_PER_SECOND",
                    MediaFoundationInterop.MfMtAudioSamplesPerSecond, 48000);
                SetUInt32(mediaType, "MF_MT_AUDIO_NUM_CHANNELS",
                    MediaFoundationInterop.MfMtAudioNumChannels, 2);
                SetUInt32(mediaType, "MF_MT_AUDIO_BITS_PER_SAMPLE",
                    MediaFoundationInterop.MfMtAudioBitsPerSample, 16);
                SetUInt32(mediaType, "MF_MT_AUDIO_BLOCK_ALIGNMENT",
                    MediaFoundationInterop.MfMtAudioBlockAlignment, PcmBlockAlignment);
                SetUInt32(mediaType, "MF_MT_AUDIO_AVG_BYTES_PER_SECOND",
                    MediaFoundationInterop.MfMtAudioAvgBytesPerSecond, 192000);

                CheckHr(
                    "IMFTransform.SetOutputType",
                    transform.SetOutputType(OutputStreamId, mediaType, 0));
            }
            finally
            {
                ReleaseComObject(mediaType);
            }

            IMFMediaType currentType = null;
            try
            {
                CheckHr(
                    "IMFTransform.GetOutputCurrentType",
                    transform.GetOutputCurrentType(OutputStreamId, out currentType));
                if (currentType == null)
                {
                    throw new InvalidOperationException(
                        "IMFTransform.GetOutputCurrentType returned no media type.");
                }

                RequireGuid(currentType, "MF_MT_MAJOR_TYPE", MediaFoundationInterop.MfMtMajorType,
                    MediaFoundationInterop.MfMediaTypeAudio);
                RequireGuid(currentType, "MF_MT_SUBTYPE", MediaFoundationInterop.MfMtSubtype,
                    MediaFoundationInterop.MfAudioFormatPcm);
                RequireUInt32(currentType, "MF_MT_AUDIO_SAMPLES_PER_SECOND",
                    MediaFoundationInterop.MfMtAudioSamplesPerSecond, 48000);
                RequireUInt32(currentType, "MF_MT_AUDIO_NUM_CHANNELS",
                    MediaFoundationInterop.MfMtAudioNumChannels, 2);
                RequireUInt32(currentType, "MF_MT_AUDIO_BITS_PER_SAMPLE",
                    MediaFoundationInterop.MfMtAudioBitsPerSample, 16);
                RequireUInt32(currentType, "MF_MT_AUDIO_BLOCK_ALIGNMENT",
                    MediaFoundationInterop.MfMtAudioBlockAlignment, PcmBlockAlignment);
                RequireUInt32(currentType, "MF_MT_AUDIO_AVG_BYTES_PER_SECOND",
                    MediaFoundationInterop.MfMtAudioAvgBytesPerSecond, 192000);
            }
            finally
            {
                ReleaseComObject(currentType);
            }
        }

        private void ReadOutputStreamInfo()
        {
            MftOutputStreamInfo info;
            CheckHr(
                "IMFTransform.GetOutputStreamInfo",
                transform.GetOutputStreamInfo(OutputStreamId, out info));
            outputStreamFlags = info.Flags;
            outputBufferSize = info.Size;
            outputBufferAlignment = info.Alignment;

            if ((outputStreamFlags & MediaFoundationInterop.MftOutputStreamProvidesSamples) != 0)
            {
                return;
            }
            if (outputBufferSize == 0)
            {
                throw new InvalidOperationException(
                    "IMFTransform.GetOutputStreamInfo returned a zero caller buffer size.");
            }
            if (outputBufferAlignment != 0 &&
                (outputBufferAlignment & (outputBufferAlignment - 1)) != 0)
            {
                throw new InvalidOperationException(
                    "IMFTransform.GetOutputStreamInfo returned a non-power-of-two alignment.");
            }
        }

        private IReadOnlyList<byte[]> SubmitCore(byte[] rawAccessUnit)
        {
            List<byte[]> chunks = new List<byte[]>();
            IMFSample sample = CreateInputSample(rawAccessUnit, nextInputIndex);
            try
            {
                bool rejectedWithoutProgress = false;
                while (true)
                {
                    int hr = transform.ProcessInput(InputStreamId, sample, 0);
                    if (hr == 0)
                    {
                        nextInputIndex = checked(nextInputIndex + 1);
                        CollectOutput(chunks);
                        return chunks;
                    }
                    if (hr == MediaFoundationInterop.MfENotAccepting)
                    {
                        bool producedOutput = CollectOutput(chunks);
                        if (!producedOutput && rejectedWithoutProgress)
                        {
                            throw new InvalidOperationException(
                                "IMFTransform.ProcessInput made no progress after MF_E_NOTACCEPTING.");
                        }
                        rejectedWithoutProgress = !producedOutput;
                        continue;
                    }
                    CheckHr("IMFTransform.ProcessInput", hr);
                    throw UnexpectedSuccess("IMFTransform.ProcessInput", hr);
                }
            }
            finally
            {
                ReleaseComObject(sample);
            }
        }

        private IMFSample CreateInputSample(byte[] rawAccessUnit, long frameIndex)
        {
            IMFSample sample = null;
            try
            {
                CheckHr("MFCreateSample(input)", MediaFoundationInterop.MFCreateSample(out sample));
                if (sample == null)
                {
                    throw new InvalidOperationException("MFCreateSample(input) returned no sample.");
                }

                IMFMediaBuffer buffer = null;
                try
                {
                    CheckHr(
                        "MFCreateMemoryBuffer(input)",
                        MediaFoundationInterop.MFCreateMemoryBuffer(
                            (uint)rawAccessUnit.Length,
                            out buffer));
                    if (buffer == null)
                    {
                        throw new InvalidOperationException(
                            "MFCreateMemoryBuffer(input) returned no buffer.");
                    }

                    IntPtr destination;
                    uint maxLength;
                    uint currentLength;
                    CheckHr(
                        "IMFMediaBuffer.Lock(input)",
                        buffer.Lock(out destination, out maxLength, out currentLength));
                    try
                    {
                        if (destination == IntPtr.Zero || maxLength < rawAccessUnit.Length)
                        {
                            throw new InvalidOperationException(
                                "IMFMediaBuffer.Lock(input) returned an invalid buffer.");
                        }
                        Marshal.Copy(rawAccessUnit, 0, destination, rawAccessUnit.Length);
                    }
                    finally
                    {
                        CheckHr("IMFMediaBuffer.Unlock(input)", buffer.Unlock());
                    }

                    CheckHr(
                        "IMFMediaBuffer.SetCurrentLength(input)",
                        buffer.SetCurrentLength((uint)rawAccessUnit.Length));
                    CheckHr("IMFSample.AddBuffer(input)", sample.AddBuffer(buffer));
                }
                finally
                {
                    ReleaseComObject(buffer);
                }

                long sampleTime = SampleTime(frameIndex);
                long sampleDuration = SampleTime(checked(frameIndex + 1)) - sampleTime;
                CheckHr("IMFSample.SetSampleTime", sample.SetSampleTime(sampleTime));
                CheckHr("IMFSample.SetSampleDuration", sample.SetSampleDuration(sampleDuration));
                return sample;
            }
            catch
            {
                ReleaseComObject(sample);
                throw;
            }
        }

        private bool CollectOutput(List<byte[]> chunks)
        {
            bool producedOutput = false;
            while (true)
            {
                IMFSample callerSample = null;
                IMFSample returnedSample = null;
                IntPtr events = IntPtr.Zero;
                try
                {
                    if ((outputStreamFlags & MediaFoundationInterop.MftOutputStreamProvidesSamples) == 0)
                    {
                        callerSample = CreateOutputSample();
                    }

                    MftOutputDataBuffer[] output =
                    {
                        new MftOutputDataBuffer
                        {
                            StreamId = OutputStreamId,
                            Sample = callerSample,
                            Status = 0,
                            Events = IntPtr.Zero
                        }
                    };
                    uint status;
                    int hr = transform.ProcessOutput(0, 1, output, out status);
                    returnedSample = output[0].Sample;
                    events = output[0].Events;

                    if (hr == MediaFoundationInterop.MfETransformNeedMoreInput)
                    {
                        return producedOutput;
                    }
                    if (hr == MediaFoundationInterop.MfETransformStreamChange)
                    {
                        CheckHr("IMFTransform.ProcessOutput", hr);
                    }
                    if (hr != 0)
                    {
                        CheckHr("IMFTransform.ProcessOutput", hr);
                        throw UnexpectedSuccess("IMFTransform.ProcessOutput", hr);
                    }
                    if (returnedSample == null)
                    {
                        throw new InvalidOperationException(
                            "IMFTransform.ProcessOutput succeeded without an output sample.");
                    }

                    chunks.Add(CopyPcm(returnedSample));
                    producedOutput = true;
                }
                finally
                {
                    try
                    {
                        if (events != IntPtr.Zero)
                        {
                            Marshal.Release(events);
                        }
                    }
                    finally
                    {
                        try
                        {
                            if (returnedSample != null &&
                                !ReferenceEquals(returnedSample, callerSample))
                            {
                                ReleaseComObject(returnedSample);
                            }
                        }
                        finally
                        {
                            ReleaseComObject(callerSample);
                        }
                    }
                }
            }
        }

        private IMFSample CreateOutputSample()
        {
            IMFSample sample = null;
            try
            {
                CheckHr("MFCreateSample(output)", MediaFoundationInterop.MFCreateSample(out sample));
                if (sample == null)
                {
                    throw new InvalidOperationException("MFCreateSample(output) returned no sample.");
                }

                IMFMediaBuffer buffer = null;
                try
                {
                    if (outputBufferAlignment == 0)
                    {
                        CheckHr(
                            "MFCreateMemoryBuffer(output)",
                            MediaFoundationInterop.MFCreateMemoryBuffer(
                                outputBufferSize,
                                out buffer));
                    }
                    else
                    {
                        CheckHr(
                            "MFCreateAlignedMemoryBuffer(output)",
                            MediaFoundationInterop.MFCreateAlignedMemoryBuffer(
                                outputBufferSize,
                                outputBufferAlignment - 1,
                                out buffer));
                    }
                    if (buffer == null)
                    {
                        throw new InvalidOperationException(
                            "Media Foundation returned no output buffer.");
                    }
                    CheckHr("IMFSample.AddBuffer(output)", sample.AddBuffer(buffer));
                }
                finally
                {
                    ReleaseComObject(buffer);
                }

                return sample;
            }
            catch
            {
                ReleaseComObject(sample);
                throw;
            }
        }

        private static byte[] CopyPcm(IMFSample sample)
        {
            IMFMediaBuffer buffer = null;
            try
            {
                CheckHr(
                    "IMFSample.ConvertToContiguousBuffer",
                    sample.ConvertToContiguousBuffer(out buffer));
                if (buffer == null)
                {
                    throw new InvalidOperationException(
                        "IMFSample.ConvertToContiguousBuffer returned no buffer.");
                }

                IntPtr source;
                uint maxLength;
                uint currentLength;
                CheckHr(
                    "IMFMediaBuffer.Lock(output)",
                    buffer.Lock(out source, out maxLength, out currentLength));
                try
                {
                    if (currentLength == 0 || currentLength % PcmBlockAlignment != 0)
                    {
                        throw new InvalidOperationException(
                            "Media Foundation returned invalid PCM output length.");
                    }
                    if (source == IntPtr.Zero || currentLength > maxLength)
                    {
                        throw new InvalidOperationException(
                            "IMFMediaBuffer.Lock(output) returned an invalid buffer.");
                    }

                    int length = checked((int)currentLength);
                    byte[] pcm = new byte[length];
                    Marshal.Copy(source, pcm, 0, length);
                    return pcm;
                }
                finally
                {
                    CheckHr("IMFMediaBuffer.Unlock(output)", buffer.Unlock());
                }
            }
            finally
            {
                ReleaseComObject(buffer);
            }
        }

        private static long SampleTime(long frameIndex)
        {
            checked
            {
                return ((frameIndex * 640000L) + 1L) / 3L;
            }
        }

        private static void SetGuid(IMFMediaType mediaType, string name, Guid key, Guid value)
        {
            CheckHr("IMFMediaType.SetGUID(" + name + ")", mediaType.SetGUID(ref key, ref value));
        }

        private static void SetUInt32(IMFMediaType mediaType, string name, Guid key, uint value)
        {
            CheckHr("IMFMediaType.SetUINT32(" + name + ")", mediaType.SetUINT32(ref key, value));
        }

        private static void RequireGuid(
            IMFMediaType mediaType,
            string name,
            Guid key,
            Guid expected)
        {
            Guid actual;
            CheckHr("IMFMediaType.GetGUID(" + name + ")", mediaType.GetGUID(ref key, out actual));
            if (actual != expected)
            {
                throw new InvalidOperationException(name + " did not match the requested PCM output type.");
            }
        }

        private static void RequireUInt32(
            IMFMediaType mediaType,
            string name,
            Guid key,
            uint expected)
        {
            uint actual;
            CheckHr(
                "IMFMediaType.GetUINT32(" + name + ")",
                mediaType.GetUINT32(ref key, out actual));
            if (actual != expected)
            {
                throw new InvalidOperationException(name + " did not match the requested PCM output type.");
            }
        }

        private void CheckOwnerThread()
        {
            if (Environment.CurrentManagedThreadId != ownerThreadId)
            {
                throw new InvalidOperationException(
                    "MediaFoundationAacDecoder must be used on its owner thread.");
            }
        }

        private void CheckNotDisposed()
        {
            if (state == DecoderState.Disposed)
            {
                throw new ObjectDisposedException(nameof(MediaFoundationAacDecoder));
            }
        }

        private Exception Cleanup()
        {
            Exception firstError = null;

            IMFTransform ownedTransform = transform;
            transform = null;
            if (ownedTransform != null)
            {
                try
                {
                    ReleaseComObject(ownedTransform);
                }
                catch (Exception ex)
                {
                    firstError = ex;
                }
            }

            if (mediaFoundationStarted)
            {
                mediaFoundationStarted = false;
                try
                {
                    CheckHr("MFShutdown", MediaFoundationInterop.MFShutdown());
                }
                catch (DllNotFoundException ex)
                {
                    firstError = firstError ?? PlatformUnavailable("MFShutdown", ex);
                }
                catch (EntryPointNotFoundException ex)
                {
                    firstError = firstError ?? PlatformUnavailable("MFShutdown", ex);
                }
                catch (Exception ex)
                {
                    firstError = firstError ?? ex;
                }
            }

            if (comInitialized)
            {
                comInitialized = false;
                try
                {
                    MediaFoundationInterop.CoUninitialize();
                }
                catch (DllNotFoundException ex)
                {
                    firstError = firstError ?? PlatformUnavailable("CoUninitialize", ex);
                }
                catch (EntryPointNotFoundException ex)
                {
                    firstError = firstError ?? PlatformUnavailable("CoUninitialize", ex);
                }
                catch (Exception ex)
                {
                    firstError = firstError ?? ex;
                }
            }

            return firstError;
        }

        private static void ReleaseComObject(object value)
        {
            if (value != null)
            {
                Marshal.ReleaseComObject(value);
            }
        }

        private static void CheckHr(string operation, int hr)
        {
            if (hr < 0)
            {
                throw new InvalidOperationException(
                    operation + " failed with HRESULT 0x" + ((uint)hr).ToString("X8") + ".",
                    Marshal.GetExceptionForHR(hr));
            }
        }

        private static InvalidOperationException UnexpectedSuccess(string operation, int hr)
        {
            return new InvalidOperationException(
                operation + " returned unexpected HRESULT 0x" + ((uint)hr).ToString("X8") + ".");
        }

        private static PlatformNotSupportedException PlatformUnavailable(
            string operation,
            Exception innerException)
        {
            return new PlatformNotSupportedException(
                operation + " is unavailable on this platform.",
                innerException);
        }
    }
}
