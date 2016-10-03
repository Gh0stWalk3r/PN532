namespace PN532.Enums
{
    /// <summary>Target type (baud rate)</summary>
    public enum TargetType : byte
    {
        /// <summary>106 kbps type A (ISO/IEC14443 Type A)</summary>
        Iso14443TypeA = 0x00,

        /// <summary>212 kbps (FeliCa polling)</summary>
        Felica212 = 0x01,

        /// <summary>424 kbps (FeliCa polling)</summary>
        Felica424 = 0x02,

        /// <summary>106 kbps type B (ISO/IEC14443-3B)</summary>
        Iso14443TypeB = 0x03,

        /// <summary>106 kbps Innovision Jewel tag</summary>
        Jewel = 0x04
    }
}