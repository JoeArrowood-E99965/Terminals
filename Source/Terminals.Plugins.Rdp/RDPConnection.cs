using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using AxMSTSCLib;
using System.IO;
using Terminals.Configuration;
using Terminals.Data;
using Terminals.Forms.Controls;
using System.Text;
using MSTSCLib;
using Terminals.TerminalServices;
using IMsTscAxEvents_OnDisconnectedEventHandler = AxMSTSCLib.IMsTscAxEvents_OnDisconnectedEventHandler;
using IMsTscAxEvents_OnFatalErrorEventHandler = AxMSTSCLib.IMsTscAxEvents_OnFatalErrorEventHandler;
using IMsTscAxEvents_OnLogonErrorEventHandler = AxMSTSCLib.IMsTscAxEvents_OnLogonErrorEventHandler;
using IMsTscAxEvents_OnWarningEventHandler = AxMSTSCLib.IMsTscAxEvents_OnWarningEventHandler;

//http://msdn.microsoft.com/en-us/library/aa381172(v=vs.85).aspx
//http://msdn.microsoft.com/en-us/library/aa380838(v=VS.85).aspx
//http://msdn.microsoft.com/en-us/library/aa380847(v=VS.85).aspx
//http://msdn.microsoft.com/en-us/library/bb892063(v=VS.85).aspx
//http://msdn.microsoft.com/en-us/library/ee338625(v=VS.85).aspx

namespace Terminals.Connections
{
    internal class RDPConnection : Connection, IConnectionExtra, ISettingsConsumer, IHandleKeyboardInput
    {
        private readonly ReconnectingControl reconecting = new ReconnectingControl();
        private readonly ConnectionStateDetector connectionStateDetector = new ConnectionStateDetector();

        private IMsRdpClientNonScriptable4 nonScriptable;
        private AxMsRdpClient6NotSafeForScripting client = null;

        private readonly RdpService service = new RdpService();

        public TerminalServer Server { get { return service.Server; } }

        public bool IsTerminalServer { get { return service.IsTerminalServer; } }

        public IConnectionSettings Settings { get; set; }

        public bool GrabInput { get; set; }

        // ------------------------------------------------

        bool IConnectionExtra.FullScreen
        {
            get
            {
                if(client != null)
                {
                    return client.FullScreen;
                }

                return false;
            }
            set
            {
                if(client != null)
                {
                    client.FullScreen = value;
                }
            }
        }

        // ------------------------------------------------

        string IConnectionExtra.Server
        {
            get
            {
                if(client != null)
                {
                    return client.Server;
                }

                return String.Empty;
            }
        }

        // ------------------------------------------------

        string IConnectionExtra.UserName
        {
            get
            {
                if(client != null)
                {
                    return client.UserName;
                }

                return String.Empty;
            }
        }

        // ------------------------------------------------

        string IConnectionExtra.Domain
        {
            get
            {
                if(client != null)
                {
                    return client.Domain;
                }

                return String.Empty;
            }
        }

        // ------------------------------------------------

        bool IConnectionExtra.ConnectToConsole
        {
            get
            {
                if(client != null)
                {
                    return client.AdvancedSettings3.ConnectToServerConsole;
                }

                return false;
            }
        }

        // ------------------------------------------------

        bool IFocusable.ContainsFocus
        {
            get
            {
                if(client != null)
                {
                    return client.ContainsFocus;
                }

                return false;
            }
        }

        // ------------------------------------------------

        void IFocusable.Focus()
        {
            if(client != null)
            {
                client.Focus();
            }
        }

        // ------------------------------------------------

        private bool ClientConnected
        {
            get
            {
                if(client != null)
                {
                    return Convert.ToBoolean(client.Connected);
                }

                return false;
            }
        }

        // ------------------------------------------------

        public override bool Connected
        {
            get
            {
                // dont let the connection to close with running reconnection
                
                return ClientConnected || connectionStateDetector.IsRunning;
            }
        }

        // ------------------------------------------------

        public override bool Connect()
        {
            try
            {
                if(!InitializeClientControl())
                {
                    return false;
                }

                ConfigureCientUserControl();
                ChangeDesktopSize(Favorite.Display.DesktopSize);

                try
                {
                    client.ConnectingText = "Connecting. Please wait...";
                    client.DisconnectedText = "Disconnecting...";

                    var rdpOptions = Favorite.ProtocolProperties as RdpOptions;

                    ConfigureColorsDepth();
                    ConfigureRedirectedDrives(rdpOptions);
                    ConfigureInterface(rdpOptions);
                    ConfigureStartBehaviour(rdpOptions);
                    ConfigureTimeouts(rdpOptions);
                    ConfigureRedirectOptions(rdpOptions);
                    ConfigureConnectionBar(rdpOptions);
                    ConfigureTsGateway(rdpOptions);
                    ConfigureSecurity(rdpOptions);
                    ConfigureConnection(rdpOptions);
                    AssignEventHandlers();

                    Text = "Connecting to RDP Server...";
                }
                catch(Exception exc)
                {
                    Logging.Info("There was an exception setting an RDP Value.", exc);
                }
                
                // if next line fails on Protected memory access exception,
                // some string property is set to null, which leads to this exception
            
                client.Connect();

                service.CheckForTerminalServer(Favorite);
                return true;
            }
            catch(Exception exc)
            {
                Logging.Fatal("Connecting to RDP", exc);
                return false;
            }
        }

        // ------------------------------------------------

        private bool InitializeClientControl()
        {
            try
            {
                client = new AxMsRdpClient6NotSafeForScripting();
            }
            catch(Exception exception)
            {
                string message = "Please update your RDP client to at least version 6.";
                Logging.Info(message, exception);
                MessageBox.Show(message);
                return false;
            }
            
            return true;
        }

        // ------------------------------------------------

        private void ConfigureCientUserControl()
        {
            var clientControl = (Control)client;
            Controls.Add(clientControl);
            client.CreateControl();
            nonScriptable = client.GetOcx() as IMsRdpClientNonScriptable4;
            client.BringToFront();
            BringToFront();
            client.Parent = Parent;
            client.AllowDrop = true;
            client.Dock = DockStyle.Fill;
            ConfigureReconnect();
        }

        // ------------------------------------------------

        private void ConfigureReconnect()
        {
            // if not added to the client control controls collection, then it isnt visible
            
            var clientControl = (Control)client;
            clientControl.Controls.Add(reconecting);
            reconecting.Hide();
            reconecting.AbortReconnectRequested += new EventHandler(Recoonecting_AbortReconnectRequested);
            connectionStateDetector.AssignFavorite(Favorite);
            connectionStateDetector.ReconnectExpired += ConnectionStateDetectorOnReconnectExpired;
            connectionStateDetector.Reconnected += ConnectionStateDetectorOnReconnected;
        }

        // ------------------------------------------------

        private void ConnectionStateDetectorOnReconnected(object sender, EventArgs eventArgs)
        {
            if(reconecting.InvokeRequired)
            {
                reconecting.Invoke(new EventHandler(ConnectionStateDetectorOnReconnected), new object[] { sender, eventArgs });
            }
            else
            {
                Reconnect();
            }
        }

        // ------------------------------------------------

        private void Reconnect()
        {
            if(!reconecting.Reconnect) return;
            StopReconnect();
            reconecting.Reconnect = false;
            client.Connect();
        }

        // ------------------------------------------------

        private void ConnectionStateDetectorOnReconnectExpired(object sender, EventArgs eventArgs)
        {
            CancelReconnect();
        }

        // ------------------------------------------------

        private void Recoonecting_AbortReconnectRequested(object sender, EventArgs e)
        {
            CancelReconnect();
        }

        // ------------------------------------------------

        private void CancelReconnect()
        {
            if(reconecting.InvokeRequired)
            {
                reconecting.Invoke(new Action(CancelReconnect));
            }
            else
            {
                StopReconnect();
                FireDisconnected();
            }
        }

        // ------------------------------------------------

        private void StopReconnect()
        {
            connectionStateDetector.Stop();
            reconecting.Hide();
            
            if(reconecting.Disable)
            {
                Settings.AskToReconnect = false;
            }
        }

        // ------------------------------------------------

        public override void ChangeDesktopSize(DesktopSize desktopSize)
        {
            Size size = DesktopSizeCalculator.GetSize(this, Favorite);

            try
            {
                switch(desktopSize)
                {
                    case DesktopSize.AutoScale:
                    case DesktopSize.FitToWindow:
                        client.AdvancedSettings3.SmartSizing = true;
                        break;
                
                    case DesktopSize.FullScreen:
                        client.FullScreen = true;
                        break;
                }

                client.DesktopWidth = size.Width;
                client.DesktopHeight = size.Height;
            }
            catch(Exception exc)
            {
                Logging.Error("Error trying to set the desktop dimensions", exc);
            }
        }

        // ------------------------------------------------

        private void ConfigureColorsDepth()
        {
            switch(Favorite.Display.Colors)
            {
                case Colors.Bits8:
                    client.ColorDepth = 8;
                    break;
                
                case Colors.Bit16:
                    client.ColorDepth = 16;
                    break;
                
                case Colors.Bits24:
                    client.ColorDepth = 24;
                    break;
             
                case Colors.Bits32:
                    client.ColorDepth = 32;
                    break;
            }
        }

        // ------------------------------------------------

        private void ConfigureRedirectedDrives(RdpOptions rdpOptions)
        {
            if(rdpOptions.Redirect.Drives.Count > 0 && rdpOptions.Redirect.Drives[0].Equals("true"))
            {
                client.AdvancedSettings2.RedirectDrives = true;
            }
            else
            {
                for(int i = 0; i < nonScriptable.DriveCollection.DriveCount; i++)
                {
                    IMsRdpDrive drive = nonScriptable.DriveCollection.get_DriveByIndex((uint)i);

                    foreach(string str in rdpOptions.Redirect.Drives)
                    {
                        if(drive.Name.IndexOf(str) > -1)
                            drive.RedirectionState = true;
                    }
                }
            }
        }

        // ------------------------------------------------

        private void ConfigureInterface(RdpOptions rdpOptions)
        {
            //advanced settings
            //bool, 0 is false, other is true
            
            if(rdpOptions.UserInterface.AllowBackgroundInput)
            {
                client.AdvancedSettings.allowBackgroundInput = -1;
            }

            if(rdpOptions.UserInterface.BitmapPeristence)
            {
                client.AdvancedSettings.BitmapPeristence = -1;
            }

            if(rdpOptions.UserInterface.EnableCompression)
            {
                client.AdvancedSettings.Compress = -1;
            }

            if(rdpOptions.UserInterface.AcceleratorPassthrough)
            {
                client.AdvancedSettings2.AcceleratorPassthrough = -1;
            }

            if(rdpOptions.UserInterface.DisableControlAltDelete)
            {
                client.AdvancedSettings2.DisableCtrlAltDel = -1;
            }

            if(rdpOptions.UserInterface.DisplayConnectionBar)
            {
                client.AdvancedSettings2.DisplayConnectionBar = true;
            }

            if(rdpOptions.UserInterface.DoubleClickDetect)
            {
                client.AdvancedSettings2.DoubleClickDetect = -1; 
            }

            if(rdpOptions.UserInterface.DisableWindowsKey)
            {
                client.AdvancedSettings2.EnableWindowsKey = -1;
            }

            if(rdpOptions.Security.EnableEncryption)
            {
                client.AdvancedSettings2.EncryptionEnabled = -1;
            }


            ConfigureAzureService(rdpOptions);
            ConfigureCustomReconnect();
        }

        // ------------------------------------------------
        /// <summary>
        /// The ActiveX component requires a UTF-8 encoded string, but .NET uses
        /// UTF-16 encoded strings by default.  The following code converts
        /// the UTF-16 encoded string so that the byte-representation of the
        /// LoadBalanceInfo string object will "appear" as UTF-8 to the Active component.
        /// Furthermore, since the final string still has to be shoehorned into
        /// a UTF-16 encoded string, I pad an extra space in case the number of
        /// bytes would be odd, in order to prevent the byte conversion from
        /// mangling the string at the end.  The space is ignored by the RDP
        /// protocol as long as it is inserted at the end.
        /// Finally, it is required that the LoadBalanceInfo setting is postfixed
        /// with \r\n in order to work properly.  Note also that \r\n MUST be
        /// the last two characters, so the space padding has to be inserted first.
        /// The following code has been tested with Windows Azure connections
        /// only - I am aware there are other types of RDP connections that
        /// require the LoadBalanceInfo parameter which I have not tested
        /// (e.g., Multi-Server Terminal Services Gateway), that may or may not
        /// work properly.
        ///
        /// Sources:
        ///  1. http://stackoverflow.com/questions/13536267/how-to-connect-to-azure-vm-with-remote-desktop-activex
        ///  2. http://social.technet.microsoft.com/Forums/windowsserver/en-US/e68d4e9a-1c8a-4e55-83b3-e3b726ff5346/issue-with-using-advancedsettings2loadbalanceinfo
        ///  3. Manual comparison of raw packets between Windows RDP client and Terminals using WireShark.
        /// </summary>
        
        private void ConfigureAzureService(RdpOptions rdpOptions)
        {
            if(!String.IsNullOrEmpty(rdpOptions.UserInterface.LoadBalanceInfo))
            {
                var lbTemp = rdpOptions.UserInterface.LoadBalanceInfo;

                if(lbTemp.Length % 2 == 1)
                {
                    lbTemp += " ";
                }

                lbTemp += "\r\n";
                byte[] bytes = Encoding.UTF8.GetBytes(lbTemp);
                string lbFinal = Encoding.Unicode.GetString(bytes);
                client.AdvancedSettings2.LoadBalanceInfo = lbFinal;
            }
        }

        private void ConfigureCustomReconnect()
        {
            client.AdvancedSettings3.EnableAutoReconnect = false;
            client.AdvancedSettings3.MaxReconnectAttempts = 0;
            client.AdvancedSettings3.keepAliveInterval = 0;
        }

        // ------------------------------------------------

        private void ConfigureStartBehaviour(RdpOptions rdpOptions)
        {
            client.AdvancedSettings2.GrabFocusOnConnect = rdpOptions.GrabFocusOnConnect;
            GrabInput = rdpOptions.GrabFocusOnConnect;

            if(rdpOptions.Security.Enabled)
            {
                if(rdpOptions.FullScreen)
                {
                    client.SecuredSettings2.FullScreen = -1;
                }

                client.SecuredSettings2.StartProgram = rdpOptions.Security.StartProgram;
                client.SecuredSettings2.WorkDir = rdpOptions.Security.WorkingFolder;
            }
        }

        // ------------------------------------------------

        private void ConfigureTimeouts(RdpOptions rdpOptions)
        {
            try
            {
                client.AdvancedSettings2.MinutesToIdleTimeout = rdpOptions.TimeOuts.IdleTimeout;

                int timeout = rdpOptions.TimeOuts.OverallTimeout;

                if(timeout > 600)
                {
                    timeout = 10;
                }

                if(timeout <= 0)
                {
                    timeout = 10;
                }
                
                client.AdvancedSettings2.overallConnectionTimeout = timeout;
                timeout = rdpOptions.TimeOuts.ConnectionTimeout;

                if(timeout > 600)
                {
                    timeout = 10;
                }

                if(timeout <= 0)
                {
                    timeout = 10;
                }

                client.AdvancedSettings2.singleConnectionTimeout = timeout;

                timeout = rdpOptions.TimeOuts.ShutdownTimeout;
                
                if(timeout > 600)
                {
                    timeout = 10;
                }
                
                if(timeout <= 0)
                {
                    timeout = 10;
                }

                client.AdvancedSettings2.shutdownTimeout = timeout;
            }
            catch(Exception exc)
            {
                Logging.Error("Error when trying to set timeout values.", exc);
            }
        }

        // ------------------------------------------------

        private void ConfigureRedirectOptions(RdpOptions rdpOptions)
        {
            client.AdvancedSettings3.RedirectPorts = rdpOptions.Redirect.Ports;
            client.AdvancedSettings3.RedirectPrinters = rdpOptions.Redirect.Printers;
            client.AdvancedSettings3.RedirectSmartCards = rdpOptions.Redirect.SmartCards;
            client.AdvancedSettings3.PerformanceFlags = rdpOptions.UserInterface.PerformanceFlags;
            client.AdvancedSettings6.RedirectClipboard = rdpOptions.Redirect.Clipboard;
            client.AdvancedSettings6.RedirectDevices = rdpOptions.Redirect.Devices;
        }

        // ------------------------------------------------

        private void ConfigureConnectionBar(RdpOptions rdpOptions)
        {
            client.AdvancedSettings6.ConnectionBarShowMinimizeButton = false;
            client.AdvancedSettings6.ConnectionBarShowPinButton = false;
            client.AdvancedSettings6.ConnectionBarShowRestoreButton = false;
            client.AdvancedSettings3.DisplayConnectionBar = rdpOptions.UserInterface.DisplayConnectionBar;
        }

        // ------------------------------------------------

        private void ConfigureTsGateway(RdpOptions rdpOptions)
        {
            // Terminal Server Gateway Settings
            
            client.TransportSettings.GatewayUsageMethod = (uint)rdpOptions.TsGateway.UsageMethod;
            client.TransportSettings.GatewayCredsSource = (uint)rdpOptions.TsGateway.CredentialSource;
            client.TransportSettings.GatewayHostname = rdpOptions.TsGateway.HostName;
            var tsgwGuarded = CredentialFactory.CreateCredential(rdpOptions.TsGateway.Security);
            client.TransportSettings2.GatewayDomain = tsgwGuarded.Domain;
            client.TransportSettings2.GatewayProfileUsageMethod = 1;
            var security = ResolveTransportGatewayCredentials(rdpOptions);
            IGuardedSecurity guarded = CredentialFactory.CreateSecurityOptoins(security);
            client.TransportSettings2.GatewayDomain = guarded.Domain;
            client.TransportSettings2.GatewayUsername = guarded.UserName;
            client.TransportSettings2.GatewayPassword = guarded.Password;
        }

        // ------------------------------------------------

        private ISecurityOptions ResolveTransportGatewayCredentials(RdpOptions rdpOptions)
        {
            if(rdpOptions.TsGateway.SeparateLogin)
            {
                return rdpOptions.TsGateway.Security;
            }

            return Favorite.Security;
        }

        // ------------------------------------------------

        private void ConfigureSecurity(RdpOptions rdpOptions)
        {
            if(rdpOptions.Security.EnableTLSAuthentication)
                client.AdvancedSettings5.AuthenticationLevel = 2;

            nonScriptable.EnableCredSspSupport = rdpOptions.Security.EnableNLAAuthentication;

            var audioMode = (int)rdpOptions.Redirect.Sounds;
            client.SecuredSettings2.AudioRedirectionMode = (audioMode >= 0 && audioMode <= 2) ? audioMode : 0;

            IGuardedSecurity security = ResolveFavoriteCredentials();

            client.UserName = security.UserName;
            client.Domain = security.Domain;

            try
            {
                if(!string.IsNullOrEmpty(security.Password) && nonScriptable != null)
                {
                    nonScriptable.ClearTextPassword = security.Password;
                }
            }
            catch(Exception exc)
            {
                Logging.Error("Error when trying to set the ClearTextPassword on the nonScriptable mstsc object", exc);
            }
        }

        // ------------------------------------------------

        private void ConfigureConnection(RdpOptions rdpOptions)
        {
            client.Server = Favorite.ServerName;
            client.AdvancedSettings3.RDPPort = Favorite.Port;
            client.AdvancedSettings3.ContainerHandledFullScreen = -1;

            // Use ConnectToServerConsole or ConnectToAdministerServer based on implementation

            client.AdvancedSettings7.ConnectToAdministerServer = rdpOptions.ConnectToConsole;
            client.AdvancedSettings3.ConnectToServerConsole = rdpOptions.ConnectToConsole;
        }

        // ------------------------------------------------

        private void AssignEventHandlers()
        {
            client.OnRequestLeaveFullScreen += new EventHandler(client_OnRequestLeaveFullScreen);
            client.OnDisconnected += new IMsTscAxEvents_OnDisconnectedEventHandler(client_OnDisconnected);
            client.OnWarning += new IMsTscAxEvents_OnWarningEventHandler(client_OnWarning);
            client.OnFatalError += new IMsTscAxEvents_OnFatalErrorEventHandler(client_OnFatalError);
            client.OnLogonError += new IMsTscAxEvents_OnLogonErrorEventHandler(client_OnLogonError);
            client.OnConnected += new EventHandler(client_OnConnected);


            // assign the drag and drop event handlers directly throws an exception

            var clientControl = (Control)client;
            clientControl.DragEnter += new DragEventHandler(client_DragEnter);
            clientControl.DragDrop += new DragEventHandler(client_DragDrop);
        }

        // ------------------------------------------------

        private void client_OnConnected(object sender, EventArgs e)
        {
            // setting the full screen directly in constructor may affect screen resolution changes

            client.FullScreen = true;
        }

        // ------------------------------------------------

        protected override void Dispose(bool disposing)
        {
            if(disposing)
            {
                connectionStateDetector.Dispose();
                client.Dispose();
                client = null;
            }

            base.Dispose(disposing);
        }

        // ------------------------------------------------

        private void client_DragDrop(object sender, DragEventArgs e)
        {
            string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
            string desktopShare = ParentForm.GetDesktopShare();

            if(String.IsNullOrEmpty(desktopShare))
            {
                MessageBox.Show(this, "A Desktop Share was not defined for this connection.\n" +
                    "Please define a share in the connection properties window (under the Local Resources tab)."
                    , "Terminals", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
            }
            else
            {
                SHCopyFiles(files, desktopShare);
            }
        }

        // ------------------------------------------------

        private void SHCopyFiles(string[] sourceFiles, string destinationFolder)
        {
            SHFileOperationWrapper fo = new SHFileOperationWrapper();
            List<string> destinationFiles = new List<string>();

            foreach(string sourceFile in sourceFiles)
            {
                destinationFiles.Add(Path.Combine(destinationFolder, Path.GetFileName(sourceFile)));
            }

            fo.Operation = SHFileOperationWrapper.FileOperations.FO_COPY;
            fo.OwnerWindow = Handle;
            fo.SourceFiles = sourceFiles;
            fo.DestFiles = destinationFiles.ToArray();
            fo.DoOperation();
        }

        // ------------------------------------------------

        private void client_DragEnter(object sender, DragEventArgs e)
        {
            if(e.Data.GetDataPresent(DataFormats.FileDrop, false))
            {
                e.Effect = DragDropEffects.Copy;
            }
            else
            {
                e.Effect = DragDropEffects.None;
            }
        }

        // ------------------------------------------------

        private void client_OnRequestLeaveFullScreen(object sender, EventArgs e)
        {
            ParentForm.OnLeavingFullScreen();
        }

        // ------------------------------------------------

        private void client_OnDisconnected(object sender, IMsTscAxEvents_OnDisconnectedEvent e)
        {
            if(DecideToReconnect(e))
            {
                TryReconnect();
            }
            else
            {
                ShowDisconnetMessageBox(e);
                FireDisconnected();
            }
        }

        // ------------------------------------------------

        private bool DecideToReconnect(IMsTscAxEvents_OnDisconnectedEvent e)
        {
            // 516 reason in case of reconnect expired
            // 2308 connection lost
            // 2 - regular logof also in case of forced reboot or shutdown

            if(e.discReason != 2308 && e.discReason != 2)
            {
                return false;
            }

            return Settings.AskToReconnect;
        }

        // ------------------------------------------------

        private void TryReconnect()
        {
            reconecting.Show();
            reconecting.BringToFront();
            connectionStateDetector.Start();
        }

        // ------------------------------------------------

        private void ShowDisconnetMessageBox(IMsTscAxEvents_OnDisconnectedEvent e)
        {
            int reason = e.discReason;
            string error = RdpClientErrorMessages.ToDisconnectMessage(client, reason);

            if(!String.IsNullOrEmpty(error))
            {
                string message = String.Format("Error connecting to {0}\n\n{1}", client.Server, error);
                MessageBox.Show(this, message, "Terminals", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
            }
        }

        // ------------------------------------------------

        private void client_OnFatalError(object sender, IMsTscAxEvents_OnFatalErrorEvent e)
        {
            int errorCode = e.errorCode;
            string message = RdpClientErrorMessages.ToFatalErrorMessage(errorCode);
            string finalMsg = String.Format("There was a fatal error returned from the RDP Connection, details:\n\nError Code:{0}\n\nError Description:{1}", errorCode, message);
            MessageBox.Show(finalMsg);
            Logging.Fatal(finalMsg);
        }

        // ------------------------------------------------

        private void client_OnWarning(object sender, IMsTscAxEvents_OnWarningEvent e)
        {
            int warningCode = e.warningCode;
            string message = RdpClientErrorMessages.ToWarningMessage(warningCode);
            string finalMsg = String.Format("There was a warning returned from the RDP Connection, details:\n\nWarning Code:{0}\n\nWarning Description:{1}", warningCode, message);
            Logging.Warn(finalMsg);
        }

        // ------------------------------------------------

        private void client_OnLogonError(object sender, IMsTscAxEvents_OnLogonErrorEvent e)
        {
            int errorCode = e.lError;
            string message = RdpClientErrorMessages.ToLogonMessage(errorCode);
            string finalMsg = String.Format("There was a logon error returned from the RDP Connection, details:\n\nLogon Code:{0}\n\nLogon Description:{1}", errorCode, message);
            Logging.Error(finalMsg);
        }
    }
}
