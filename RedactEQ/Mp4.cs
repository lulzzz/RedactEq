namespace Equature.Integration
{
    internal static class Mp4
    {

        const string Mp4Dll = "Equature.InteropServices.Mp4.dll";

        [System.Flags]
        public enum Flags : int
        {
            None                    = 0,
            EnableThreading         = 1 << 4,
            DataFromSplitter        = 1 << 5,
            FragmentedAtIPictures   = 1 << 6,
            FragmentedByHeaderSize  = 1 << 7,
            StartWithHeader         = 1 << 8,
            FragmentedByAudioFrames = 1 << 9,
            Iso5Brand               = 1 << 10,

            FragmentedMp4 = DataFromSplitter | FragmentedAtIPictures | FragmentedByAudioFrames | Iso5Brand,
            StandardMp4   = DataFromSplitter
        }

        [System.Runtime.InteropServices.DllImport(Mp4Dll, CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        public static extern bool AudioConfig(System.IntPtr mux, byte[] config, int configLength);

        [System.Runtime.InteropServices.DllImport(Mp4Dll, CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        public static extern int AudioSample(System.IntPtr mux, byte[] data, int dataLength, double timestamp);

        [System.Runtime.InteropServices.DllImport(Mp4Dll, CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        public static extern System.IntPtr Create();

        [System.Runtime.InteropServices.DllImport(Mp4Dll, CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        public static extern System.IntPtr CreateMp4Reader([System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPTStr)] string filename);

        [System.Runtime.InteropServices.DllImport(Mp4Dll, CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        public static extern void Destroy(System.IntPtr mux);

        [System.Runtime.InteropServices.DllImport(Mp4Dll, CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        public static extern void DestroyMp4Reader(System.IntPtr handle);

        [System.Runtime.InteropServices.DllImport(Mp4Dll, CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        public static extern void GetHeader(System.IntPtr mux, [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPArray)] byte[] header, ref int HeaderLength);

        [System.Runtime.InteropServices.DllImport(Mp4Dll, CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        public static extern long GetNextVideoFrame(System.IntPtr handle, [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPArray)] byte[] data, out bool key, int outputWidth = 0, int outputHeight = 0);

        [System.Runtime.InteropServices.DllImport(Mp4Dll, CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        public static extern void GetSegment(System.IntPtr mux, [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPArray)] byte[] data, ref int dataLength, out ulong baseMediaDecodeTime);

        [System.Runtime.InteropServices.DllImport(Mp4Dll, CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        public static extern bool GetVideoProperties(System.IntPtr handle, out long durationMilliseconds, out double frameRate, out int width, out int height, out int sampleCount);

        [System.Runtime.InteropServices.DllImport(Mp4Dll, CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        public static extern bool Init(System.IntPtr mux, Flags flags, int videoWidth, int videoHeight, int videoFrameRate, int videoBitrate, int audioSamplesPerSecond, int audioChannels, int audioBitsPerSample, int audioBitrate);

        [System.Runtime.InteropServices.DllImport(Mp4Dll, CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        public static extern void SetAudioMaximumSamplesPerSegment(System.IntPtr mux, int AudioMaximumSamplesPerSegment);

        [System.Runtime.InteropServices.DllImport(Mp4Dll, CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        public static extern void SetBaseMediaDecodeTime(System.IntPtr mux, [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPArray)] byte[] data, ulong baseMediaDecodeTime);

        [System.Runtime.InteropServices.DllImport(Mp4Dll, CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        public static extern bool SetOutput(System.IntPtr mux, [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPTStr)] string filename);

        [System.Runtime.InteropServices.DllImport(Mp4Dll, CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        public static extern long SetTimePosition(System.IntPtr handle, long millisecondsFromStart);

        [System.Runtime.InteropServices.DllImport(Mp4Dll, CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        public static extern double SetTimePositionAbsolute(System.IntPtr handle, double absoluteSeconds);

        [System.Runtime.InteropServices.DllImport(Mp4Dll, CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        public static extern bool Start(System.IntPtr mux, int memoryBufferSize);

        [System.Runtime.InteropServices.DllImport(Mp4Dll, CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        public static extern void SetVideoMaximumSamplesPerSegment(System.IntPtr mux, int VideoMaximumSamplesPerSegment);

        [System.Runtime.InteropServices.DllImport(Mp4Dll, CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        public static extern bool VideoConfig(System.IntPtr mux, byte[] sps, int spsLength, byte[] pps, int ppsLength);

        [System.Runtime.InteropServices.DllImport(Mp4Dll, CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        public static extern int VideoSample(System.IntPtr mux, byte[] data, int dataLength, double timestamp, bool isKey);

        [System.Runtime.InteropServices.DllImport(Mp4Dll, CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        public static extern System.IntPtr CreateH264Encoder();

        [System.Runtime.InteropServices.DllImport(Mp4Dll, CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        public static extern bool InitH264Encoder(System.IntPtr encoder, int width, int height, int framerate, int bitrate);

        [System.Runtime.InteropServices.DllImport(Mp4Dll, CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        public static extern bool Encode(System.IntPtr encoder, System.IntPtr rgbaFrame, [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPArray)] byte[] h264Nal, ref int nalSize, out bool key);

        [System.Runtime.InteropServices.DllImport(Mp4Dll, CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        public static extern void DestroyH264Encoder(System.IntPtr encoder);

        [System.Runtime.InteropServices.DllImport(Mp4Dll, CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        public static extern bool TranscodeJPEG(System.IntPtr encoder, byte[] jpegFrame, int jpegFrameSize, [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPArray)] byte[] h264Nal, ref int nalSize, out bool key);

        [System.Runtime.InteropServices.DllImport(Mp4Dll, CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        public static extern void GetVideoKeyFrameTimestamps(System.IntPtr encoder, [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.SafeArray)] out double[] timestamps);

        [System.Runtime.InteropServices.DllImport(Mp4Dll, CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        public static extern void GetVideoGOPLengths(System.IntPtr encoder, [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.SafeArray)] out int[] gopLength);

    }
}
