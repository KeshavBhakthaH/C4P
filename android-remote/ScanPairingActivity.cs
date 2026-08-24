using System;
using System.Collections.Generic;
using AndroidResult = Android.App.Result;
using Android;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.OS;
using Android.Views;
using Android.Widget;
using AndroidX.Camera.Core;
using AndroidX.Camera.Lifecycle;
using AndroidX.Camera.View;
using AndroidX.Core.Content;
using AndroidX.Lifecycle;
using Java.Lang;
using Java.Nio;
using Java.Util.Concurrent;
using ZXing;
using ZXing.Common;
using ZXing.QrCode;

namespace A2dpRemote;

[Activity(
    Label = "C4P - Scan pairing QR",
    ScreenOrientation = ScreenOrientation.Portrait,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize)]
public class ScanPairingActivity : Activity
{
    public const string ExtraPayload = "payload";

    private const int CameraPermissionCode = 41;
    private const int MinAnalysisIntervalMs = 150;

    private PreviewView? _previewView;
    private ProcessCameraProvider? _provider;
    private long _lastAttemptMs;
    private bool _finished;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        var root = new FrameLayout(this);

        _previewView = new PreviewView(this)
        {
            LayoutParameters = new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent)
        };
        root.AddView(_previewView);

        var hint = new TextView(this)
        {
            Text = "Point the camera at the PC's QR\n(tray menu: Show pairing QR...)",
            Gravity = GravityFlags.Center,
            TextSize = 15f
        };
        hint.SetTextColor(Color.White);
        hint.SetBackgroundColor(new Color(0, 0, 0, 150));
        root.AddView(hint, new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent, GravityFlags.Bottom));

        SetContentView(root);

        if (CheckSelfPermission(Manifest.Permission.Camera) == Permission.Granted)
            StartCamera();
        else
            RequestPermissions(new[] { Manifest.Permission.Camera }, CameraPermissionCode);
    }

    public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults)
    {
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);

        if (requestCode != CameraPermissionCode)
            return;

        if (grantResults.Length > 0 && grantResults[0] == Permission.Granted)
        {
            StartCamera();
        }
        else
        {
            Toast.MakeText(this, "Camera permission denied.", ToastLength.Long)?.Show();
            Finish();
        }
    }

    public void OnQrCandidate(string text)
    {
        if (_finished || !PcPairing.TryParse(text, out _, out _, out _, out _))
            return;

        _finished = true;

        RunOnUiThread(() =>
        {
            Intent result = new();
            result.PutExtra(ExtraPayload, text);
            SetResult(AndroidResult.Ok, result);
            Finish();
        });
    }

    protected override void OnDestroy()
    {
        try
        {
            _provider?.UnbindAll();
        }
        catch
        {
        }

        base.OnDestroy();
    }

    private void StartCamera()
    {
        try
        {
            var future = ProcessCameraProvider.GetInstance(this);

            future.AddListener(new Runnable(() =>
            {
                try
                {
                    _provider = (ProcessCameraProvider)future.Get();

                    var preview = new Preview.Builder().Build();
                    preview.SetSurfaceProvider(ContextCompat.GetMainExecutor(this), _previewView!.SurfaceProvider);

                    var analysis = new ImageAnalysis.Builder()
                        .SetBackpressureStrategy(ImageAnalysis.StrategyKeepOnlyLatest)
                        .Build();

                    analysis.SetAnalyzer(
                        Executors.NewSingleThreadExecutor(),
                        new QrAnalyzer(this));

                    _provider.UnbindAll();
                    _provider.BindToLifecycle(new AlwaysResumedOwner(), CameraSelector.DefaultBackCamera, preview, analysis);
                }
                catch (System.Exception ex)
                {
                    Fail(ex.Message);
                }
            }), ContextCompat.GetMainExecutor(this));
        }
        catch (System.Exception ex)
        {
            Fail(ex.Message);
        }
    }

    private string? DecodeFrame(IImageProxy image)
    {
        long now = SystemClock.ElapsedRealtime();

        if (_finished || now - _lastAttemptMs < MinAnalysisIntervalMs)
            return null;

        _lastAttemptMs = now;

        try
        {
            IImageProxyPlaneProxy plane = image.GetPlanes()[0];
            ByteBuffer buffer = plane.Buffer;
            buffer.Rewind();

            int width = image.Width;
            int height = image.Height;
            int rowStride = plane.RowStride;
            int pixelStride = plane.PixelStride;

            if (pixelStride != 4)
                return null;

            byte[] rowData = new byte[rowStride];
            var luminance = new byte[width * height];

            for (int y = 0; y < height; y++)
            {
                int count = System.Math.Min(rowStride, buffer.Remaining());
                buffer.Get(rowData, 0, count);

                for (int x = 0; x < width; x++)
                {
                    int offset = x * pixelStride;

                    if (offset + 2 >= count)
                        break;

                    luminance[y * width + x] =
                        (byte)((rowData[offset] * 114 + rowData[offset + 1] * 587 + rowData[offset + 2] * 299) / 1000);
                }
            }

            int rotation = image.ImageInfo?.RotationDegrees ?? 0;

            foreach (int angle in RotationCandidates(rotation))
            {
                (byte[] rotated, int rw, int rh) = RotateLuminance(luminance, width, height, angle);
                string? text = TryDecode(rotated, rw, rh);

                if (text is not null)
                    return text;
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private static IEnumerable<int> RotationCandidates(int sensorRotation)
    {
        yield return 0;

        foreach (int extra in new[] { 0, 90, 180, 270 })
        {
            int combined = ((sensorRotation + extra) % 360 + 360) % 360;

            if (combined != 0)
                yield return combined;
        }
    }

    private static string? TryDecode(byte[] luminance, int width, int height)
    {
        try
        {
            var hints = new Dictionary<DecodeHintType, object>
            {
                [DecodeHintType.TRY_HARDER] = true
            };

            ZXing.Result? result = new QRCodeReader()
                .decode(new BinaryBitmap(new HybridBinarizer(new GrayLuminanceSource(luminance, width, height))), hints);

            return result?.Text;
        }
        catch
        {
            return null;
        }
    }

    private static (byte[] Data, int Width, int Height) RotateLuminance(byte[] source, int width, int height, int degrees)
    {
        degrees = ((degrees % 360) + 360) % 360;

        if (degrees == 0)
            return (source, width, height);

        var target = new byte[width * height];

        if (degrees == 180)
        {
            for (int i = 0; i < target.Length; i++)
                target[i] = source[target.Length - 1 - i];

            return (target, width, height);
        }

        bool clockwise = degrees == 90;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                byte value = source[y * width + x];
                int tx = clockwise ? height - 1 - y : x;
                int ty = clockwise ? x : width - 1 - y;
                target[ty * height + tx] = value;
            }
        }

        return (target, height, width);
    }

    private sealed class QrAnalyzer(ScanPairingActivity owner) : Java.Lang.Object, ImageAnalysis.IAnalyzer
    {
        public void Analyze(IImageProxy? image)
        {
            string? text;

            try
            {
                text = owner.DecodeFrame(image!);
            }
            finally
            {
                image?.Close();
            }

            if (text is not null)
                owner.OnQrCandidate(text);
        }
    }

    private sealed class GrayLuminanceSource(byte[] luminance, int width, int height)
        : LuminanceSource(width, height)
    {
        public override byte[] Matrix => luminance;

        public override byte[] getRow(int y, byte[]? row)
        {
            row ??= new byte[Width];
            Array.Copy(luminance, y * Width, row, 0, Width);
            return row;
        }
    }

    private sealed class AlwaysResumedOwner : Java.Lang.Object, ILifecycleOwner
    {
        private readonly LifecycleRegistry _registry;

        public AlwaysResumedOwner()
        {
            _registry = new LifecycleRegistry(this);
            _registry.HandleLifecycleEvent(Lifecycle.Event.OnCreate);
            _registry.HandleLifecycleEvent(Lifecycle.Event.OnStart);
            _registry.HandleLifecycleEvent(Lifecycle.Event.OnResume);
        }

        public Lifecycle Lifecycle => _registry;
    }

    private void Fail(string message)
    {
        RunOnUiThread(() =>
        {
            Toast.MakeText(this, $"Scanner failed: {message}", ToastLength.Long)?.Show();
            Finish();
        });
    }
}
