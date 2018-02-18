using System;
using System.Linq;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CaptureScreenApp.Quantization;
using CaptureScreenApp.DeviceControl;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CaptureScreenApp
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        DispatcherTimer _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1300) };
        MedianCutQuantizer _quantizer = new MedianCutQuantizer();

        DeviceScanner deviceScanner;
        DeviceIO deviceIO;

        [System.Runtime.InteropServices.DllImport("gdi32.dll")]
        public static extern bool DeleteObject(IntPtr hObject);

        public MainWindow()
        {
            InitializeComponent();

            Loaded += MainWindow_Loaded;
            _timer.Tick += Timer_Tick;

            deviceIO = new DeviceIO();

            deviceScanner = new DeviceScanner();
            deviceScanner.StartListening();
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            while (true)
            {
                // Send Discovery Message
                deviceScanner.SendDiscoveryMessage();
                await Task.Delay(100);
                if (deviceScanner.DiscoveredDevices.Any())
                {
                    deviceIO.Connect(deviceScanner.DiscoveredDevices.First());
                    deviceIO.SetBrightness(1, 500);
                    break;
                }
            }

            _timer.Start();
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            using (var screenBmp = new Bitmap((int)SystemParameters.PrimaryScreenWidth,
                                              (int)SystemParameters.PrimaryScreenHeight,
                                              System.Drawing.Imaging.PixelFormat.Format32bppRgb))
            {

                using (var bmpGraphics = Graphics.FromImage(screenBmp))
                {
                    bmpGraphics.CopyFromScreen(0, 0, 0, 0, screenBmp.Size);

                    var bitmapPtr = screenBmp.GetHbitmap();

                    var bitmapSource = Imaging.CreateBitmapSourceFromHBitmap(
                        bitmapPtr,
                        IntPtr.Zero,
                        Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions());

                    Bitmap bitmap;

                    using (var outStream = new MemoryStream())
                    {
                        BitmapEncoder enc = new BmpBitmapEncoder();
                        enc.Frames.Add(BitmapFrame.Create(bitmapSource));
                        enc.Save(outStream);
                        bitmap = new Bitmap(outStream);
                    }

                    DeleteObject(bitmapPtr);

                    var bounds = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
                    var bitmapData =
                        bitmap.LockBits(bounds, System.Drawing.Imaging.ImageLockMode.ReadOnly,
                        bitmap.PixelFormat);

                    IntPtr bitmapDataPointer = bitmapData.Scan0;

                    // initalizes the pixel read buffer
                    Int32[] sourceBuffer = new Int32[bitmap.Width];

                    // sets the offset to the first pixel in the image
                    Int64 sourceOffset = bitmapData.Scan0.ToInt64();

                    for (Int32 row = 0; row < bitmap.Height; row++)
                    {
                        // copies the whole row of pixels to the buffer
                        Marshal.Copy(new IntPtr(sourceOffset), sourceBuffer, 0, bitmap.Width);

                        // scans all the colors in the buffer
                        foreach (Color color in sourceBuffer.Select(argb => Color.FromArgb(argb)))
                        {
                            _quantizer.AddColor(color);
                        }

                        // increases a source offset by a row
                        sourceOffset += bitmapData.Stride;
                    }

                    bitmap.UnlockBits(bitmapData);

                    var mainColor = _quantizer.GetPalette(2).OrderBy(c => GetBrightness(c)).FirstOrDefault();

                    _quantizer.Clear();
                    //GC.Collect();

                    Background.Fill = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(mainColor.A, mainColor.R, mainColor.G, mainColor.B));

                    deviceIO.SetColor(mainColor.R, mainColor.G, mainColor.B, 500);
                }
            }
        }

        private int GetBrightness(Color c)
        {
            return (int)Math.Sqrt(
               c.R * c.R * .299 +
               c.G * c.G * .587 +
               c.B * c.B * .114);
        }
    }
}
