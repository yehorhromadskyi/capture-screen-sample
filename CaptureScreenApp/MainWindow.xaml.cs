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
                    deviceIO.SetBrightness(1, 100);
                    break;
                }
            }

            _timer.Start();
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            //Debug.WriteLine("Started: " + GC.GetTotalMemory(true) / 1000 / 1000);

            using (var screenBmp = new Bitmap((int)SystemParameters.PrimaryScreenWidth,
                                              (int)SystemParameters.PrimaryScreenHeight,
                                              System.Drawing.Imaging.PixelFormat.Format16bppRgb555))
            {

                using (var bmpGraphics = Graphics.FromImage(screenBmp))
                {
                    bmpGraphics.CopyFromScreen(0, 0, 0, 0, screenBmp.Size);

                    //Debug.WriteLine("Copied from screen: " + GC.GetTotalMemory(true) / 1000 / 1000);

                    var bitmapPtr = screenBmp.GetHbitmap();

                    var image = Imaging.CreateBitmapSourceFromHBitmap(
                        bitmapPtr,
                        IntPtr.Zero,
                        Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions());

                    //Debug.WriteLine("Created source from HBitmap: " + GC.GetTotalMemory(true) / 1000 / 1000);

                    Bitmap bitmap;

                    using (var outStream = new MemoryStream())
                    {
                        BitmapEncoder enc = new BmpBitmapEncoder();
                        enc.Frames.Add(BitmapFrame.Create(image));
                        enc.Save(outStream);
                        bitmap = new Bitmap(outStream);
                    }

                    DeleteObject(bitmapPtr);

                    //Debug.WriteLine("Created Bitmap: " + GC.GetTotalMemory(true) / 1000 / 1000);

                    var bounds = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
                    var bitmapData =
                        bitmap.LockBits(bounds, System.Drawing.Imaging.ImageLockMode.ReadOnly,
                        bitmap.PixelFormat);

                    //Debug.WriteLine("Lock Bits: " + GC.GetTotalMemory(true) / 1000 / 1000);

                    IntPtr bitmapDataPointer = bitmapData.Scan0;

                    // initalizes the pixel read buffer
                    Int32[] sourceBuffer = new Int32[bitmap.Width];

                    // sets the offset to the first pixel in the image
                    Int64 sourceOffset = bitmapData.Scan0.ToInt64();

                    for (Int32 row = 0; row < bitmap.Height; row++)
                    {
                        // copies the whole row of pixels to the buffer
                        Marshal.Copy(new IntPtr(sourceOffset), sourceBuffer, 0, bitmap.Width);

                        //Debug.WriteLine("Marshal Copied: " + GC.GetTotalMemory(true) / 1000 / 1000);

                        // scans all the colors in the buffer
                        foreach (Color color in sourceBuffer.Select(argb => Color.FromArgb(argb)))
                        {
                            _quantizer.AddColor(color);
                        }

                        // increases a source offset by a row
                        sourceOffset += bitmapData.Stride;
                    }

                    //Debug.WriteLine("Quantizer filled: " + GC.GetTotalMemory(true) / 1000 / 1000);

                    bitmap.UnlockBits(bitmapData);

                    //Debug.WriteLine("Unlock Bits: " + GC.GetTotalMemory(true) / 1000 / 1000);

                    var mainColor = _quantizer.GetPalette(2).OrderBy(c => GetBrightness(c)).FirstOrDefault();

                    //Debug.WriteLine("Quantizer gave palette: " + GC.GetTotalMemory(true) / 1000 / 1000);

                    _quantizer.Clear();
                    //GC.Collect();

                    //Debug.WriteLine("Quantizer cleared: " + GC.GetTotalMemory(true) / 1000 / 1000);

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
