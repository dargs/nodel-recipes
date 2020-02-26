using System;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;
using System.IO;
using System.Text;
using System.Threading;

class ComputerController
{
    static bool s_running = true;

    static void Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.ForegroundColor = ConsoleColor.Blue;
        Console.WriteLine("// ʕ•ᴥ•ʔ");
        Console.ResetColor();
        Console.WriteLine("// Computer Controller started");

        AppDomain.CurrentDomain.ProcessExit += new EventHandler(CurrentDomain_ProcessExit);

        MonitorCPU();

        MonitorSystemVolumeChanges();

        MonitorScreenshotChanges();

        ProcessStandardInput();
    }

    static void CurrentDomain_ProcessExit(object sender, EventArgs e)
    {
        s_running = false;
        Console.WriteLine("// Computer Controller exited");
    }

    static void PrintUsage()
    {
        Console.WriteLine("// get-mute");
        Console.WriteLine("// set-mute (0 or 1)");
        Console.WriteLine("// get-volume");
        Console.WriteLine("// set-volume (0 - 100)");
        Console.WriteLine("// q");
    }

    static async void ProcessStandardInput()
    {
        while (s_running)
        {
            Task<string> task = Console.In.ReadLineAsync();

            var line = await task;

            if (string.IsNullOrWhiteSpace(line))
                continue;

            var parts = line.Split(' ');

            string action = parts[0];
            string arg;

            switch (action)
            {
                case "get-mute":
                    EmitMute();
                    break;

                case "set-mute":
                    arg = parts[1];

                    bool state;
                    if (arg == "1")
                        state = true;
                    else if (arg == "0")
                        state = false;
                    else
                        break;

                    AudioEndpoint.Mute = state;
                    EmitMute();
                    break;

                case "get-volume":
                    EmitVolume();
                    break;

                case "set-volume":
                    arg = parts[1];
                    AudioEndpoint.Volume = float.Parse(arg);
                    EmitVolume();
                    break;

                case "q":
                    Console.WriteLine("// Goodbye");
                    return;

                default:
                    PrintUsage();
                    break;
            };
        }

    }

    #region CPU

    static async void MonitorCPU()
    {
        Console.WriteLine("// ...measuring overall CPU usage every 10s");

        PerformanceCounter cpuCounter = new PerformanceCounter();
        cpuCounter.CategoryName = "Processor";
        cpuCounter.CounterName = "% Processor Time";
        cpuCounter.InstanceName = "_Total";

        // Get Current Cpu Usage
        var baseSnap = cpuCounter.NextSample();

        var currentCpuUsage = cpuCounter.NextValue();

        while (s_running)
        {
            await Task.Delay(10000); // check every 10 seconds

            var currentSnap = cpuCounter.NextSample();

            var diff = currentSnap.RawValue - baseSnap.RawValue;

            currentCpuUsage = cpuCounter.NextValue();
            // log("CPU:" + currentCpuUsage + ", diff:" + diff + ", ave:" + diff/4);
            Console.WriteLine("{ event: CPU, arg: " + currentCpuUsage + " }");
        }

    }

    #endregion

    #region Audio

    #region (Win32 wrappers, etc.)

    [Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioEndpointVolume
    {
        // f(), g(), ... are unused COM method slots.
        int f(); int g(); int h(); int i();
        int SetMasterVolumeLevelScalar(float fLevel, Guid pguidEventContext);
        int j();
        int GetMasterVolumeLevelScalar(out float pfLevel);
        int k(); int l(); int m(); int n();
        int SetMute([MarshalAs(UnmanagedType.Bool)] bool bMute, Guid pguidEventContext);
        int GetMute(out bool pbMute);
    }

    [Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice
    {
        int Activate(ref Guid id, int clsCtx, int activationParams, out IAudioEndpointVolume aev);
    }

    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator
    {
        int f(); // Unused
        int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);
    }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    class MMDeviceEnumeratorComObject { }

    #endregion

    static bool s_mute = AudioEndpoint.Mute;

    static float s_volume = AudioEndpoint.Volume;

    static void EmitMute()
    {
        Console.WriteLine("{ event: Mute, arg: " + (AudioEndpoint.Mute ? "true" : "false") + " }");
    }

    static void EmitVolume()
    {
        Console.WriteLine("{ event: Volume, arg: " + Math.Round(AudioEndpoint.Volume * 100) + " }");
    }

    static async void MonitorSystemVolumeChanges()
    {
        Console.WriteLine("// ...monitoring volume and mute changes every 2.5s");
        while (s_running)
        {
            // Compare previous and current (from endpoint) mute status updating if neccesary.
            if (s_mute != AudioEndpoint.Mute)
            {
                s_mute = AudioEndpoint.Mute;
                EmitMute();
            }
            // Compare previous and current (from endpoint) volume status updating if neccesary.
            if (s_volume != AudioEndpoint.Volume)
            {
                s_volume = AudioEndpoint.Volume;
                EmitVolume();
            }

            await Task.Delay(2500); // check every 2.5 seconds
        }
    }

    // see https://community.idera.com/database-tools/powershell/powertips/b/tips/posts/controlling-audio-volume-and-mute-status
    public class AudioEndpoint
    {
        static IAudioEndpointVolume Vol()
        {
            var enumerator = new MMDeviceEnumeratorComObject() as IMMDeviceEnumerator;
            IMMDevice dev = null;
            Marshal.ThrowExceptionForHR(enumerator.GetDefaultAudioEndpoint(/*eRender*/ 0, /*eMultimedia*/ 1, out dev));
            IAudioEndpointVolume epv = null;
            var epvid = typeof(IAudioEndpointVolume).GUID;
            Marshal.ThrowExceptionForHR(dev.Activate(ref epvid, /*CLSCTX_ALL*/ 23, 0, out epv));
            return epv;
        }

        public static float Volume
        {
            get { float v = -1; Marshal.ThrowExceptionForHR(Vol().GetMasterVolumeLevelScalar(out v)); return v; }
            set { Marshal.ThrowExceptionForHR(Vol().SetMasterVolumeLevelScalar(value, Guid.Empty)); }
        }

        public static bool Mute
        {
            get { bool mute; Marshal.ThrowExceptionForHR(Vol().GetMute(out mute)); return mute; }
            set { Marshal.ThrowExceptionForHR(Vol().SetMute(value, Guid.Empty)); }
        }
    }

    #endregion

    #region Screenshots

    public async static void MonitorScreenshotChanges()
    {
        Console.WriteLine("// ...taking screenshots after 10s then every 60s");

        await Task.Delay(10000); // first after 10s
        Screenshots();

        while (s_running)
        {
            await Task.Delay(60000); // then every 60s
            Screenshots();
        }
    }

    private static StringBuilder[] s_lastScreenshots;

    private static StringBuilder[] s_currentScreenshots; // use this to reduce memory allocation

    public static void Screenshots()
    {
        int i = 0;

        var allScreens = Screen.AllScreens;

        // check if screens come and / or go
        if (s_lastScreenshots == null || s_lastScreenshots.Length != allScreens.Length) {
            s_lastScreenshots = new StringBuilder[allScreens.Length];
            s_currentScreenshots = new StringBuilder[allScreens.Length];
        }

        foreach (var screen in allScreens)
        {
            Bitmap screenshot = new Bitmap(screen.Bounds.Width, screen.Bounds.Height, PixelFormat.Format32bppArgb);
            Graphics screenshotGraphics = Graphics.FromImage(screenshot);

            // Make the screenshot
            screenshotGraphics.CopyFromScreen(Screen.PrimaryScreen.Bounds.X, Screen.PrimaryScreen.Bounds.Y, 0, 0, Screen.PrimaryScreen.Bounds.Size, CopyPixelOperation.SourceCopy);

            Image newImage = ScaleImage(screenshot, 400, 400);

            // Other types
            // screenshot.Save("screen" + i + ".jpg", GetEncoderInfo("image/jpeg"), p);
            // newImage.Save("screen" + i + ".png", ImageFormat.Png);

            // DON'T SAVE TO DISK
            // screenshot.Save("screen" + i + ".jpg", GetEncoderInfo("image/jpeg"), p);

            using (MemoryStream m = new MemoryStream())
            {
                // must keep it tiny, fit for thumbnail purposes (nodel not designed to handle large binary blobs)
                string contentType = "image/jpeg";
                var p = new EncoderParameters(1);
                p.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 10L);
                newImage.Save(m, GetEncoderInfo("image/jpeg"), p);

                // for PNG
                // string contentType = "image/png";
                // newImage.Save(m, ImageFormat.Png);

                byte[] imageBytes = m.ToArray();

                // Convert byte[] to Base64 String
                string base64String = Convert.ToBase64String(imageBytes, Base64FormattingOptions.None); // or InsertLineBreaks


                var currentScreenshot = s_currentScreenshots[i];
                if (currentScreenshot == null) {
                    currentScreenshot = new StringBuilder();
                    s_currentScreenshots[i] = currentScreenshot;
                }
                currentScreenshot.Length = 0;
                currentScreenshot.Append("{ event: Screenshot" + (i + 1) + ", arg: 'data:" + contentType + ";base64," + base64String + "' }");

                var lastScreenshot = s_lastScreenshots[i];

                if (!IsSame(currentScreenshot, lastScreenshot))
                {
                    Console.WriteLine(currentScreenshot);
                    s_lastScreenshots[i] = currentScreenshot;
                }
            }

            i++;
        }
    }

    private static ImageCodecInfo GetEncoderInfo(String mimeType)
    {
        int j;
        ImageCodecInfo[] encoders;
        encoders = ImageCodecInfo.GetImageEncoders();
        for (j = 0; j < encoders.Length; ++j)
        {
            if (encoders[j].MimeType == mimeType)
                return encoders[j];
        }
        return null;
    }

    public static Image ScaleImage(Image image, int maxWidth, int maxHeight)
    {
        var ratioX = (double)maxWidth / image.Width;
        var ratioY = (double)maxHeight / image.Height;
        var ratio = Math.Min(ratioX, ratioY);

        var newWidth = (int)(image.Width * ratio);
        var newHeight = (int)(image.Height * ratio);

        var newImage = new Bitmap(newWidth, newHeight);

        using (var graphics = Graphics.FromImage(newImage))
            graphics.DrawImage(image, 0, 0, newWidth, newHeight);

        return newImage;
    }

    #endregion

    #region (Win32 wrappers, etc.)

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool CloseHandle(IntPtr hObject);

    [StructLayout(LayoutKind.Sequential)]
    struct IO_COUNTERS
    {
        public UInt64 ReadOperationCount;
        public UInt64 WriteOperationCount;
        public UInt64 OtherOperationCount;
        public UInt64 ReadTransferCount;
        public UInt64 WriteTransferCount;
        public UInt64 OtherTransferCount;
    }


    [StructLayout(LayoutKind.Sequential)]
    struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public Int64 PerProcessUserTimeLimit;
        public Int64 PerJobUserTimeLimit;
        public UInt32 LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public UInt32 ActiveProcessLimit;
        public UIntPtr Affinity;
        public UInt32 PriorityClass;
        public UInt32 SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SECURITY_ATTRIBUTES
    {
        public UInt32 nLength;
        public IntPtr lpSecurityDescriptor;
        public Int32 bInheritHandle;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    public enum JobObjectInfoType
    {
        AssociateCompletionPortInformation = 7,
        BasicLimitInformation = 2,
        BasicUIRestrictions = 4,
        EndOfJobTimeInformation = 6,
        ExtendedLimitInformation = 9,
        SecurityLimitInformation = 5,
        GroupInformation = 11
    }

    public static uint ProcessSecurityAndAccessRights_SYNCHRONIZE = 0x00100000;

    [DllImport("kernel32.dll")]
    public static extern IntPtr OpenProcess(
      uint dwDesiredAccess,
      bool bInheritHandle,
      int dwProcessId
    );

    #endregion

    #region (convenience functions)

    private static IEnumerator<String> StringStream(String[] strings)
    {
        foreach (String s in strings)
            yield return s;
    }

    private static string JSONEscape(string value)
    {
        var result = new StringBuilder(value.Length);
        foreach (char c in value)
        {
            if (c == '\\')
                result.Append("\\\\");
            else if (c == '"')
                result.Append("\\\"");
            else
                result.Append(c);
        }

        return result.ToString();
    }

    /// <summary>
    /// Need SafeWaitHandle functionality here.
    /// </summary>
    private static ManualResetEvent IntPtrToManualResetEvent(IntPtr intPtr)
    {
        ManualResetEvent mre = new ManualResetEvent(true);
        mre.SafeWaitHandle = new SafeWaitHandle(intPtr, false);
        return mre;
    }

    private static bool IsSame(StringBuilder sb1, StringBuilder sb2)
    {
        if (sb1 == null && sb2 == null)
            return true; // both are null
        
        if (sb1 == null || sb2 == null)
            return false; // only one is null

        var len = sb1.Length;

        if (len != sb2.Length) 
            return false; // different lengths so different

        for (int i =0; i<len; i++) //
        {
            var c1 = sb1[i];
            var c2 = sb2[i];

            if (c1 != c2)
                return false;
        }

        return true;
    }

    #endregion

}