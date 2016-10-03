namespace PN532.Enums
{
    /// <summary>HSU frame parser state</summary>
    internal enum HsuParserState
    {
        Preamble,
        StartCode,
        Length,
        LengthChecksum,
        FrameIdentifierAndData,
        DataChecksum,
        Postamble,
        AckCode
    }
}