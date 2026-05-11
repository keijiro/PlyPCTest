using System.IO;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace Pcx.Editor
{
    [CustomEditor(typeof(PlyImporter))]
    public sealed class PlyImporterEditor : ScriptedImporterEditor
    {
        PlyHeader _header;
        bool _hasColor;
        bool _hasNormal;
        string _error;

        public override void OnEnable()
        {
            base.OnEnable();
            ParseHeader();
        }

        public override void OnInspectorGUI()
        {
            if (_error != null)
            {
                EditorGUILayout.HelpBox(_error, MessageType.Warning);
            }
            else if (_header != null)
            {
                EditorGUILayout.LabelField("Format", _header.Format.ToString());
                var v = _header.Vertex;
                EditorGUILayout.LabelField("Vertex Count",
                    v != null ? v.Count.ToString() : "(none)");
                EditorGUILayout.LabelField("Color", _hasColor ? "Yes" : "No");
                EditorGUILayout.LabelField("Normals", _hasNormal ? "Yes" : "No");
            }

            ApplyRevertGUI();
        }

        void ParseHeader()
        {
            _header = null;
            _hasColor = _hasNormal = false;
            _error = null;
            try
            {
                var path = ((PlyImporter)target).assetPath;
                using var stream = File.OpenRead(path);
                _header = PlyHeader.Read(stream);
                var v = _header.Vertex;
                if (v != null)
                {
                    var hasR = false; var hasG = false; var hasB = false;
                    var hasNx = false; var hasNy = false; var hasNz = false;
                    foreach (var p in v.Properties)
                    {
                        switch (p.Slot)
                        {
                            case PlySlot.R: hasR = true; break;
                            case PlySlot.G: hasG = true; break;
                            case PlySlot.B: hasB = true; break;
                            case PlySlot.Nx: hasNx = true; break;
                            case PlySlot.Ny: hasNy = true; break;
                            case PlySlot.Nz: hasNz = true; break;
                        }
                    }
                    _hasColor = hasR && hasG && hasB;
                    _hasNormal = hasNx && hasNy && hasNz;
                }
            }
            catch (System.Exception e)
            {
                _error = e.Message;
            }
        }
    }
}
