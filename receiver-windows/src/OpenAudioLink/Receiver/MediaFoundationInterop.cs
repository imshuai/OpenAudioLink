using System;
using System.Runtime.InteropServices;

namespace OpenAudioLink.Receiver
{
    internal static class MediaFoundationInterop
    {
        internal const uint CoinitMultithreaded = 0;
        internal const uint ClsctxInprocServer = 1;
        internal const uint MfVersion = 0x00020070;
        internal const uint MfStartupFull = 0;
        internal const int ENotImpl = unchecked((int)0x80004001);
        internal const int RegdbEClassNotReg = unchecked((int)0x80040154);
        internal const int MfEAttributeNotFound = unchecked((int)0xC00D36E6);
        internal const int MfENotAccepting = unchecked((int)0xC00D36B5);
        internal const int MfETransformStreamChange = unchecked((int)0xC00D6D61);
        internal const int MfETransformNeedMoreInput = unchecked((int)0xC00D6D72);
        internal const uint MftOutputStreamProvidesSamples = 0x100;
        internal const uint MftOutputStreamCanProvideSamples = 0x200;
        internal const uint MftMessageCommandDrain = 0x00000001;
        internal const uint MftMessageNotifyBeginStreaming = 0x10000000;
        internal const uint MftMessageNotifyEndStreaming = 0x10000001;
        internal const uint MftMessageNotifyEndOfStream = 0x10000002;
        internal const uint MftMessageNotifyStartOfStream = 0x10000003;

        internal static readonly Guid ClsidCmsAacDecMft =
            new Guid("32D186A7-218F-4C75-8876-DD77273A8999");
        internal static readonly Guid IidImfTransform =
            new Guid("BF94C121-5B05-4E6F-8000-BA598961414D");
        internal static readonly Guid MfTransformAsync =
            new Guid("F81A699A-649A-497D-8C73-29F8FED6AD7A");
        internal static readonly Guid MfMediaTypeAudio =
            new Guid("73647561-0000-0010-8000-00AA00389B71");
        internal static readonly Guid MfAudioFormatAac =
            new Guid("00001610-0000-0010-8000-00AA00389B71");
        internal static readonly Guid MfAudioFormatPcm =
            new Guid("00000001-0000-0010-8000-00AA00389B71");
        internal static readonly Guid MfMtMajorType =
            new Guid("48EBA18E-F8C9-4687-BF11-0A74C9F96A8F");
        internal static readonly Guid MfMtSubtype =
            new Guid("F7E34C9A-42E8-4714-B74B-CB29D72C35E5");
        internal static readonly Guid MfMtAudioNumChannels =
            new Guid("37E48BF5-645E-4C5B-89DE-ADA9E29B696A");
        internal static readonly Guid MfMtAudioSamplesPerSecond =
            new Guid("5FAEEAE7-0290-4C31-9E8A-C534F68D9DBA");
        internal static readonly Guid MfMtAudioAvgBytesPerSecond =
            new Guid("1AAB75C8-CFEF-451C-AB95-AC034B8E1731");
        internal static readonly Guid MfMtAudioBlockAlignment =
            new Guid("322DE230-9EEB-43BD-AB7A-FF412251541D");
        internal static readonly Guid MfMtAudioBitsPerSample =
            new Guid("F2DEB57F-40FA-4764-AA33-ED4F2D1FF669");
        internal static readonly Guid MfMtAacPayloadType =
            new Guid("BFBABE79-7434-4D1C-94F0-72A3B9E17188");
        internal static readonly Guid MfMtAacAudioProfileLevelIndication =
            new Guid("7632F0E6-9538-4D61-ACDA-EA29C8C14456");
        internal static readonly Guid MfMtUserData =
            new Guid("B6BC765F-4C3B-40A4-BD51-2535B66FE09D");

        [DllImport("ole32.dll", ExactSpelling = true)]
        [PreserveSig]
        internal static extern int CoInitializeEx(IntPtr reserved, uint coInit);

        [DllImport("ole32.dll", ExactSpelling = true)]
        internal static extern void CoUninitialize();

        [DllImport("ole32.dll", ExactSpelling = true)]
        [PreserveSig]
        internal static extern int CoCreateInstance(
            ref Guid classId,
            IntPtr outer,
            uint context,
            ref Guid interfaceId,
            [MarshalAs(UnmanagedType.Interface)] out IMFTransform transform);

        [DllImport("mfplat.dll", ExactSpelling = true)]
        [PreserveSig]
        internal static extern int MFStartup(uint version, uint flags);

        [DllImport("mfplat.dll", ExactSpelling = true)]
        [PreserveSig]
        internal static extern int MFShutdown();

        [DllImport("mfplat.dll", ExactSpelling = true)]
        [PreserveSig]
        internal static extern int MFCreateMediaType(
            [MarshalAs(UnmanagedType.Interface)] out IMFMediaType mediaType);

        [DllImport("mfplat.dll", ExactSpelling = true)]
        [PreserveSig]
        internal static extern int MFCreateSample(
            [MarshalAs(UnmanagedType.Interface)] out IMFSample sample);

        [DllImport("mfplat.dll", ExactSpelling = true)]
        [PreserveSig]
        internal static extern int MFCreateMemoryBuffer(
            uint maxLength,
            [MarshalAs(UnmanagedType.Interface)] out IMFMediaBuffer buffer);

        [DllImport("mfplat.dll", ExactSpelling = true)]
        [PreserveSig]
        internal static extern int MFCreateAlignedMemoryBuffer(
            uint maxLength,
            uint alignmentMask,
            [MarshalAs(UnmanagedType.Interface)] out IMFMediaBuffer buffer);
    }

    [ComImport]
    [Guid("2CD2D921-C447-44A7-A13C-4ADABFC247E3")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMFAttributes
    {
        [PreserveSig] int Slot00();
        [PreserveSig] int Slot01();
        [PreserveSig] int Slot02();
        [PreserveSig] int Slot03();
        [PreserveSig] int GetUINT32(ref Guid key, out uint value);
    }

    [ComImport]
    [Guid("44AE0FA8-EA31-4109-8D2E-4CAE4997C555")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMFMediaType
    {
        [PreserveSig] int Slot00();
        [PreserveSig] int Slot01();
        [PreserveSig] int Slot02();
        [PreserveSig] int Slot03();
        [PreserveSig] int GetUINT32(ref Guid key, out uint value);
        [PreserveSig] int Slot05();
        [PreserveSig] int Slot06();
        [PreserveSig] int GetGUID(ref Guid key, out Guid value);
        [PreserveSig] int Slot08();
        [PreserveSig] int Slot09();
        [PreserveSig] int Slot10();
        [PreserveSig] int Slot11();
        [PreserveSig] int Slot12();
        [PreserveSig] int Slot13();
        [PreserveSig] int Slot14();
        [PreserveSig] int Slot15();
        [PreserveSig] int Slot16();
        [PreserveSig] int Slot17();
        [PreserveSig] int SetUINT32(ref Guid key, uint value);
        [PreserveSig] int Slot19();
        [PreserveSig] int Slot20();
        [PreserveSig] int SetGUID(ref Guid key, ref Guid value);
        [PreserveSig] int Slot22();
        [PreserveSig] int SetBlob(
            ref Guid key,
            [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 2)] byte[] value,
            uint size);
    }

    [ComImport]
    [Guid("045FA593-8799-42B8-BC8D-8968C6453507")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMFMediaBuffer
    {
        [PreserveSig] int Lock(out IntPtr buffer, out uint maxLength, out uint currentLength);
        [PreserveSig] int Unlock();
        [PreserveSig] int GetCurrentLength(out uint currentLength);
        [PreserveSig] int SetCurrentLength(uint currentLength);
        [PreserveSig] int GetMaxLength(out uint maxLength);
    }

    [ComImport]
    [Guid("C40A00F2-B93A-4D80-AE8C-5A1C634F58E4")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMFSample
    {
        [PreserveSig] int AttributeSlot00();
        [PreserveSig] int AttributeSlot01();
        [PreserveSig] int AttributeSlot02();
        [PreserveSig] int AttributeSlot03();
        [PreserveSig] int AttributeSlot04();
        [PreserveSig] int AttributeSlot05();
        [PreserveSig] int AttributeSlot06();
        [PreserveSig] int AttributeSlot07();
        [PreserveSig] int AttributeSlot08();
        [PreserveSig] int AttributeSlot09();
        [PreserveSig] int AttributeSlot10();
        [PreserveSig] int AttributeSlot11();
        [PreserveSig] int AttributeSlot12();
        [PreserveSig] int AttributeSlot13();
        [PreserveSig] int AttributeSlot14();
        [PreserveSig] int AttributeSlot15();
        [PreserveSig] int AttributeSlot16();
        [PreserveSig] int AttributeSlot17();
        [PreserveSig] int AttributeSlot18();
        [PreserveSig] int AttributeSlot19();
        [PreserveSig] int AttributeSlot20();
        [PreserveSig] int AttributeSlot21();
        [PreserveSig] int AttributeSlot22();
        [PreserveSig] int AttributeSlot23();
        [PreserveSig] int AttributeSlot24();
        [PreserveSig] int AttributeSlot25();
        [PreserveSig] int AttributeSlot26();
        [PreserveSig] int AttributeSlot27();
        [PreserveSig] int AttributeSlot28();
        [PreserveSig] int AttributeSlot29();
        [PreserveSig] int SampleSlot00();
        [PreserveSig] int SampleSlot01();
        [PreserveSig] int SampleSlot02();
        [PreserveSig] int SetSampleTime(long sampleTime);
        [PreserveSig] int SampleSlot04();
        [PreserveSig] int SetSampleDuration(long sampleDuration);
        [PreserveSig] int SampleSlot06();
        [PreserveSig] int SampleSlot07();
        [PreserveSig] int ConvertToContiguousBuffer(
            [MarshalAs(UnmanagedType.Interface)] out IMFMediaBuffer buffer);
        [PreserveSig] int AddBuffer([MarshalAs(UnmanagedType.Interface)] IMFMediaBuffer buffer);
    }

    [ComImport]
    [Guid("BF94C121-5B05-4E6F-8000-BA598961414D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMFTransform
    {
        [PreserveSig] int Slot00GetStreamLimits();
        [PreserveSig] int GetStreamCount(out uint inputStreams, out uint outputStreams);
        [PreserveSig] int GetStreamIDs(
            uint inputIdArraySize,
            [Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] uint[] inputIds,
            uint outputIdArraySize,
            [Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 2)] uint[] outputIds);
        [PreserveSig] int Slot03GetInputStreamInfo();
        [PreserveSig] int GetOutputStreamInfo(uint streamId, out MftOutputStreamInfo streamInfo);
        [PreserveSig] int GetAttributes(
            [MarshalAs(UnmanagedType.Interface)] out IMFAttributes attributes);
        [PreserveSig] int Slot06GetInputStreamAttributes();
        [PreserveSig] int Slot07GetOutputStreamAttributes();
        [PreserveSig] int Slot08DeleteInputStream();
        [PreserveSig] int Slot09AddInputStreams();
        [PreserveSig] int Slot10GetInputAvailableType();
        [PreserveSig] int Slot11GetOutputAvailableType();
        [PreserveSig] int SetInputType(
            uint streamId,
            [MarshalAs(UnmanagedType.Interface)] IMFMediaType mediaType,
            uint flags);
        [PreserveSig] int SetOutputType(
            uint streamId,
            [MarshalAs(UnmanagedType.Interface)] IMFMediaType mediaType,
            uint flags);
        [PreserveSig] int Slot14GetInputCurrentType();
        [PreserveSig] int GetOutputCurrentType(
            uint streamId,
            [MarshalAs(UnmanagedType.Interface)] out IMFMediaType mediaType);
        [PreserveSig] int Slot16GetInputStatus();
        [PreserveSig] int Slot17GetOutputStatus();
        [PreserveSig] int Slot18SetOutputBounds();
        [PreserveSig] int Slot19ProcessEvent();
        [PreserveSig] int ProcessMessage(uint message, UIntPtr parameter);
        [PreserveSig] int ProcessInput(
            uint streamId,
            [MarshalAs(UnmanagedType.Interface)] IMFSample sample,
            uint flags);
        [PreserveSig] int ProcessOutput(
            uint flags,
            uint outputBufferCount,
            [In, Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)]
            MftOutputDataBuffer[] outputSamples,
            out uint status);
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MftOutputStreamInfo
    {
        internal uint Flags;
        internal uint Size;
        internal uint Alignment;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MftOutputDataBuffer
    {
        internal uint StreamId;

        [MarshalAs(UnmanagedType.Interface)]
        internal IMFSample Sample;

        internal uint Status;
        internal IntPtr Events;
    }
}
