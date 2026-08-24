using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;

namespace VECLauncher.Services;


public class GltfSkinModelService
{
    public class BoneNode
    {
        public int Index { get; set; }
        public string Name { get; set; } = string.Empty;
        public List<int> Children { get; } = new();
        public int Parent { get; set; } = -1;

        public Vector3D RestTranslation { get; set; }
        public Quaternion RestRotation { get; set; } = Quaternion.Identity;
        public Vector3D RestScale { get; set; } = new Vector3D(1, 1, 1);

        public Vector3D CurrentTranslation { get; set; }
        public Quaternion CurrentRotation { get; set; } = Quaternion.Identity;
        public Vector3D CurrentScale { get; set; } = new Vector3D(1, 1, 1);

        public Matrix3D WorldMatrix { get; set; } = Matrix3D.Identity;
        public MatrixTransform3D Transform { get; } = new();
        public GeometryModel3D? Model { get; set; }
        public bool IsCape { get; set; }
    }

    public class Keyframe<T>
    {
        public double Time { get; set; }
        public T Value { get; set; } = default!;
    }

    public class AnimationChannel
    {
        public int TargetNode { get; set; }
        public string Path { get; set; } = string.Empty; 
        public List<Keyframe<Vector3D>> TranslationKeys { get; } = new();
        public List<Keyframe<Quaternion>> RotationKeys { get; } = new();
        public List<Keyframe<Vector3D>> ScaleKeys { get; } = new();
    }

    public class AnimationClip
    {
        public string Name { get; set; } = string.Empty;
        public double Duration { get; set; }
        public List<AnimationChannel> Channels { get; } = new();
    }

    private class RawMeshData
    {
        public Point3D[] Positions = Array.Empty<Point3D>();
        public Point[] Uvs = Array.Empty<Point>();
        public int[] VertexJoints = Array.Empty<int>();
        public List<(int[] indices, int matIdx, bool isCape)> Primitives = new();
        public Matrix3D[] IbmMatrices = Array.Empty<Matrix3D>();
        public List<int> SkinJointsList = new();
    }

    private readonly List<BoneNode> _nodes = new();
    private readonly Dictionary<string, AnimationClip> _animations = new(StringComparer.OrdinalIgnoreCase);
    private readonly Model3DGroup _rootModelGroup = new();

    private DiffuseMaterial? _skinMaterial;
    private DiffuseMaterial? _capeMaterial;
    private BoneNode? _capeNode;
    private bool _hasCape = false;
    public bool IsSlim { get; set; } = false;

    private byte[]? _currentSkinBytes;
    private byte[]? _currentCapeBytes;
    private RawMeshData? _rawMesh;

    private string _currentAnimationName = "idle_breathe";
    private string _nextAnimationName = string.Empty;
    private double _animTime = 0;
    private double _nextAnimTime = 0;
    private double _blendFactor = 1.0;
    private double _blendDuration = 0.25;
    private List<(Vector3D t, Quaternion r, Vector3D s)>? _blendSourcePose;

    private DateTime _lastFrameTime = DateTime.UtcNow;
    private DateTime _lastSkyLookTime = DateTime.UtcNow;
    private bool _isRendering = false;

    public Model3DGroup RootModelGroup => _rootModelGroup;
    public string CurrentAnimation => _currentAnimationName;

    public GltfSkinModelService()
    {
        var masterTransform = new Transform3DGroup();
        masterTransform.Children.Add(new ScaleTransform3D(16.0, 16.0, 16.0));
        masterTransform.Children.Add(new TranslateTransform3D(0, -16.0, 0));
        _rootModelGroup.Transform = masterTransform;

        LoadModel();
    }


    public void LoadModel()
    {
        try
        {
            string? jsonText = null;

            var asm = Assembly.GetExecutingAssembly();
            var resName = asm.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("3D_Model.gltf", StringComparison.OrdinalIgnoreCase));

            if (resName != null)
            {
                using var stream = asm.GetManifestResourceStream(resName);
                if (stream != null)
                {
                    using var reader = new StreamReader(stream);
                    jsonText = reader.ReadToEnd();
                }
            }

            if (string.IsNullOrEmpty(jsonText))
            {
                var localPaths = new[]
                {
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "3D_Model.gltf"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "3D_Model.gltf"),
                    "3D_Model.gltf"
                };

                foreach (var lp in localPaths)
                {
                    if (File.Exists(lp))
                    {
                        jsonText = File.ReadAllText(lp);
                        break;
                    }
                }
            }

            if (string.IsNullOrEmpty(jsonText)) return;

            using var doc = JsonDocument.Parse(jsonText);
            var root = doc.RootElement;

            byte[] bufferData = Array.Empty<byte>();
            if (root.TryGetProperty("buffers", out var buffers) && buffers.GetArrayLength() > 0)
            {
                var uri = buffers[0].GetProperty("uri").GetString() ?? "";
                if (uri.StartsWith("data:"))
                {
                    var b64 = uri.Substring(uri.IndexOf(',') + 1);
                    bufferData = Convert.FromBase64String(b64);
                }
            }

            var bufferViews = new List<(int offset, int length, int stride)>();
            if (root.TryGetProperty("bufferViews", out var bvArray))
            {
                foreach (var bv in bvArray.EnumerateArray())
                {
                    int off = bv.TryGetProperty("byteOffset", out var pOff) ? pOff.GetInt32() : 0;
                    int len = bv.GetProperty("byteLength").GetInt32();
                    int str = bv.TryGetProperty("byteStride", out var pStr) ? pStr.GetInt32() : 0;
                    bufferViews.Add((off, len, str));
                }
            }

            T[] ReadAccessor<T>(int accIdx, Func<byte[], int, T> readerFunc, int numComponents, int compSize)
            {
                var acc = root.GetProperty("accessors")[accIdx];
                int bvIdx = acc.GetProperty("bufferView").GetInt32();
                int accByteOffset = acc.TryGetProperty("byteOffset", out var pOff) ? pOff.GetInt32() : 0;
                int count = acc.GetProperty("count").GetInt32();

                var (bvOffset, _, bvStride) = bufferViews[bvIdx];
                int startOffset = bvOffset + accByteOffset;
                int elementSize = compSize * numComponents;
                int stride = bvStride > 0 ? bvStride : elementSize;

                var result = new T[count];
                for (int i = 0; i < count; i++)
                {
                    int pos = startOffset + i * stride;
                    result[i] = readerFunc(bufferData, pos);
                }
                return result;
            }

            var ibmAccIdx = root.GetProperty("skins")[0].GetProperty("inverseBindMatrices").GetInt32();
            var skinJointsList = root.GetProperty("skins")[0].GetProperty("joints")
                .EnumerateArray().Select(j => j.GetInt32()).ToList();

            var ibmMatrices = ReadAccessor(ibmAccIdx, (b, offset) =>
            {
                var floats = new float[16];
                for (int i = 0; i < 16; i++)
                    floats[i] = BitConverter.ToSingle(b, offset + i * 4);

                return new Matrix3D(
                    floats[0], floats[1], floats[2], floats[3],
                    floats[4], floats[5], floats[6], floats[7],
                    floats[8], floats[9], floats[10], floats[11],
                    floats[12], floats[13], floats[14], floats[15]
                );
            }, 16, 4);

            _nodes.Clear();
            int nodeCount = root.GetProperty("nodes").GetArrayLength();
            for (int i = 0; i < nodeCount; i++)
            {
                var nodeEl = root.GetProperty("nodes")[i];
                var node = new BoneNode
                {
                    Index = i,
                    Name = nodeEl.TryGetProperty("name", out var pName) ? pName.GetString() ?? "" : $"Node_{i}"
                };

                if (nodeEl.TryGetProperty("translation", out var pTrans))
                {
                    var a = pTrans.EnumerateArray().Select(v => v.GetDouble()).ToArray();
                    node.RestTranslation = new Vector3D(a[0], a[1], a[2]);
                }
                if (nodeEl.TryGetProperty("rotation", out var pRot))
                {
                    var a = pRot.EnumerateArray().Select(v => v.GetDouble()).ToArray();
                    node.RestRotation = new Quaternion(a[0], a[1], a[2], a[3]);
                }
                if (nodeEl.TryGetProperty("scale", out var pScale))
                {
                    var a = pScale.EnumerateArray().Select(v => v.GetDouble()).ToArray();
                    node.RestScale = new Vector3D(a[0], a[1], a[2]);
                }

                node.CurrentTranslation = node.RestTranslation;
                node.CurrentRotation = node.RestRotation;
                node.CurrentScale = node.RestScale;

                if (nodeEl.TryGetProperty("children", out var pKids))
                {
                    foreach (var kid in pKids.EnumerateArray())
                    {
                        int kidIdx = kid.GetInt32();
                        node.Children.Add(kidIdx);
                    }
                }

                _nodes.Add(node);
            }

            foreach (var n in _nodes)
            {
                foreach (var c in n.Children)
                {
                    if (c >= 0 && c < _nodes.Count)
                        _nodes[c].Parent = n.Index;
                }
            }

            var mesh0 = root.GetProperty("meshes")[0];
            var prim0 = mesh0.GetProperty("primitives")[0];

            int posAccIdx = prim0.GetProperty("attributes").GetProperty("POSITION").GetInt32();
            int uvAccIdx = prim0.GetProperty("attributes").GetProperty("TEXCOORD_0").GetInt32();
            int jointAccIdx = prim0.GetProperty("attributes").GetProperty("JOINTS_0").GetInt32();
            int weightAccIdx = prim0.GetProperty("attributes").GetProperty("WEIGHTS_0").GetInt32();

            var restPositions = ReadAccessor(posAccIdx, (b, off) =>
                new Point3D(BitConverter.ToSingle(b, off), BitConverter.ToSingle(b, off + 4), BitConverter.ToSingle(b, off + 8)), 3, 4);

            var restUvs = ReadAccessor(uvAccIdx, (b, off) =>
                new Point(BitConverter.ToSingle(b, off), BitConverter.ToSingle(b, off + 4)), 2, 4);

            var rawJoints = ReadAccessor(jointAccIdx, (b, off) =>
                (BitConverter.ToUInt16(b, off), BitConverter.ToUInt16(b, off + 2), BitConverter.ToUInt16(b, off + 4), BitConverter.ToUInt16(b, off + 6)), 4, 2);

            var rawWeights = ReadAccessor(weightAccIdx, (b, off) =>
                (BitConverter.ToSingle(b, off), BitConverter.ToSingle(b, off + 4), BitConverter.ToSingle(b, off + 8), BitConverter.ToSingle(b, off + 12)), 4, 4);

            int[] vertexJoints = new int[rawJoints.Length];
            for (int i = 0; i < rawJoints.Length; i++)
            {
                var j = rawJoints[i];
                var w = rawWeights[i];
                if (w.Item2 > 0.5f) vertexJoints[i] = j.Item2;
                else if (w.Item3 > 0.5f) vertexJoints[i] = j.Item3;
                else if (w.Item4 > 0.5f) vertexJoints[i] = j.Item4;
                else vertexJoints[i] = j.Item1;
            }

            _rawMesh = new RawMeshData
            {
                Positions = restPositions,
                Uvs = restUvs,
                VertexJoints = vertexJoints,
                IbmMatrices = ibmMatrices,
                SkinJointsList = skinJointsList
            };

            foreach (var prim in mesh0.GetProperty("primitives").EnumerateArray())
            {
                int indAccIdx = prim.GetProperty("indices").GetInt32();
                int matIdx = prim.TryGetProperty("material", out var pMat) ? pMat.GetInt32() : 0;
                bool isCape = matIdx == 1;

                var indices = ReadAccessor(indAccIdx, (b, off) => (int)BitConverter.ToUInt16(b, off), 1, 2);
                _rawMesh.Primitives.Add((indices, matIdx, isCape));
            }

            _currentSkinBytes ??= DefaultSkinService.GetDefaultSkin("Steve", false);
            _currentCapeBytes ??= DefaultSkinService.GetDefaultCape();

            var skinBmp = UpscaleNearestNeighbor(DecodeBitmap(_currentSkinBytes), 1024);
            _skinMaterial = CreateMaterial(skinBmp);

            var capeBmp = UpscaleNearestNeighbor(DecodeBitmap(_currentCapeBytes), 1024);
            _capeMaterial = CreateMaterial(capeBmp);

            RebuildBoneGeometries();

            _animations.Clear();
            if (root.TryGetProperty("animations", out var animsArray))
            {
                foreach (var aEl in animsArray.EnumerateArray())
                {
                    var clip = new AnimationClip
                    {
                        Name = aEl.GetProperty("name").GetString() ?? ""
                    };

                    double maxDuration = 0;
                    var samplersList = aEl.GetProperty("samplers").EnumerateArray().ToList();

                    foreach (var chEl in aEl.GetProperty("channels").EnumerateArray())
                    {
                        int samplerIdx = chEl.GetProperty("sampler").GetInt32();
                        var targetEl = chEl.GetProperty("target");
                        int targetNodeIdx = targetEl.GetProperty("node").GetInt32();
                        string targetPath = targetEl.GetProperty("path").GetString() ?? "";

                        var samplerEl = samplersList[samplerIdx];
                        int inAccIdx = samplerEl.GetProperty("input").GetInt32();
                        int outAccIdx = samplerEl.GetProperty("output").GetInt32();

                        var times = ReadAccessor(inAccIdx, (b, off) => (double)BitConverter.ToSingle(b, off), 1, 4);
                        if (times.Length > 0 && times.Max() > maxDuration)
                            maxDuration = times.Max();

                        var channel = new AnimationChannel
                        {
                            TargetNode = targetNodeIdx,
                            Path = targetPath
                        };

                        if (targetPath.Equals("translation", StringComparison.OrdinalIgnoreCase))
                        {
                            var values = ReadAccessor(outAccIdx, (b, off) =>
                                new Vector3D(BitConverter.ToSingle(b, off), BitConverter.ToSingle(b, off + 4), BitConverter.ToSingle(b, off + 8)), 3, 4);

                            for (int i = 0; i < times.Length; i++)
                                channel.TranslationKeys.Add(new Keyframe<Vector3D> { Time = times[i], Value = values[i] });
                        }
                        else if (targetPath.Equals("rotation", StringComparison.OrdinalIgnoreCase))
                        {
                            var values = ReadAccessor(outAccIdx, (b, off) =>
                                new Quaternion(BitConverter.ToSingle(b, off), BitConverter.ToSingle(b, off + 4), BitConverter.ToSingle(b, off + 8), BitConverter.ToSingle(b, off + 12)), 4, 4);

                            for (int i = 0; i < times.Length; i++)
                                channel.RotationKeys.Add(new Keyframe<Quaternion> { Time = times[i], Value = values[i] });
                        }
                        else if (targetPath.Equals("scale", StringComparison.OrdinalIgnoreCase))
                        {
                            var values = ReadAccessor(outAccIdx, (b, off) =>
                                new Vector3D(BitConverter.ToSingle(b, off), BitConverter.ToSingle(b, off + 4), BitConverter.ToSingle(b, off + 8)), 3, 4);

                            for (int i = 0; i < times.Length; i++)
                                channel.ScaleKeys.Add(new Keyframe<Vector3D> { Time = times[i], Value = values[i] });
                        }

                        clip.Channels.Add(channel);
                    }

                    clip.Duration = maxDuration > 0 ? maxDuration : 1.0;
                    _animations[clip.Name] = clip;
                }
            }

            UpdateBoneHierarchy();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("Ошибка загрузки glTF модели: " + ex);
        }
    }

    private void RebuildBoneGeometries()
    {
        if (_rawMesh == null) return;

        byte[] skinData = _currentSkinBytes ?? DefaultSkinService.GetDefaultSkin("Steve", IsSlim);
        var srcBmp = DecodeBitmap(skinData);
        var convertedBmp = new FormatConvertedBitmap(srcBmp, PixelFormats.Bgra32, null, 0);
        int w = convertedBmp.PixelWidth;
        int h = convertedBmp.PixelHeight;
        if (w <= 0) w = 64;
        if (h <= 0) h = 64;

        int stride = w * 4;
        byte[] skinPixels = new byte[h * stride];
        convertedBmp.CopyPixels(skinPixels, stride, 0);

        bool IsQuadSolid(double u0, double u1, double v0, double v1, bool isCape)
        {
            if (isCape) return true;

            int x0 = (int)Math.Floor(Math.Min(u0, u1) * w);
            int x1 = (int)Math.Ceiling(Math.Max(u0, u1) * w);
            int y0 = (int)Math.Floor(Math.Min(v0, v1) * h);
            int y1 = (int)Math.Ceiling(Math.Max(v0, v1) * h);

            int solidCount = 0;
            for (int y = y0; y < y1; y++)
            {
                for (int x = x0; x < x1; x++)
                {
                    if (x >= 0 && x < w && y >= 0 && y < h)
                    {
                        byte alpha = skinPixels[y * stride + x * 4 + 3];
                        if (alpha > 20) solidCount++;
                    }
                }
            }

            return solidCount > 0;
        }

        var boneMeshTriangles = new Dictionary<int, List<(int i0, int i1, int i2, Point uv0, Point uv1, Point uv2)>>();
        var boneIsCape = new Dictionary<int, bool>();

        const double eps = 0.05 / 64.0;

        foreach (var (indices, matIdx, isCape) in _rawMesh.Primitives)
        {
            for (int q = 0; q < indices.Length; q += 6)
            {
                int[] qInd = indices.Skip(q).Take(6).ToArray();
                if (qInd.Length < 6) break;

                int skinJointIdx = _rawMesh.VertexJoints[qInd[0]];
                int nodeIdx = _rawMesh.SkinJointsList[skinJointIdx];
                var node = _nodes[nodeIdx];

                bool isCapeNode = node.Name.Equals("cape", StringComparison.OrdinalIgnoreCase) || isCape;

                bool isSlimBone = node.Name.EndsWith("_slim", StringComparison.OrdinalIgnoreCase);
                bool isClassicBone = node.Name.EndsWith("_classic", StringComparison.OrdinalIgnoreCase);

                if (isSlimBone && !IsSlim) continue;
                if (isClassicBone && IsSlim) continue;

                double uMin = qInd.Min(idx => _rawMesh.Uvs[idx].X);
                double uMax = qInd.Max(idx => _rawMesh.Uvs[idx].X);
                double vMin = qInd.Min(idx => _rawMesh.Uvs[idx].Y);
                double vMax = qInd.Max(idx => _rawMesh.Uvs[idx].Y);

                if (!isCapeNode && !IsQuadSolid(uMin, uMax, vMin, vMax, isCapeNode))
                {
                    continue;
                }

                Point InsetUv(Point uv)
                {
                    double u = uv.X;
                    double v = uv.Y;

                    if (isCapeNode)
                    {
                        double uPx = u * 64.0;
                        double vPx = v * 64.0;

                        v = Math.Clamp(vPx / 32.0, 0.0, 1.0);

                        if (vPx >= 1.0)
                        {
                            if (uPx >= 1.0 && uPx <= 11.0)
                                u = (uPx + 11.0) / 64.0;
                            else if (uPx >= 12.0 && uPx <= 22.0)
                                u = (uPx - 11.0) / 64.0;
                        }

                        return new Point(u, v);
                    }

                    if (Math.Abs(u - uMin) < 1e-5) u += eps;
                    else if (Math.Abs(u - uMax) < 1e-5) u -= eps;

                    if (Math.Abs(v - vMin) < 1e-5) v += eps;
                    else if (Math.Abs(v - vMax) < 1e-5) v -= eps;

                    return new Point(u, v);
                }

                if (!boneMeshTriangles.ContainsKey(nodeIdx))
                {
                    boneMeshTriangles[nodeIdx] = new List<(int, int, int, Point, Point, Point)>();
                    boneIsCape[nodeIdx] = isCapeNode;
                }

                boneMeshTriangles[nodeIdx].Add((
                    qInd[0], qInd[1], qInd[2],
                    InsetUv(_rawMesh.Uvs[qInd[0]]), InsetUv(_rawMesh.Uvs[qInd[1]]), InsetUv(_rawMesh.Uvs[qInd[2]])
                ));

                boneMeshTriangles[nodeIdx].Add((
                    qInd[3], qInd[4], qInd[5],
                    InsetUv(_rawMesh.Uvs[qInd[3]]), InsetUv(_rawMesh.Uvs[qInd[4]]), InsetUv(_rawMesh.Uvs[qInd[5]])
                ));
            }
        }

        _rootModelGroup.Children.Clear();

        foreach (var node in _nodes)
        {
            node.Model = null;
        }
        _capeNode = null;

        foreach (var (nodeIdx, tris) in boneMeshTriangles)
        {
            var node = _nodes[nodeIdx];
            int skinJointIdx = _rawMesh.SkinJointsList.IndexOf(nodeIdx);
            var ibm = (skinJointIdx >= 0 && skinJointIdx < _rawMesh.IbmMatrices.Length) ? _rawMesh.IbmMatrices[skinJointIdx] : Matrix3D.Identity;

            var meshGeom = new MeshGeometry3D();
            var vertMap = new Dictionary<(int origIdx, Point uv), int>();

            foreach (var (i0, i1, i2, uv0, uv1, uv2) in tris)
            {
                int AddVertex(int origIdx, Point uv)
                {
                    var key = (origIdx, uv);
                    if (vertMap.TryGetValue(key, out int mapped)) return mapped;

                    var pRest = _rawMesh.Positions[origIdx];
                    var pLocal = ibm.Transform(pRest);

                    int newIdx = meshGeom.Positions.Count;
                    meshGeom.Positions.Add(pLocal);
                    meshGeom.TextureCoordinates.Add(uv);

                    vertMap[key] = newIdx;
                    return newIdx;
                }

                int v0 = AddVertex(i0, uv0);
                int v1 = AddVertex(i1, uv1);
                int v2 = AddVertex(i2, uv2);

                meshGeom.TriangleIndices.Add(v0);
                meshGeom.TriangleIndices.Add(v1);
                meshGeom.TriangleIndices.Add(v2);
            }

            bool isCape = boneIsCape.TryGetValue(nodeIdx, out bool ic) && ic;
            node.IsCape = isCape;

            var model = new GeometryModel3D
            {
                Geometry = meshGeom,
                Material = isCape ? _capeMaterial : _skinMaterial,
                BackMaterial = null,
                Transform = node.Transform
            };

            node.Model = model;
            if (isCape)
            {
                _capeNode = node;
                if (_hasCape)
                {
                    _rootModelGroup.Children.Add(model);
                }
            }
            else
            {
                _rootModelGroup.Children.Add(model);
            }
        }
    }

    private static DiffuseMaterial CreateMaterial(BitmapSource bmp)
    {
        var brush = new ImageBrush(bmp)
        {
            ViewportUnits = BrushMappingMode.Absolute,
            Viewport = new Rect(0, 0, 1, 1),
            TileMode = TileMode.None,
            Stretch = Stretch.Fill
        };
        RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.NearestNeighbor);
        return new DiffuseMaterial(brush);
    }

    public void UpdateSkinTexture(byte[]? skinRawBytes, bool isSlim = false)
    {
        IsSlim = isSlim;

        if (skinRawBytes == null || skinRawBytes.Length == 0)
            skinRawBytes = DefaultSkinService.GetDefaultSkin("Steve", isSlim);

        _currentSkinBytes = skinRawBytes;
        var skinBmp = UpscaleNearestNeighbor(DecodeBitmap(skinRawBytes), 1024);

        if (_skinMaterial != null)
        {
            var brush = new ImageBrush(skinBmp)
            {
                ViewportUnits = BrushMappingMode.Absolute,
                Viewport = new Rect(0, 0, 1, 1),
                TileMode = TileMode.None,
                Stretch = Stretch.Fill
            };
            RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.NearestNeighbor);
            _skinMaterial.Brush = brush;
        }

        RebuildBoneGeometries();
    }

    public void UpdateCapeTexture(byte[]? capeRawBytes)
    {
        _hasCape = capeRawBytes != null && capeRawBytes.Length > 0;
        _currentCapeBytes = capeRawBytes;

        if (_hasCape)
        {
            var capeBmp = UpscaleNearestNeighbor(DecodeBitmap(capeRawBytes!), 1024);
            if (_capeMaterial != null)
            {
                var brush = new ImageBrush(capeBmp)
                {
                    ViewportUnits = BrushMappingMode.Absolute,
                    Viewport = new Rect(0, 0, 1, 1),
                    TileMode = TileMode.None,
                    Stretch = Stretch.Fill
                };
                RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.NearestNeighbor);
                _capeMaterial.Brush = brush;
            }

            if (_capeNode?.Model != null)
            {
                _capeNode.Model.Material = _capeMaterial;
                if (!_rootModelGroup.Children.Contains(_capeNode.Model))
                {
                    _rootModelGroup.Children.Add(_capeNode.Model);
                }
            }
        }
        else
        {
            if (_capeNode?.Model != null)
            {
                _capeNode.Model.Material = null;
                if (_rootModelGroup.Children.Contains(_capeNode.Model))
                {
                    _rootModelGroup.Children.Remove(_capeNode.Model);
                }
            }
        }
    }

    private static BitmapSource DecodeBitmap(byte[] data)
    {
        var bmp = new BitmapImage();
        using var ms = new MemoryStream(data);
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.StreamSource = ms;
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
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

                dstPixels[dstPixelOffset + 0] = srcPixels[srcPixelOffset + 0]; // B
                dstPixels[dstPixelOffset + 1] = srcPixels[srcPixelOffset + 1]; // G
                dstPixels[dstPixelOffset + 2] = srcPixels[srcPixelOffset + 2]; // R
                dstPixels[dstPixelOffset + 3] = srcPixels[srcPixelOffset + 3]; // A
            }
        }

        var result = BitmapSource.Create(dstW, dstH, 96, 96, PixelFormats.Bgra32, null, dstPixels, dstStride);
        result.Freeze();
        return result;
    }

    public void StartAnimation()
    {
        if (_isRendering) return;
        _isRendering = true;
        _lastFrameTime = DateTime.UtcNow;
        _lastSkyLookTime = DateTime.UtcNow;
        CompositionTarget.Rendering += OnRenderFrame;
    }


    public void StopAnimation()
    {
        if (!_isRendering) return;
        _isRendering = false;
        CompositionTarget.Rendering -= OnRenderFrame;
    }

    public void PlayAnimation(string name, double blendDuration = 0.35)
    {
        if (!_animations.ContainsKey(name)) return;
        if (_currentAnimationName.Equals(name, StringComparison.OrdinalIgnoreCase) && string.IsNullOrEmpty(_nextAnimationName)) return;

        _blendSourcePose = GetCurrentPoseSnapshot();
        _nextAnimationName = name;
        _nextAnimTime = 0.0;
        _blendDuration = blendDuration > 0 ? blendDuration : 0.01;
        _blendFactor = 0.0;
    }

    private List<(Vector3D t, Quaternion r, Vector3D s)> GetCurrentPoseSnapshot()
    {
        var list = new List<(Vector3D, Quaternion, Vector3D)>(_nodes.Count);
        foreach (var n in _nodes)
        {
            list.Add((n.CurrentTranslation, n.CurrentRotation, n.CurrentScale));
        }
        return list;
    }

    private enum ScenarioState
    {
        IdleBreatheLong,   
        LookSky,           
        IdleBreatheShort,  
        WalkToRun,        
        RunSprint,        
        RunToWalk          
    }

    private ScenarioState _scenarioState = ScenarioState.IdleBreatheLong;
    private double _stateTimer = 0.0;

    private void OnRenderFrame(object? sender, EventArgs e)
    {
        var now = DateTime.UtcNow;
        double dt = (now - _lastFrameTime).TotalSeconds;
        _lastFrameTime = now;

        if (dt <= 0 || dt > 0.1) dt = 0.016; 
        _stateTimer += dt;

        if (string.IsNullOrEmpty(_nextAnimationName))
        {
            switch (_scenarioState)
            {
                case ScenarioState.IdleBreatheLong:
                    if (_stateTimer >= 12.0)
                    {
                        _scenarioState = ScenarioState.LookSky;
                        _stateTimer = 0;
                        PlayAnimation("idle_look_sky", 0.45);
                    }
                    break;

                case ScenarioState.LookSky:
                    if (_stateTimer >= 10.0)
                    {
                        _scenarioState = ScenarioState.IdleBreatheShort;
                        _stateTimer = 0;
                        PlayAnimation("idle_breathe", 0.45);
                    }
                    break;

                case ScenarioState.IdleBreatheShort:
                    if (_stateTimer >= 5.0)
                    {
                        _scenarioState = ScenarioState.WalkToRun;
                        _stateTimer = 0;
                        PlayAnimation("walk", 0.35);
                    }
                    break;

                case ScenarioState.WalkToRun:
                    if (_stateTimer >= 3.5)
                    {
                        _scenarioState = ScenarioState.RunSprint;
                        _stateTimer = 0;
                        PlayAnimation("run", 0.30);
                    }
                    break;

                case ScenarioState.RunSprint:
                    if (_stateTimer >= 3.5)
                    {
                        _scenarioState = ScenarioState.RunToWalk;
                        _stateTimer = 0;
                        PlayAnimation("walk", 0.35);
                    }
                    break;

                case ScenarioState.RunToWalk:
                    if (_stateTimer >= 2.5)
                    {
                        _scenarioState = ScenarioState.IdleBreatheLong;
                        _stateTimer = 0;
                        PlayAnimation("idle_breathe", 0.45);
                    }
                    break;
            }
        }

        UpdateAnimation(dt);
    }

    private void UpdateAnimation(double dt)
    {
        if (!_animations.TryGetValue(_currentAnimationName, out var currentClip)) return;

        _animTime += dt;
        if (_animTime > currentClip.Duration)
        {
            _animTime %= currentClip.Duration;
        }

        if (!string.IsNullOrEmpty(_nextAnimationName) && _animations.TryGetValue(_nextAnimationName, out var nextClip))
        {
            _nextAnimTime += dt;
            if (_nextAnimTime > nextClip.Duration)
            {
                _nextAnimTime %= nextClip.Duration;
            }

            _blendFactor += dt / _blendDuration;
            var targetPose = EvaluateClipPose(nextClip, _nextAnimTime);
            var sourcePose = _blendSourcePose ?? EvaluateClipPose(currentClip, _animTime);

            if (_blendFactor >= 1.0)
            {
                _blendFactor = 1.0;
                _currentAnimationName = _nextAnimationName;
                _nextAnimationName = string.Empty;
                _animTime = _nextAnimTime;
                _blendSourcePose = null;

                for (int i = 0; i < _nodes.Count; i++)
                {
                    var (t, r, s) = targetPose[i];
                    _nodes[i].CurrentTranslation = t;
                    _nodes[i].CurrentRotation = r;
                    _nodes[i].CurrentScale = s;
                }
            }
            else
            {
                for (int i = 0; i < _nodes.Count; i++)
                {
                    var n = _nodes[i];
                    var (srcT, srcR, srcS) = sourcePose[i];
                    var (tgtT, tgtR, tgtS) = targetPose[i];

                    n.CurrentTranslation = srcT + (tgtT - srcT) * _blendFactor;
                    n.CurrentRotation = Quaternion.Slerp(srcR, tgtR, _blendFactor);
                    n.CurrentScale = srcS + (tgtS - srcS) * _blendFactor;
                }
            }
        }
        else
        {
            var currentPose = EvaluateClipPose(currentClip, _animTime);
            for (int i = 0; i < _nodes.Count; i++)
            {
                var (t, r, s) = currentPose[i];
                _nodes[i].CurrentTranslation = t;
                _nodes[i].CurrentRotation = r;
                _nodes[i].CurrentScale = s;
            }
        }

        UpdateBoneHierarchy();
    }

    private List<(Vector3D t, Quaternion r, Vector3D s)> EvaluateClipPose(AnimationClip clip, double time)
    {
        var pose = new List<(Vector3D, Quaternion, Vector3D)>(_nodes.Count);
        for (int i = 0; i < _nodes.Count; i++)
        {
            pose.Add((_nodes[i].RestTranslation, _nodes[i].RestRotation, _nodes[i].RestScale));
        }

        foreach (var ch in clip.Channels)
        {
            int nIdx = ch.TargetNode;
            if (nIdx < 0 || nIdx >= _nodes.Count) continue;

            if (ch.Path.Equals("translation", StringComparison.OrdinalIgnoreCase) && ch.TranslationKeys.Count > 0)
            {
                var trans = SampleVectorKeys(ch.TranslationKeys, time);
                if (nIdx == 17 || (nIdx < _nodes.Count && _nodes[nIdx].Name.Equals("Model", StringComparison.OrdinalIgnoreCase)))
                {
                    if (trans.Y > 0.5)
                    {
                        trans = new Vector3D(trans.X, trans.Y - 1.0625, trans.Z);
                    }
                }
                pose[nIdx] = (trans, pose[nIdx].Item2, pose[nIdx].Item3);
            }
            else if (ch.Path.Equals("rotation", StringComparison.OrdinalIgnoreCase) && ch.RotationKeys.Count > 0)
            {
                pose[nIdx] = (pose[nIdx].Item1, SampleQuatKeys(ch.RotationKeys, time), pose[nIdx].Item3);
            }
            else if (ch.Path.Equals("scale", StringComparison.OrdinalIgnoreCase) && ch.ScaleKeys.Count > 0)
            {
                pose[nIdx] = (pose[nIdx].Item1, pose[nIdx].Item2, SampleVectorKeys(ch.ScaleKeys, time));
            }
        }

        return pose;
    }

    private Vector3D SampleVectorKeys(List<Keyframe<Vector3D>> keys, double time)
    {
        if (keys.Count == 0) return new Vector3D();
        if (keys.Count == 1 || time <= keys[0].Time) return keys[0].Value;
        if (time >= keys[^1].Time) return keys[^1].Value;

        for (int i = 0; i < keys.Count - 1; i++)
        {
            if (time >= keys[i].Time && time <= keys[i + 1].Time)
            {
                double span = keys[i + 1].Time - keys[i].Time;
                double t = span > 0.00001 ? (time - keys[i].Time) / span : 0;
                var v0 = keys[i].Value;
                var v1 = keys[i + 1].Value;
                return v0 + (v1 - v0) * t;
            }
        }
        return keys[^1].Value;
    }

    private Quaternion SampleQuatKeys(List<Keyframe<Quaternion>> keys, double time)
    {
        if (keys.Count == 0) return Quaternion.Identity;
        if (keys.Count == 1 || time <= keys[0].Time) return keys[0].Value;
        if (time >= keys[^1].Time) return keys[^1].Value;

        for (int i = 0; i < keys.Count - 1; i++)
        {
            if (time >= keys[i].Time && time <= keys[i + 1].Time)
            {
                double span = keys[i + 1].Time - keys[i].Time;
                double t = span > 0.00001 ? (time - keys[i].Time) / span : 0;
                return Quaternion.Slerp(keys[i].Value, keys[i + 1].Value, t);
            }
        }
        return keys[^1].Value;
    }


    private void UpdateBoneHierarchy()
    {
        foreach (var rootNode in _nodes.Where(n => n.Parent == -1))
        {
            UpdateNodeWorldMatrix(rootNode, Matrix3D.Identity);
        }
    }

    private void UpdateNodeWorldMatrix(BoneNode node, Matrix3D parentWorld)
    {
        var local = Matrix3D.Identity;

        local.Scale(node.CurrentScale);

        local.Rotate(node.CurrentRotation);

        local.Translate(node.CurrentTranslation);

        var world = local * parentWorld;
        node.WorldMatrix = world;

     
        node.Transform.Matrix = world;

        foreach (var childIdx in node.Children)
        {
            if (childIdx >= 0 && childIdx < _nodes.Count)
            {
                UpdateNodeWorldMatrix(_nodes[childIdx], world);
            }
        }
    }
}
