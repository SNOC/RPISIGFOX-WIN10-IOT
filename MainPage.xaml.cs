// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.ObjectModel;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.Devices.Enumeration;
using Windows.Devices.SerialCommunication;
using Windows.Storage.Streams;
using System.Threading;
using System.Threading.Tasks;

namespace SerialSample
{

    public static class StringExtensions
    {
        public static string ToHex(this string input)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            foreach (char c in input)
                sb.AppendFormat("{0:X2}", (int)c);
            return sb.ToString();
        }
    }

    public sealed partial class MainPage : Page
    {
        /// <summary>
        /// Private variables
        /// </summary>
        private SerialDevice serialPort = null;
        DataWriter dataWriteObject = null;
        DataReader dataReaderObject = null;

        private ObservableCollection<DeviceInformation> listOfDevices;
        private CancellationTokenSource ReadCancellationTokenSource;
       
        public MainPage()
        {
            this.InitializeComponent();            
            comPortInput.IsEnabled = false;
            sendTextButton.IsEnabled = false;
            listOfDevices = new ObservableCollection<DeviceInformation>();
            ListAvailablePorts();
        }

        /// <summary>
        /// ListAvailablePorts
        /// - Use SerialDevice.GetDeviceSelector to enumerate all serial devices
        /// - Attaches the DeviceInformation to the ListBox source so that DeviceIds are displayed
        /// </summary>
        private async void ListAvailablePorts()
        {
            try
            {
                string aqs = SerialDevice.GetDeviceSelector();
                var dis = await DeviceInformation.FindAllAsync(aqs);

                status.Text = "Select a device and connect";

                for (int i = 0; i < dis.Count; i++)
                {
                    listOfDevices.Add(dis[i]);
                }

                DeviceListSource.Source = listOfDevices;
                comPortInput.IsEnabled = true;
                ConnectDevices.SelectedIndex = -1;
            }
            catch (Exception ex)
            {
                status.Text = ex.Message;
            }
        }

        /// <summary>
        /// comPortInput_Click: Action to take when 'Connect' button is clicked
        /// - Get the selected device index and use Id to create the SerialDevice object
        /// - Configure default settings for the serial port
        /// - Create the ReadCancellationTokenSource token
        /// - Add text to rcvdText textbox to invoke rcvdText_TextChanged event
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void comPortInput_Click(object sender, RoutedEventArgs e)
        {
            var selection = ConnectDevices.SelectedItems;

            if (selection.Count <= 0)
            {
                status.Text = "Select a device and connect";
                return;
            }

            DeviceInformation entry = (DeviceInformation)selection[0];         

            try
            {                
                serialPort = await SerialDevice.FromIdAsync(entry.Id);

                // Disable the 'Connect' button 
                comPortInput.IsEnabled = false;

                // Configure serial settings
                serialPort.WriteTimeout = TimeSpan.FromMilliseconds(1000);
                serialPort.ReadTimeout = TimeSpan.FromMilliseconds(1000);                
                serialPort.BaudRate = 9600;
                serialPort.Parity = SerialParity.None;
                serialPort.StopBits = SerialStopBitCount.One;
                serialPort.DataBits = 8;

                // Display configured settings
                status.Text = "Serial port configured successfully!\n ----- Properties ----- \n";
                status.Text += "BaudRate: " + serialPort.BaudRate.ToString() + "\n";
                status.Text += "DataBits: " + serialPort.DataBits.ToString() + "\n";
                status.Text += "Handshake: " + serialPort.Handshake.ToString() + "\n";
                status.Text += "Parity: " + serialPort.Parity.ToString() + "\n";
                status.Text += "StopBits: " + serialPort.StopBits.ToString();

                statusNotConnectedLabel.Visibility = Visibility.Collapsed;
                statusConnectedLabel.Visibility = Visibility.Visible;
                statusOKLabel.Visibility = Visibility.Collapsed;
                statusFailLabel.Visibility = Visibility.Collapsed;

                // Create cancellation token object to close I/O operations when closing the device
                ReadCancellationTokenSource = new CancellationTokenSource();

                await sendString("AT");

                // Enable 'WRITE' button to allow sending data
                sendText_TextChanged(null, null);
            }
            catch (Exception ex)
            {
                status.Text = ex.Message;
                comPortInput.IsEnabled = true;
                sendTextButton.IsEnabled = false;
            }
        }

        /// <summary>
        /// sendTextButton_Click: Action to take when 'WRITE' button is clicked
        /// - Create a DataWriter object with the OutputStream of the SerialDevice
        /// - Create an async task that performs the write operation
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void sendTextButton_Click(object sender, RoutedEventArgs e)
        {
            sendTextButton.IsEnabled = false;
            await sendString("AT$SS=" + sendText.Text/*.ToHex()*/);
            sendTextButton.IsEnabled = true;
        }


        /// <summary>
        /// sendString: sends an ASCII hex-encoded encapsulated string over selected serial port
        /// - Create a DataWriter object with the OutputStream of the SerialDevice
        /// - Create an async task that performs the write operation
        /// </summary>
        /// <param name="s">String to send</param>
        private async Task sendString(string s)
        {
            try
            {
                if (serialPort != null)
                {
                    // Create the DataWriter object and attach to OutputStream
                    dataWriteObject = new DataWriter(serialPort.OutputStream);

                    //Launch the WriteAsync task to perform the write
                    await WriteAsync(s);

                    await receiveData();
                }
                else
                {
                    status.Text = "Select a device and connect";
                }
            }
            catch (Exception ex)
            {
                status.Text = "sendString: " + ex.Message;
            }
            finally
            {
                // Cleanup once complete
                if (dataWriteObject != null)
                {
                    dataWriteObject.DetachStream();
                    dataWriteObject = null;
                }
            }
        }

        /// <summary>
        /// WriteAsync: Task that asynchronously writes data from the input text box 'sendText' to the OutputStream 
        /// </summary>
        /// <returns></returns>
        private async Task WriteAsync(string s)
        {
            Task<UInt32> storeAsyncTask;

            if (s.Length != 0)
            {
                // Load the text from the sendText input text box to the dataWriter object
                dataWriteObject.WriteString(s + '\n');                

                // Launch an async task to complete the write operation
                storeAsyncTask = dataWriteObject.StoreAsync().AsTask();

                UInt32 bytesWritten = await storeAsyncTask;
                if (bytesWritten > 0)
                {                    
                    status.Text += "\nWrote \"" + s + "\" (" + bytesWritten + ")";
                }
                //sendText.Text = "";
            }
            else
            {
                status.Text = "Enter the text you want to write and then click on 'WRITE'";
            }
        }

        /// <summary>
        /// receiveData: Wait for incomming serial data
        /// - Create a DataReader object
        /// - Create an async task to read from the SerialDevice InputStream
        /// </summary>
        private async Task receiveData()
        {
            try
            {
                if (serialPort != null)
                {
                    dataReaderObject = new DataReader(serialPort.InputStream);
                    //ReadCancellationTokenSource.CancelAfter(7500);
                    await ReadAsync(ReadCancellationTokenSource.Token);
                }
            }
            catch(OperationCanceledException ex )
            {
                status.Text += "Timeout while reading";
            }
            catch (Exception ex)
            {
                if (ex.GetType().Name == "TaskCanceledException")
                {
                    status.Text += "Reading task was cancelled, closing device and cleaning up";
                    CloseDevice();
                }
                else
                {
                    status.Text += ex.Message;
                }
            }
            finally
            {
                // Cleanup once complete
                if (dataReaderObject != null)
                {
                    dataReaderObject.DetachStream();
                    dataReaderObject = null;
                }
            }
        }

        /// <summary>
        /// ReadAsync: Task that waits on data and reads asynchronously from the serial device InputStream
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        private async Task ReadAsync(CancellationToken cancellationToken)
        {
            Task<UInt32> loadAsyncTask;

            uint ReadBufferLength = 1024;

            // If task cancellation was requested, comply
            cancellationToken.ThrowIfCancellationRequested();

            Boolean loop = true;
            int counter = 7;
            while( loop && (--counter != 0) )
            {
                // Set InputStreamOptions to complete the asynchronous read operation when one or more bytes is available
                //dataReaderObject.InputStreamOptions = InputStreamOptions.Partial;
                dataReaderObject.InputStreamOptions = InputStreamOptions.ReadAhead;

                // Create a task object to wait for data on the serialPort.InputStream
                loadAsyncTask = dataReaderObject.LoadAsync(ReadBufferLength).AsTask(cancellationToken);

                // Launch the task and wait
                UInt32 bytesRead = await loadAsyncTask;
                string readString = "";
                while( dataReaderObject.UnconsumedBufferLength > 0 )
                {
                    char c = (char) dataReaderObject.ReadByte();
                    //status.Text += String.Format(" 0x{0:X2}", (int)c);
                    readString += c;
                }
                status.Text += "\nRead \"" + readString.Replace('\r', ' ').Replace('\n', ' ').Trim() + "\" (" + bytesRead + ")";
                if (readString.EndsWith("OK\r\n"))
                {
                    status.Text += " -> OK detected";
                    loop = false;
                    statusNotConnectedLabel.Visibility = Visibility.Collapsed;
                    statusConnectedLabel.Visibility = Visibility.Collapsed;
                    statusOKLabel.Visibility = Visibility.Visible;
                    statusFailLabel.Visibility = Visibility.Collapsed;
                }
                else if (readString.Contains("\r\nER"))
                {
                    status.Text += " -> ERROR detected";
                    loop = false;
                    // OK was not found
                    statusNotConnectedLabel.Visibility = Visibility.Collapsed;
                    statusConnectedLabel.Visibility = Visibility.Collapsed;
                    statusOKLabel.Visibility = Visibility.Collapsed;
                    statusFailLabel.Visibility = Visibility.Visible;
                }

            }
        }

        /// <summary>
        /// CancelReadTask:
        /// - Uses the ReadCancellationTokenSource to cancel read operations
        /// </summary>
        private void CancelReadTask()
        {         
            if (ReadCancellationTokenSource != null)
            {
                if (!ReadCancellationTokenSource.IsCancellationRequested)
                {
                    ReadCancellationTokenSource.Cancel();
                }
            }         
        }

        /// <summary>
        /// CloseDevice:
        /// - Disposes SerialDevice object
        /// - Clears the enumerated device Id list
        /// </summary>
        private void CloseDevice()
        {            
            if (serialPort != null)
            {
                serialPort.Dispose();
            }
            serialPort = null;

            comPortInput.IsEnabled = true;
            sendTextButton.IsEnabled = false;
            statusNotConnectedLabel.Visibility = Visibility.Visible;
            statusConnectedLabel.Visibility = Visibility.Collapsed;
            statusOKLabel.Visibility = Visibility.Collapsed;
            statusFailLabel.Visibility = Visibility.Collapsed;
            listOfDevices.Clear();               
        }

        /// <summary>
        /// closeDevice_Click: Action to take when 'Disconnect and Refresh List' is clicked on
        /// - Cancel all read operations
        /// - Close and dispose the SerialDevice object
        /// - Enumerate connected devices
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void closeDevice_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                status.Text = "";
                CancelReadTask();
                CloseDevice();
                ListAvailablePorts();
            }
            catch (Exception ex)
            {
                status.Text = ex.Message;
            }          
        }

        private void sendText_TextChanged(object sender, TextChangedEventArgs e)
        {
            if( System.Text.RegularExpressions.Regex.IsMatch(sendText.Text, "^([0-9A-F][0-9A-F]){1,12}$", System.Text.RegularExpressions.RegexOptions.IgnoreCase) )
            {
                sendTextButton.IsEnabled = true;
                sendLabel.Text = "";
            } else
            {
                sendTextButton.IsEnabled = false;
                if( !System.Text.RegularExpressions.Regex.IsMatch(sendText.Text, "^([0-9A-F])+$", System.Text.RegularExpressions.RegexOptions.IgnoreCase) )
                {
                    sendLabel.Text = "(only 0..9 and A..F characters allowed)";
                } else if( sendText.Text.Length % 2 == 1 )
                {
                    sendLabel.Text = "(even number of digits required for bytes)";
                } else
                {
                    sendLabel.Text = "(12 bytes maximum per message)";
                }
            }
            
        }

        public static T FindDescendant<T>(DependencyObject obj) where T : DependencyObject
        {
            if (obj == null) return default(T);
            int numberChildren = Windows.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(obj);
            if (numberChildren == 0) return default(T);

            for (int i = 0; i < numberChildren; i++)
            {
                DependencyObject child = Windows.UI.Xaml.Media.VisualTreeHelper.GetChild(obj, i);
                if (child is T)
                {
                    return (T)(object)child;
                }
            }

            for (int i = 0; i < numberChildren; i++)
            {
                DependencyObject child = Windows.UI.Xaml.Media.VisualTreeHelper.GetChild(obj, i);
                var potentialMatch = FindDescendant<T>(child);
                if (potentialMatch != default(T))
                {
                    return potentialMatch;
                }
            }

            return default(T);
        }
    }
}
