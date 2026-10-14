using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace PsdUgui.Editor
{
    // Read embedded borders only; retain target-project texture platform settings.
    public class TextureImportSettings : AssetPostprocessor
    {
        [Serializable]
        private sealed class NineSliceBorder
        {
            public int left;
            public int bottom;
            public int right;
            public int top;
        }

        private void OnPreprocessTexture()
        {
            if (Path.GetFileNameWithoutExtension(assetPath).EndsWith("_9s", StringComparison.OrdinalIgnoreCase)
                && TryReadNineSliceBorder(assetPath, out NineSliceBorder border))
            {
                var slicedImporter = (TextureImporter)assetImporter;
                slicedImporter.textureType = TextureImporterType.Sprite;
                slicedImporter.spriteImportMode = SpriteImportMode.Single;
                slicedImporter.alphaIsTransparency = true;
                slicedImporter.mipmapEnabled = false;
                slicedImporter.spriteBorder = new Vector4(border.left, border.bottom, border.right, border.top);
            }
        }

        // Read the exporter's PNG tEXt chunk without creating a companion asset.
        private static bool TryReadNineSliceBorder(string path, out NineSliceBorder border)
        {
            border = null;
            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream);
            byte[] signature = reader.ReadBytes(8);
            if (signature.Length != 8 || signature[0] != 137 || signature[1] != 80 ||
                signature[2] != 78 || signature[3] != 71 || signature[4] != 13 ||
                signature[5] != 10 || signature[6] != 26 || signature[7] != 10)
                return false;
            while (stream.Length - stream.Position >= 12)
            {
                byte[] size = reader.ReadBytes(4);
                uint length = ((uint)size[0] << 24) | ((uint)size[1] << 16) | ((uint)size[2] << 8) | size[3];
                string type = System.Text.Encoding.ASCII.GetString(reader.ReadBytes(4));
                if (length > stream.Length - stream.Position - 4)
                    return false;
                if (type == "tEXt" && length <= 4096)
                {
                    string text = System.Text.Encoding.UTF8.GetString(reader.ReadBytes((int)length));
                    const string prefix = "UnityNineSlice\0";
                    if (text.StartsWith(prefix, StringComparison.Ordinal))
                    {
                        border = JsonUtility.FromJson<NineSliceBorder>(text.Substring(prefix.Length));
                        return border != null;
                    }
                }
                else
                    stream.Seek(length, SeekOrigin.Current);
                stream.Seek(4, SeekOrigin.Current); // CRC
                if (type == "IEND")
                    break;
            }
            return false;
        }

    }
}
