using System.Drawing;
using System.Numerics;
using System.Reflection.Metadata;
using TM_GenericMapping.MediaTracker.Components;

namespace TM_GenericMapping.Common.IO
{
    public interface IBinarySerializable
    {
        void Write(BinaryWriter w);
        void Read(BinaryReader r);
    }

    /// <summary>
    /// Serializable form of TriangleObjects
    /// </summary>
    public class TriangleObjectData : IBinarySerializable
    {
        private const uint MagicNumber = 0x4D455348; // "MESH"
        private static byte Version = 4;

        public Vector3[] Vertices = Array.Empty<Vector3>();
        public Color[] Colors = Array.Empty<Color>();
        public int[] Triangles = Array.Empty<int>();
        public Vector3 LocalPosition;
        public Quaternion LocalRotation;
        public Vector3 LocalScale;
        public string Name = string.Empty;
        public int FillVertexCount;
        public int FillTrianglesCount;
        public bool HasOutline;
        public float OutlineWidth;
        public OutlineExtendsDirection OutlineExtends;
        public bool CanFill;
        public bool IsFilled;
        public bool HasUniqueVertices;
        public BlockShareMode BlockShareMode;
        public int? BlockShareId;
        public bool HasBlockShareId;

        public TriangleObjectData[] SubObjects = Array.Empty<TriangleObjectData>();
        public ISerializableComponent[] SerializableComponents = Array.Empty<ISerializableComponent>();

        public void Write(BinaryWriter w) 
        {
            w.Write(MagicNumber);
            w.Write(Version);

            w.Write(Name);

            w.Write(FillVertexCount);
            w.Write(FillTrianglesCount);
            w.Write(HasOutline);
            w.Write(OutlineWidth);
            w.Write((byte)OutlineExtends);
            w.Write(CanFill);
            w.Write(IsFilled);
            w.Write(HasUniqueVertices);
            w.Write((byte)BlockShareMode);

            w.Write(LocalPosition.X);
            w.Write(LocalPosition.Y);
            w.Write(LocalPosition.Z);

            w.Write(LocalRotation.X);
            w.Write(LocalRotation.Y);
            w.Write(LocalRotation.Z);
            w.Write(LocalRotation.W);

            w.Write(LocalScale.X);
            w.Write(LocalScale.Y);
            w.Write(LocalScale.Z);

            w.Write(Vertices.Length);
            foreach (var v in Vertices)
            {
                w.Write(v.X); 
                w.Write(v.Y); 
                w.Write(v.Z);
            }

            foreach (var c in Colors)
            {
                w.Write(c.A);
                w.Write(c.R); 
                w.Write(c.G); 
                w.Write(c.B);
            }

            w.Write(Triangles.Length);
            foreach (var t in Triangles) 
                w.Write(t);

            w.Write(SubObjects?.Length ?? 0);
            if (SubObjects != null)
            {
                foreach (var sub in SubObjects)
                    sub.Write(w);
            }

            if (Version < 3)
                return;

            w.Write(SerializableComponents?.Length ?? 0);
            if(SerializableComponents != null)
            {
                foreach (var cmp in SerializableComponents)
                {
                    string typeName;
                    if (Version < 4)
                    {
                        typeName = cmp.GetType().AssemblyQualifiedName!;
                    }
                    else
                    {
                        typeName = ComponentRegistry.GetId(cmp.GetType());
                    }
                    w.Write(typeName);
                    using var ms = new MemoryStream();
                    using var bw = new BinaryWriter(ms);
                    cmp.Serialize(bw, Version);
                    bw.Flush();
                    byte[] data = ms.ToArray();
                    w.Write(data.Length); // length prefix
                    w.Write(data);
                }
            }

            if (Version < 4)
                return;

            w.Write(HasBlockShareId);
            w.Write(BlockShareId ?? 0);
        }
        public void Read(BinaryReader r)
        {
            long fileLength = r.BaseStream.Length;

            if (fileLength < 6) // magic + version + at least one count
                throw new InvalidDataException("File too small");

            uint magic = r.ReadUInt32();
            if (magic != MagicNumber)
                throw new InvalidDataException($"Invalid file format (expected MESH header)");

            byte version = r.ReadByte();
            if (version > Version)
                throw new InvalidDataException($"Unsupported version: {version}");

            Name = r.ReadString();
            FillVertexCount = r.ReadInt32();
            FillTrianglesCount = r.ReadInt32();
            HasOutline = r.ReadBoolean();
            OutlineWidth = r.ReadSingle();
            OutlineExtends = (OutlineExtendsDirection)r.ReadByte();
            CanFill = r.ReadBoolean();
            IsFilled = r.ReadBoolean();
            HasUniqueVertices = r.ReadBoolean();

            if(version < 4)
            {
                bool canShareBlock = r.ReadBoolean();
                BlockShareMode = canShareBlock ? BlockShareMode.Hierarchy : BlockShareMode.Standalone;
            } 
            else
                BlockShareMode = (BlockShareMode)r.ReadByte();


            LocalPosition = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
            LocalRotation = new Quaternion(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
            LocalScale = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());

            int vertCount = r.ReadInt32();
            if (vertCount < 0 || vertCount > 10_000_000) // sanity check
                throw new InvalidDataException($"Invalid vertex count: {vertCount}");

            // check if file is large enough for claimed data
            long needed = 6 + 4 + (vertCount * 12) + (vertCount * 4) + 4; // header + verts + colors + triCount
            if (fileLength < needed)
                throw new InvalidDataException("File truncated");

            Vertices = new Vector3[vertCount];
            Colors = new Color[vertCount];

            for (int i = 0; i < vertCount; i++)
                Vertices[i] = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
            for (int i = 0; i < vertCount; i++)
                Colors[i] = Color.FromArgb(r.ReadByte(), r.ReadByte(), r.ReadByte(), r.ReadByte());

            int triCount = r.ReadInt32();
            if (triCount < 0 || triCount > 30_000_000 || triCount % 3 != 0)
                throw new InvalidDataException($"Invalid triangle count: {triCount}");

            Triangles = new int[triCount];
            for (int i = 0; i < triCount; i++)
            {
                int idx = r.ReadInt32();
                if (idx < 0 || idx >= vertCount)
                    throw new InvalidDataException($"Triangle index out of range: {idx}");
                Triangles[i] = idx;
            }

            int subCount = r.ReadInt32();
            if (subCount < 0 || subCount > 10_000)
                throw new InvalidDataException($"Invalid subobject count: {subCount}");

            SubObjects = new TriangleObjectData[subCount];
            for (int i = 0; i < subCount; i++)
            {
                SubObjects[i] = new TriangleObjectData();
                SubObjects[i].Read(r);
            }

            if (version < 3)
                return;

            int cmpCount = r.ReadInt32();
            if (cmpCount < 0 || cmpCount > 10_000)
                throw new InvalidDataException($"Invalid component count: {subCount}");

            SerializableComponents = new ISerializableComponent[cmpCount];
            for (int i = 0; i < cmpCount; i++)
            {
                string typeName = r.ReadString();
                Type type = Type.GetType(typeName)!;

                int length = r.ReadInt32();
                byte[] data = r.ReadBytes(length);

                using var ms = new MemoryStream(data);
                using var br = new BinaryReader(ms);
                ISerializableComponent cmp = null!;
                if (version < 4)
                {
                    if (type == null) // probably old version < v3
                    {
                        // strip assembly info → keep only full type name
                        string fullName = typeName.Split(',')[0];

                        type = AppDomain.CurrentDomain
                            .GetAssemblies()
                            .Select(a => a.GetType(fullName, false))
                            .FirstOrDefault(t => t != null)!;
                    }
                    if (type == null)
                        throw new InvalidOperationException($"Type not found: {typeName}");
                    cmp = (ISerializableComponent)Activator.CreateInstance(type)!;
                  
                }
                else
                {
                    cmp = ComponentRegistry.Create(typeName);
                }


                cmp.Deserialize(br, version);
                SerializableComponents[i] = cmp;
            }

            if (version < 4)
                return;

            HasBlockShareId = r.ReadBoolean();
            BlockShareId = r.ReadInt32();
        }

        public TriangleObjectData Copy()
        {
            using (var ms = new MemoryStream())
            {
                // Write this object to memory
                using (var writer = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
                {
                    Write(writer);
                }

                // Reset stream position
                ms.Position = 0;

                // Read into new object
                var copy = new TriangleObjectData();
                using (var reader = new BinaryReader(ms, System.Text.Encoding.UTF8))
                {
                    copy.Read(reader);
                }

                return copy;
            }
        }

    }
}
