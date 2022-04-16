using System;
using System.Net;
using System.Threading;
using System.Windows.Forms;
using System.Collections.Generic;

using Metro;
using Metro.Scanning;

using Terminals.Data;
using Terminals.Connections;
using Terminals.Integration.Import;

namespace Terminals.Wizard
{
    internal partial class AddExistingRDPConnections : UserControl
    {
        private MethodInvoker _miv;
        private NetworkInterfaceList _nil;
        private IPAddress _endPointAddress;
        private List<TcpSynScanner> _scannerList = new List<TcpSynScanner>(1275);
        private int _scannerCount = 0;
        private int _pendingRequests = 0;
        private object _uiElementsLock = new object();

        private ConnectionManager _connectionManager;

        public delegate void DiscoveryCompleted();
        public event DiscoveryCompleted OnDiscoveryCompleted;

        public List<FavoriteConfigurationElement> DiscoveredConnections { get; private set; }

        // ------------------------------------------------

        public AddExistingRDPConnections()
        {
            DiscoveredConnections = new List<FavoriteConfigurationElement>();

            InitializeComponent();

            dataGridView1.Visible = false;
            _miv = new MethodInvoker(UpdateConnections);

            try
            {
                _nil = new NetworkInterfaceList();
            }
            catch(Exception exc)
            {
                Logging.Error("Could not new up Metro.NetworkInterfaceList in AddExistingRDPConnections", exc);
            }
        }

        // ------------------------------------------------

        public void StartImport(ConnectionManager connectionManager)
        {
            _connectionManager = connectionManager;
            ImportFromRegistry();
            ScanInterfaceList();
        }

        // ------------------------------------------------

        private void ImportFromRegistry()
        {
            List<FavoriteConfigurationElement> favoritesFromRegistry = ImportRdpRegistry.Import(_connectionManager);

            lock(DiscoveredConnections)
            {
                DiscoveredConnections.AddRange(favoritesFromRegistry);
            }
        }

        /// -----------------------------------------------
        /// <summary>
        ///     then kick up the port scan for the entire subnet
        /// </summary>

        private void ScanInterfaceList()
        {
            if(_nil != null)
            {
                try
                {
                    foreach(NetworkInterface face in _nil.Interfaces)
                    {
                        if(face.IsEnabled && !face.isLoopback)
                        {
                            _endPointAddress = face.Address;
                            break;
                        }
                    }

                    ThreadPool.QueueUserWorkItem(new WaitCallback(ScanSubnet), null);
                }
                catch(Exception e)
                {
                    Logging.Error("Port Scan error", e);
                }
            }
        }

        // ------------------------------------------------

        internal void CancelDiscovery()
        {
            try
            {
                if(_scannerCount > 0)
                {
                    foreach(TcpSynScanner scanner in _scannerList)
                    {
                        if(scanner.Running)
                            scanner.CancelScan();
                    }
                }
            }
            catch(Exception e)
            {
                Logging.Info("Cancel Discovery", e);
            }
        }

        // ------------------------------------------------

        private void Scanner_ScanComplete()
        {
            try
            {
                lock(_uiElementsLock)
                {
                    _scannerCount--;
                }

                Invoke(_miv);
            }
            catch(Exception e)
            {
                Logging.Error("Scanner Complete Error", e);
            }
        }

        // ------------------------------------------------

        private void ScanSubnet(object nullstate)
        {
            try
            {
                _pendingRequests = 254 * 5;
                string ipAddress = _endPointAddress.ToString();
                string start = ipAddress.Substring(0, ipAddress.LastIndexOf('.')) + ".";

                for(int x = 1; x < 255; x++)
                {
                    IPAddress address = IPAddress.Parse(start + x.ToString());
                    ThreadPool.QueueUserWorkItem(new WaitCallback(ScanMachine), address);
                }
            }
            catch(Exception e)
            {
                Logging.Error("Scan Subnet Error", e);
            }

            Invoke(_miv);
        }

        // ------------------------------------------------

        private void ScanMachine(object machine)
        {
            try
            {
                TcpSynScanner scanner;
                scanner = new TcpSynScanner(new IPEndPoint(_endPointAddress, 0));
                scanner.PortReply += new TcpPortReplyHandler(Scanner_PortReply);
                scanner.ScanComplete += new TcpPortScanComplete(Scanner_ScanComplete);

                IPAddress address = (IPAddress) machine;
                _scannerList.Add(scanner);
                scanner.StartScan(address, _connectionManager.SupportedPorts(), 1000, 100, true);
                _scannerCount++;
            }
            catch(Exception)
            {
                // its safe to ignore exceptions here as well  

                lock(_uiElementsLock)
                {
                    _pendingRequests = _pendingRequests - 5;
                }
            }

            if(!IsDisposed)
            {
                Invoke(_miv);
            }

            Application.DoEvents();
        }

        // ------------------------------------------------

        private void Scanner_PortReply(IPEndPoint remoteEndPoint, TcpPortState state)
        {
            try
            {
                lock(_uiElementsLock) _pendingRequests--;

                if(state == TcpPortState.Opened)
                {
                    AddFavorite(remoteEndPoint);
                }

                Invoke(_miv);
            }
            catch(Exception e)
            {
                Logging.Error("Scanner Port Reply", e);
            }
        }

        // ------------------------------------------------

        private void AddFavorite(IPEndPoint endPoint)
        {
            try
            {
                string protocol = _connectionManager.GetPortName(endPoint.Port);
                string serverName = endPoint.Address.ToString();
                string connectionName = String.Format("{0}_{1}", serverName, protocol);
                FavoriteConfigurationElement newFavorite =
                    FavoritesFactory.CreateNewFavorite(_connectionManager, connectionName, serverName, endPoint.Port);

                AddFavoriteToDiscovered(newFavorite);
            }
            catch(Exception e)
            {
                Logging.Error("Add Favorite Error", e);
            }
        }

        // ------------------------------------------------

        private void AddFavoriteToDiscovered(FavoriteConfigurationElement newFavorite)
        {
            lock(DiscoveredConnections)
            {
                DiscoveredConnections.Add(newFavorite);
            }
        }

        // ------------------------------------------------

        private void UpdateConnections()
        {
            try
            {
                ConnectionsCountLabel.Text = DiscoveredConnections.Count.ToString();
                PendingRequestsLabel.Text = _pendingRequests.ToString();

                if(_pendingRequests <= 0 && OnDiscoveryCompleted != null)
                {
                    OnDiscoveryCompleted();
                }

                Application.DoEvents();
            }
            catch(Exception e)
            {
                Logging.Error("Update Connections", e);
            }
        }

        /// -----------------------------------------------
        /// <summary>
        ///     hidden egg to show the connections.  
        ///     Just click on the connections count label to show and update the list
        /// </summary>

        private void ConnectionsCountLabel_Click(object sender, EventArgs e)
        {
            try
            {
                List<BindingElement> list = new List<BindingElement>();
                foreach(FavoriteConfigurationElement elm in DiscoveredConnections)
                {
                    BindingElement be = new BindingElement();
                    be.Element = string.Format("{0}:{1}", elm.ServerName, elm.Protocol);
                    list.Add(be);
                }

                dataGridView1.DataSource = list;
                dataGridView1.Visible = true;
                Application.DoEvents();
            }
            catch(Exception exc)
            {
                Logging.Info("Connections Count Label", exc);
            }
        }
    }

    /// -----------------------------------------------
    /// <summary>
    ///     Data grid binding class to show discovered connection name in grid
    /// </summary>

    internal class BindingElement
    {
        public string Element { get; set; }
    }
}