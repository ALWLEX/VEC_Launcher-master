using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace VECLauncher.Services;

public static class SkinBodyRenderer
{
    public static BitmapSource? Render(byte[] png, bool slim = false)
    {
        try
        {
            var sheet = new BitmapImage();
            sheet.BeginInit();
            sheet.CacheOption = BitmapCacheOption.OnLoad;
            sheet.StreamSource = new MemoryStream(png);
            sheet.EndInit();
            sheet.Freeze();

            var s = sheet.PixelWidth / 64.0;
            if (sheet.PixelWidth != 64 && sheet.PixelWidth != 128) return null;
            if (sheet.PixelHeight != 64 && sheet.PixelHeight != 128 && sheet.PixelHeight != 32) return null;

            var armW = slim ? 3.0 : 4.0;
            const double step = 6;
            var width = 16 * step;
            var height = 32 * step;

            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                void Part(double sx, double sy, double sw, double sh, double dx, double dy)
                {
                    var src = new Int32Rect((int)(sx * s), (int)(sy * s), (int)(sw * s), (int)(sh * s));
                    if (src.Width <= 0 || src.Height <= 0) return;
                    var crop = new CroppedBitmap(sheet, src);
                    crop.Freeze();
                    dc.DrawImage(crop, new Rect(dx * step, dy * step, sw * step, sh * step));
                }

                Part(8, 8, 8, 8, 4, 0);
                Part(40, 8, 8, 8, 4, 0);
                Part(20, 20, 8, 12, 4, 8);
                Part(44, 20, armW, 12, 0, 8);
                Part(36, 52, armW, 12, 16 - armW, 8);
                Part(4, 20, 4, 12, 4, 20);
                Part(20, 52, 4, 12, 8, 20);
            }

            var rtb = new RenderTargetBitmap((int)width, (int)height, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(visual);
            rtb.Freeze();
            return rtb;
        }
        catch
        {
            return null;
        }
    }
}