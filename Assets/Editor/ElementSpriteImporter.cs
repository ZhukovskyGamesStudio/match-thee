using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;

// Спрайты элементов мира: пиксель-арт 24x24, три кадра дрожания в одной полоске (72x24).
// При импорте выставляет Point-фильтр и PPU и режет полоску на кадры <name>_0, _1, _2.
public class ElementSpriteImporter : AssetPostprocessor {
    private const string ElementsFolder = "Assets/Sprites/Elements/";
    private const int FrameSize = 24;

    // Если полоски успели импортироваться раньше, чем скомпилировался этот скрипт, дожимаем их.
    [InitializeOnLoadMethod]
    private static void ReimportUnslicedElements() {
        foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { ElementsFolder.TrimEnd('/') })) {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (AssetImporter.GetAtPath(path) is TextureImporter importer
                && (importer.spriteImportMode != SpriteImportMode.Multiple || importer.filterMode != FilterMode.Point)) {
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            }
        }
    }

    private void OnPreprocessTexture() {
        if (!assetPath.StartsWith(ElementsFolder) || !assetPath.EndsWith(".png")) {
            return;
        }

        TextureImporter importer = (TextureImporter)assetImporter;
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Multiple;
        importer.spritePixelsPerUnit = FrameSize;
        importer.filterMode = FilterMode.Point;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.mipmapEnabled = false;
        importer.alphaIsTransparency = true;

        SliceFrames(importer);
    }

    private void SliceFrames(TextureImporter importer) {
        if (!TryReadPngSize(assetPath, out int width, out int height)) {
            return;
        }

        int frames = Mathf.Max(1, width / FrameSize);
        string baseName = Path.GetFileNameWithoutExtension(assetPath);

        SpriteDataProviderFactories factory = new();
        factory.Init();
        ISpriteEditorDataProvider provider = factory.GetSpriteEditorDataProviderFromObject(importer);
        provider.InitSpriteEditorDataProvider();

        // Сохраняем id уже нарезанных кадров, чтобы ссылки на спрайты не ломались при реимпорте.
        Dictionary<string, GUID> existingIds = new();
        foreach (SpriteRect existing in provider.GetSpriteRects()) {
            existingIds[existing.name] = existing.spriteID;
        }

        SpriteRect[] rects = new SpriteRect[frames];
        for (int i = 0; i < frames; i++) {
            string spriteName = $"{baseName}_{i}";
            rects[i] = new SpriteRect {
                name = spriteName,
                rect = new Rect(i * FrameSize, 0, FrameSize, height),
                alignment = SpriteAlignment.Center,
                pivot = new Vector2(0.5f, 0.5f),
                spriteID = existingIds.TryGetValue(spriteName, out GUID id) ? id : StableGuid(assetPath + spriteName)
            };
        }

        provider.SetSpriteRects(rects);
        provider.Apply();
    }

    private static GUID StableGuid(string key) {
        using MD5 md5 = MD5.Create();
        byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes(key));
        StringBuilder hex = new(32);
        foreach (byte b in hash) {
            hex.Append(b.ToString("x2"));
        }

        return new GUID(hex.ToString());
    }

    // Размер берём из заголовка PNG: до импорта у Unity ещё нет текстуры.
    private static bool TryReadPngSize(string path, out int width, out int height) {
        width = height = 0;
        byte[] header = new byte[24];
        using FileStream stream = File.OpenRead(path);
        if (stream.Read(header, 0, header.Length) < header.Length) {
            return false;
        }

        width = (header[16] << 24) | (header[17] << 16) | (header[18] << 8) | header[19];
        height = (header[20] << 24) | (header[21] << 16) | (header[22] << 8) | header[23];
        return width > 0 && height > 0;
    }
}
