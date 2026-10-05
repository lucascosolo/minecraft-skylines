using System;
using System.IO;
using System.Text.Json;

namespace Skylines.Bridge.Tests
{
    /// <summary>Locates and loads protocol/vectors/*.json by walking up from the test binary.</summary>
    public static class VectorFiles
    {
        public static JsonDocument Load(string name)
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                string candidate = Path.Combine(dir.FullName, "protocol", "vectors", name);
                if (File.Exists(candidate)) return JsonDocument.Parse(File.ReadAllText(candidate));
                dir = dir.Parent;
            }
            throw new FileNotFoundException("protocol/vectors/" + name + " not found above " + AppContext.BaseDirectory);
        }

        public static byte[] Hex(string hex)
        {
            var b = new byte[hex.Length / 2];
            for (int i = 0; i < b.Length; i++) b[i] = Convert.ToByte(hex.Substring(2 * i, 2), 16);
            return b;
        }

        public static string ToHex(byte[] b)
        {
            return Convert.ToHexString(b).ToLowerInvariant();
        }
    }
}
