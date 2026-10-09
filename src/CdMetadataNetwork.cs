using System;
using System.Net;

namespace DJLibrary
{
    internal static class CdMetadataNetwork
    {
        private const SecurityProtocolType Tls12 = (SecurityProtocolType)3072;

        public static void EnsureModernTls()
        {
            ServicePointManager.SecurityProtocol = ServicePointManager.SecurityProtocol | Tls12;
        }

        internal static string RunSelfTest()
        {
            EnsureModernTls();
            if ((((int)ServicePointManager.SecurityProtocol) & (int)Tls12) != (int)Tls12)
                throw new InvalidOperationException("Metadaten-Netzwerk: TLS 1.2 wurde nicht aktiviert.");
            return "metadata HTTPS TLS 1.2 bootstrap";
        }
    }
}
