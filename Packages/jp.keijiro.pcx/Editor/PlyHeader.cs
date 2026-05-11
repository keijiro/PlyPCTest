using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Pcx.Editor
{
    public enum PlyFormat
    {
        Ascii,
        BinaryLittleEndian,
        BinaryBigEndian
    }

    public enum PlyType
    {
        Char,
        UChar,
        Short,
        UShort,
        Int,
        UInt,
        Float,
        Double
    }

    public enum PlySlot
    {
        Skip,
        X, Y, Z,
        R, G, B, A,
        Nx, Ny, Nz
    }

    public sealed class PlyProperty
    {
        public string Name;
        public PlyType Type;
        public bool IsList;
        public PlyType ListCountType;
        public PlySlot Slot = PlySlot.Skip;

        public int ByteWidth => SizeOf(Type);
        public int ListCountByteWidth => SizeOf(ListCountType);

        public static int SizeOf(PlyType t) => t switch
        {
            PlyType.Char => 1,
            PlyType.UChar => 1,
            PlyType.Short => 2,
            PlyType.UShort => 2,
            PlyType.Int => 4,
            PlyType.UInt => 4,
            PlyType.Float => 4,
            PlyType.Double => 8,
            _ => 0
        };
    }

    public sealed class PlyElement
    {
        public string Name;
        public int Count;
        public List<PlyProperty> Properties = new();
    }

    public sealed class PlyHeader
    {
        public PlyFormat Format;
        public List<PlyElement> Elements = new();

        public PlyElement Vertex
        {
            get
            {
                foreach (var e in Elements)
                    if (e.Name == "vertex") return e;
                return null;
            }
        }

        public static PlyHeader Read(Stream stream)
        {
            var header = new PlyHeader();
            var firstLine = ReadLine(stream);
            if (firstLine != "ply")
                throw new InvalidDataException("Not a PLY file (missing magic).");

            PlyElement currentElement = null;
            while (true)
            {
                var line = ReadLine(stream);
                if (line == null)
                    throw new EndOfStreamException("Unexpected EOF in PLY header.");
                if (line.Length == 0) continue;
                if (line == "end_header") break;

                var tokens = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                switch (tokens[0])
                {
                    case "format":
                        header.Format = tokens[1] switch
                        {
                            "ascii" => PlyFormat.Ascii,
                            "binary_little_endian" => PlyFormat.BinaryLittleEndian,
                            "binary_big_endian" => PlyFormat.BinaryBigEndian,
                            _ => throw new InvalidDataException($"Unknown PLY format '{tokens[1]}'.")
                        };
                        break;

                    case "comment":
                    case "obj_info":
                        break;

                    case "element":
                        currentElement = new PlyElement
                        {
                            Name = tokens[1],
                            Count = int.Parse(tokens[2])
                        };
                        header.Elements.Add(currentElement);
                        break;

                    case "property":
                        if (currentElement == null)
                            throw new InvalidDataException("Property before element.");
                        currentElement.Properties.Add(ParseProperty(tokens, currentElement.Name));
                        break;
                }
            }

            return header;
        }

        static PlyProperty ParseProperty(string[] tokens, string elementName)
        {
            var prop = new PlyProperty();
            if (tokens[1] == "list")
            {
                prop.IsList = true;
                prop.ListCountType = ParseType(tokens[2]);
                prop.Type = ParseType(tokens[3]);
                prop.Name = tokens[4];
            }
            else
            {
                prop.Type = ParseType(tokens[1]);
                prop.Name = tokens[2];
            }

            if (elementName == "vertex" && !prop.IsList)
                prop.Slot = MapSlot(prop.Name);

            return prop;
        }

        static PlySlot MapSlot(string name) => name switch
        {
            "x" => PlySlot.X,
            "y" => PlySlot.Y,
            "z" => PlySlot.Z,
            "red" or "r" => PlySlot.R,
            "green" or "g" => PlySlot.G,
            "blue" or "b" => PlySlot.B,
            "alpha" or "a" => PlySlot.A,
            "nx" => PlySlot.Nx,
            "ny" => PlySlot.Ny,
            "nz" => PlySlot.Nz,
            _ => PlySlot.Skip
        };

        static PlyType ParseType(string token) => token switch
        {
            "char" or "int8" => PlyType.Char,
            "uchar" or "uint8" => PlyType.UChar,
            "short" or "int16" => PlyType.Short,
            "ushort" or "uint16" => PlyType.UShort,
            "int" or "int32" => PlyType.Int,
            "uint" or "uint32" => PlyType.UInt,
            "float" or "float32" => PlyType.Float,
            "double" or "float64" => PlyType.Double,
            _ => throw new InvalidDataException($"Unknown PLY scalar type '{token}'.")
        };

        static string ReadLine(Stream stream)
        {
            var sb = new StringBuilder(64);
            while (true)
            {
                var b = stream.ReadByte();
                if (b < 0) return sb.Length == 0 ? null : sb.ToString();
                if (b == '\n') return sb.ToString();
                if (b == '\r') continue;
                sb.Append((char)b);
            }
        }
    }
}
