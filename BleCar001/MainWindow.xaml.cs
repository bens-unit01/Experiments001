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

using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Diagnostics;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace nRFUart
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        nRFUartController controller;
        bool isControllerInitialized = false;
        bool isControllerConnected = false;

        const string strConnect = "Connect";
        const string strScanning = "Stop scanning";
        const string strDisconnect = "Disconnect";
        const string strStopSendData = "Stop sending data";
        const string strStartSendData = "Send 100kB data";

        const UInt32 logHighWatermark = 10000;  // If we reach high watermark, we delete until we're
                                                // down to low watermark
        const UInt32 logLowWatermark = 5000;

        private ObservableCollection<String> _outputText = null;
        public ObservableCollection<string> OutputText
        {
            get { return _outputText ?? (_outputText = new ObservableCollection<string>()); }
            set { _outputText = value; }
        }

        public MainWindow()
        {
            InitializeComponent();
            InitializeNrfUartController();

            /* Retrieve persisted setting. */
            cbDebug.IsChecked = Properties.Settings.Default.IsDebugEnabled;
            DataContext = this;
        }

        protected override void OnInitialized(EventArgs e)
        {
            base.OnInitialized(e);

            btnConnect.IsEnabled = false;
            Mouse.OverrideCursor = Cursors.Wait;
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            try
            {
                /* Persist user settings before the applications closes. */
                Properties.Settings.Default.Save();

                controller.Close();
            }
            catch (Exception ex)
            {
                Trace.WriteLine(ex.ToString());
            }

            base.OnClosing(e);
        }

        void InitializeNrfUartController()
        {
            controller = new nRFUartController();

            /* Registering event handler methods for all nRFUartController events. */
            controller.LogMessage += OnLogMessage;
            controller.Initialized += OnControllerInitialized;
            controller.Scanning += OnScanning;
            controller.ScanningCanceled += OnScanningCanceled;
            controller.Connecting += OnConnecting;
            controller.ConnectionCanceled += OnConnectionCanceled;
            controller.Connected += OnConnected;
            controller.PipeDiscoveryCompleted += OnControllerPipeDiscoveryCompleted;
            controller.Disconnected += OnDisconnected;
            controller.SendDataStarted += OnSendDataStarted;
            controller.SendDataCompleted += OnSendDataCompleted;
            controller.ProgressUpdated += OnProgressUpdated;

            controller.Initialize();
        }

        void SetConnectButtonText(string text)
        {
            SetButtonText(btnConnect, text);
        }

        void SetButtonText(Button button, string text)
        {
            /* Requesting GUI update to be done in main thread since this 
             * method will be called from a different thread. */
            Dispatcher.BeginInvoke((Action)delegate()
            {
                button.Content = text;
            });
        }

        void SetStartSendIsEnabled(bool isEnabled)
        {
           // SetButtonIsEnabled(btnStartSend, isEnabled);
        }

        void SetStartSendFileIsEnabled(bool isEnabled)
        {
           // SetButtonIsEnabled(btnStartSendFile, isEnabled);
        }

        void SetStopDataIsEnabled(bool isEnabled)
        {
            SetButtonIsEnabled(btnStopData, isEnabled);
        }

        void SetButtonIsEnabled(Button button, bool isEnabled)
        {
            /* Requesting GUI update to be done in main thread since this 
             * method will be called from a different thread. */
            Dispatcher.BeginInvoke((Action)delegate()
            {
                button.IsEnabled = isEnabled;
            });
        }

        void SetProgressBarValue(int newValue)
        {
            /* Requesting GUI update to be done in main thread since this 
             * method will be called from a different thread. */
            Dispatcher.BeginInvoke((Action)delegate()
            {
             //   progressBar.Value = newValue;
            });
        }

        void AddToOutput(string text)
        {
            /* Need to call Invoke since method will be called from a background thread. */
            Dispatcher.BeginInvoke((Action)delegate()
            {
                string timestamp = DateTime.Now.ToString("HH:mm:ss.ffff");
                text = String.Format("[{0}] {1}", timestamp, text);

                if (OutputText.Count >= logHighWatermark)
                {
                    UInt32 numToDelete = (UInt32)OutputText.Count - logLowWatermark;
                    for (UInt32 i = 0; i < numToDelete; i++)
                    {
                        OutputText.RemoveAt(0);
                    }
                }

                OutputText.Add(text);
                lbOutput.ScrollIntoView(text);
            });
        }

        #region nRFUart event handlers
        void OnControllerInitialized(object sender, EventArgs e)
        {
            isControllerInitialized = true;

            Dispatcher.BeginInvoke((Action)delegate()
            {
                btnConnect.IsEnabled = true;
                Mouse.OverrideCursor = null;
            });

            AddToOutput("Ready to connect");
        }

        void OnLogMessage(object sender, OutputReceivedEventArgs e)
        {
            AddToOutput(e.Message);
        }

        void OnScanning(object sender, EventArgs e)
        {
            AddToOutput("Scanning...");
            SetConnectButtonText(strScanning);
        }

        void OnScanningCanceled(object sender, EventArgs e)
        {
            AddToOutput("Stopped scanning");
            SetConnectButtonText(strConnect);
        }

        void OnConnectionCanceled(object sender, EventArgs e)
        {
            SetConnectButtonText(strConnect);
        }

        void OnConnecting(object sender, EventArgs e)
        {
            AddToOutput("Connecting...");
        }

        void OnConnected(object sender, EventArgs e)
        {
            isControllerConnected = true;
            SetConnectButtonText(strDisconnect);
        }

        void OnControllerPipeDiscoveryCompleted(object sender, EventArgs e)
        {
            AddToOutput("Ready to send");
        }

        void OnSendDataStarted(object sender, EventArgs e)
        {
            AddToOutput("Started sending data...");
            SetStopDataIsEnabled(true);
            SetStartSendIsEnabled(false);
            SetStartSendFileIsEnabled(false);
        }

        void OnSendDataCompleted(object sender, EventArgs e)
        {
            AddToOutput("Data transfer ended");
            SetStopDataIsEnabled(false);
            SetStartSendIsEnabled(true);
            SetStartSendFileIsEnabled(true);
            SetProgressBarValue(0);
        }

        void OnDisconnected(object sender, EventArgs e)
        {
            isControllerConnected = false;
            AddToOutput("Disconnected");
            SetConnectButtonText(strConnect);
            SetStopDataIsEnabled(false);
            SetStartSendIsEnabled(true);
            SetStartSendFileIsEnabled(true);
        }

        void OnProgressUpdated(object sender, Nordicsemi.ValueEventArgs<int> e)
        {
            int progress = e.Value;
            if (0 <= progress && progress <= 100)
            {
                SetProgressBarValue(progress);
            }
        }
        #endregion

        #region GUI event handlers

        /* Event handler for Connect button. Depending on what state nRFUart is in 
         * different actions will be performed. */
        void OnBtnConnectClick(object sender, RoutedEventArgs e)
        {
            if (!isControllerInitialized)
            {
                return;
            }

            if (btnConnect.Content.ToString() == strConnect)
            {
                controller.InitiateConnection();
            }
            else if (btnConnect.Content.ToString() == strScanning)
            {
                controller.StopScanning();
            }
            else if (btnConnect.Content.ToString() == strDisconnect)
            {
                controller.InitiateDisconnect();
            }
        }

        void OnBtnSendClick(object sender, RoutedEventArgs e)
        {
            if (!isControllerConnected)
            {
                return;
            }

            controller.SendData(tbInput.Text);
        }

        /// <summary>
        /// Adds ability to initiate send by hitting enter key when textbox has focus.
        /// </summary>
        void OnTbInputKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter)
            {
                return;
            }

            if (!isControllerConnected)
            {
                return;
            }

            controller.SendData(tbInput.Text);
        }

        void OnCbDebugChecked(object sender, RoutedEventArgs e)
        {
            /* Store the state of the checkbox in application settings. */
            Properties.Settings.Default.IsDebugEnabled = (bool)cbDebug.IsChecked;
            controller.DebugMessagesEnabled = (bool)cbDebug.IsChecked;
        }

        void OnMenuItemLogfileClick(object sender, RoutedEventArgs e)
        {
            string logfilePath = controller.GetLogfilePath();
            Process.Start(logfilePath);
        }

        void OnMenuItemExitClick(object sender, RoutedEventArgs e)
        {
            Close();
        }

        void OnMenuItemAboutClick(object sender, RoutedEventArgs e)
        {
            About aboutDialog = new About();
            aboutDialog.Owner = this;
            aboutDialog.ShowDialog();
        }

        void OnBtnSendFile(object sender, RoutedEventArgs e)
        {
            string sendFilePath = String.Empty;

            Microsoft.Win32.OpenFileDialog ofd = new Microsoft.Win32.OpenFileDialog();
            ofd.FileName = "File";
            ofd.DefaultExt = "*.*";
            ofd.Filter = "All files (*.*)|*.*";
            ofd.FilterIndex = 0;
            ofd.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyComputer);

            ofd.Title = "Please select a file to send";

            bool? ofdResult = ofd.ShowDialog();

            if (ofdResult == false) //Failure
            {
                return;
            }

            SendFile(ofd.FileName);
        }

        void SendFile(string filePath)
        {
            if (!File.Exists(filePath))
            {
                return;
            }

            filePath = filePath.Replace("\\", "/");


            byte[] fileContent = File.ReadAllBytes(filePath);
            controller.StartSendData(fileContent);
        }

        void OnBtnSend100K(object sender, RoutedEventArgs e)
        {
            Send100K();
        }

        void Send100K()
        {
            /* Instantiate byte array with 18 bytes of data. */
            byte[] data = new byte[] { 0x61, 0x62, 0x63, 0x64, 0x65, 0x66, 0x67, 0x68, 0x69, 0x6A,
            0x6B, 0x6C, 0x6D, 0x6E, 0x6F, 0x70, 0x71, 0x72};

            /* Calculate number of packets required to send 100kB of data. */
            int maxBytesPerPacket = 18;
            int kibiBytes = 1024;
            int numberOfRepetitions = (100 * kibiBytes) / maxBytesPerPacket; /* 5120 packets */

            controller.StartSendData(data, numberOfRepetitions);
        }

        void OnBtnStopData(object sender, RoutedEventArgs e)
        {
            AddToOutput("Stop transfer");
            controller.StopSendData();
        }
        #endregion

        #region Command bindings

        private void CopyExecuted(object sender, ExecutedRoutedEventArgs e)
        {
            if (lbOutput.SelectedItems.Count > 0)
            {
                List<String> copyText = new List<String>();
                foreach (var item in lbOutput.SelectedItems)
                {
                    copyText.Add(item.ToString());
                }

                copyText.Sort();
                string joined = string.Join(System.Environment.NewLine, copyText.ToArray());

                Clipboard.SetText(joined);
            }
        }

        private void CanExecuteCopy(object sender, CanExecuteRoutedEventArgs e)
        {
            if (lbOutput.SelectedItems.Count > 0)
            {
                e.CanExecute = true;
            }
            else
            {
                e.CanExecute = false;
            }
        }

        private void CanExecuteDeleteAll(object sender, CanExecuteRoutedEventArgs e)
        {
            if (lbOutput.Items.Count > 0)
            {
                e.CanExecute = true;
            }
            else
            {
                e.CanExecute = false;
            }
        }

        private void DeleteAllExecuted(object sender, ExecutedRoutedEventArgs e)
        {
            OutputText.Clear();
        }

        private void SelectAllExecuted(object sender, ExecutedRoutedEventArgs e)
        {
            lbOutput.SelectAll();
        }

        private void CanExecuteSelectAll(object sender, CanExecuteRoutedEventArgs e)
        {
            if (lbOutput.Items.Count > 0)
            {
                e.CanExecute = true;
            }
            else
            {
                e.CanExecute = false;
            }
        }

        #endregion
    }
}
