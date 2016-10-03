namespace PN532.TagConnections
{
    using Interfaces;

    /// <summary>Abstract class for connection to NFC tag</summary>
    public abstract class NfcTagConnection
    {
        /// <summary>The reference to NFC reader</summary>
        protected INfcReader Reader;

        /// <summary>The NFC tag identifier</summary>
        /// <value>The identifier.</value>
        public byte[] Id { get; set; }
    }
}