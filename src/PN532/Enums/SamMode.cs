namespace PN532.Enums
{
    /// <summary>Way to use SAM (Security Access Module)</summary>
    public enum SamMode : byte
    {
        /// <summary>SAM not used</summary>
        NormalMode = 0x01,

        VirtualCard = 0x02,
        WiredCard = 0x03,
        DualMode = 0x04
    }
}