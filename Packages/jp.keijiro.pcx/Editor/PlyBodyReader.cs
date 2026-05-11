using System;
using System.Buffers.Binary;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace Pcx.Editor
{
    public sealed class PlyBody
    {
        public Vector3[] Positions;
        public Color32[] Colors;
        public Vector3[] Normals;
    }

    public static class PlyBodyReader
    {
        public static PlyBody Read(Stream stream, PlyHeader header)
        {
            var vertex = header.Vertex
                ?? throw new InvalidDataException("PLY file has no 'vertex' element.");

            var hasColor = HasSlots(vertex, PlySlot.R, PlySlot.G, PlySlot.B);
            var hasNormal = HasSlots(vertex, PlySlot.Nx, PlySlot.Ny, PlySlot.Nz);

            var body = new PlyBody
            {
                Positions = new Vector3[vertex.Count],
                Colors = hasColor ? new Color32[vertex.Count] : null,
                Normals = hasNormal ? new Vector3[vertex.Count] : null
            };

            foreach (var element in header.Elements)
            {
                if (element == vertex)
                    ReadVertices(stream, header, vertex, body);
                else
                    SkipElement(stream, header, element);
            }

            return body;
        }

        static bool HasSlots(PlyElement element, params PlySlot[] required)
        {
            foreach (var slot in required)
            {
                var found = false;
                foreach (var p in element.Properties)
                {
                    if (p.Slot == slot) { found = true; break; }
                }
                if (!found) return false;
            }
            return true;
        }

        // ------------------------------------------------------------ Vertex

        static void ReadVertices(Stream stream, PlyHeader header, PlyElement vertex, PlyBody body)
        {
            switch (header.Format)
            {
                case PlyFormat.Ascii:
                    ReadVerticesAscii(stream, vertex, body);
                    break;
                case PlyFormat.BinaryLittleEndian:
                    ReadVerticesBinary(stream, vertex, body, bigEndian: false);
                    break;
                case PlyFormat.BinaryBigEndian:
                    ReadVerticesBinary(stream, vertex, body, bigEndian: true);
                    break;
            }
        }

        static void ReadVerticesAscii(Stream stream, PlyElement vertex, PlyBody body)
        {
            using var reader = new StreamReader(stream, System.Text.Encoding.ASCII,
                detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);

            for (var i = 0; i < vertex.Count; i++)
            {
                var line = reader.ReadLine()
                    ?? throw new EndOfStreamException($"Unexpected EOF at vertex {i}.");
                var tokens = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (tokens.Length < vertex.Properties.Count)
                    throw new InvalidDataException($"Vertex {i} has too few tokens.");

                Vector3 pos = default, normal = default;
                byte cr = 255, cg = 255, cb = 255, ca = 255;

                for (var p = 0; p < vertex.Properties.Count; p++)
                {
                    var prop = vertex.Properties[p];
                    var token = tokens[p];
                    var d = double.Parse(token, CultureInfo.InvariantCulture);
                    AssignSlot(prop.Slot, d, ref pos, ref normal,
                        ref cr, ref cg, ref cb, ref ca);
                }

                body.Positions[i] = pos;
                if (body.Colors != null) body.Colors[i] = new Color32(cr, cg, cb, ca);
                if (body.Normals != null) body.Normals[i] = normal;
            }
        }

        static void ReadVerticesBinary(Stream stream, PlyElement vertex, PlyBody body, bool bigEndian)
        {
            Span<byte> buf = stackalloc byte[8];

            for (var i = 0; i < vertex.Count; i++)
            {
                Vector3 pos = default, normal = default;
                byte cr = 255, cg = 255, cb = 255, ca = 255;

                foreach (var prop in vertex.Properties)
                {
                    if (prop.IsList)
                    {
                        SkipBinaryList(stream, prop, bigEndian, buf);
                        continue;
                    }

                    var d = ReadScalarAsDouble(stream, prop.Type, bigEndian, buf);
                    AssignSlot(prop.Slot, d, ref pos, ref normal,
                        ref cr, ref cg, ref cb, ref ca);
                }

                body.Positions[i] = pos;
                if (body.Colors != null) body.Colors[i] = new Color32(cr, cg, cb, ca);
                if (body.Normals != null) body.Normals[i] = normal;
            }
        }

        static void AssignSlot(PlySlot slot, double v,
            ref Vector3 pos, ref Vector3 normal,
            ref byte cr, ref byte cg, ref byte cb, ref byte ca)
        {
            switch (slot)
            {
                case PlySlot.X: pos.x = (float)v; break;
                case PlySlot.Y: pos.y = (float)v; break;
                case PlySlot.Z: pos.z = (float)v; break;
                case PlySlot.Nx: normal.x = (float)v; break;
                case PlySlot.Ny: normal.y = (float)v; break;
                case PlySlot.Nz: normal.z = (float)v; break;
                case PlySlot.R: cr = ColorByte(v); break;
                case PlySlot.G: cg = ColorByte(v); break;
                case PlySlot.B: cb = ColorByte(v); break;
                case PlySlot.A: ca = ColorByte(v); break;
            }
        }

        static byte ColorByte(double v)
        {
            // uchar values arrive in 0..255; float colors arrive in 0..1.
            var scaled = v > 1.0 ? v : v * 255.0;
            if (scaled < 0) scaled = 0;
            if (scaled > 255) scaled = 255;
            return (byte)scaled;
        }

        // ------------------------------------------------------------ Skip

        static void SkipElement(Stream stream, PlyHeader header, PlyElement element)
        {
            if (header.Format == PlyFormat.Ascii)
            {
                using var reader = new StreamReader(stream, System.Text.Encoding.ASCII,
                    detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);
                for (var i = 0; i < element.Count; i++) reader.ReadLine();
                return;
            }

            var bigEndian = header.Format == PlyFormat.BinaryBigEndian;
            Span<byte> buf = stackalloc byte[8];

            // Optimisation: if no list properties, we can fast-skip a fixed row size.
            var fixedRowSize = 0;
            var hasList = false;
            foreach (var p in element.Properties)
            {
                if (p.IsList) { hasList = true; break; }
                fixedRowSize += p.ByteWidth;
            }

            if (!hasList)
            {
                var total = (long)fixedRowSize * element.Count;
                Skip(stream, total);
                return;
            }

            for (var i = 0; i < element.Count; i++)
            {
                foreach (var p in element.Properties)
                {
                    if (p.IsList)
                    {
                        SkipBinaryList(stream, p, bigEndian, buf);
                    }
                    else
                    {
                        Skip(stream, p.ByteWidth);
                    }
                }
            }
        }

        static void SkipBinaryList(Stream stream, PlyProperty prop, bool bigEndian, Span<byte> buf)
        {
            var count = (int)ReadScalarAsDouble(stream, prop.ListCountType, bigEndian, buf);
            Skip(stream, (long)count * prop.ByteWidth);
        }

        static void Skip(Stream stream, long bytes)
        {
            if (bytes <= 0) return;
            if (stream.CanSeek)
            {
                stream.Seek(bytes, SeekOrigin.Current);
                return;
            }
            Span<byte> trash = stackalloc byte[256];
            while (bytes > 0)
            {
                var take = (int)Math.Min(bytes, trash.Length);
                ReadExact(stream, trash[..take]);
                bytes -= take;
            }
        }

        // ------------------------------------------------------------ IO

        static double ReadScalarAsDouble(Stream stream, PlyType type, bool bigEndian, Span<byte> buf)
        {
            var width = PlyProperty.SizeOf(type);
            ReadExact(stream, buf[..width]);

            switch (type)
            {
                case PlyType.Char:
                    return (sbyte)buf[0];
                case PlyType.UChar:
                    return buf[0];
                case PlyType.Short:
                    return bigEndian
                        ? BinaryPrimitives.ReadInt16BigEndian(buf)
                        : BinaryPrimitives.ReadInt16LittleEndian(buf);
                case PlyType.UShort:
                    return bigEndian
                        ? BinaryPrimitives.ReadUInt16BigEndian(buf)
                        : BinaryPrimitives.ReadUInt16LittleEndian(buf);
                case PlyType.Int:
                    return bigEndian
                        ? BinaryPrimitives.ReadInt32BigEndian(buf)
                        : BinaryPrimitives.ReadInt32LittleEndian(buf);
                case PlyType.UInt:
                    return bigEndian
                        ? BinaryPrimitives.ReadUInt32BigEndian(buf)
                        : BinaryPrimitives.ReadUInt32LittleEndian(buf);
                case PlyType.Float:
                {
                    var bits = bigEndian
                        ? BinaryPrimitives.ReadInt32BigEndian(buf)
                        : BinaryPrimitives.ReadInt32LittleEndian(buf);
                    return BitConverter.Int32BitsToSingle(bits);
                }
                case PlyType.Double:
                {
                    var bits = bigEndian
                        ? BinaryPrimitives.ReadInt64BigEndian(buf)
                        : BinaryPrimitives.ReadInt64LittleEndian(buf);
                    return BitConverter.Int64BitsToDouble(bits);
                }
                default:
                    throw new InvalidDataException("Unknown PLY scalar type.");
            }
        }

        static void ReadExact(Stream stream, Span<byte> buffer)
        {
            var total = 0;
            while (total < buffer.Length)
            {
                var read = stream.Read(buffer[total..]);
                if (read <= 0)
                    throw new EndOfStreamException("Unexpected EOF in PLY body.");
                total += read;
            }
        }
    }
}
