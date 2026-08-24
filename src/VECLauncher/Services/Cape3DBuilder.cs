using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;

namespace VECLauncher.Services;

public static class Cape3DBuilder
{
    public static Model3DGroup BuildCapeModel(byte[]? capeBytes, double waveAngle = 0.0, bool isSelected = false, bool isLocked = false)
    {
        var group = new Model3DGroup();

        if (capeBytes == null || capeBytes.Length == 0)
        {
            return group;
        }

        BitmapSource? origBitmap = null;
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = new MemoryStream(capeBytes);
            bmp.EndInit();
            bmp.Freeze();
            origBitmap = bmp;
        }
        catch
        {
            return group;
        }

        var tex = UpscaleNearestNeighbor(origBitmap, 512);
        var brush = new ImageBrush(tex)
        {
            ViewportUnits = BrushMappingMode.Absolute
        };
        RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.NearestNeighbor);

        var material = new DiffuseMaterial(brush);
        var backMaterial = new DiffuseMaterial(brush);

        double w = 10.0;
        double h = 16.0;
        double d = 1.0;

        double x0 = -w / 2.0;
        double x1 = w / 2.0;
        double y0 = -h;
        double y1 = 0.0;
        double z0 = -d / 2.0;
        double z1 = d / 2.0;

        var mesh = new MeshGeometry3D();

        AddQuad(mesh,
            new Point3D(x0, y1, z1),
            new Point3D(x1, y1, z1),
            new Point3D(x1, y0, z1),
            new Point3D(x0, y0, z1),
            new Point(1.0 / 64.0, 1.0 / 32.0),
            new Point(11.0 / 64.0, 1.0 / 32.0),
            new Point(11.0 / 64.0, 17.0 / 32.0),
            new Point(1.0 / 64.0, 17.0 / 32.0));

        AddQuad(mesh,
            new Point3D(x1, y1, z0),
            new Point3D(x0, y1, z0),
            new Point3D(x0, y0, z0),
            new Point3D(x1, y0, z0),
            new Point(12.0 / 64.0, 1.0 / 32.0),
            new Point(22.0 / 64.0, 1.0 / 32.0),
            new Point(22.0 / 64.0, 17.0 / 32.0),
            new Point(12.0 / 64.0, 17.0 / 32.0));

        AddQuad(mesh,
            new Point3D(x0, y1, z0),
            new Point3D(x1, y1, z0),
            new Point3D(x1, y1, z1),
            new Point3D(x0, y1, z1),
            new Point(1.0 / 64.0, 0.0),
            new Point(11.0 / 64.0, 0.0),
            new Point(11.0 / 64.0, 1.0 / 32.0),
            new Point(1.0 / 64.0, 1.0 / 32.0));

        AddQuad(mesh,
            new Point3D(x0, y0, z1),
            new Point3D(x1, y0, z1),
            new Point3D(x1, y0, z0),
            new Point3D(x0, y0, z0),
            new Point(11.0 / 64.0, 0.0),
            new Point(21.0 / 64.0, 0.0),
            new Point(21.0 / 64.0, 1.0 / 32.0),
            new Point(11.0 / 64.0, 1.0 / 32.0));

        AddQuad(mesh,
            new Point3D(x0, y1, z0),
            new Point3D(x0, y1, z1),
            new Point3D(x0, y0, z1),
            new Point3D(x0, y0, z0),
            new Point(0.0, 1.0 / 32.0),
            new Point(1.0 / 64.0, 1.0 / 32.0),
            new Point(1.0 / 64.0, 17.0 / 32.0),
            new Point(0.0, 17.0 / 32.0));

        AddQuad(mesh,
            new Point3D(x1, y1, z1),
            new Point3D(x1, y1, z0),
            new Point3D(x1, y0, z0),
            new Point3D(x1, y0, z1),
            new Point(11.0 / 64.0, 1.0 / 32.0),
            new Point(12.0 / 64.0, 1.0 / 32.0),
            new Point(12.0 / 64.0, 17.0 / 32.0),
            new Point(11.0 / 64.0, 17.0 / 32.0));

        mesh.Freeze();
        var model = new GeometryModel3D(mesh, material)
        {
            BackMaterial = backMaterial
        };

        if (isLocked)
        {
            var emissive = new EmissiveMaterial(new SolidColorBrush(Color.FromArgb(50, 0, 0, 0)));
            var matGroup = new MaterialGroup();
            matGroup.Children.Add(material);
            matGroup.Children.Add(emissive);
            model.Material = matGroup;
        }

        group.Children.Add(model);
        return group;
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

    private static void AddTriangle(MeshGeometry3D mesh, Point3D p0, Point3D p1, Point3D p2)
    {
        var idx = mesh.Positions.Count;
        mesh.Positions.Add(p0);
        mesh.Positions.Add(p1);
        mesh.Positions.Add(p2);

        mesh.TextureCoordinates.Add(new Point(0.5, 0.5));
        mesh.TextureCoordinates.Add(new Point(0, 0));
        mesh.TextureCoordinates.Add(new Point(1, 0));

        mesh.TriangleIndices.Add(idx + 0);
        mesh.TriangleIndices.Add(idx + 1);
        mesh.TriangleIndices.Add(idx + 2);
    }

    private static BitmapSource UpscaleNearestNeighbor(BitmapSource source, int targetWidth = 512)
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

        var dst = BitmapSource.Create(dstW, dstH, 96, 96, PixelFormats.Bgra32, null, dstPixels, dstStride);
        dst.Freeze();
        return dst;
    }
}