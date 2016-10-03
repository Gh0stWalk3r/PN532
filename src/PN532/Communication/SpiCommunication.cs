namespace PN532.Communication
{
    using System;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Windows.Devices.Enumeration;
    using Windows.Devices.Gpio;
    using Windows.Devices.Spi;
    using Interfaces;

    /// <summary>SPI communication layer</summary>
    public class SpiCommunication : ICommLayer
    {
        #region SPI Constants ...

        private const bool SPI_CS_ACTIVE_STATE = false; // LOW (pn532um.pdf, pag. 25)
        private const uint SPI_CS_SETUP_TIME = 2;
        private const uint SPI_CS_HOLD_TIME = 2;
        private const bool SPI_CLK_IDLE_STATE = false;  // LOW (pn532um.pdf, pag. 25)
        private const bool SPI_CLK_EDGE = true;         // LOW (pn532um.pdf, pag. 25)
        private const int SPI_CLK_RATE = 1000000;         // 1 Mhz (up to 5 Mhz as pn532um.pdf, pag. 45)

        // (pn532um.pdf, pag. 45)
        private const byte PN532_SPI_DATAWRITE = 0x01;
        private const byte PN532_SPI_STATREAD = 0x02;
        private const byte PN532_SPI_DATAREAD = 0x03;

        #endregion

        // spi interface
        private SpiDevice spi;

        // spi slave selection port
        private GpioPin nssPort;

        // irq interrupt port and event related
        GpioPin irq;

        AutoResetEvent whIrq;

        /// <summary>Initializes a new instance of the <see cref="SpiCommunication"/> class.</summary>
        /// <param name="nss">Slave Selection</param>
        public SpiCommunication(int nss) : this(nss, null)
        {
        }

        /// <summary>Initializes a new instance of the <see cref="SpiCommunication"/> class.</summary>
        /// <param name="nss">The NSS.</param>
        /// <param name="irq">The irq.</param>
        public SpiCommunication(int nss, int? irq)
        {
            var settings = new SpiConnectionSettings(nss);
            settings.ClockFrequency = SPI_CLK_RATE;
            settings.Mode = SpiMode.Mode3;

            var selector = SpiDevice.GetDeviceSelector();
            var deviceInfos = DeviceInformation.FindAllAsync(selector).GetResults();
            this.spi = SpiDevice.FromIdAsync(deviceInfos.First().Id, settings).GetResults();

            var gpioController = GpioController.GetDefault();
            this.nssPort = gpioController.OpenPin(nss);
            this.nssPort.SetDriveMode(GpioPinDriveMode.Output);
            this.nssPort.Write(GpioPinValue.High);

            this.irq = null;
            // use advanced handshake with IRQ pin (pn532um.pdf, pag. 47)
            if (irq == null) return;

            this.irq = gpioController.OpenPin((int)irq);
            this.irq.SetDriveMode(GpioPinDriveMode.Input);
            this.irq.ValueChanged += IrqOnValueChanged;
            this.whIrq = new AutoResetEvent(false);
        }

        #region IPN532CommunicationLayer interface ...

        public bool SendNormalFrame(byte[] frame)
        {
            var write = Utility.ReverseBytes(frame);

            // send frame
            this.nssPort.Write(GpioPinValue.Low);
            Task.Delay(2).Wait();

            this.spi.Write(new byte[] { Utility.ReverseBits(PN532_SPI_DATAWRITE) });
            this.spi.Write(write);

            this.nssPort.Write(GpioPinValue.High);

            // using IRQ enabled
            if (this.irq != null)
            {
                // wait for IRQ from PN532 if enabled
                if (!this.whIrq.WaitOne(PN532.PN532_READY_TIMEOUT))
                    return false;

                // IRQ signaled, read status byte
                //if (this.ReadStatus() != PN532.PN532_READY)
                //    return false;
            }
            else
            {
                var start = DateTime.Now.Ticks;
                // waiting for status ready
                while (this.ReadStatus() != PN532.PN532_READY)
                {
                    // check timeout
                    if ((DateTime.Now.Ticks - start) / PN532.TICKS_PER_MILLISECONDS < PN532.PN532_READY_TIMEOUT)
                        Task.Delay(10).Wait();
                    else
                        return false;
                }
            }

            // read acknowledge
            var acknowledge = this.ReadAcknowledge();

            // return true or flase if ACK or NACK
            return acknowledge[0] == PN532.ACK_PACKET_CODE[0] &&
                   acknowledge[1] == PN532.ACK_PACKET_CODE[1];
        }

        public byte[] ReadNormalFrame()
        {
            // using IRQ enabled
            if (this.irq != null)
            {
                // wait for IRQ from PN532 if enabled
                if (!this.whIrq.WaitOne(PN532.PN532_READY_TIMEOUT))
                    return null;
            }
            else
            {
                long start = DateTime.Now.Ticks;
                // waiting for status ready
                while (this.ReadStatus() != PN532.PN532_READY)
                {
                    // check timeout
                    if ((DateTime.Now.Ticks - start) / PN532.TICKS_PER_MILLISECONDS < PN532.PN532_READY_TIMEOUT)
                        Task.Delay(10).Wait();
                    else
                        return null;
                }
            }

            // dummy bytes from master to force clock a reading from slave
            var write = new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF };
            var read = new byte[PN532.PN532_LCS_OFFSET + 1];

            this.nssPort.Write(GpioPinValue.Low);
            Task.Delay(2).Wait();

            // send data reading request and read response
            this.spi.Write(new[] { Utility.ReverseBits(PN532_SPI_DATAREAD) });
            // write 5 (PN532_LCS_OFFSET + 1) dummy bytes to read until LCS byte
            this.spi.TransferFullDuplex(write,read);

            // extract data len
            var len = Utility.ReverseBits(read[PN532.PN532_LEN_OFFSET]);

            // create buffer for all frame bytes
            var frame = new byte[5 + len + 2];
            // save first part of received frame (first 5 bytes until LCS)
            Array.Copy(read, frame, read.Length);

            write = new byte[len + 2]; // dummy bytes for reading remaining bytes (data + DCS + POSTAMBLE)
            for (int i = 0; i < write.Length; i++)
                write[i] = 0xFF;

            read = new byte[len + 2];

            // write dummy bytes to read data, DCS and POSTAMBLE
            this.spi.TransferFullDuplex(write, read);

            this.nssPort.Write(GpioPinValue.High);

            // copy last part of the frame (data + DCS + POSTAMBLE)
            Array.Copy(read, 0, frame, 5, read.Length);
            var reversed = Utility.ReverseBytes(frame);

            return reversed;
        }

        public void WakeUp()
        {
            // TODO : SPI need wake up ?? Is there a 2 ms delay after SS goes low..it is alredy weak up !
        }

        #endregion

        private void IrqOnValueChanged(GpioPin sender, GpioPinValueChangedEventArgs args)
        {
            if (args.Edge != GpioPinEdge.FallingEdge) return;
            this.whIrq.Set();
        }

        /// <summary>
        /// Read status from PN532
        /// </summary>
        /// <returns>Status byte</returns>
        private byte ReadStatus()
        {
            // prepare
            byte[] write = { Utility.ReverseBits(PN532_SPI_STATREAD) };
            var read = new byte[1];

            this.nssPort.Write(GpioPinValue.Low);
            Task.Delay(2).Wait();

            this.spi.TransferFullDuplex(write, read);

            this.nssPort.Write(GpioPinValue.High);

            byte[] status = Utility.ReverseBytes(read);

            return status[0];
        }

        /// <summary>
        /// Read ACK/NACK frame fromPN532
        /// </summary>
        /// <returns>ACK/NACK frame</returns>
        private byte[] ReadAcknowledge()
        {
            // dummy bytes from master to force clock a reading from slave
            byte[] write = { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF };
            var read = new byte[PN532.ACK_PACKET_SIZE];

            // send data reading request and read response
            this.nssPort.Write(GpioPinValue.Low);
            Task.Delay(2).Wait();

            this.spi.Write(new[] { Utility.ReverseBits(PN532_SPI_DATAREAD) });
            this.spi.TransferFullDuplex(write, read);

            this.nssPort.Write(GpioPinValue.High);

            var acknowledge = Utility.ReverseBytes(read);

            // return only ACK/NACK packet code from frame received
            return new[] { acknowledge[3], acknowledge[4] };
        }

        public void Dispose()
        {
            if (this.spi != null)
            {
                this.spi.Dispose();
            }
        }
    }
}