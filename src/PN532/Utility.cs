namespace PN532
{
    using System.Text;

    internal static class Utility
    {
        private const string HexChars = "0123456789ABCDEF";

        /// <summary>
        /// Reverse bits order inside a byte (MSB to LSB and viceversa)
        /// </summary>
        /// <param name="value">Byte value to reverse</param>
        /// <returns>Byte value after reverse</returns>
        internal static byte ReverseBits(byte value)
        {
            byte reversed = 0x00;

            int i = 7, j = 0;

            while (i >= 0)
            {
                reversed |= (byte)(((value >> i) & 0x01) << j);
                i--;
                j++;
            }

            return reversed;
        }

        /// <summary>
        /// Reverse bits order of all a byte (MSB to LSB and viceversa) inside array
        /// </summary>
        /// <param name="data">Byte array to reverse order</param>
        /// <returns>Byte array reversed</returns>
        internal static byte[] ReverseBytes(byte[] data)
        {
            var reversed = new byte[data.Length];

            for (var i = 0; i < data.Length; i++)
                reversed[i] = ReverseBits(data[i]);

            return reversed;
        }

        /// <summary>
        /// Convert hex byte array in a hex string
        /// </summary>
        /// <param name="value">Byte array with hex values</param>
        /// <returns>Hex string</returns>
        internal static string HexToString(byte[] value)
        {
            var hexString = new StringBuilder();
            for (var i = 0; i < value.Length; i++)
            {
                hexString.Append(HexChars[(value[i] >> 4) & 0x0F]);
                hexString.Append(HexChars[value[i] & 0x0F]);
            }

            return hexString.ToString();
        }
    }
}