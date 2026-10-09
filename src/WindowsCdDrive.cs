using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace DJLibrary
{
    public sealed class WindowsCdDrive : ICdDrive
    {
        private const uint GENERIC_READ = 0x80000000;
        private const uint FILE_SHARE_READ = 0x00000001;
        private const uint FILE_SHARE_WRITE = 0x00000002;
        private const uint OPEN_EXISTING = 3;
        private const uint IOCTL_CDROM_READ_TOC = 0x00024000;
        private const uint IOCTL_CDROM_READ_TOC_EX = 0x00024054;

        private readonly string _root;
        public WindowsCdDrive(string root)
        {
            if (String.IsNullOrWhiteSpace(root)) throw new ArgumentException("Drive is missing.");
            _root = root.EndsWith("\\") ? root.Substring(0, root.Length - 1) : root;
        }

        public string DisplayName { get { return _root; } }

        public static List<WindowsCdDrive> Enumerate()
        {
            List<WindowsCdDrive> result = new List<WindowsCdDrive>();
            foreach (DriveInfo drive in DriveInfo.GetDrives())
                if (drive.DriveType == DriveType.CDRom) result.Add(new WindowsCdDrive(drive.Name));
            return result;
        }

        public CdSnapshot Capture()
        {
            string device = @"\\.\" + _root.TrimEnd('\\');
            using (SafeFileHandle handle = Native.CreateFile(device, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE,
                IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero))
            {
                if (handle.IsInvalid) throw new IOException("CD-Laufwerk konnte nicht geöffnet werden: " + _root,
                    Marshal.GetExceptionForHR(Marshal.GetHRForLastWin32Error()));

                byte[] toc = new byte[804];
                int returned;
                if (!Native.DeviceIoControl(handle, IOCTL_CDROM_READ_TOC, IntPtr.Zero, 0, toc, toc.Length, out returned, IntPtr.Zero))
                    throw new IOException("CD-TOC konnte nicht gelesen werden. Win32=" + Marshal.GetLastWin32Error());

                CdSnapshot snapshot = ParseToc(toc, returned);
                snapshot.DriveId = _root;

                try
                {
                    Dictionary<int, string> titles;
                    Dictionary<int, string> performers;
                    snapshot.CdTextStatus = ReadCdTextStatus(handle, out titles, out performers);
                    snapshot.CdTextPresent = snapshot.CdTextStatus == "drive_present";
                    if (snapshot.CdTextPresent) snapshot.CdTextSource = "Windows CDROM READ_TOC_EX";
                    if (!snapshot.CdTextPresent)
                    {
                        Dictionary<int, string> fallbackTitles;
                        Dictionary<int, string> fallbackPerformers;
                        string fallbackError;
                        if (WindowsCdTextFallback.TryRead(_root, out fallbackTitles, out fallbackPerformers, out fallbackError))
                        {
                            titles = fallbackTitles;
                            performers = fallbackPerformers;
                            snapshot.CdTextPresent = true;
                            snapshot.CdTextStatus = "drive_present";
                            snapshot.CdTextSource = "MMC/SCSI READ TOC format 5";
                        }
                        else if (!String.IsNullOrWhiteSpace(fallbackError)) snapshot.CdTextError = fallbackError;
                    }
                    if (snapshot.CdTextPresent)
                    {
                        string value;
                        if (titles.TryGetValue(0, out value)) snapshot.Album = value;
                        if (performers.TryGetValue(0, out value)) snapshot.AlbumArtist = value;
                        foreach (CdTrackCapture track in snapshot.Tracks)
                        {
                            if (titles.TryGetValue(track.Position, out value)) track.Title = value;
                            if (performers.TryGetValue(track.Position, out value)) track.Artist = value;
                        }
                    }
                }
                catch
                {
                    // TOC remains authoritative. CD-TEXT is optional; a failed/unsupported CD-TEXT
                    // query must never invent metadata or invalidate an otherwise readable audio CD.
                    snapshot.CdTextPresent = false;
                    snapshot.CdTextStatus = "drive_read_error";
                    snapshot.CdTextError = "CD-TEXT-Auswertung ist fehlgeschlagen.";
                }
                return snapshot;
            }
        }

        internal static CdSnapshot ParseToc(byte[] buffer, int length)
        {
            if (buffer == null || length < 20) throw new InvalidDataException("CD-TOC ist zu kurz.");
            int first = buffer[2];
            int last = buffer[3];
            if (first <= 0 || last < first || last - first > 99) throw new InvalidDataException("CD TOC track range is invalid.");
            int trackCount = last - first + 1;
            int needed = 4 + (trackCount + 1) * 8;
            if (length < needed) throw new InvalidDataException("CD-TOC enthält keinen vollständigen Lead-out.");

            List<int> frames = new List<int>();
            for (int i = 0; i <= trackCount; i++)
            {
                int offset = 4 + i * 8;
                int m = buffer[offset + 5];
                int s = buffer[offset + 6];
                int f = buffer[offset + 7];
                if (s >= 60 || f >= 75) throw new InvalidDataException("CD-TOC enthält ungültige MSF-Werte.");
                frames.Add(((m * 60) + s) * 75 + f);
            }
            for (int i = 1; i < frames.Count; i++)
                if (frames[i] < frames[i - 1]) throw new InvalidDataException("CD-TOC ist nicht monoton.");

            CdSnapshot snapshot = new CdSnapshot();
            snapshot.Toc = String.Join(" ", frames.Select(x => x.ToString()).ToArray());
            for (int i = 0; i < trackCount; i++)
            {
                snapshot.Tracks.Add(new CdTrackCapture
                {
                    Position = i + 1,
                    DurationSeconds = Math.Max(0, (frames[i + 1] - frames[i]) / 75.0),
                    Artist = "",
                    Title = ""
                });
            }
            return snapshot;
        }

        private static string ReadCdTextStatus(SafeFileHandle handle, out Dictionary<int, string> titles, out Dictionary<int, string> performers)
        {
            titles = new Dictionary<int, string>();
            performers = new Dictionary<int, string>();

            byte[] input = new byte[4];
            input[0] = 0x05; // CDROM_READ_TOC_EX_FORMAT_CDTEXT, LBA mode
            GCHandle pin = GCHandle.Alloc(input, GCHandleType.Pinned);
            try
            {
                byte[] output = new byte[65536];
                int returned;
                if (!Native.DeviceIoControl(handle, IOCTL_CDROM_READ_TOC_EX, pin.AddrOfPinnedObject(), input.Length,
                    output, output.Length, out returned, IntPtr.Zero)) return "drive_unavailable";
                if (returned < 4) return "drive_absent";
                int payloadLength = (output[0] << 8) | output[1];
                int total = Math.Min(returned, payloadLength + 2);
                if (total <= 4) return "drive_absent";

                List<CdTextPack> titlePacks = new List<CdTextPack>();
                List<CdTextPack> performerPacks = new List<CdTextPack>();
                for (int offset = 4; offset + 18 <= total; offset += 18)
                {
                    CdTextPack pack = new CdTextPack();
                    pack.Type = output[offset];
                    pack.Track = output[offset + 1];
                    pack.Sequence = output[offset + 2];
                    pack.Flags = output[offset + 3];
                    pack.Text = new byte[12];
                    Buffer.BlockCopy(output, offset + 4, pack.Text, 0, 12);
                    if (pack.Type == 0x80) titlePacks.Add(pack);
                    else if (pack.Type == 0x81) performerPacks.Add(pack);
                }

                DecodePacks(titlePacks, titles);
                DecodePacks(performerPacks, performers);
                return titles.Count > 0 || performers.Count > 0 ? "drive_present" : "drive_absent";
            }
            finally { pin.Free(); }
        }

        private static void DecodePacks(List<CdTextPack> packs, Dictionary<int, string> output)
        {
            if (packs.Count == 0) return;
            packs.Sort(delegate(CdTextPack a, CdTextPack b) { return a.Sequence.CompareTo(b.Sequence); });
            List<byte> pending = new List<byte>();
            int currentTrack = packs[0].Track;
            foreach (CdTextPack pack in packs)
            {
                if (pending.Count == 0 && pack.Track != 0) currentTrack = pack.Track;
                foreach (byte b in pack.Text)
                {
                    if (b == 0)
                    {
                        if (pending.Count > 0)
                        {
                            string text = DecodeCdText(pending.ToArray()).Trim();
                            if (!String.IsNullOrEmpty(text)) output[currentTrack] = text;
                            pending.Clear();
                        }
                        currentTrack++;
                    }
                    else pending.Add(b);
                }
            }
            if (pending.Count > 0)
            {
                string text = DecodeCdText(pending.ToArray()).Trim();
                if (!String.IsNullOrEmpty(text)) output[currentTrack] = text;
            }
        }

        private static string DecodeCdText(byte[] bytes)
        {
            try { return Encoding.GetEncoding(28591).GetString(bytes); }
            catch { return Encoding.UTF8.GetString(bytes); }
        }

        private sealed class CdTextPack
        {
            public byte Type;
            public byte Track;
            public byte Sequence;
            public byte Flags;
            public byte[] Text;
        }

        private static class Native
        {
            [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            internal static extern SafeFileHandle CreateFile(string fileName, uint desiredAccess, uint shareMode,
                IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool DeviceIoControl(SafeFileHandle device, uint controlCode,
                IntPtr inputBuffer, int inputSize, [Out] byte[] outputBuffer, int outputSize,
                out int bytesReturned, IntPtr overlapped);
        }
    }
}
