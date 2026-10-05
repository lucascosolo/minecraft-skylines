using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace Skylines.Host.Overlay
{
    /// <summary>The frame a <see cref="SharedOverlayReader"/> currently owns (its front slot).</summary>
    public struct OverlayFrame
    {
        /// <summary>Slot index 0..2.</summary>
        public int Slot;
        /// <summary>Used width in pixels.</summary>
        public int Width;
        /// <summary>Used height in pixels.</summary>
        public int Height;
        /// <summary>Slot flags; bit 0 means rows are stored bottom-up.</summary>
        public uint Flags;
        /// <summary>The guest's frame number.</summary>
        public ulong FrameId;
        /// <summary>First pixel (RGBA8, premultiplied, stride Width*4) inside the mapping; valid until the next Acquire or Close.</summary>
        public IntPtr Pixels;
        /// <summary>Width * Height * 4.</summary>
        public int ByteCount;

        /// <summary>Flags bit 0.</summary>
        public bool RowsBottomUp { get { return (Flags & 1) != 0; } }
    }

    /// <summary>The file is not a valid overlay file (magic, version, sizes or length).</summary>
    public class OverlayFormatException : IOException
    {
        /// <summary>Creates the exception.</summary>
        public OverlayFormatException(string message) : base(message) { }
    }

    /// <summary>
    /// Host side of the shared-memory overlay triple buffer (protocol/minecraft-skylines-v1.md, minor 3).
    /// Maps the guest's file read-write (MAP_SHARED) through libc, owns the front slot, and writes only the
    /// <c>state</c> word, with a 32-bit atomic exchange. Never deletes, truncates or resizes the file.
    /// Not thread-safe: one consumer thread.
    /// </summary>
    public sealed unsafe class SharedOverlayReader : IDisposable
    {
        /// <summary>"MSOV" little-endian.</summary>
        public const uint Magic = 0x564F534D;
        /// <summary>The layout version this reader understands.</summary>
        public const uint LayoutVersion = 1;
        /// <summary>Header and slot headers; slot pixels start here.</summary>
        public const int HeaderBytes = 0x100;
        /// <summary>Bit of <c>state</c> set while the middle slot holds an unread frame.</summary>
        public const int DirtyBit = 4;

        private const uint MaxDimension = 16384;
        private const string Libc = "libc.so.6";
        private const int O_RDWR = 2, O_CLOEXEC = 0x80000, PROT_READ = 1, PROT_WRITE = 2, MAP_SHARED = 1, SEEK_END = 2;

        private byte* _base;
        private int _fd = -1;
        private readonly long _slotBytes;
        private OverlayFrame _front;

        private SharedOverlayReader(string path, int fd, byte* map, long length)
        {
            Path = path;
            _fd = fd;
            _base = map;
            FileLength = length;
            MaxWidth = *(uint*)(map + 0x08);
            MaxHeight = *(uint*)(map + 0x0C);
            SlotCount = *(uint*)(map + 0x10);
            Generation = *(ulong*)(map + 0x20);
            _slotBytes = (long)MaxWidth * MaxHeight * 4;
            FrontSlot = 2;
        }

        /// <summary>The mapped file.</summary>
        public string Path { get; private set; }
        /// <summary>File length at open.</summary>
        public long FileLength { get; private set; }
        /// <summary>Largest frame a slot holds.</summary>
        public uint MaxWidth { get; private set; }
        /// <summary>Largest frame a slot holds.</summary>
        public uint MaxHeight { get; private set; }
        /// <summary>Always 3.</summary>
        public uint SlotCount { get; private set; }
        /// <summary>The file's generation, read at open.</summary>
        public ulong Generation { get; private set; }

        /// <summary>True once the file was re-initialised by the guest (generation changed); the caller should wait for the new offer.</summary>
        public bool Stale { get; private set; }
        /// <summary>The slot the host owns.</summary>
        public int FrontSlot { get; private set; }
        /// <summary>True while the front slot holds a valid frame.</summary>
        public bool HasFrame { get; private set; }
        /// <summary>Frames taken with a valid header.</summary>
        public long FramesAcquired { get; private set; }
        /// <summary>Frames taken whose slot header was invalid.</summary>
        public long FramesRejected { get; private set; }
        /// <summary>True until <see cref="Close"/>.</summary>
        public bool IsOpen { get { return _base != null; } }

        /// <summary>The front frame; meaningful while <see cref="HasFrame"/>.</summary>
        public OverlayFrame Front { get { return _front; } }

        /// <summary>The guest's published-frame counter (live).</summary>
        public ulong FramesPublished
        {
            get
            {
                ThrowIfClosed();
                return (ulong)Interlocked.Read(ref *(long*)(_base + 0x18));
            }
        }

        /// <summary>
        /// Opens and validates the file. Throws <see cref="OverlayFormatException"/> for a file that is not a
        /// valid overlay file and <see cref="IOException"/> when it cannot be opened or mapped.
        /// </summary>
        public static SharedOverlayReader Open(string path)
        {
            byte[] cpath = Encoding.UTF8.GetBytes(path + "\0");
            int fd = open(cpath, O_RDWR | O_CLOEXEC);
            if (fd < 0) throw new IOException("open " + path + " failed, errno " + Marshal.GetLastWin32Error());
            IntPtr map = IntPtr.Zero;
            long length = 0;
            try
            {
                length = lseek(fd, 0, SEEK_END);
                if (length < 0) throw new IOException("lseek " + path + " failed, errno " + Marshal.GetLastWin32Error());
                if (length < HeaderBytes) throw new OverlayFormatException("file is " + length + " bytes, shorter than the 0x100-byte header");
                map = mmap(IntPtr.Zero, new UIntPtr((ulong)length), PROT_READ | PROT_WRITE, MAP_SHARED, fd, IntPtr.Zero);
                if (map == new IntPtr(-1))
                {
                    map = IntPtr.Zero;
                    throw new IOException("mmap " + path + " failed, errno " + Marshal.GetLastWin32Error());
                }
                Validate((byte*)map, length);
                return new SharedOverlayReader(path, fd, (byte*)map, length);
            }
            catch
            {
                if (map != IntPtr.Zero) munmap(map, new UIntPtr((ulong)length));
                close(fd);
                throw;
            }
        }

        /// <summary>
        /// If the middle slot holds an unread frame, swaps it with the front slot. Returns true when the new
        /// front slot holds a valid frame; false when nothing new arrived or the new frame is invalid.
        /// Throws <see cref="OverlayFormatException"/> if the file shrank below the size it was opened with.
        /// </summary>
        public bool Acquire()
        {
            ThrowIfClosed();
            int* state = (int*)(_base + 0x14);
            if ((Interlocked.CompareExchange(ref *state, 0, 0) & DirtyBit) == 0) return false;
            if (lseek(_fd, 0, SEEK_END) < FileLength) throw new OverlayFormatException("file shrank while mapped");
            // The guest reuses one file and re-initialises it (new generation) when it re-offers; until the
            // new OVERLAY_OFFER is processed this mapping's slot bookkeeping is stale, and exchanging our
            // front index into the fresh header would corrupt it. Stop touching state on a mismatch.
            if (Interlocked.Read(ref *(long*)(_base + 0x20)) != (long)Generation)
            {
                Stale = true;
                return false;
            }
            int old = Interlocked.Exchange(ref *state, FrontSlot);
            FrontSlot = old & 3;
            if (FrontSlot > 2) throw new OverlayFormatException("state names slot " + FrontSlot);
            byte* hdr = _base + 0x40 + 0x40 * FrontSlot;
            uint w = *(uint*)hdr, h = *(uint*)(hdr + 4);
            if (w == 0 || h == 0 || w > MaxWidth || h > MaxHeight)
            {
                HasFrame = false;
                FramesRejected++;
                return false;
            }
            _front = new OverlayFrame
            {
                Slot = FrontSlot,
                Width = (int)w,
                Height = (int)h,
                Flags = *(uint*)(hdr + 8),
                FrameId = *(ulong*)(hdr + 16),
                Pixels = (IntPtr)(_base + HeaderBytes + FrontSlot * _slotBytes),
                ByteCount = (int)(w * h * 4),
            };
            HasFrame = true;
            FramesAcquired++;
            return true;
        }

        /// <summary>Copies the front frame's bytes, rows as stored, into <paramref name="dest"/> from index 0.</summary>
        public void CopyFront(byte[] dest)
        {
            ThrowIfClosed();
            if (!HasFrame) throw new InvalidOperationException("no frame acquired");
            if (dest == null || dest.Length < _front.ByteCount) throw new ArgumentException("destination holds fewer than " + _front.ByteCount + " bytes");
            Marshal.Copy(_front.Pixels, dest, 0, _front.ByteCount);
        }

        /// <summary>Unmaps the file and closes the descriptor; the file itself is left untouched. Idempotent.</summary>
        public void Close()
        {
            if (_base == null) return;
            munmap((IntPtr)_base, new UIntPtr((ulong)FileLength));
            _base = null;
            close(_fd);
            _fd = -1;
            HasFrame = false;
        }

        /// <summary>Same as <see cref="Close"/>.</summary>
        public void Dispose()
        {
            Close();
        }

        private static void Validate(byte* p, long length)
        {
            uint magic = *(uint*)p, version = *(uint*)(p + 4), w = *(uint*)(p + 8), h = *(uint*)(p + 12), slots = *(uint*)(p + 16);
            if (magic != Magic) throw new OverlayFormatException("bad magic 0x" + magic.ToString("x8"));
            if (version != LayoutVersion) throw new OverlayFormatException("layout version " + version + " (expected " + LayoutVersion + ")");
            if (slots != 3) throw new OverlayFormatException("slot count " + slots + " (expected 3)");
            if (w == 0 || h == 0 || w > MaxDimension || h > MaxDimension) throw new OverlayFormatException("bad max size " + w + "x" + h);
            long need = HeaderBytes + 3L * w * h * 4;
            if (length < need) throw new OverlayFormatException("file is " + length + " bytes, layout needs " + need);
        }

        private void ThrowIfClosed()
        {
            if (_base == null) throw new ObjectDisposedException("SharedOverlayReader");
        }

        [DllImport(Libc, SetLastError = true)]
        private static extern int open(byte[] path, int flags);

        [DllImport(Libc, SetLastError = true)]
        private static extern int close(int fd);

        [DllImport(Libc, SetLastError = true)]
        private static extern long lseek(int fd, long offset, int whence);

        [DllImport(Libc, SetLastError = true)]
        private static extern IntPtr mmap(IntPtr addr, UIntPtr length, int prot, int flags, int fd, IntPtr offset);

        [DllImport(Libc, SetLastError = true)]
        private static extern int munmap(IntPtr addr, UIntPtr length);
    }
}
