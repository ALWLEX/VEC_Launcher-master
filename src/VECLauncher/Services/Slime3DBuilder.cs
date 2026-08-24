using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;

namespace VECLauncher.Services;

public static class Slime3DBuilder
{
    private static BitmapSource? _slimeBitmap;

    public static BitmapSource GetSlimeBitmap()
    {
        if (_slimeBitmap != null) return _slimeBitmap;

        BitmapImage? rawBmp = null;

        string[] searchPaths = new[]
        {
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "slime.png"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "Assets", "slime.png")
        };

        foreach (var p in searchPaths)
        {
            try
            {
                if (File.Exists(p))
                {
                    var bytes = File.ReadAllBytes(p);
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.StreamSource = new MemoryStream(bytes);
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.EndInit();
                    bmp.Freeze();
                    rawBmp = bmp;
                    break;
                }
            }
            catch (Exception ex) { Log.Warn(ex.Message); }
        }

        if (rawBmp == null)
        {
            string[] resourceUris = new[]
            {
                "pack://application:,,,/VECLauncher;component/Assets/slime.png",
                "pack://application:,,,/Assets/slime.png"
            };

            foreach (var uriStr in resourceUris)
            {
                try
                {
                    var uri = new Uri(uriStr, UriKind.Absolute);
                    var streamInfo = Application.GetResourceStream(uri);
                    if (streamInfo != null)
                    {
                        var bmp = new BitmapImage();
                        bmp.BeginInit();
                        bmp.StreamSource = streamInfo.Stream;
                        bmp.CacheOption = BitmapCacheOption.OnLoad;
                        bmp.EndInit();
                        bmp.Freeze();
                        rawBmp = bmp;
                        break;
                    }
                }
                catch (Exception ex) { Log.Warn(ex.Message); }
            }
        }

        if (rawBmp == null)
        {
            rawBmp = CreateFallbackSlimeTexture();
        }

        _slimeBitmap = UpscaleNearestNeighbor(rawBmp, 1024);
        return _slimeBitmap;
    }

    public static Model3DGroup BuildSlimeModel()
    {
        var group = new Model3DGroup();
        var skinBitmap = GetSlimeBitmap();

        int texW = skinBitmap.PixelWidth;
        int texH = skinBitmap.PixelHeight;

        var skinBrush = new ImageBrush(skinBitmap)
        {
            ViewportUnits = BrushMappingMode.Absolute,
            TileMode = TileMode.None,
            Stretch = Stretch.Fill
        };
        RenderOptions.SetBitmapScalingMode(skinBrush, BitmapScalingMode.NearestNeighbor);
        skinBrush.Freeze();

        var coreMaterial = new DiffuseMaterial(skinBrush);
        coreMaterial.Freeze();

        var outerBrush = new ImageBrush(skinBitmap)
        {
            ViewportUnits = BrushMappingMode.Absolute,
            TileMode = TileMode.None,
            Stretch = Stretch.Fill,
            Opacity = 0.65
        };
        RenderOptions.SetBitmapScalingMode(outerBrush, BitmapScalingMode.NearestNeighbor);
        outerBrush.Freeze();

        var outerMaterial = new DiffuseMaterial(outerBrush);
        outerMaterial.Freeze();

        const double S = 0.15;
        const double CY = -20.0;

        AddMinecraftBox(group, coreMaterial,
            offX: -3, offY: 17, offZ: -3,
            dx: 6, dy: 6, dz: 6,
            texU: 0, texV: 16,
            texW, texH, S, CY);

        AddMinecraftBox(group, coreMaterial,
            offX: -3.25, offY: 18, offZ: -3.5,
            dx: 2, dy: 2, dz: 2,
            texU: 32, texV: 0,
            texW, texH, S, CY);

        AddMinecraftBox(group, coreMaterial,
            offX: 1.25, offY: 18, offZ: -3.5,
            dx: 2, dy: 2, dz: 2,
            texU: 32, texV: 4,
            texW, texH, S, CY);

        AddMinecraftBox(group, coreMaterial,
            offX: 0, offY: 21, offZ: -3.5,
            dx: 1, dy: 1, dz: 1,
            texU: 32, texV: 8,
            texW, texH, S, CY);

        AddMinecraftBox(group, outerMaterial,
            offX: -4, offY: 16, offZ: -4,
            dx: 8, dy: 8, dz: 8,
            texU: 0, texV: 0,
            texW, texH, S, CY);

        return group;
    }

    private static void AddMinecraftBox(
        Model3DGroup group, Material material,
        double offX, double offY, double offZ,
        double dx, double dy, double dz,
        double texU, double texV,
        int texW, int texH,
        double scale, double centerYOffset)
    {
        var mesh = new MeshGeometry3D();

        double x0 = offX * scale;
        double x1 = (offX + dx) * scale;
        double y0 = -(offY + dy + centerYOffset) * scale;
        double y1 = -(offY + centerYOffset) * scale;
        double z0 = -(offZ + dz) * scale;
        double z1 = -offZ * scale;

        var scaleX = (double)texW / 64.0;
        var scaleY = (double)texH / 32.0;

        Point UV(double px, double py) => new(px * scaleX / texW, py * scaleY / texH);

        int tw = (int)dx;
        int th = (int)dy;
        int td = (int)dz;
        double u = texU;
        double v = texV;

        AddQuad(mesh,
            new Point3D(x0, y1, z1),
            new Point3D(x1, y1, z1),
            new Point3D(x1, y0, z1),
            new Point3D(x0, y0, z1),
            UV(u + td, v + td),
            UV(u + td + tw, v + td),
            UV(u + td + tw, v + td + th),
            UV(u + td, v + td + th));

        AddQuad(mesh,
            new Point3D(x1, y1, z0),
            new Point3D(x0, y1, z0),
            new Point3D(x0, y0, z0),
            new Point3D(x1, y0, z0),
            UV(u + td + tw + td, v + td),
            UV(u + td + tw + td + tw, v + td),
            UV(u + td + tw + td + tw, v + td + th),
            UV(u + td + tw + td, v + td + th));

        AddQuad(mesh,
            new Point3D(x0, y1, z0),
            new Point3D(x1, y1, z0),
            new Point3D(x1, y1, z1),
            new Point3D(x0, y1, z1),
            UV(u + td, v),
            UV(u + td + tw, v),
            UV(u + td + tw, v + td),
            UV(u + td, v + td));

        AddQuad(mesh,
            new Point3D(x0, y0, z1),
            new Point3D(x1, y0, z1),
            new Point3D(x1, y0, z0),
            new Point3D(x0, y0, z0),
            UV(u + td + tw, v),
            UV(u + td + tw + tw, v),
            UV(u + td + tw + tw, v + td),
            UV(u + td + tw, v + td));

        AddQuad(mesh,
            new Point3D(x0, y1, z0),
            new Point3D(x0, y1, z1),
            new Point3D(x0, y0, z1),
            new Point3D(x0, y0, z0),
            UV(u, v + td),
            UV(u + td, v + td),
            UV(u + td, v + td + th),
            UV(u, v + td + th));

        AddQuad(mesh,
            new Point3D(x1, y1, z1),
            new Point3D(x1, y1, z0),
            new Point3D(x1, y0, z0),
            new Point3D(x1, y0, z1),
            UV(u + td + tw, v + td),
            UV(u + td + tw + td, v + td),
            UV(u + td + tw + td, v + td + th),
            UV(u + td + tw, v + td + th));

        mesh.Freeze();

        var model = new GeometryModel3D(mesh, material)
        {
            BackMaterial = material
        };
        model.Freeze();
        group.Children.Add(model);
    }

    private static void AddQuad(
        MeshGeometry3D mesh,
        Point3D p0, Point3D p1, Point3D p2, Point3D p3,
        Point uv0, Point uv1, Point uv2, Point uv3)
    {
        var startIndex = mesh.Positions.Count;

        mesh.Positions.Add(p0);
        mesh.Positions.Add(p1);
        mesh.Positions.Add(p2);
        mesh.Positions.Add(p3);

        mesh.TextureCoordinates.Add(uv0);
        mesh.TextureCoordinates.Add(uv1);
        mesh.TextureCoordinates.Add(uv2);
        mesh.TextureCoordinates.Add(uv3);

        mesh.TriangleIndices.Add(startIndex + 0);
        mesh.TriangleIndices.Add(startIndex + 1);
        mesh.TriangleIndices.Add(startIndex + 2);

        mesh.TriangleIndices.Add(startIndex + 0);
        mesh.TriangleIndices.Add(startIndex + 2);
        mesh.TriangleIndices.Add(startIndex + 3);
    }

    private static BitmapSource UpscaleNearestNeighbor(BitmapSource source, int targetWidth = 1024)
    {
        var scale = Math.Max(1, targetWidth / source.PixelWidth);
        if (scale <= 1) return source;

        var srcBmp = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var srcW = srcBmp.PixelWidth;
        var srcH = srcBmp.PixelHeight;
        var srcStride = srcW * 4;
        var srcPixels = new byte[srcH * srcStride];
        srcBmp.CopyPixels(srcPixels, srcStride, 0);

        var dstW = srcW * scale;
        var dstH = srcH * scale;
        var dstStride = dstW * 4;
        var dstPixels = new byte[dstH * dstStride];

        for (int y = 0; y < dstH; y++)
        {
            int srcY = y / scale;
            int srcRowOffset = srcY * srcStride;
            int dstRowOffset = y * dstStride;

            for (int x = 0; x < dstW; x++)
            {
                int srcX = x / scale;
                int srcPixelOffset = srcRowOffset + srcX * 4;
                int dstPixelOffset = dstRowOffset + x * 4;

                dstPixels[dstPixelOffset + 0] = srcPixels[srcPixelOffset + 0];
                dstPixels[dstPixelOffset + 1] = srcPixels[srcPixelOffset + 1];
                dstPixels[dstPixelOffset + 2] = srcPixels[srcPixelOffset + 2];
                dstPixels[dstPixelOffset + 3] = srcPixels[srcPixelOffset + 3];
            }
        }

        var result = BitmapSource.Create(dstW, dstH, 96, 96, PixelFormats.Bgra32, null, dstPixels, dstStride);
        result.Freeze();
        return result;
    }

    private static BitmapImage CreateFallbackSlimeTexture()
    {
        int w = 64, h = 32;
        var pixels = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int idx = (y * w + x) * 4;
                pixels[idx] = 0x62;
                pixels[idx + 1] = 0xD4;
                pixels[idx + 2] = 0x7E;
                pixels[idx + 3] = 0xFF;
            }
        }
        var wb = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgra32, null);
        wb.WritePixels(new Int32Rect(0, 0, w, h), pixels, w * 4, 0);
        wb.Freeze();

        var bmp = new BitmapImage();
        using var ms = new MemoryStream();
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(wb));
        enc.Save(ms);
        ms.Seek(0, SeekOrigin.Begin);
        bmp.BeginInit();
        bmp.StreamSource = ms;
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }
}