using UnityEngine;

namespace Skylines.Host.Rendering
{
    /// <summary>Texture creation for streamed images. Every Unity object made here is the caller's to destroy (<see cref="Replace{T}"/>).</summary>
    public static class TextureUtil
    {
        /// <summary>
        /// Decodes a PNG (Texture2D.LoadImage) into an sRGB texture with point filtering, clamp and no mipmaps:
        /// a texture atlas packs sprites edge to edge, so mip levels and bilinear taps would blend neighbouring
        /// sprites, and the source art is meant to be seen as pixels. LoadImage puts the image's top row at v = 1.
        /// Returns null (nothing leaked) if the data is not a decodable image.
        /// </summary>
        public static Texture2D LoadPng(byte[] png, string name)
        {
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!t.LoadImage(png))
            {
                Object.Destroy(t);
                return null;
            }
            t.name = name;
            t.filterMode = FilterMode.Point;
            t.wrapMode = TextureWrapMode.Clamp;
            t.anisoLevel = 0;
            return t;
        }

        /// <summary>A 4x4 texture of one colour (for shader maps that must be bound but carry no information).</summary>
        public static Texture2D Solid(Color32 colour, bool linear, string name)
        {
            var t = new Texture2D(4, 4, TextureFormat.RGBA32, false, linear);
            var px = new Color32[16];
            for (int i = 0; i < px.Length; i++) px[i] = colour;
            t.SetPixels32(px);
            t.Apply(false, true);
            t.name = name;
            t.filterMode = FilterMode.Point;
            t.wrapMode = TextureWrapMode.Clamp;
            return t;
        }

        /// <summary>Stores <paramref name="value"/> in <paramref name="slot"/>, destroying the object it replaces.</summary>
        public static void Replace<T>(ref T slot, T value) where T : Object
        {
            if (slot != null && slot != value) Object.Destroy(slot);
            slot = value;
        }
    }
}
