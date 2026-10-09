using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace DJLibrary
{
    // MMC fallback for drives whose Windows CDROM class driver does not expose CD-TEXT
    // through IOCTL_CDROM_READ_TOC_EX even though READ TOC/PMA/ATIP format 5 works.
    internal static class WindowsCdTextFallback
    {
        private const uint GENERIC_READ = 0x80000000;
        private const uint GENERIC_WRITE = 0x40000000;
        private const uint FILE_SHARE_READ = 0x00000001;
        private const uint FILE_SHARE_WRITE = 0x00000002;
        private const uint OPEN_EXISTING = 3;
        private const uint IOCTL_SCSI_PASS_THROUGH = 0x0004D004;
        private const byte SCSI_IOCTL_DATA_IN = 1;
        private const int SenseLength = 32;
        private const int DataLength = 65535;

        [StructLayout(LayoutKind.Sequential)]
        private struct ScsiPassThrough
        {
            public ushort Length;
            public byte ScsiStatus;
            public byte PathId;
            public byte TargetId;
            public byte Lun;
            public byte CdbLength;
            public byte SenseInfoLength;
            public byte DataIn;
            public uint DataTransferLength;
            public uint TimeOutValue;
            public UIntPtr DataBufferOffset;
            public uint SenseInfoOffset;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
            public byte[] Cdb;
        }

        public static bool TryRead(string driveId, out Dictionary<int, string> titles, out Dictionary<int, string> performers, out string error)
        {
            titles = new Dictionary<int, string>();
            performers = new Dictionary<int, string>();
            error = "";
            if (String.IsNullOrWhiteSpace(driveId)) { error = "Drive is missing"; return false; }

            string root = driveId.TrimEnd('\\');
            string device = @"\\.\" + root;
            using (SafeFileHandle handle = Native.CreateFile(device, GENERIC_READ | GENERIC_WRITE,
                FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero))
            {
                if (handle.IsInvalid)
                {
                    // Some optical stacks deny GENERIC_WRITE although pass-through itself is read-only.
                    using (SafeFileHandle readOnly = Native.CreateFile(device, GENERIC_READ,
                        FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero))
                    {
                        if (readOnly.IsInvalid) { error = "SCSI-Zugriff auf Laufwerk fehlgeschlagen"; return false; }
                        return TryReadHandle(readOnly, titles, performers, out error);
                    }
                }
                return TryReadHandle(handle, titles, performers, out error);
            }
        }

        private static bool TryReadHandle(SafeFileHandle handle, Dictionary<int, string> titles, Dictionary<int, string> performers, out string error)
        {
            error = "";
            int headerSize = Marshal.SizeOf(typeof(ScsiPassThrough));
            int senseOffset = headerSize;
            int dataOffset = senseOffset + SenseLength;
            int totalSize = dataOffset + DataLength;
            IntPtr buffer = Marshal.AllocHGlobal(totalSize);
            try
            {
                for (int i = 0; i < totalSize; i++) Marshal.WriteByte(buffer, i, 0);
                ScsiPassThrough spt = new ScsiPassThrough();
                spt.Length = (ushort)headerSize;
                spt.CdbLength = 10;
                spt.SenseInfoLength = SenseLength;
                spt.DataIn = SCSI_IOCTL_DATA_IN;
                spt.DataTransferLength = DataLength;
                spt.TimeOutValue = 10;
                spt.DataBufferOffset = (UIntPtr)(ulong)dataOffset;
                spt.SenseInfoOffset = (uint)senseOffset;
                spt.Cdb = new byte[16];
                spt.Cdb[0] = 0x43; // READ TOC/PMA/ATIP
                spt.Cdb[2] = 0x05; // format 5 = CD-TEXT
                spt.Cdb[7] = (byte)(DataLength >> 8);
                spt.Cdb[8] = (byte)(DataLength & 0xff);
                Marshal.StructureToPtr(spt, buffer, false);

                int returned;
                if (!Native.DeviceIoControl(handle, IOCTL_SCSI_PASS_THROUGH, buffer, totalSize, buffer, totalSize, out returned, IntPtr.Zero))
                {
                    error = "SCSI READ TOC fehlgeschlagen (Win32=" + Marshal.GetLastWin32Error().ToString() + ")";
                    return false;
                }

                ScsiPassThrough result = (ScsiPassThrough)Marshal.PtrToStructure(buffer, typeof(ScsiPassThrough));
                if (result.ScsiStatus != 0)
                {
                    error = "SCSI-Status 0x" + result.ScsiStatus.ToString("X2");
                    return false;
                }

                byte[] data = new byte[DataLength];
                Marshal.Copy(IntPtr.Add(buffer, dataOffset), data, 0, data.Length);
                return DecodeResponse(data, titles, performers, out error);
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }

        internal static bool DecodeResponse(byte[] data, Dictionary<int, string> titles, Dictionary<int, string> performers, out string error)
        {
            error = "";
            if (data == null || data.Length < 4) { error = "CD-TEXT-Antwort zu kurz"; return false; }
            int payloadLength = (data[0] << 8) | data[1];
            int total = Math.Min(data.Length, payloadLength + 2);
            if (total <= 4) { error = "Kein CD-TEXT-Payload"; return false; }

            List<CdTextPack> titlePacks = new List<CdTextPack>();
            List<CdTextPack> performerPacks = new List<CdTextPack>();
            for (int offset = 4; offset + 18 <= total; offset += 18)
            {
                CdTextPack pack = new CdTextPack();
                pack.Type = data[offset];
                pack.Track = data[offset + 1];
                pack.Sequence = data[offset + 2];
                pack.Text = new byte[12];
                Buffer.BlockCopy(data, offset + 4, pack.Text, 0, 12);
                if (pack.Type == 0x80) titlePacks.Add(pack);
                else if (pack.Type == 0x81) performerPacks.Add(pack);
            }
            DecodePacks(titlePacks, titles);
            DecodePacks(performerPacks, performers);
            if (titles.Count == 0 && performers.Count == 0) { error = "Keine TITLE/PERFORMER-Packs"; return false; }
            return true;
        }

        private static void DecodePacks(List<CdTextPack> packs, Dictionary<int, string> output)
        {
            if (packs.Count == 0) return;
            packs.Sort(delegate(CdTextPack a, CdTextPack b) { return a.Sequence.CompareTo(b.Sequence); });
            List<byte> pending = new List<byte>();
            int currentTrack = packs[0].Track;
            foreach (CdTextPack pack in packs)
            {
                if (pending.Count == 0) currentTrack = pack.Track;
                foreach (byte b in pack.Text)
                {
                    if (b == 0)
                    {
                        string text = DecodeText(pending.ToArray()).Trim();
                        if (!String.IsNullOrEmpty(text)) output[currentTrack] = text;
                        pending.Clear();
                        currentTrack++;
                    }
                    else pending.Add(b);
                }
            }
            if (pending.Count > 0)
            {
                string text = DecodeText(pending.ToArray()).Trim();
                if (!String.IsNullOrEmpty(text)) output[currentTrack] = text;
            }
        }

        private static string DecodeText(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return "";
            try { return Encoding.GetEncoding(28591).GetString(bytes); }
            catch { return Encoding.UTF8.GetString(bytes); }
        }

        private sealed class CdTextPack
        {
            public byte Type;
            public byte Track;
            public byte Sequence;
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
                IntPtr inputBuffer, int inputSize, IntPtr outputBuffer, int outputSize,
                out int bytesReturned, IntPtr overlapped);
        }
    }
}
