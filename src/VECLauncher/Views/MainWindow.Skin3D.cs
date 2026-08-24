using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows;
using VECLauncher.Services;

namespace VECLauncher.Views;

/// <summary>
/// Partial class handling 3D skin viewer: ground/shadow rendering, mouse rotation/zoom,
/// glTF model loading, cape mode transitions, and skin 3D texture updates.
/// </summary>
public partial class MainWindow
{
    private void BuildGroundAndShadow()
    {
        var groundGroup = new Model3DGroup();


        const int texSize = 1024;
        var gridBitmap = new System.Windows.Media.Imaging.WriteableBitmap(
            texSize, texSize, 96, 96, PixelFormats.Bgra32, null);
        var pixels = new byte[texSize * texSize * 4];

        const int mainStep = 64;
        const int subStep  = 16;
        const int mainLineW = 2;
        const int subLineW  = 1;

        double cx = texSize / 2.0;
        double cy = texSize / 2.0;
        double maxR = texSize / 2.0;

        for (int py = 0; py < texSize; py++)
        {
            for (int px = 0; px < texSize; px++)
            {
                int idx = (py * texSize + px) * 4;

                double dist = Math.Sqrt((px - cx) * (px - cx) + (py - cy) * (py - cy));
                double fade = Math.Max(0.0, 1.0 - dist / maxR);
                fade *= fade;

                int modX_main = px % mainStep;
                int modY_main = py % mainStep;
                int modX_sub  = px % subStep;
                int modY_sub  = py % subStep;

                bool isMainLine = modX_main < mainLineW || modX_main >= mainStep - mainLineW
                               || modY_main < mainLineW || modY_main >= mainStep - mainLineW;
                bool isSubLine  = modX_sub < subLineW || modY_sub < subLineW;

                if (isMainLine)
                {
                    byte a = (byte)(fade * 90);
                    pixels[idx + 0] = 255; // B
                    pixels[idx + 1] = 250; // G
                    pixels[idx + 2] = 240; // R
                    pixels[idx + 3] = a;
                }
                else if (isSubLine)
                {
                    byte a = (byte)(fade * 30);
                    pixels[idx + 0] = 220; // B
                    pixels[idx + 1] = 215; // G
                    pixels[idx + 2] = 210; // R
                    pixels[idx + 3] = a;
                }
                else
                {
                    byte a = (byte)(fade * 6);
                    pixels[idx + 0] = 200; // B
                    pixels[idx + 1] = 210; // G
                    pixels[idx + 2] = 220; // R
                    pixels[idx + 3] = a;
                }
            }
        }

        gridBitmap.WritePixels(
            new System.Windows.Int32Rect(0, 0, texSize, texSize),
            pixels, texSize * 4, 0);
        gridBitmap.Freeze();

        var gridTexBrush = new ImageBrush(gridBitmap)
        {
            TileMode    = TileMode.None,
            Stretch     = Stretch.Fill,
            ViewportUnits = BrushMappingMode.RelativeToBoundingBox
        };
        gridTexBrush.Freeze();

        const double planeExtent = 100.0;
        const double groundY = -16.0;

        var planeMesh = new MeshGeometry3D();
        planeMesh.Positions.Add(new Point3D(-planeExtent, groundY, -planeExtent));
        planeMesh.Positions.Add(new Point3D( planeExtent, groundY, -planeExtent));
        planeMesh.Positions.Add(new Point3D( planeExtent, groundY,  planeExtent));
        planeMesh.Positions.Add(new Point3D(-planeExtent, groundY,  planeExtent));

        planeMesh.TextureCoordinates.Add(new System.Windows.Point(0, 0));
        planeMesh.TextureCoordinates.Add(new System.Windows.Point(1, 0));
        planeMesh.TextureCoordinates.Add(new System.Windows.Point(1, 1));
        planeMesh.TextureCoordinates.Add(new System.Windows.Point(0, 1));

        planeMesh.TriangleIndices.Add(0);
        planeMesh.TriangleIndices.Add(1);
        planeMesh.TriangleIndices.Add(2);
        planeMesh.TriangleIndices.Add(0);
        planeMesh.TriangleIndices.Add(2);
        planeMesh.TriangleIndices.Add(3);

        var gridMat = new EmissiveMaterial(gridTexBrush);
        var planeModel = new GeometryModel3D
        {
            Geometry     = planeMesh,
            Material     = gridMat,
            BackMaterial = gridMat
        };
        groundGroup.Children.Add(planeModel);

        const double shadowY  = -15.98;
        const double shadowRx = 9.0;
        const double shadowRz = 6.0;
        const int    segs     = 40;

        var shadowMesh = new MeshGeometry3D();
        shadowMesh.Positions.Add(new Point3D(0, shadowY, 0));
        shadowMesh.TextureCoordinates.Add(new System.Windows.Point(0.5, 0.5));
        for (int i = 0; i < segs; i++)
        {
            double angle = 2 * Math.PI * i / segs;
            double cosA  = Math.Cos(angle);
            double sinA  = Math.Sin(angle);
            shadowMesh.Positions.Add(new Point3D(cosA * shadowRx, shadowY, sinA * shadowRz));
            shadowMesh.TextureCoordinates.Add(new System.Windows.Point(0.5 + cosA * 0.5, 0.5 + sinA * 0.5));
        }
        for (int i = 0; i < segs; i++)
        {
            shadowMesh.TriangleIndices.Add(0);
            shadowMesh.TriangleIndices.Add(i + 1);
            shadowMesh.TriangleIndices.Add((i + 1) % segs + 1);
        }

        var shadowBrush = new RadialGradientBrush();
        shadowBrush.GradientStops.Add(new GradientStop(Color.FromArgb(80, 0, 0, 0), 0.0));
        shadowBrush.GradientStops.Add(new GradientStop(Color.FromArgb(40, 0, 0, 0), 0.5));
        shadowBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0,  0, 0, 0), 1.0));
        shadowBrush.Freeze();

        var shadowModel = new GeometryModel3D
        {
            Geometry     = shadowMesh,
            Material     = new DiffuseMaterial(shadowBrush),
            BackMaterial = new DiffuseMaterial(shadowBrush)
        };
        groundGroup.Children.Add(shadowModel);

        _groundVisual = new ModelVisual3D { Content = groundGroup };
        _groundVisual.Transform = SkinModelVisual.Transform;
        SkinViewport3D.Children.Add(_groundVisual);
    }

    private void ApplySkin3DTransforms()
    {
        _skinRotY.Rotation = new AxisAngleRotation3D(new Vector3D(0, 1, 0), _rotAngleY);
        _skinRotX.Rotation = new AxisAngleRotation3D(new Vector3D(1, 0, 0), _rotAngleX);
    }

    private void SkinViewport_MouseMove(object sender, MouseEventArgs e)
    {
        if (_isCapeMode) return;
        if (_isDraggingSkin)
        {
            var cur = e.GetPosition(SkinViewerHost);
            var dx = cur.X - _lastMousePos.X;
            var dy = cur.Y - _lastMousePos.Y;
            _lastMousePos = cur;

            _rotAngleY += dx * 0.7;
            _rotAngleX = Math.Clamp(_rotAngleX + dy * 0.7, -80, 80);
            ApplySkin3DTransforms();
        }
    }

    private void SkinViewport_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDraggingSkin)
        {
            _isDraggingSkin = false;
            SkinViewerHost.ReleaseMouseCapture();
        }
    }
    private void SkinViewport_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_isCapeMode) return;
        var pos = SkinCamera.Position;
        var newZ = Math.Clamp(pos.Z - (e.Delta > 0 ? 3 : -3), 16, 80);
        SkinCamera.Position = new Point3D(pos.X, pos.Y, newZ);
        e.Handled = true;
    }
    private readonly GltfSkinModelService _gltfSkinModel = new();

    private void ReloadSkin3DModel()
    {
        Dispatcher.Invoke(() =>
        {
            if (_currentSkinRawBytes != null && _currentSkinRawBytes.Length > 0)
            {
                var isSlim = _currentSkinModel.Equals("slim", StringComparison.OrdinalIgnoreCase);
                _gltfSkinModel.UpdateSkinTexture(_currentSkinRawBytes, isSlim);
                _gltfSkinModel.UpdateCapeTexture(_currentCapeRawBytes);
                SkinModelVisual.Content = _gltfSkinModel.RootModelGroup;
                _gltfSkinModel.StartAnimation();
                SkinViewerPlaceholder.Visibility = Visibility.Collapsed;
            }
            else
            {
                _gltfSkinModel.StopAnimation();
                SkinModelVisual.Content = null;
                SkinViewerPlaceholder.Visibility = Visibility.Visible;
            }
        });
    }

    private byte[]? _savedOriginalCapeBytes;

    private void ExitCapeMode()
    {
        _isCapeMode = false;
        _capeTransitionTimer.Stop();
        CapeModeOverlay.Visibility = Visibility.Collapsed;

        if (BtnWardrobe.Content is TextBlock wtb) wtb.Text = "🎽";

        SkinCamera.Position = _savedCamPos;
        SkinCamera.LookDirection = _savedCamLookDir;
        _rotAngleY = _savedCamYaw;
        _rotAngleX = _savedCamPitch;
        ApplySkin3DTransforms();
        _gltfSkinModel.StartAnimation();
    }

    private void CapeTransitionTimer_Tick(object? sender, EventArgs e)
    {
        _capeTransitionAlpha += 0.15;
        if (_capeTransitionAlpha >= 0.5)
        {
            _capeTransitionTimer.Stop();
            _gltfSkinModel.UpdateCapeTexture(_capeTransitionTarget);
        }
    }

}
