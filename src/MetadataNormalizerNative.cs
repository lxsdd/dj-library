using System;
using System.Runtime.InteropServices;
using System.Text;

namespace DJLibrary
{
    // Read-only boundary to the single shared DJ Metadata Normalizer engine.
    // This adapter analyzes Bridge metadata only; it has no file/tag write API.
    internal static class MetadataNormalizerNative
    {
        private const string DllName = "djmeta_native.dll";
        internal const uint ExpectedAbiVersion = 1;

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern uint djmeta_abi_version();

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int djmeta_analyze_json_v1(
            byte[] metadataVectorsJsonUtf8,
            byte[] rulesetJsonUtf8,
            out IntPtr analysisJsonUtf8,
            out IntPtr errorUtf8);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern void djmeta_free_string_v1(IntPtr value);

        internal static bool IsAbiCompatible()
        {
            return djmeta_abi_version() == ExpectedAbiVersion;
        }

        internal static string Analyze(string metadataVectorsJson, string rulesetJson)
        {
            if (metadataVectorsJson == null) throw new ArgumentNullException("metadataVectorsJson");
            if (rulesetJson == null) throw new ArgumentNullException("rulesetJson");
            if (!IsAbiCompatible())
                throw new InvalidOperationException("Unsupported DJ Metadata Normalizer native ABI.");

            IntPtr analysis = IntPtr.Zero;
            IntPtr error = IntPtr.Zero;
            try
            {
                int status = djmeta_analyze_json_v1(
                    Utf8Z(metadataVectorsJson), Utf8Z(rulesetJson), out analysis, out error);
                if (status != 0)
                    throw new InvalidOperationException(
                        "DJ Metadata Normalizer analysis failed (" + status + "): " +
                        (error == IntPtr.Zero ? "unknown error" : PtrToUtf8(error)));
                if (analysis == IntPtr.Zero)
                    throw new InvalidOperationException("DJ Metadata Normalizer returned no analysis.");
                return PtrToUtf8(analysis);
            }
            finally
            {
                if (analysis != IntPtr.Zero) djmeta_free_string_v1(analysis);
                if (error != IntPtr.Zero) djmeta_free_string_v1(error);
            }
        }

        private static byte[] Utf8Z(string value)
        {
            byte[] raw = Encoding.UTF8.GetBytes(value);
            byte[] terminated = new byte[raw.Length + 1];
            Buffer.BlockCopy(raw, 0, terminated, 0, raw.Length);
            return terminated;
        }

        private static string PtrToUtf8(IntPtr value)
        {
            int length = 0;
            while (Marshal.ReadByte(value, length) != 0) length++;
            byte[] bytes = new byte[length];
            Marshal.Copy(value, bytes, 0, length);
            return Encoding.UTF8.GetString(bytes);
        }
    }
}
