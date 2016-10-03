namespace PN532.Interfaces
{
    using System;
    using Enums;

    // Delegate for handling events tag related (detected and lost)
    public delegate void TagEventHandler(object sender, NfcTagEventArgs e);

    /// <summary>Interface for NFC reader</summary>
    public interface INfcReader
    {
        /// <summary>Open NFC reader to detect tag</summary>
        /// <param name="nfcTagType">Type of the NFC tag.</param>
        void Open(NfcTagType nfcTagType);

        /// <summary>Close NFC reader</summary>
        void Close();

        /// <summary>Write and read response to/from NFC reader</summary>
        /// <param name="data">Data to write to NFC reader</param>
        /// <returns>Response received from NFC reader</returns>
        byte[] WriteRead(byte[] data);

        /// <summary>Event for detected tag</summary>
        event TagEventHandler TagDetected;

        /// <summary>Event for lost tag</summary>
        event TagEventHandler TagLost;
    }
}