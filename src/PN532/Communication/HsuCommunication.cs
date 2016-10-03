namespace PN532.Communication
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Windows.Devices.SerialCommunication;
    using Windows.Storage.Streams;
    using Enums;
    using Interfaces;

    /// <summary>
    /// HSU communication layer
    /// </summary>
    public class HsuCommunication : ICommLayer
    {
        // default timeout to wait PN532
        private const int WAIT_TIMEOUT = 500;

        #region HSU Constants ...

        private const int HSU_BAUD_RATE = 115200;
        private const SerialParity HSU_PARITY = SerialParity.None;
        private const int HSU_DATA_BITS = 8;
        private const SerialStopBitCount HSU_STOP_BITS = SerialStopBitCount.One;

        #endregion

        // hsu interface
        private SerialDevice port;

        // event on received frame
        private AutoResetEvent received;

        // frame parser state
        private HsuParserState state;
        private bool isFirstStartCode;
        private bool isWaitingAck;
        // frame buffer
        private IList frame;
        private int length;
        // internal buffer from serial port
        private IList inputBuffer;

        // frames queue received
        private Queue<byte[]> queueFrame;


        private DataReader dataReaderObject;

        private CancellationTokenSource ReadCancellationTokenSource = new CancellationTokenSource();

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="portName">Serial port name</param>
        public HsuCommunication(string portName)
        {
            this.length = 0;
            this.isFirstStartCode = false;
            this.isWaitingAck = false;

            this.frame = new List<byte>();
            this.inputBuffer = new List<byte>();
            this.queueFrame = new Queue<byte[]>();

            this.state = HsuParserState.Preamble;
            this.received = new AutoResetEvent(false);

            // create and open serial port
            this.port = SerialDevice.FromIdAsync(portName).GetResults();
            this.port.BaudRate = HSU_BAUD_RATE;
            this.port.Parity = HSU_PARITY;
            this.port.DataBits = HSU_DATA_BITS;
            this.port.StopBits = HSU_STOP_BITS;

            Task.Factory.StartNew(this.Listen, TaskCreationOptions.LongRunning);
        }

        private async void Listen()
        {
            if (this.port == null) return;
            using (dataReaderObject = new DataReader(this.port.InputStream))
            {
                try
                {
                    while (true)
                    {
                        await ReadAsync(ReadCancellationTokenSource.Token);
                    }
                }
                finally
                {
                    dataReaderObject.DetachStream();
                }
            }
        }

        private async Task ReadAsync(CancellationToken cancellationToken)
        {
            Task<uint> loadAsyncTask;

            const uint readBufferLength = 1024;

            // If task cancellation was requested, comply
            cancellationToken.ThrowIfCancellationRequested();

            // Set InputStreamOptions to complete the asynchronous read operation when one or more bytes is available
            dataReaderObject.InputStreamOptions = InputStreamOptions.Partial;

            // Create a task object to wait for data on the serialPort.InputStream
            loadAsyncTask = dataReaderObject.LoadAsync(readBufferLength).AsTask(cancellationToken);

            // Launch the task and wait
            var bytesRead = await loadAsyncTask;
            if (bytesRead <= 0) return;
            while (bytesRead-- > 0)
            {
                this.inputBuffer.Add(dataReaderObject.ReadByte());
            }

            // frame parsing
            this.ExtractFrame();
        }

        private void CancelReadTask()
        {
            if (ReadCancellationTokenSource != null && !ReadCancellationTokenSource.IsCancellationRequested)
            {
                ReadCancellationTokenSource.Cancel();
            }
        }

        void ExtractFrame()
        {
            lock (this.inputBuffer)
            {
                foreach (byte byteRx in this.inputBuffer)
                {
                    switch (this.state)
                    {
                        case HsuParserState.Preamble:

                            // preamble arrived, frame started
                            if (byteRx == PN532.PN532_PREAMBLE)
                            {
                                this.length = 0;
                                this.isFirstStartCode = false;
                                this.frame.Clear();

                                this.frame.Add(byteRx);
                                this.state = HsuParserState.StartCode;
                            }

                            break;

                        case HsuParserState.StartCode:

                            // first start code byte not received yet
                            if (!this.isFirstStartCode)
                            {
                                if (byteRx == PN532.PN532_STARTCODE_1)
                                {
                                    this.frame.Add(byteRx);
                                    this.isFirstStartCode = true;
                                }
                            }
                            // first start code byte already received
                            else
                            {
                                if (byteRx == PN532.PN532_STARTCODE_2)
                                {
                                    this.frame.Add(byteRx);
                                    this.state = HsuParserState.Length;
                                }
                            }

                            break;

                        case HsuParserState.Length:

                            // not waiting ack, the byte is LEN
                            if (!this.isWaitingAck)
                            {
                                // save data length (TFI + PD0...PDn) for counting received data
                                this.length = byteRx;
                                this.frame.Add(byteRx);
                                this.state = HsuParserState.LengthChecksum;
                            }
                            // waiting ack, the byte is first of ack/nack code
                            else
                            {
                                this.frame.Add(byteRx);
                                this.state = HsuParserState.AckCode;
                            }

                            break;

                        case HsuParserState.LengthChecksum:

                            // arrived LCS
                            this.frame.Add(byteRx);
                            this.state = HsuParserState.FrameIdentifierAndData;
                            break;

                        case HsuParserState.FrameIdentifierAndData:

                            this.frame.Add(byteRx);
                            // count received data bytes (TFI + PD0...PDn)
                            this.length--;

                            // all data bytes received
                            if (this.length == 0)
                                this.state = HsuParserState.DataChecksum;
                            break;

                        case HsuParserState.DataChecksum:

                            // arrived DCS
                            this.frame.Add(byteRx);
                            this.state = HsuParserState.Postamble;
                            break;

                        case HsuParserState.Postamble:

                            // postamble received, frame end
                            if (byteRx == PN532.PN532_POSTAMBLE)
                            {
                                this.frame.Add(byteRx);
                                this.state = HsuParserState.Preamble;

                                // enqueue received frame
                                byte[] frameReceived = new byte[this.frame.Count];
                                this.frame.CopyTo(frameReceived, 0);
                                this.queueFrame.Enqueue(frameReceived);

                                this.received.Set();
                            }

                            break;

                        case HsuParserState.AckCode:

                            // second byte of ack/nack code
                            this.frame.Add(byteRx);
                            this.state = HsuParserState.Postamble;

                            this.isWaitingAck = false;
                            break;
                    }
                }

                // clear internal buffer
                this.inputBuffer.Clear();
            }
        }

        #region IPN532CommunicationLayer interface ...

        public bool SendNormalFrame(byte[] frame)
        {
            //this.frame.Clear();
            this.isWaitingAck = true;
            // send frame...
            this.WriteAsync(frame).Wait();

            // wait for ack/nack
            if (this.received.WaitOne(WAIT_TIMEOUT))
            {
                this.isWaitingAck = false;

                // dequeue received frame
                var frameReceived = this.queueFrame.Dequeue();

                // read acknowledge
                byte[] acknowledge = { frameReceived[3], frameReceived[4] };

                // return true or flase if ACK or NACK
                return acknowledge[0] == PN532.ACK_PACKET_CODE[0] &&
                       acknowledge[1] == PN532.ACK_PACKET_CODE[1];
            }
            else
                return false;
        }

        public byte[] ReadNormalFrame()
        {
            this.isWaitingAck = false;
            return this.received.WaitOne(WAIT_TIMEOUT) ? this.queueFrame.Dequeue() : null;
        }

        public void WakeUp()
        {
            // PN532 Application Note C106 pag. 23

            // HSU wake up consist to send a SAM configuration command with a "long" preamble 
            // here we send preamble that will be followed by regular SAM configuration command
            byte[] preamble = { 0x55, 0x55, 0x00, 0x00, 0x00 };
            WriteAsync(preamble).Wait();
        }

        private async Task WriteAsync(byte[] bytes)
        {
            if (this.port == null || bytes == null || bytes.Length <= 0) return;

            using (var dataWriteObject = new DataWriter(this.port.OutputStream))
            {
                try
                {
                    // Load the text from the sendText input text box to the dataWriter object
                    dataWriteObject.WriteBytes(bytes);

                    // Launch an async task to complete the write operation
                    var storeAsyncTask = dataWriteObject.StoreAsync().AsTask();

                    var bytesWritten = await storeAsyncTask;
                }
                finally
                {
                    dataWriteObject.DetachStream();
                }
            }
        }

        #endregion

        public void Dispose()
        {
            this.CancelReadTask();
            if (this.port != null)
            {
                this.port.Dispose();
            }
        }
    }
}