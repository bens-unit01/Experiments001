/* Copyright (c) 2013 Nordic Semiconductor. All Rights Reserved.
 *
 * The information contained herein is property of Nordic Semiconductor ASA.
 * Terms and conditions of usage are described in detail in NORDIC
 * SEMICONDUCTOR STANDARD SOFTWARE LICENSE AGREEMENT. 
 *
 * Licensees are granted free, non-transferable use of the information. NO
 * WARRANTY of ANY KIND is provided. This heading must NOT be removed from
 * the file.
 *
 */

/* This application is targeted to work with a peripheral loaded with an nRF UART peripheral
 * application.
 */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Text;
using System.IO;
using Nordicsemi;

namespace nRFUart
{
    /// <summary>
    /// Provides data for the OutputReceived event.
    /// </summary>
    public class OutputReceivedEventArgs : EventArgs
    {
        public string Message { get; set; }

        public OutputReceivedEventArgs(string message)
        {
            Message = message;
        }
    }

    /// <summary>
    /// This class controls all calls to MasterEmulator DLL and implements the nRF UART 
    /// logic.
    /// </summary>
    public class nRFUartController
    {
        /* Event declarations */
        public event EventHandler<OutputReceivedEventArgs> LogMessage;
        public event EventHandler<EventArgs> Initialized;
        public event EventHandler<EventArgs> Scanning;
        public event EventHandler<EventArgs> ScanningCanceled;
        public event EventHandler<EventArgs> Connecting;
        public event EventHandler<EventArgs> ConnectionCanceled;
        public event EventHandler<EventArgs> Connected;
        public event EventHandler<EventArgs> PipeDiscoveryCompleted;
        public event EventHandler<EventArgs> Disconnected;
        public event EventHandler<EventArgs> SendDataStarted;
        public event EventHandler<EventArgs> SendDataCompleted;
        public event EventHandler<ValueEventArgs<int>> ProgressUpdated;

        /* Public properties */
        public bool DebugMessagesEnabled { get; set; }

        /* Instance variables */
        MasterEmulator masterEmulator;
        PipeSetup pipeSetup;
        bool connectionInProgress = false;
        bool sendData = false;

        const int maxPacketLength = 20;
        const int counterFieldLength = 2;
        const int maxPayloadLength = maxPacketLength - counterFieldLength;

        /// <summary>
        /// Constructor.
        /// </summary>
        public nRFUartController()
        { }

        /// <summary>
        /// Connect to peer device.
        /// </summary>
        public void InitiateConnection()
        {
            bool success = StartDeviceDiscovery();
        }

        /// <summary>
        /// Stop scanning for devices.
        /// </summary>
        public void StopScanning()
        {
            if (!masterEmulator.IsDeviceDiscoveryOngoing)
            {
                return;
            }

            bool success = masterEmulator.StopDeviceDiscovery();

            if (success)
            {
                ScanningCanceled(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// Send data to peer device.
        /// </summary>
        /// <param name="value"></param>
        public void SendData(string value)
        {
           // byte[] encodedBytes = Encoding.UTF8.GetBytes(value);
            byte[] encodedBytes ={(byte) Convert.ToInt16(value)};
            if (encodedBytes.Length > maxPacketLength)
            {
                Array.Resize<byte>(ref encodedBytes, maxPacketLength);
                AddToLog("Max packet size is 20 characters, text is truncated.");
            }

            masterEmulator.SendData(pipeSetup.UartRxPipe, encodedBytes);

            string decodedString = Encoding.UTF8.GetString(encodedBytes);
            AddToLog(string.Format("TX: {0}", Convert.ToString(encodedBytes[0])));
        }

        /// <summary>
        /// Start sending a data to the peer device.
        /// </summary>
        /// <param name="data">An arbitrarily large byte array of data to send.</param>
        /// <remarks>The method will continue to send until all data has been
        /// transmitted or the transmission has been stopped by <see cref="StopSendData"/>.
        /// </remarks>
        public void StartSendData(byte[] data)
        {
            if (data == null)
            {
                throw new ArgumentNullException();
            }

            IList<byte[]> splitData = SplitDataAndAddCounter(data, maxPayloadLength);

            sendData = true;

            /* Starting a task to perform the sending of packets asynchronouly.
             * The SendDataCompleted event will notify the application when ready. */
            Task.Factory.StartNew(() =>
            {
                try
                {
                    SendDataStarted(this, EventArgs.Empty);

                    int numberOfPackets = splitData.Count;
                    int progressInPercent = 0;

                    for (int i = 0; i < numberOfPackets; i++)
                    {
                        if (!sendData)
                        {
                            break;
                        }

                        /* Send one packet of data on the UartRx pipe. */
                        masterEmulator.SendData(pipeSetup.UartRxPipe, splitData[i]);

                        int currentProgressInPercent = ((i + 1) * 100) / numberOfPackets;

                        if (currentProgressInPercent > progressInPercent)
                        {
                            progressInPercent = currentProgressInPercent;
                            ProgressUpdated(this, new ValueEventArgs<int>(progressInPercent));
                        }
                    }
                }
                catch (Exception ex)
                {
                    AddToLog("Sending of data failed.");
                    Trace.WriteLine(ex.ToString());
                }

                SendDataCompleted(this, EventArgs.Empty);
            });
        }

        /// <summary>
        /// Start to send data to the peer device.
        /// </summary>
        /// <param name="data">A byte array no larger than max packet size.</param>
        /// <param name="numberOfRepetitions">The number of times to repeat the packet.</param>
        public void StartSendData(byte[] data, int numberOfRepetitions)
        {
            if (data == null)
            {
                throw new ArgumentNullException();
            }

            if (data.Length > maxPayloadLength)
            {
                throw new ArgumentException(string.Format("Length of data must not exceed {0}.",
                    maxPayloadLength));
            }

            int totalPacketSize = data.Length * numberOfRepetitions;

            var aggregatedData = new List<byte>(totalPacketSize);

            for (int i = 0; i < numberOfRepetitions; i++)
            {
                aggregatedData.AddRange(data);
            }

            StartSendData(aggregatedData.ToArray());
        }

        /// <summary>
        /// Split to max length packets and leave room for counter values.
        /// </summary>
        IList<byte[]> SplitDataAndAddCounter(byte[] data, int partSize)
        {
            /* Collection of packets split out from source array. */
            IList<byte[]> packets = new List<byte[]>();

            /* Counter value, incremented for each new packet. */
            int counter = 0;

            /* Index of counter field in packet. */
            const int counterIndex = 0;

            /* Last index where a packet of full length may start. */
            /* Note const and readonly won't work here since const is compile-time const and */
            /* readonly requires this to be a member. Since SplitDataAndAddCounter is called multiple*/
            /* times, it breaks the readonly usage as well. */
            int lastFullPacketIndex = (data.Length - partSize);

            /* Current index in source array. */
            int index;

            for (index = 0; index < lastFullPacketIndex; index += partSize)
            {
                byte[] packet = new byte[maxPacketLength];
                Array.Copy(data, index, packet, counterFieldLength, partSize);

                InjectCounter(counter, counterIndex, packet);

                packets.Add(packet);

                counter += 1;
            }

            /* Special treatment of last packet. */
            int lastPacketPayloadSize = (data.Length - index);
            byte[] lastPacket = new byte[maxPacketLength];
            Array.Copy(data, index, lastPacket, counterFieldLength, lastPacketPayloadSize);
            InjectCounter(counter, counterIndex, lastPacket);

            packets.Add(lastPacket);

            return packets;
        }

        /// <summary>
        /// Insert 16 bit counter in Least Significant Byte order.
        /// </summary>
        void InjectCounter(int counter, int index, byte[] packet)
        {
            byte leastSignificantByte = (byte)(counter & 0xFF);
            byte mostSignificantByte = (byte)((counter >> 8) & 0xFF);

            packet[index] = leastSignificantByte;
            packet[index + 1] = mostSignificantByte;
        }

        /// <summary>
        /// Signal StartSendData task to cancel sending of data.
        /// </summary>
        public void StopSendData()
        {
            sendData = false;
        }

        /// <summary>
        /// Disconnect from peer device.
        /// </summary>
        public void InitiateDisconnect()
        {
            if (!masterEmulator.IsConnected)
            {
                return;
            }

            masterEmulator.Disconnect();
        }

        /// <summary>
        /// Close MasterEmulator.
        /// </summary>
        public void Close()
        {
            if (!masterEmulator.IsOpen)
            {
                return;
            }

            masterEmulator.Close();
        }

        /// <summary>
        ///  Method for adding text to the textbox and logfile.
        ///  When called on the main thread, invoke is not required.
        ///  For other threads, the invoke is required.
        /// </summary>
        /// <param name="message">The message string to add to the log.</param>
        void AddToLog(string message)
        {
            if (LogMessage != null)
            {
                LogMessage(this, new OutputReceivedEventArgs(message));
            }

            /* Writing to trace also, which causes the message to be put in the log file. */
            Trace.WriteLine(message);
        }

        /// <summary>
        /// Convenience method for logging exception messages.
        /// </summary>
        void LogErrorMessage(string errorMessage, string stackTrace)
        {
            AddToLog(errorMessage);
            Trace.WriteLine(stackTrace);
        }

        /// <summary>
        /// Get the path to the log file of MasterEmulator.
        /// </summary>
        /// <returns>Returns path to log file.</returns>
        public string GetLogfilePath()
        {
            return masterEmulator.GetLogFilePath();

        }

        /// <summary>
        /// Collection of method calls to start and setup MasterEmulator.
        /// The calls are placed in a background task for not blocking the gui thread.
        /// </summary>
        public void Initialize()
        {
            Task.Factory.StartNew(() =>
            {
                try
                {
                    InitializeMasterEmulator();
                    RegisterEventHandlers();
                    string device = FindUsbDevice();
                    OpenMasterEmulatorDevice(device);
                    pipeSetup = new PipeSetup(masterEmulator);
                    pipeSetup.PerformPipeSetup();
                    Run();
                    Initialized(this, EventArgs.Empty);
                }
                catch (Exception ex)
                {
                    LogErrorMessage(string.Format("Exception in StartMasterEmulator", ex.Message),
                    ex.StackTrace);
                }
            });
        }

        /// <summary>
        /// Create MasterEmulator instance.
        /// </summary>
        void InitializeMasterEmulator()
        {
            AddToLog("Loading...");
            masterEmulator = new MasterEmulator();
        }

        /// <summary>
        /// Register event handlers for MasterEmulator events.
        /// </summary>
        void RegisterEventHandlers()
        {
            masterEmulator.Connected += OnConnected;
            masterEmulator.ConnectionUpdateRequest += OnConnectionUpdateRequest;
            masterEmulator.DataReceived += OnDataReceived;
            masterEmulator.DeviceDiscovered += OnDeviceDiscovered;
            masterEmulator.Disconnected += OnDisconnected;
            masterEmulator.LogMessage += OnLogMessage;
        }

        /// <summary>
        /// Searching for master emulator devices attached to the pc. 
        /// If more than one is connected it will simply return the first in the list.
        /// </summary>
        /// <returns>Returns the first master emulator device found.</returns>
        string FindUsbDevice()
        {
            /* The UsbDeviceType argument is used for filtering master emulator device types. */
            var devices = masterEmulator.EnumerateUsb(UsbDeviceType.AnyMasterEmulator);

            if (devices.Count > 0)
            {
                return devices[0];
            }
            else
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// Tell the api to use the given master emulator device.
        /// </summary>
        /// <param name="device"></param>
        void OpenMasterEmulatorDevice(string device)
        {
            if (masterEmulator.IsOpen)
            {
                return;
            }

            masterEmulator.Open(device);
            masterEmulator.Reset();
        }

        /// <summary>
        /// By calling Run, the pipesetup is processed and the stack engine is started.
        /// </summary>
        void Run()
        {
            if (masterEmulator.IsRunning)
            {
                return;
            }

            masterEmulator.Run();
        }

        /// <summary>
        /// Device discovery is started with the given scan parameters.
        /// By stating active scan, we will be receiving data from both advertising
        /// and scan repsonse packets.
        /// </summary>
        /// <returns></returns>
        bool StartDeviceDiscovery()
        {
            if (!masterEmulator.IsRunning)
            {
                AddToLog("Not ready.");
                return false;
            }

            BtScanParameters scanParameters = new BtScanParameters();
            scanParameters.ScanType = BtScanType.ActiveScanning;
            bool startSuccess = masterEmulator.StartDeviceDiscovery(scanParameters);

            if (startSuccess)
            {
                Scanning(this, EventArgs.Empty);
            }

            return startSuccess;
        }

        /// <summary>
        /// Connecting to the given device, and with the given connection parameters.
        /// </summary>
        /// <param name="device">Device to connect to.</param>
        /// <returns>Returns success indication.</returns>
        bool Connect(BtDevice device)
        {
            if (masterEmulator.IsDeviceDiscoveryOngoing)
            {
                masterEmulator.StopDeviceDiscovery();
            }

            string deviceName = GetDeviceName(device.DeviceInfo);
            AddToLog(string.Format("Connecting to {0}, Device name: {1}",
                device.DeviceAddress.ToString(), deviceName));

            BtConnectionParameters connectionParams = new BtConnectionParameters();
            connectionParams.ConnectionIntervalMs = 11.25;
            connectionParams.ScanIntervalMs = 250;
            connectionParams.ScanWindowMs = 200;
            bool connectSuccess = masterEmulator.Connect(device.DeviceAddress, connectionParams);
            return connectSuccess;
        }

        /// <summary>
        /// By discovering pipes, the pipe setup we have specified will be matched up
        /// to the remote device's ATT table by ATT service discovery.
        /// </summary>
        void DiscoverPipes()
        {
            bool success = masterEmulator.DiscoverPipes();

            if (!success)
            {
                AddToLog("DiscoverPipes did not succeed.");
            }
        }

        /// <summary>
        /// Pipes of type PipeType.Receive must be opened before they will start receiving notifications.
        /// This maps to ATT Client Configuration Descriptors.
        /// </summary>
        void OpenRemotePipes()
        {
            var openedPipesEnumeration = masterEmulator.OpenAllRemotePipes();
            List<int> openedPipes = new List<int>(openedPipesEnumeration);
        }

        /// <summary>
        /// Event handler for DeviceDiscovered. This handler will be called when devices
        /// are discovered during asynchronous device discovery.
        /// </summary>
        void OnDeviceDiscovered(object sender, ValueEventArgs<BtDevice> arguments)
        {
            /* Avoid call after a connect procedure is being started,
             * and the discovery procedure hasn't yet been stopped. */
            if (connectionInProgress)
            {
                return;
            }

            BtDevice device = arguments.Value;

            if (!IsEligibleForConnection(device))
            {
                return;
            }

            connectionInProgress = true;

            /* Start the connection procedure in a background task to avoid 
             * blocking the event caller. */
            Task.Factory.StartNew(() =>
            {
                try
                {
                    Connecting(this, EventArgs.Empty);

                    bool success = Connect(device);
                    if (!success)
                    {
                        ConnectionCanceled(this, EventArgs.Empty);
                        return;
                    }
                }
                catch (Exception ex)
                {
                    LogErrorMessage(string.Format("Exception in OnDeviceDiscovered: {0}",
                        ex.Message), ex.StackTrace);
                    ConnectionCanceled(this, EventArgs.Empty);
                }
            });
        }

        /// <summary>
        /// Check if a device has the advertising data we are looking for.
        /// </summary>
        bool IsEligibleForConnection(BtDevice device)
        {
            IDictionary<DeviceInfoType, string> deviceInfo = device.DeviceInfo;

            bool hasServicesCompleteAdField =
                deviceInfo.ContainsKey(DeviceInfoType.ServicesCompleteListUuid128);

            if (!hasServicesCompleteAdField)
            {
                return false;
            }

            const string bleUartUuid = "6E400001B5A3F393E0A9E50E24DCCA9E";
            bool hasHidServiceUuid =
                    deviceInfo[DeviceInfoType.ServicesCompleteListUuid128].Contains(
                    bleUartUuid);

            if (!hasHidServiceUuid)
            {
                return false;
            }

            /* If we have reached here it means all the criterias have passed. */
            return true;
        }

        /// <summary>
        /// Extract the device name from the advertising data.
        /// </summary>
        string GetDeviceName(IDictionary<DeviceInfoType, string> deviceInfo)
        {
            string deviceName = string.Empty;
            bool hasNameField = deviceInfo.ContainsKey(DeviceInfoType.CompleteLocalName);
            if (hasNameField)
            {
                deviceName = deviceInfo[DeviceInfoType.CompleteLocalName];
            }
            return deviceName;
        }

        /// <summary>
        /// This event handler is called when data has been received on any of our pipes.
        /// </summary>
        void OnDataReceived(object sender, PipeDataEventArgs arguments)
        {
            if (arguments.PipeNumber != pipeSetup.UartTxPipe)
            {
                AddToLog("Received data on unknown pipe.");
                return;
            }

            StringBuilder stringBuffer = new StringBuilder();
            foreach (byte element in arguments.PipeData)
            {
                stringBuffer.AppendFormat(" 0x{0:X2}", element);
            }

            if (DebugMessagesEnabled)
            {
                AddToLog(string.Format("Data received on pipe number {0}:{1}", arguments.PipeNumber,
                    stringBuffer.ToString()));
            }

            byte[] utf8Array = arguments.PipeData;
            string convertedText = Encoding.UTF8.GetString(utf8Array);
            AddToLog(string.Format("RX: {0}", convertedText));
        }

        /// <summary>
        /// This event handler is called when a connection has been successfully established.
        /// </summary>
        void OnConnected(object sender, EventArgs arguments)
        {
            if (Connected != null)
            {
                Connected(this, EventArgs.Empty);
            }

            /* The connection is up, proceed with pipe discovery. 
             * Using a background task in order not to block the event caller. */
            Task.Factory.StartNew(() =>
            {
                try
                {
                    DiscoverPipes();
                    OpenRemotePipes();
                    PipeDiscoveryCompleted(this, EventArgs.Empty);
                }
                catch (Exception ex)
                {
                    LogErrorMessage(string.Format("Exception in OnConnected: {0}", ex.Message),
                        ex.StackTrace);
                }
            });
        }

        /// <summary>
        /// This event handler is called when a connection update request has been received.
        /// A connection update must be responded to in two steps: sending a connection update
        /// response, and performing the actual update.
        /// </summary>
        void OnConnectionUpdateRequest(object sender, ConnectionUpdateRequestEventArgs arguments)
        {
            Task.Factory.StartNew(() =>
            {
                masterEmulator.SendConnectionUpdateResponse(arguments.Identifier,
                    ConnectionUpdateResponse.Accepted);
                BtConnectionParameters updateParams = new BtConnectionParameters();
                updateParams.ConnectionIntervalMs = arguments.ConnectionIntervalMinMs;
                updateParams.SupervisionTimeoutMs = arguments.ConnectionSupervisionTimeoutMs;
                updateParams.SlaveLatency = arguments.SlaveLatency;
                masterEmulator.UpdateConnectionParameters(updateParams);
            });
        }

        /// <summary>
        /// This event handler is called when a connection has been terminated.
        /// </summary>
        void OnDisconnected(object sender, ValueEventArgs<DisconnectReason> arguments)
        {
            connectionInProgress = false;
            sendData = false;
            Disconnected(this, EventArgs.Empty);
        }

        /// <summary>
        /// Relay received log message events to the log method.
        /// </summary>
        void OnLogMessage(object sender, ValueEventArgs<string> arguments)
        {
            string message = arguments.Value;

            if (message.Contains("Connected to"))
            {
                /* Don't filter out */
            }
            else if (message.Contains("Disconnected"))
            {
                return;
            }
            else if (!DebugMessagesEnabled)
            {
                return;
            }

            AddToLog(string.Format("{0}", arguments.Value));
        }
    }
}
