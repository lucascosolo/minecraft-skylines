using System;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Threading;

namespace Skylines.Host.Tests
{
    /// <summary>Test double for the guest side of the overlay protocol; shares memory through the same file.</summary>
    internal sealed unsafe class GuestWriter : IDisposable
    {
        private readonly MemoryMappedFile _file;
        private readonly MemoryMappedViewAccessor _view;
        private byte* _base;
        private readonly uint _maxW;
        private readonly uint _maxH;

        public int Back { get; private set; }
        public ulong FramesPublished { get; private set; }

        public static string NewTempDir()
        {
            string dir = Path.Combine(Path.GetTempPath(), "mcskylines-overlay-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        public static long FileSize(uint maxW, uint maxH) { return 0x100 + 3L * maxW * maxH * 4; }

        public GuestWriter(string path, uint maxW, uint maxH, ulong generation = 1)
        {
            _maxW = maxW;
            _maxH = maxH;
            using (var fs = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.ReadWrite))
                fs.SetLength(FileSize(maxW, maxH));
            _file = MemoryMappedFile.CreateFromFile(path, FileMode.Open, null, 0, MemoryMappedFileAccess.ReadWrite);
            _view = _file.CreateViewAccessor(0, 0, MemoryMappedFileAccess.ReadWrite);
            _view.SafeMemoryMappedViewHandle.AcquirePointer(ref _base);
            *(uint*)(_base + 0x00) = 0x564F534D;
            *(uint*)(_base + 0x04) = 1;
            *(uint*)(_base + 0x08) = maxW;
            *(uint*)(_base + 0x0C) = maxH;
            *(uint*)(_base + 0x10) = 3;
            *(uint*)(_base + 0x14) = 1;
            *(ulong*)(_base + 0x18) = 0;
            *(ulong*)(_base + 0x20) = generation;
            Back = 0;
        }

        public int Middle { get { return (int)(Volatile.Read(ref *(int*)(_base + 0x14)) & 3); } }

        public void Publish(uint width, uint height, uint flags, ulong frameId, byte[] pixels)
        {
            long slotBytes = (long)_maxW * _maxH * 4;
            byte* px = _base + 0x100 + Back * slotBytes;
            long n = Math.Min(pixels.Length, slotBytes);
            for (long i = 0; i < n; i++) px[i] = pixels[i];
            byte* hdr = _base + 0x40 + 0x40 * Back;
            *(uint*)(hdr + 0) = width;
            *(uint*)(hdr + 4) = height;
            *(uint*)(hdr + 8) = flags;
            *(uint*)(hdr + 12) = 0;
            *(ulong*)(hdr + 16) = frameId;
            FramesPublished++;
            Volatile.Write(ref *(ulong*)(_base + 0x18), FramesPublished);
            int old = Interlocked.Exchange(ref *(int*)(_base + 0x14), Back | 4);
            Back = old & 3;
        }

        public void Dispose()
        {
            if (_base != null) { _view.SafeMemoryMappedViewHandle.ReleasePointer(); _base = null; }
            _view.Dispose();
            _file.Dispose();
        }
    }
}
