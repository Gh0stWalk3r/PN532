namespace PN532
{
    using System;
    using Enums;
    using TagConnections;

    public class NfcTagEventArgs : EventArgs
    {
        /// <summary>NFC tag type</summary>
        public NfcTagType NfcTagType { get; private set; }

        /// <summary>Connection instance to NFC tag</summary>
        public NfcTagConnection Connection { get; private set; }

        /// <summary>Initializes a new instance of the <see cref="NfcTagEventArgs"/> class.</summary>
        /// <param name="nfcTagType">NFC tag type</param>
        /// <param name="conn">Connection instance to NFC tag</param>
        public NfcTagEventArgs(NfcTagType nfcTagType, NfcTagConnection conn)
        {
            this.NfcTagType = nfcTagType;
            this.Connection = conn;
        }
    }
}