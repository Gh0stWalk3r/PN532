namespace PN532.Interfaces
{
    using System;

    /// <summary>Interface for communication layer with PN532 (eg. HSU (High Speed UART), I2C or SPI)</summary>
    public interface ICommLayer : IDisposable
    {
        /// <summary>Send a normal frame to PN532 (PN532 User Manual Rev. 02, page 28)</summary>
        /// <param name="frame">Frame bytes to send</param>
        bool SendNormalFrame(byte[] frame);

        /// <summary>Read a normal frame from PN532 (PN532 User Manual Rev. 02, page 28)</summary>
        /// <returns>Normal frame read (or null for timeout)</returns>
        byte[] ReadNormalFrame();

        /// <summary>Execute wake up procedure for PN532 (PN532 User Manual Rev. 02, from page 99)</summary>
        void WakeUp();
    }
}