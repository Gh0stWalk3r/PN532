namespace PN532.Communication
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Windows.Devices.Gpio;
    using Windows.Devices.I2c;
    using Interfaces;

    /// <summary>I2C communication layer</summary>
    public class I2cCommunication : ICommLayer
    {
        #region I2C Constants ...

        // NOTE : in pn532um.pdf on pag. 43 the following addresses are reported :
        //        Write = 0x48, Read = 0x49
        //        These addresses already consider the last bit of I2C (0 = W, 1 = R) but the address of
        //        a I2C device is 7 bit so we have to consider only the first 7 bit -> 0x24 is PN532 unique address
        private const ushort PN532_I2C_ADDRESS = 0x24;

        /// <summary>The PN532 I2C clock rate kHz (p. 26 -> max 400 kHz)</summary>
        private const I2cBusSpeed PN532_I2C_CLOCK_RATE_KHZ = I2cBusSpeed.FastMode;

        #endregion

        // i2c interface
        I2cDevice i2c;

        // irq interrupt port and event related
        GpioPin irq;
        AutoResetEvent whIrq;

        /// <summary>Initializes a new instance of the <see cref="I2cCommunication"/> class.</summary>
        public I2cCommunication() : this(null)
        {
        }

        /// <summary>Initializes a new instance of the <see cref="I2cCommunication"/> class.</summary>
        /// <param name="irq">Pin related to IRQ from PN532</param>
        public I2cCommunication(int? irq)
        {
            var i2CSettings = new I2cConnectionSettings(PN532_I2C_ADDRESS);
            i2CSettings.BusSpeed = PN532_I2C_CLOCK_RATE_KHZ;

            var controller = I2cController.GetDefaultAsync().GetResults();
            this.i2c = controller.GetDevice(i2CSettings);
            this.irq = null;

            // use advanced handshake with IRQ pin (pn532um.pdf, pag. 44)
            if (irq == null) return;

            var gpioController = GpioController.GetDefault();
            this.irq = gpioController.OpenPin((int)irq);
            this.irq.SetDriveMode(GpioPinDriveMode.Input);
            this.irq.ValueChanged += IrqOnValueChanged;
            this.whIrq = new AutoResetEvent(false);
        }

        #region IPN532CommunicationLayer interface ...

        public bool SendNormalFrame(byte[] frame)
        {
            this.I2cWrite(frame);

            // read acknowledge
            var acknowledge = this.ReadAcknowledge();

            // if null, timeout waiting READY byte
            if (acknowledge == null) return false;

            // return true or flase if ACK or NACK
            return acknowledge[0] == PN532.ACK_PACKET_CODE[0] &&
                   acknowledge[1] == PN532.ACK_PACKET_CODE[1];
        }

        public byte[] ReadNormalFrame()
        {
            var read = new byte[PN532.PN532_EXTENDED_FRAME_MAX_LEN];

            // using IRQ enabled
            if (this.irq != null)
            {
                // wait for IRQ from PN532 if enabled
                if (!this.whIrq.WaitOne(PN532.PN532_READY_TIMEOUT)) return null;

                this.I2CRead(read);
            }
            else
            {
                var start = DateTime.Now.Ticks;
                // waiting for status ready
                while (read[0] != PN532.PN532_READY)
                {
                    this.I2CRead(read);

                    // check timeout
                    if ((DateTime.Now.Ticks - start) / PN532.TICKS_PER_MILLISECONDS < PN532.PN532_READY_TIMEOUT)
                        Task.Delay(10).Wait();
                    else
                        return null;
                }
            }

            // extract data len
            var len = read[PN532.PN532_LEN_OFFSET + 1]; // + 1, first byte is READY BYTE

            // create buffer for all frame bytes
            var frame = new byte[5 + len + 2];
            // save first part of received frame (first 5 bytes until LCS)
            Array.Copy(read, 1, frame, 0, PN532.PN532_LCS_OFFSET + 1); // sourceIndex = 1, first byte is READY BYTE

            // copy last part of the frame (data + DCS + POSTAMBLE)
            Array.Copy(read, PN532.PN532_LCS_OFFSET + 1 + 1, frame, 5, len + 2); // sourceIndex = (PN532.PN532_LCS_OFFSET + 1) + 1, first byte is READY BYTE

            return frame;
        }

        public void WakeUp()
        {
            // not needed (pn532um.pdf, pag. 100)
        }

        #endregion

        private void IrqOnValueChanged(GpioPin sender, GpioPinValueChangedEventArgs args)
        {
            if (args.Edge != GpioPinEdge.FallingEdge) return;
            this.whIrq.Set();
        }

        /// <summary>
        /// Read ACK/NACK frame fromPN532
        /// </summary>
        /// <returns>ACK/NACK frame</returns>
        private byte[] ReadAcknowledge()
        {
            var read = new byte[PN532.ACK_PACKET_SIZE + 1]; // + 1, first byte is READY BYTE

            // using IRQ enabled
            if (this.irq != null)
            {
                // wait for IRQ from PN532 if enabled
                if (!this.whIrq.WaitOne(PN532.PN532_READY_TIMEOUT)) return null;

                this.I2CRead(read);
            }
            else
            {
                var start = DateTime.Now.Ticks;
                // waiting for status ready
                while (read[0] != PN532.PN532_READY)
                {
                    this.I2CRead(read);

                    // check timeout
                    if ((DateTime.Now.Ticks - start) / PN532.TICKS_PER_MILLISECONDS < PN532.PN532_READY_TIMEOUT)
                        Task.Delay(10).Wait();
                    else
                        return null;
                }
            }

            return new[] { read[3 + 1], read[4 + 1] }; // + 1, first byte is READY BYTE
        }

        /// <summary>
        /// Execute an I2C read transaction
        /// </summary>
        /// <param name="data">Output buffer with bytes read</param>
        /// <returns>Number of bytes read</returns>
        private uint I2CRead(byte[] data)
        {
            // receive data from PN532
            var result = this.i2c.WritePartial(data);

            // make sure the data was received.
            if (result.Status != I2cTransferStatus.FullTransfer) throw new Exception("Error executing I2C reading from PN532");

            return result.BytesTransferred;
        }

        /// <summary>
        /// Execute an I2C write transaction
        /// </summary>
        /// <param name="data">Input buffer with bytes to write</param>
        /// <returns>Number of bytes written</returns>
        private uint I2cWrite(byte[] data)
        {
            // write data to PN532
            var transfer = this.i2c.WritePartial(data);

            // make sure the data was sent.
            if (transfer.Status != I2cTransferStatus.FullTransfer) throw new Exception("Error executing I2C writing from PN532");

            return transfer.BytesTransferred;
        }

        /// <summary>Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.</summary>
        public void Dispose()
        {
            if (this.i2c != null)
            {
                this.i2c.Dispose();
            }
        }
    }
}