using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using NAudio.Wave;
using NAudio.Dsp;
using MkRgb.Core;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;
using WinRT;

namespace MkRgb.Desktop;

public sealed class SessionInputs : IDisposable
{
    WasapiLoopbackCapture? audio; Direct3D11CaptureFramePool? pool; GraphicsCaptureSession? session; IDirect3DDevice? device;
    readonly float[] samples = new float[1024]; int sampleIndex; int busy;
    public float[] Spectrum { get; private set; } = new float[32]; public Rgb Screen { get; private set; } = Rgb.Black;
    public string AudioState { get; private set; } = "Off"; public string ScreenState { get; private set; } = "Off";
    public void StartAudio()
    {
        StopAudio();
        audio = new();
        audio.DataAvailable += (_, e) =>
        {
            var format = audio.WaveFormat;
            var bytes = format.BitsPerSample / 8;
            for (var i = 0; i + format.BlockAlign <= e.BytesRecorded; i += format.BlockAlign)
            {
                var value = bytes == 4 ? BitConverter.ToSingle(e.Buffer, i) : BitConverter.ToInt16(e.Buffer, i) / 32768f;
                if (!float.IsFinite(value))
                    value = 0;
                samples[sampleIndex++] = value;
                if (sampleIndex == 1024)
                {
                    sampleIndex = 0;
                    var fft = new Complex[1024];
                    for (var j = 0; j < 1024; j++)
                        fft[j].X = samples[j] * (float)FastFourierTransform.HammingWindow(j, 1024);
                    FastFourierTransform.FFT(true, 10, fft);
                    var next = new float[32];
                    for (var b = 0; b < 32; b++)
                    {
                        var start = (int)Math.Pow(512, b / 32d);
                        var end = Math.Max(start + 1, (int)Math.Pow(512, (b + 1) / 32d));
                        double sum = 0;
                        for (var k = start; k < Math.Min(512, end); k++)
                            sum += Math.Sqrt(fft[k].X * fft[k].X + fft[k].Y * fft[k].Y);
                        next[b] = (float)Math.Clamp(Math.Sqrt(sum / (end - start)) * 5, 0, 1);
                    }
                    Spectrum = next;
                }
            }
        };
        audio.StartRecording();
        AudioState = "Listening to system audio";
    }
    public void StopAudio()
    {
        audio?.StopRecording();
        audio?.Dispose();
        audio = null;
        Spectrum = new float[32];
        AudioState = "Off";
    }
    public async Task StartScreen(nint window)
    {
        StopScreen();
        if (!GraphicsCaptureSession.IsSupported())
            throw new NotSupportedException("Windows Graphics Capture is unavailable.");
        var picker = new GraphicsCapturePicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, window);
        var item = await picker.PickSingleItemAsync();
        if (item is null)
            return;
        var hr = D3D11CreateDevice(0, 1, 0, 0x20, 0, 0, 7, out var native, out _, out var context);
        Marshal.ThrowExceptionForHR(hr);
        try
        {
            var iid = new Guid("54EC77FA-1377-44E6-8C32-88FD5F44C84C");
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(native, in iid, out var dxgi));
            try
            {
                Marshal.ThrowExceptionForHR(CreateDirect3D11DeviceFromDXGIDevice(dxgi, out var inspectable));
                try
                {
                    device = MarshalInterface<IDirect3DDevice>.FromAbi(inspectable);
                }
                finally { Marshal.Release(inspectable); }
            }
            finally { Marshal.Release(dxgi); }
        }
        finally { Marshal.Release(native); Marshal.Release(context); }
        pool = Direct3D11CaptureFramePool.CreateFreeThreaded(device, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, item.Size);
        pool.FrameArrived += async (sender, _) =>
        {
            if (Interlocked.Exchange(ref busy, 1) != 0)
                return;
            try
            {
                using var frame = sender.TryGetNextFrame();
                if (frame is null)
                    return;
                using var bitmap = await SoftwareBitmap.CreateCopyFromSurfaceAsync(frame.Surface);
                var bytes = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
                bitmap.CopyToBuffer(bytes.AsBuffer());
                long r = 0, g = 0, b = 0, n = 0;
                for (var i = 0; i < bytes.Length; i += 256)
                {
                    b += bytes[i];
                    g += bytes[i + 1];
                    r += bytes[i + 2];
                    n++;
                }
                if (n > 0)
                    Screen = new((byte)(r / n), (byte)(g / n), (byte)(b / n));
                if (frame.ContentSize.Width != item.Size.Width || frame.ContentSize.Height != item.Size.Height)
                    sender.Recreate(device, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, frame.ContentSize);
            }
            catch (Exception e) { ScreenState = e.Message; Screen = Rgb.Black; }
            finally { Interlocked.Exchange(ref busy, 0); }
        };
        item.Closed += (_, _) => StopScreen();
        session = pool.CreateCaptureSession(item);
        session.StartCapture();
        ScreenState = "Capturing " + item.DisplayName;
    }
    public void StopScreen()
    {
        session?.Dispose();
        session = null;
        pool?.Dispose();
        pool = null;
        device?.Dispose();
        device = null;
        Screen = Rgb.Black;
        ScreenState = "Off";
    }
    public void Dispose()
    {
        StopAudio();
        StopScreen();
    }
    [DllImport("d3d11.dll")] static extern int D3D11CreateDevice(nint adapter, uint driver, nint software, uint flags, nint levels, uint levelCount, uint sdk, out nint device, out uint level, out nint context);
    [DllImport("d3d11.dll")] static extern int CreateDirect3D11DeviceFromDXGIDevice(nint dxgi, out nint device);
}
