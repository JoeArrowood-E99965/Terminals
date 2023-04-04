using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using TabControl;
using Terminals.Data;
using Terminals.Forms;
using Terminals.Forms.Controls;
using Terminals.Forms.Rendering;
using Terminals.CommandLine;
using Terminals.Configuration;
using Terminals.Connections;
using Terminals.Credentials;
using Terminals.Native;
using Terminals.Services;
using Terminals.Updates;
using Settings = Terminals.Configuration.Settings;

namespace Terminals
{
    internal partial class MainForm : Form, IConnectionMainView, IConnectionCommands
    {
        private readonly IPersistence _persistence;

        private readonly Settings _settings = Settings.Instance;

        private const String FULLSCREEN_ERROR_MSG = "Screen properties not available for RDP";

        private FavsList _favsList1;

        private readonly FormSettings _formSettings;
        private TabControlItem _currentToolTipItem;
        private ToolTip _currentToolTip;
        private Boolean _allScreens;
        private readonly TerminalTabsSelectionControler _terminalsControler;
        private readonly FavoritesMenuLoader _menuLoader;
        private readonly MainFormFullScreenSwitch _fullScreenSwitch;

        private readonly ConnectionsUiFactory _connectionsUiFactory;

        private readonly TabControlRemover _tabControlRemover;

        private readonly TabControlFilter _tabsFilter;

        private readonly IToolbarExtender[] _toolbarExtenders;

        private readonly ToolTipBuilder _toolTipBuilder;

        private readonly ConnectionManager _connectionManager;

        private FavoriteIcons _favoriteIcons;

        private IFavorites PersistedFavorites
        {
            get { return _persistence.Favorites; }
        }

        /// <summary>
        /// Get or set wether the MainForm window is in fullscreen mode.
        /// </summary>
        internal bool FullScreen
        {
            private get { return _fullScreenSwitch.FullScreen; }
            set { _fullScreenSwitch.FullScreen = value; }
        }

        /// <summary>
        /// Gets wether the MainForm window is switching to or from fullscreen mode.
        /// </summary>
        internal Boolean SwitchingFullScreen
        {
            get { return _fullScreenSwitch.SwitchingFullScreen; }
        }

        private IConnectionExtra CurrentTerminal
        {
            get
            {
                return _terminalsControler.CurrentConnection as IConnectionExtra;
            }
        }

        protected override void SetVisibleCore(Boolean value)
        {
            _formSettings.LoadFormSize();
            base.SetVisibleCore(value);
        }

        public void OnLeavingFullScreen()
        {
            if (this.CurrentTerminal != null)
            {
                if (this.CurrentTerminal.ContainsFocus)
                    this.tscConnectTo.Focus();
            }
            else
                this.BringToFront();
        }

        public MainForm(IPersistence persistence, ConnectionManager connectionManager, FavoriteIcons favoriteIcons)
        {
            try
            {
                _persistence = persistence;
                _connectionManager = connectionManager;
                _favoriteIcons = favoriteIcons;

                _toolTipBuilder = new ToolTipBuilder(_persistence.Security);
                _settings.StartDelayedUpdate();

                // Set default font type by Windows theme to use for all controls on form
                this.Font = SystemFonts.IconTitleFont;

                InitializeComponent(); // main designer procedure

                _formSettings = new FormSettings(this);
                _tabsFilter = new TabControlFilter(this.tcTerminals);
                _terminalsControler = new TerminalTabsSelectionControler(this.tcTerminals, _persistence);
                _connectionsUiFactory = new ConnectionsUiFactory(this, _terminalsControler,
                    _persistence, _connectionManager, _favoriteIcons);
                _terminalsControler.AssingUiFactory(_connectionsUiFactory);
                _toolbarExtenders = _connectionManager.CreateToolbarExtensions(_terminalsControler);

                // Initialize FavsList outside of InitializeComponent
                // Inside InitializeComponent it sometimes caused the design view in VS to return errors
                this.InitializeFavsListControl();

                // Set notifyicon icon from embedded png image
                this.MainWindowNotifyIcon.Icon = Icon.FromHandle(Properties.Resources.terminalsicon.GetHicon());
                _menuLoader = new FavoritesMenuLoader(this, _persistence);
                this.favoriteToolBar.Visible = this.toolStripMenuItemShowHideFavoriteToolbar.Checked;
                _fullScreenSwitch = new MainFormFullScreenSwitch(this);
                _tabControlRemover = new TabControlRemover(_settings, this, _terminalsControler, this.tcTerminals);
                _favsList1.AssignServices(_persistence, _connectionManager, favoriteIcons, this);
                this.AssignToolStripsToContainer();
                this.ApplyControlsEnableAndVisibleState();

                _menuLoader.LoadGroups();
                this.UpdateControls();
                this.LoadWindowState();
                this.CheckForMultiMonitorUse();

                this.tcTerminals.TabControlItemDetach += new TabControlItemChangedHandler(this.TcTerminals_TabDetach);
                this.tcTerminals.MouseClick += new MouseEventHandler(this.TcTerminals_MouseClick);

                this.QuickContextMenu.ItemClicked += new ToolStripItemClickedEventHandler(QuickContextMenu_ItemClicked);
                this.LoadSpecialCommands();

                ProtocolHandler.Register();
                _persistence.AssignSynchronizationObject(this);
            }
            catch (Exception exc)
            {
                Logging.Error("Error loading the Main Form", exc);
                throw;
            }
        }

        private void InitializeFavsListControl()
        {
            _favsList1 = new FavsList();
            this.pnlTagsFavorites.Controls.Add(_favsList1);
            _favsList1.Dock = DockStyle.Fill;
            _favsList1.Location = new Point(5, 0);
            _favsList1.Padding = new Padding(4, 4, 4, 4);
            _favsList1.Name = "favsList1";
            _favsList1.Size = new Size(200, 497);
            _favsList1.TabIndex = 2;
            _favsList1.ConnectionsUiFactory = _connectionsUiFactory;
        }

        private void ApplyControlsEnableAndVisibleState()
        {
            this.MainWindowNotifyIcon.Visible = _settings.MinimizeToTray;
            if (!_settings.MinimizeToTray && !this.Visible)
                this.Visible = true;

            this.lockToolbarsToolStripMenuItem.Checked = _settings.ToolbarsLocked;
            this.MainMenuStrip.GripStyle = _settings.ToolbarsLocked ? ToolStripGripStyle.Hidden : ToolStripGripStyle.Visible;

            this.tcTerminals.ShowToolTipOnTitle = _settings.ShowInformationToolTips;
            IFavorite selectedFavorite = _terminalsControler.SelectedFavorite;
            // TODO this should update all favorites
            if (selectedFavorite != null)
                _terminalsControler.Selected.ToolTipText = _toolTipBuilder.BuildTooTip(selectedFavorite);

            this.groupsToolStripMenuItem.Visible = _settings.EnableGroupsMenu;
            this.tsbTags.Checked = _settings.ShowFavoritePanel;
            this.pnlTagsFavorites.Width = 7;

            this.HideShowFavoritesPanel(_settings.ShowFavoritePanel);
            this.UpdateCaptureButtonEnabled();
            this.ApplyTheme();
        }

        private void ApplyTheme()
        {
            if (_settings.Office2007BlueFeel)
                ToolStripManager.Renderer = Office2007Renderer.GetRenderer(RenderColors.Blue);
            else if (_settings.Office2007BlackFeel)
                ToolStripManager.Renderer = Office2007Renderer.GetRenderer(RenderColors.Black);
            else
                ToolStripManager.Renderer = new ToolStripProfessionalRenderer();

            // Update the old treeview theme to the new theme from Win Vista and up
            Methods.SetWindowTheme(this.menuStrip.Handle, "Explorer", null);
        }

        /// <summary>
        /// Assignes toolbars and menu items to toolstrip container.
        /// They arent moved to the container because of designer
        /// </summary>
        private void AssignToolStripsToContainer()
        {
            this.toolStripContainer.ToolbarStd = this.toolbarStd;
            this.toolStripContainer.StandardToolbarToolStripMenuItem = this.standardToolbarToolStripMenuItem;
            this.toolStripContainer.FavoriteToolBar = this.favoriteToolBar;
            this.toolStripContainer.ToolStripMenuItemShowHideFavoriteToolbar = this.toolStripMenuItemShowHideFavoriteToolbar;
            this.toolStripContainer.SpecialCommandsToolStrip = this.SpecialCommandsToolStrip;
            this.toolStripContainer.ShortcutsToolStripMenuItem = this.shortcutsToolStripMenuItem;
            this.toolStripContainer.MenuStrip = this.menuStrip;
            this.toolStripContainer.TsRemoteToolbar = this.tsRemoteToolbar;
            this.toolStripContainer.AssignToolStripsLocationChangedEventHandler();
        }

        private void LoadWindowState()
        {
            this.AssingTitle();
            this.HideShowFavoritesPanel(_settings.ShowFavoritePanel);
            this.toolStripContainer.LoadToolStripsState();
        }

        internal void UpdateControls()
        {
            this.tcTerminals.ShowToolTipOnTitle = _settings.ShowInformationToolTips;
            this.UpdateCommandsByActiveConnection();
            this.UpgadeGrabInput();
            this.UpdateQuickConnectCommands();

            foreach (IToolbarExtender extender in _toolbarExtenders)
            {
                extender.Visit(this.toolbarStd);
            }
        }

        private void UpgadeGrabInput()
        {
            try
            {
                var currentConnection = _terminalsControler.CurrentConnection as IHandleKeyboardInput;
                bool canGrab = currentConnection != null;
                this.tsbGrabInput.Checked = canGrab && currentConnection.GrabInput;
                this.tsbGrabInput.Enabled = canGrab;
                this.grabInputToolStripMenuItem.Checked = this.tsbGrabInput.Checked;
                this.grabInputToolStripMenuItem.Enabled = canGrab;
            }
            catch (Exception exc)
            {
                Logging.Error(FULLSCREEN_ERROR_MSG, exc);
            }
        }

        private void UpdateQuickConnectCommands()
        {
            bool quickConnectEnabled = !string.IsNullOrEmpty(this.tscConnectTo.Text);
            this.tsbConnect.Enabled = quickConnectEnabled;
            this.tsbConnectToConsole.Enabled = quickConnectEnabled;
            this.tsbConnectAs.Enabled = quickConnectEnabled;
        }

        public String GetDesktopShare()
        {
            // it is safe to ask for favorite here, is only called from connection supporting this feature
            String currentDesktopShare = _terminalsControler.SelectedFavorite.DesktopShare;
            var desktopShares = new DesktopShares(this.CurrentTerminal, _settings.DefaultDesktopShare);
            return desktopShares.EvaluateDesktopShare(currentDesktopShare);
        }

        internal void SendNativeMessageToFocus()
        {
            if (!this.Visible)
            {
                this.Show();
                if (this.WindowState == FormWindowState.Minimized)
                    Methods.ShowWindow(new HandleRef(this, this.Handle), 9);

                Methods.SetForegroundWindow(new HandleRef(this, this.Handle));
            }
        }

        private void ToggleGrabInput()
        {
            var currentConnection = _terminalsControler.CurrentConnection as IHandleKeyboardInput;
            if (currentConnection != null)
            {
                currentConnection.GrabInput = !currentConnection.GrabInput;
                this.UpgadeGrabInput();
            }
        }

        private void CheckForMultiMonitorUse()
        {
            if (Screen.AllScreens.Length > 1)
            {
                this.showInDualScreensToolStripMenuItem.Enabled = true;

                //Lazy check to see if we are using dual screens
                int w = this.Width / Screen.PrimaryScreen.Bounds.Width;
                if (w > 2)
                {
                    _allScreens = true;
                    this.showInDualScreensToolStripMenuItem.Text = "Show in single screens";
                }
            }
            else
            {
                this.showInDualScreensToolStripMenuItem.ToolTipText = "You only have one screen";
                this.showInDualScreensToolStripMenuItem.Enabled = false;
            }
        }

        internal void AssignEventsToConnectionTab(TerminalTabControlItem terminalTabPage)
        {
            terminalTabPage.DragOver += this.TerminalTabPage_DragOver;
            terminalTabPage.DragEnter += new DragEventHandler(this.terminalTabPage_DragEnter);
            terminalTabPage.Resize += new EventHandler(terminalTabPage_Resize);
        }

        internal void AssingDoubleClickEventHandler(TerminalTabControlItem terminalTabPage)
        {
            terminalTabPage.DoubleClick += new EventHandler(this.TerminalTabPage_DoubleClick);
        }

        private void LoadSpecialCommands()
        {
            SpecialCommandsToolStrip.Items.Clear();

            foreach (SpecialCommandConfigurationElement cmd in _settings.SpecialCommands)
            {
                var mi = new ToolStripMenuItem(cmd.Name);
                mi.DisplayStyle = ToolStripItemDisplayStyle.Image;
                mi.ToolTipText = cmd.Name;
                mi.Text = cmd.Name;
                mi.Tag = cmd;
                mi.Image = cmd.LoadThumbnail();
                mi.ImageTransparentColor = Color.Magenta;
                mi.Overflow = ToolStripItemOverflow.AsNeeded;
                SpecialCommandsToolStrip.Items.Add(mi);
            }
        }

        private void ShowCredentialsManager()
        {
            using (var mgr = new CredentialManager(_persistence))
                mgr.ShowDialog();
        }

        private void OpenSavedConnections()
        {
            _connectionsUiFactory.ConnectByFavoriteNames(_settings.SavedConnections);
            _settings.ClearSavedConnectionsList();
        }

        private void HideShowFavoritesPanel(bool show)
        {
            if (_settings.EnableFavoritesPanel)
            {
                if (show)
                {
                    splitContainer1.Panel1MinSize = 10;
                    splitContainer1.SplitterDistance = _settings.FavoritePanelWidth;
                    splitContainer1.Panel1Collapsed = false;
                    splitContainer1.IsSplitterFixed = false;
                    pnlHideTagsFavorites.Show();
                    pnlShowTagsFavorites.Hide();
                }
                else
                {
                    // noticed performance issue when set to 6 and Terminals.config file is empty
                    splitContainer1.Panel1MinSize = 9;
                    splitContainer1.SplitterDistance = 9;
                    splitContainer1.IsSplitterFixed = true;
                    pnlHideTagsFavorites.Hide();
                    pnlShowTagsFavorites.Show();
                }

                _settings.ShowFavoritePanel = show;
                tsbTags.Checked = show;
            }
            else
            {
                //just hide it completely
                splitContainer1.Panel1Collapsed = true;
                splitContainer1.Panel1MinSize = 0;
                splitContainer1.SplitterDistance = 0;
            }
        }

        internal void FocusFavoriteInQuickConnectCombobox(string favoriteName)
        {
            this.tscConnectTo.SelectedIndex = this.tscConnectTo.Items.IndexOf(favoriteName);
        }

        private void QuickConnect(String server, Int32 port, Boolean connectToConsole)
        {
            IFavorite favorite = FavoritesFactory.GetOrCreateQuickConnectFavorite(_persistence, server, connectToConsole, port);
            _connectionsUiFactory.Connect(favorite);
        }

        internal void HandleCommandLineActions(CommandLineArgs commandLineArgs)
        {
            Boolean connectToConsole = commandLineArgs.console;
            this.FullScreen = commandLineArgs.fullscreen;
            if (commandLineArgs.HasUrlDefined)
                QuickConnect(commandLineArgs.UrlServer, commandLineArgs.UrlPort, connectToConsole);

            if (commandLineArgs.HasMachineDefined)
                QuickConnect(commandLineArgs.MachineName, commandLineArgs.Port, connectToConsole);

            this.ConnectToFavorites(commandLineArgs, connectToConsole);
        }

        private void ConnectToFavorites(CommandLineArgs commandLineArgs, bool connectToConsole)
        {
            if (commandLineArgs.Favorites.Length > 0)
            {
                _connectionsUiFactory.ConnectByFavoriteNames(commandLineArgs.Favorites, connectToConsole);
            }
        }

        private void SaveActiveConnections()
        {
            var activeConnections = new List<string>();
            foreach (TabControlItem item in tcTerminals.Items)
            {
                activeConnections.Add(item.Title);
            }

            _settings.CreateSavedConnectionsList(activeConnections.ToArray());
        }

        private void CheckForNewRelease()
        {
            var updateManager = new UpdateManager();
            Task<ReleaseInfo> downloadTask = updateManager.CheckForUpdates(false);
            downloadTask.ContinueWith(this.CheckForNewRelease, TaskScheduler.FromCurrentSynchronizationContext());
        }

        private void CheckForNewRelease(Task<ReleaseInfo> downloadTask)
        {
            ReleaseInfo downloaded = downloadTask.Result;
            if (downloaded.NewAvailable && !_settings.NeverShowTerminalsWindow)
                ExternalLinks.AskIfShowReleasePage(_settings, downloaded);

            this.UpdateReleaseToolStripItem(downloaded);
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            this.AssingTitle();
            this.CheckForNewRelease();
            this.OpenSavedConnections();
        }

        private void MainForm_Shown(object sender, EventArgs e)
        {
            // Get initial window state, location and after the form has finished loading
            this.SetWindowState();
        }

        private void MainForm_KeyUp(object sender, KeyEventArgs e)
        {
            if ((this.tsbGrabInput.Checked || this.FullScreen) && e.KeyCode != Keys.Cancel)
            {
                if (this.CurrentTerminal != null)
                {
                    this.CurrentTerminal.Focus();
                    return;
                }
            }

            if (e.KeyCode == Keys.Cancel)
            {
                this.ToggleGrabInput();
            }
            else if (e.Control && e.KeyCode == Keys.F12)
            {
                _terminalsControler.CaptureScreen();
            }
            else if (e.KeyCode == Keys.F4)
            {
                if (!this.tscConnectTo.Focused)
                    this.tscConnectTo.Focus();
            }
            else if (e.KeyCode == Keys.F3)
            {
                this.ShowQuickConnect();
            }
        }

        private void ShowQuickConnect()
        {
            using (var qc = new QuickConnect(_persistence))
            {
                if (qc.ShowDialog(this) == DialogResult.OK && !string.IsNullOrEmpty(qc.ConnectionName))
                    _connectionsUiFactory.ConnectByFavoriteNames(new List<string>() { qc.ConnectionName });
            }
        }

        private void MainForm_Activated(object sender, EventArgs e)
        {
            _formSettings.EnsureVisibleScreenArea();

            if (this.FullScreen)
                this.tcTerminals.ShowTabs = false;
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            _favsList1.SaveState();

            if (this.FullScreen)
                this.FullScreen = false;

            this.MainWindowNotifyIcon.Visible = false;
            CloseOpenedConnections(e);
            this.toolStripContainer.SaveLayout();

            if (!e.Cancel)
                SingleInstanceApplication.Instance.Close();
        }

        private void CloseOpenedConnections(FormClosingEventArgs args)
        {
            if (_terminalsControler.HasSelected)
            {
                if (_settings.ShowConfirmDialog)
                    SaveConnectonsIfRequested(args);

                if (_settings.SaveConnectionsOnClose)
                    this.SaveActiveConnections();
            }
        }

        private void SaveConnectonsIfRequested(FormClosingEventArgs args)
        {
            var frmSaveActiveConnections = new SaveActiveConnectionsForm();
            if (frmSaveActiveConnections.ShowDialog() == DialogResult.OK)
            {
                _settings.ShowConfirmDialog = frmSaveActiveConnections.PromptNextTime;
                if (frmSaveActiveConnections.OpenConnectionsNextTime)
                    this.SaveActiveConnections();
            }
            else
                args.Cancel = true;
        }

        private void MainForm_Resize(object sender, EventArgs e)
        {
            if (this.WindowState == FormWindowState.Minimized)
            {
                if (_settings.MinimizeToTray)
                    this.Visible = false;
            }
            else
            {
                SetWindowState();
            }
        }

        private void MainForm_Move(object sender, EventArgs e)
        {
            if (!_fullScreenSwitch.SwitchingFullScreen && this.WindowState == FormWindowState.Normal)
                _fullScreenSwitch.LastWindowStateNormalLocation = this.Location;
        }

        /// <summary>
        /// Save the MainForm windowstate, location and size for reference,
        /// when restoring from fullscreen mode.
        /// </summary>
        private void SetWindowState()
        {
            // In a higher DPI mode, the form resize event is called before
            // the fullScreenSwitch class is initialized.
            if (_fullScreenSwitch == null)
                return;

            // Save window state only when not switching to and from fullscreen mode
            if (_fullScreenSwitch.SwitchingFullScreen)
                return;

            _fullScreenSwitch.LastWindowState = this.WindowState;

            if (this.WindowState == FormWindowState.Normal)
            {
                _fullScreenSwitch.LastWindowStateNormalLocation = this.Location;
                _fullScreenSwitch.LastWindowStateNormalSize = this.Size;
            }
        }

        private void TcTerminals_TabDetach(TabControlItemChangedEventArgs args)
        {
            this.tcTerminals.SelectedItem = args.Item;
            _terminalsControler.DetachTabToNewWindow();
        }

        private void TcTerminals_MouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                if (tcTerminals != null && sender != null)
                    this.QuickContextMenu.Show(tcTerminals, e.Location);
            }
        }

        private void QuickContextMenu_Opening(object sender, CancelEventArgs e)
        {
            this.TcTerminals_MouseClick(null, new MouseEventArgs(MouseButtons.Right, 1, 0, 0, 0));
            e.Cancel = false;
        }

        private void QuickContextMenu_ItemClicked(object sender, ToolStripItemClickedEventArgs e)
        {
            ToolStripItem clickedItem = e.ClickedItem;
            if (clickedItem.Text == Program.Resources.GetString("Restore") ||
                clickedItem.Name == FavoritesMenuLoader.COMMAND_RESTORESCREEN ||
                clickedItem.Name == FavoritesMenuLoader.COMMAND_FULLSCREEN)
            {
                this.FullScreen = !this.FullScreen;
            }
            else switch (clickedItem.Name)
                {
                    case FavoritesMenuLoader.COMMAND_CREDENTIALMANAGER:
                        this.ShowCredentialsManager();
                        break;
                    case FavoritesMenuLoader.COMMAND_ORGANIZEFAVORITES:
                        this.ManageConnectionsToolStripMenuItem_Click(null, null);
                        break;
                    case FavoritesMenuLoader.COMMAND_OPTIONS:
                        this.OptionsToolStripMenuItem_Click(null, null);
                        break;
                    case FavoritesMenuLoader.COMMAND_NETTOOLS:
                        this.ToolStripButton2_Click(null, null);
                        break;
                    case FavoritesMenuLoader.COMMAND_CAPTUREMANAGER:
                        _terminalsControler.FocusCaptureManager();
                        break;
                    case FavoritesMenuLoader.COMMAND_EXIT:
                        this.Close();
                        break;
                    case FavoritesMenuLoader.QUICK_CONNECT:
                        this.ShowQuickConnect();
                        break;
                    case FavoritesMenuLoader.COMMAND_SHOWMENU:
                        {
                            Boolean visible = !this.menuStrip.Visible;
                            this.menuStrip.Visible = visible;
                            this.menubarToolStripMenuItem.Checked = visible;
                        }
                        break;
                    case FavoritesMenuLoader.COMMAND_SPECIAL:
                        return;
                    default:
                        this.OnFavoriteTrayToolsStripClick(e);
                        break;
                }

            this.QuickContextMenu.Hide();
        }

        private void OnFavoriteTrayToolsStripClick(ToolStripItemClickedEventArgs e)
        {
            var tag = e.ClickedItem.Tag as String;

            if (tag != null)
            {
                String itemName = e.ClickedItem.Text;
                if (tag == FavoritesMenuLoader.FAVORITE)
                    _connectionsUiFactory.ConnectByFavoriteNames(new List<string>() { itemName });
            }
        }

        private void ExitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.Close();
        }

        private void GroupAddToolStripMenuItem_Click(object sender, EventArgs e)
        {
            //TODO Bug: doenst work, the menu doesnt refresh and the favorite isnt put into the group
            IGroup selectedGroup = ((GroupMenuItem)sender).Group;
            IFavorite selectedFavorite = _terminalsControler.SelectedOriginFavorite;

            if (selectedGroup != null && selectedFavorite != null)
            {
                selectedGroup.AddFavorite(selectedFavorite);
            }
        }

        private void GroupToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var groupMenuItem = (GroupMenuItem)sender;
            foreach (IFavorite favorite in groupMenuItem.Favorites)
            {
                _connectionsUiFactory.Connect(favorite);
            }
        }

        private void ServerToolStripMenuItem_Click(object sender, EventArgs e)
        {
            string connectionName = ((ToolStripItem)sender).Text;
            IFavorite favorite = PersistedFavorites[connectionName];
            _connectionsUiFactory.Connect(favorite);
        }

        private void terminalTabPage_Resize(object sender, EventArgs e)
        {
            var terminalTabControlItem = sender as TerminalTabControlItem;
            if (terminalTabControlItem != null)
            {
                // TODO Fix the smart sizing issue added on 7/27/2011
                //var rdpConnection = terminalTabControlItem.Connection as RDPConnection;
                //if (rdpConnection != null &&
                //    !rdpConnection.AxMsRdpClient.AdvancedSettings3.SmartSizing)
                //{
                //    //rdpConnection.AxMsRdpClient.DesktopWidth = terminalTabControlItem.Width;
                //    //rdpConnection.AxMsRdpClient.DesktopHeight = terminalTabControlItem.Height;
                //    //Debug.WriteLine("Tab size:" + terminalTabControlItem.Size.ToString() + ";" +
                //    //  rdpConnection.AxMsRdpClient.DesktopHeight.ToString() + "," +
                //    //  rdpConnection.AxMsRdpClient.DesktopWidth.ToString());
                //}
            }
        }

        private void terminalTabPage_DragEnter(object sender, DragEventArgs e)
        {
            e.Effect = e.Data.GetDataPresent(DataFormats.FileDrop, false) ? DragDropEffects.Copy : DragDropEffects.None;
        }

        private void TerminalTabPage_DragOver(object sender, DragEventArgs e)
        {
            _terminalsControler.Select(sender as TerminalTabControlItem);
        }

        private void TerminalTabPage_DoubleClick(object sender, EventArgs e)
        {
            this.Disconnect();
        }

        private void NewTerminalToolStripMenuItem_Click(object sender, EventArgs e)
        {
            _connectionsUiFactory.CreateFavorite();
        }

        private void TsbConnect_Click(object sender, EventArgs e)
        {
            this.ConnectFromQuickCombobox(false);
        }

        private void TsbConnectToConsole_Click(object sender, EventArgs e)
        {
            this.ConnectFromQuickCombobox(true);
        }

        private void TsbConnectAs_Click(object sender, EventArgs e)
        {
            using (var usrForm = new ConnectExtraForm(_persistence))
            {
                if (usrForm.ShowDialog() != DialogResult.OK)
                    return;

                this.ConnectFromQuickCombobox(usrForm.Console, usrForm.NewWindow, usrForm.Credentials);
            }
        }

        private void ConnectFromQuickCombobox(bool forceConsole, bool forceNewWindow = false, ICredentialSet credentials = null)
        {
            string connectionName = this.tscConnectTo.Text;
            if (!string.IsNullOrEmpty(connectionName))
            {
                _connectionsUiFactory.ConnectByFavoriteNames(new List<string>() { connectionName }, forceConsole, forceNewWindow, credentials);
            }
        }

        private void TscConnectTo_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                this.tsbConnect.PerformClick();
            }

            if (e.KeyCode == Keys.Delete && this.tscConnectTo.DroppedDown &&
                this.tscConnectTo.SelectedIndex != -1)
            {
                String connectionName = tscConnectTo.Items[tscConnectTo.SelectedIndex].ToString();
                this.DeleteFavorite(connectionName);
            }
        }

        private void DeleteFavorite(string name)
        {
            tscConnectTo.Items.Remove(name);
            var favorite = PersistedFavorites[name];
            PersistedFavorites.Delete(favorite);
            favoritesToolStripMenuItem.DropDownItems.RemoveByKey(name);
        }

        private void TsbDisconnect_Click(object sender, EventArgs e)
        {
            this.Disconnect();
        }

        private void NewTerminalToolStripMenuItem_Click_1(object sender, EventArgs e)
        {
            _connectionsUiFactory.CreateFavorite();
        }

        private void TsbGrabInput_Click(object sender, EventArgs e)
        {
            this.ToggleGrabInput();
        }

        internal void OnDisconnected(Connection connection)
        {
            _tabControlRemover.OnDisconnected(connection);
        }

        private void TcTerminals_TabControlItemSelectionChanged(TabControlItemChangedEventArgs e)
        {
            this.UpdateControls();
            this.AssingTitle();
        }

        private void UpdateCommandsByActiveConnection()
        {
            bool hasSelectedConnection = _terminalsControler.HasSelected;
            this.tsbDisconnect.Enabled = hasSelectedConnection;
            this.disconnectToolStripMenuItem.Enabled = hasSelectedConnection;
            this.toolStripButtonReconnect.Enabled = hasSelectedConnection;
            this.reconnectToolStripMenuItem.Enabled = hasSelectedConnection;
            this.addTerminalToGroupToolStripMenuItem.Enabled = hasSelectedConnection;
            this.saveTerminalsAsGroupToolStripMenuItem.Enabled = hasSelectedConnection;
        }

        private void AssingTitle()
        {
            TabControlItem selectedTab = this.tcTerminals.SelectedItem;
            if (_settings.ShowInformationToolTips && selectedTab != null)
                this.Text = selectedTab.ToolTipText.Replace("\r\n", "; ");
            else
                this.Text = Program.Info.GetAboutText(_persistence.Name);
        }

        private void TscConnectTo_TextChanged(object sender, EventArgs e)
        {
            this.UpdateControls();
        }

        private void TcTerminals_MouseHover(object sender, EventArgs e)
        {
            if (this.tcTerminals != null && !this.tcTerminals.ShowTabs)
                this.timerHover.Enabled = true;
        }

        private void TcTerminals_MouseLeave(object sender, EventArgs e)
        {
            this.timerHover.Enabled = false;
            if (this.FullScreen && this.tcTerminals.ShowTabs && !this.tcTerminals.MenuOpen)
                this.tcTerminals.ShowTabs = false;

            if (_currentToolTipItem != null)
            {
                _currentToolTip.Hide(_currentToolTipItem);
                _currentToolTip.Active = false;
            }
        }

        private void TcTerminals_DoubleClick(object sender, EventArgs e)
        {
            this.FullScreen = !this.FullScreen;
        }

        private void TsbFullScreen_Click(object sender, EventArgs e)
        {
            this.FullScreen = !this.FullScreen;
            this.UpdateControls();
        }

        private void TcTerminals_MenuItemsLoaded(object sender, EventArgs e)
        {
            this.UpdateTabControlMenuItemIcons();

            if (this.FullScreen)
            {
                var sep = new ToolStripSeparator();
                this.tcTerminals.Menu.Items.Add(sep);
                var item = new ToolStripMenuItem(Program.Resources.GetString("Restore"), null, this.TcTerminals_DoubleClick);
                this.tcTerminals.Menu.Items.Add(item);
                item = new ToolStripMenuItem(Program.Resources.GetString("Minimize"), null, this.Minimize);
                this.tcTerminals.Menu.Items.Add(item);
            }
        }

        private void UpdateTabControlMenuItemIcons()
        {
            foreach (ToolStripItem menuItem in this.tcTerminals.Menu.Items)
            {
                // the menu item always has name of connected favorite, so search by name works
                IFavorite favorite = _tabsFilter.FindFavoriteByTabTitle(menuItem.Text);

                if (favorite != null)
                    menuItem.Image = this.PersistedFavorites.LoadFavoriteIcon(favorite);
            }
        }

        private void SaveTerminalsAsGroupToolStripMenuItem_Click(object sender, EventArgs e)
        {
            string newGroupName = NewGroupForm.AskFroGroupName(_persistence);
            if (string.IsNullOrEmpty(newGroupName))
                return;

            IGroup group = FavoritesFactory.GetOrAddNewGroup(_persistence, newGroupName);
            foreach (IFavorite favorite in _tabsFilter.SelectTabsWithFavorite())
            {
                group.AddFavorite(favorite);
            }

            _menuLoader.LoadGroups();
        }

        private void OrganizeGroupsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            using (var frmOrganizeGroups = new OrganizeGroupsForm(_persistence, _favoriteIcons))
            {
                frmOrganizeGroups.ShowDialog();
                _menuLoader.LoadGroups();
            }
        }

        private void TcTerminals_TabControlMouseOnTitle(TabControlMouseOnTitleEventArgs e)
        {
            if (_settings.ShowInformationToolTips)
            {
                if (_currentToolTip == null)
                {
                    _currentToolTip = new ToolTip();
                    _currentToolTip.Active = false;
                }
                else if ((_currentToolTipItem != null) && (_currentToolTipItem != e.Item))
                {
                    _currentToolTip.Hide(_currentToolTipItem);
                    _currentToolTip.Active = false;
                }

                if (!_currentToolTip.Active)
                {
                    _currentToolTip = new ToolTip();
                    _currentToolTip.ToolTipTitle = Program.Resources.GetString("ConnectionInformation");
                    _currentToolTip.ToolTipIcon = ToolTipIcon.Info;
                    _currentToolTip.UseFading = true;
                    _currentToolTip.UseAnimation = true;
                    _currentToolTip.IsBalloon = false;
                    _currentToolTip.Show(e.Item.ToolTipText, e.Item, (int)e.Item.StripRect.X, 2);
                    _currentToolTipItem = e.Item;
                    _currentToolTip.Active = true;
                }
            }
        }

        private void TcTerminals_TabControlMouseLeftTitle(TabControlMouseOnTitleEventArgs e)
        {
            if (_currentToolTipItem != null)
            {
                _currentToolTip.Hide(_currentToolTipItem);
                _currentToolTip.Active = false;
            }
            /*if (previewPictureBox != null)
            {
                previewPictureBox.Image.Dispose();
                previewPictureBox.Dispose();
                previewPictureBox = null;
            }*/
        }

        private void TimerHover_Tick(object sender, EventArgs e)
        {
            if (this.timerHover.Enabled)
            {
                this.timerHover.Enabled = false;
                this.tcTerminals.ShowTabs = true;
            }
        }

        private void OrganizeFavoritesToolbarToolStripMenuItem_Click(object sender, EventArgs e)
        {
            using (var frmOrganizeFavoritesToolbar = new OrganizeFavoritesToolbarForm(_persistence))
            {
                frmOrganizeFavoritesToolbar.ShowDialog();
            }
        }

        private void OptionsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            using (var frmOptions = new OptionDialog(CurrentTerminal, _persistence))
            {
                if (frmOptions.ShowDialog() == DialogResult.OK)
                {
                    this.ApplyControlsEnableAndVisibleState();
                }
            }
        }

        /// <summary>
        /// Disable capture button when function is disabled in options
        /// </summary>
        private void UpdateCaptureButtonEnabled()
        {
            Boolean enableCapture = _settings.EnabledCaptureToFolderAndClipBoard;
            this.CaptureScreenToolStripButton.Enabled = enableCapture;
            this.captureTerminalScreenToolStripMenuItem.Enabled = enableCapture;
            _terminalsControler.UpdateCaptureButtonOnDetachedPopUps();
        }

        private void AboutToolStripMenuItem_Click(object sender, EventArgs e)
        {
            using (var frmAbout = new AboutForm(_persistence.Name))
            {
                frmAbout.ShowDialog();
            }
        }

        private void TsbTags_Click(object sender, EventArgs e)
        {
            this.HideShowFavoritesPanel(this.tsbTags.Checked);
        }

        private void PbShowTags_Click(object sender, EventArgs e)
        {
            this.HideShowFavoritesPanel(true);
        }

        private void PbHideTags_Click(object sender, EventArgs e)
        {
            this.HideShowFavoritesPanel(false);
        }

        private void TsbFavorites_Click(object sender, EventArgs e)
        {
            _settings.EnableFavoritesPanel = this.tsbFavorites.Checked;
            this.HideShowFavoritesPanel(_settings.ShowFavoritePanel);
        }

        private void Minimize(object sender, EventArgs e)
        {
            this.WindowState = FormWindowState.Minimized;
        }

        private void MainWindowNotifyIcon_MouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                if (_settings.MinimizeToTray)
                {
                    this.Visible = !this.Visible;
                    if (this.Visible && this.WindowState == FormWindowState.Minimized)
                        this.WindowState = _fullScreenSwitch.LastWindowState;
                }
                else
                {
                    if (this.WindowState == FormWindowState.Normal)
                        this.WindowState = FormWindowState.Minimized;
                    else
                        this.WindowState = _fullScreenSwitch.LastWindowState;
                }
            }
        }

        private void CaptureScreenToolStripButton_Click(object sender, EventArgs e)
        {
            _terminalsControler.CaptureScreen();
        }

        private void CaptureTerminalScreenToolStripMenuItem_Click(object sender, EventArgs e)
        {
            _terminalsControler.CaptureScreen();
        }

        private void ToolStripButton1_Click(object sender, EventArgs e)
        {
            ExternalLinks.OpenTerminalServiceCommandPrompt(this.CurrentTerminal, _settings.PsexecLocation);
        }

        private void TsbCmd_Click(object sender, EventArgs e)
        {
            ExternalLinks.OpenTerminalServiceCommandPrompt(this.CurrentTerminal, _settings.PsexecLocation);
        }

        private void StandardToolbarToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.AddShowStrip(this.toolbarStd, this.standardToolbarToolStripMenuItem, !this.toolbarStd.Visible);
        }

        private void ToolStripMenuItemShowHideFavoriteToolbar_Click(object sender, EventArgs e)
        {
            this.AddShowStrip(this.favoriteToolBar, this.toolStripMenuItemShowHideFavoriteToolbar, !this.favoriteToolBar.Visible);
        }

        private void ShortcutsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.AddShowStrip(this.SpecialCommandsToolStrip, this.shortcutsToolStripMenuItem, !this.SpecialCommandsToolStrip.Visible);
        }

        private void MenubarToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.AddShowStrip(this.menuStrip, this.menubarToolStripMenuItem, !this.menuStrip.Visible);
        }

        private void AddShowStrip(ToolStrip strip, ToolStripMenuItem menu, Boolean visible)
        {
            strip.Visible = visible;
            menu.Checked = visible;
            this.toolStripContainer.SaveLayout();
        }

        private void ToolsToolStripMenuItem_DropDownOpening(object sender, EventArgs e)
        {
            this.shortcutsToolStripMenuItem.Checked = this.SpecialCommandsToolStrip.Visible;
            this.toolStripMenuItemShowHideFavoriteToolbar.Checked = this.favoriteToolBar.Visible;
            this.standardToolbarToolStripMenuItem.Checked = this.toolbarStd.Visible;
        }

        private void ToolStripMenuItem3_Click(object sender, EventArgs e)
        {
            using (var org = new OrganizeShortcuts())
            {
                org.ShowDialog(this);
            }

            this.LoadSpecialCommands();
        }

        private void ShortcutsContextMenu_MouseClick(object sender, MouseEventArgs e)
        {
            this.ToolStripMenuItem3_Click(null, null);
        }

        private void SpecialCommandsToolStrip_MouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
                this.ShortcutsContextMenu.Show(e.X, e.Y);
        }

        private void SpecialCommandsToolStrip_ItemClicked(object sender, ToolStripItemClickedEventArgs e)
        {
            var command = e.ClickedItem.Tag as SpecialCommandConfigurationElement;
            if (command != null)
                ExternalLinks.Launch(command);
        }

        private void ToolStripButton2_Click(object sender, EventArgs e)
        {
            _connectionsUiFactory.OpenNetworkingTools(NettworkingTools.None, string.Empty);
        }

        private void NetworkingToolsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.ToolStripButton2_Click(null, null);
        }

        private void ToolStripMenuItemCaptureManager_Click(object sender, EventArgs e)
        {
            _terminalsControler.FocusCaptureManager();
        }

        private void ToolStripButtonCaptureManager_Click(object sender, EventArgs e)
        {
            _terminalsControler.FocusCaptureManager();
        }

        private void ToolStripButton4_Click(object sender, EventArgs e)
        {
            IConnection connection = _terminalsControler.CurrentConnection;
            if (connection != null && connection.Favorite != null)
            {
                DesktopSize desktop = connection.Favorite.Display.DesktopSize;
                connection.ChangeDesktopSize(desktop);
            }
        }

        private void PbShowTagsFavorites_MouseMove(object sender, MouseEventArgs e)
        {
            if (_settings.AutoExapandTagsPanel)
                this.HideShowFavoritesPanel(true);
        }

        private void LockToolbarsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.toolStripContainer.SaveLayout();
            this.lockToolbarsToolStripMenuItem.Checked = !this.lockToolbarsToolStripMenuItem.Checked;
            _settings.ToolbarsLocked = this.lockToolbarsToolStripMenuItem.Checked;
            this.toolStripContainer.ChangeLockState();
        }

        private void UpdateReleaseToolStripItem(ReleaseInfo downloaded)
        {
            if (this.updateToolStripItem != null && !this.updateToolStripItem.Visible)
            {
                if (downloaded.NewAvailable)
                {
                    this.updateToolStripItem.Visible = true;
                    string newText = String.Format("{0} - {1}", this.updateToolStripItem.Text, downloaded.Version);
                    this.updateToolStripItem.Text = newText;
                }
            }
        }

        private void OpenLocalComputeManagement_Click(object sender, EventArgs e)
        {
            ExternalLinks.OpenLocalComputerManagement();
        }

        private void RebuildTagsIndexToolStripMenuItem_Click(object sender, EventArgs e)
        {
            _persistence.Groups.Rebuild();
            _menuLoader.LoadGroups();
            this.UpdateControls();
        }

        private void ViewInNewWindowToolStripMenuItem_Click(object sender, EventArgs e)
        {
             _terminalsControler.DetachTabToNewWindow();
        }

        private void RebuildShortcutsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            _settings.SpecialCommands.Clear();
            _settings.SpecialCommands = Wizard.SpecialCommandsWizard.LoadSpecialCommands();
            this.LoadSpecialCommands();
        }

        private void RebuildToolbarsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.LoadWindowState();
        }

        // todo Move openig putty tools to putty plugin as menu extender
        private void OpenSshAgentToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.OpenPuttyTool("pageant.exe");
        }

        private void OpenSshKeygenToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.OpenPuttyTool("puttygen.exe");
        }

        private void OpenPuttyTool(string name)
        {
            string path = Path.Combine(PluginsLoader.FindBasePluginDirectory(), "Putty", "Resources", name);
            ExternalLinks.OpenPath(path);
        }

        private void OpenLogFolderToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ExternalLinks.OpenPath(FileLocations.LogDirectory);
        }

        private void SplitContainer1_SplitterMoved(object sender, SplitterEventArgs e)
        {
            if (splitContainer1.Panel1.Width > 15)
                _settings.FavoritePanelWidth = splitContainer1.Panel1.Width;
        }

        private void CredentialManagementToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.ShowCredentialsManager();
        }

        private void CredentialManagementToolStripButton_Click(object sender, EventArgs e)
        {
            this.ShowCredentialsManager();
        }

        private void ExportConnectionsListToolStripMenuItem_Click(object sender, EventArgs e)
        {
            using (var frm = new ExportForm(_persistence, _connectionManager, _favoriteIcons))
                frm.ShowDialog();
        }

        private void ManageConnectionsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            using (OrganizeFavoritesForm conMgr = this.CreateOrganizeFavoritesForm())
            {
                conMgr.ShowDialog();
            }
        }

        private void ToolStripMenuItemImport_Click(object sender, EventArgs e)
        {
            using (OrganizeFavoritesForm conMgr = this.CreateOrganizeFavoritesForm())
            {
                conMgr.CallImport();
                conMgr.ShowDialog();
            }
        }

        private OrganizeFavoritesForm CreateOrganizeFavoritesForm()
        {
            var organizeForm = new OrganizeFavoritesForm(_persistence, _connectionManager, _favoriteIcons);
            organizeForm.AssignConnectionsUiFactory(_connectionsUiFactory);
            return organizeForm;
        }

        private void ShowInDualScreensToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Screen[] screenArr = Screen.AllScreens;
            Int32 with = 0;
            if (!_allScreens)
            {
                if (this.WindowState == FormWindowState.Maximized)
                    this.WindowState = FormWindowState.Normal;

                foreach (Screen screen in screenArr)
                {
                    with += screen.Bounds.Width;
                }

                this.showInDualScreensToolStripMenuItem.Text = "Show in Single Screen";
                this.BringToFront();
            }
            else
            {
                with = Screen.PrimaryScreen.Bounds.Width;
                this.showInDualScreensToolStripMenuItem.Text = "Show In Multi Screens";
            }

            this.Top = 0;
            this.Left = 0;
            this.Height = Screen.PrimaryScreen.Bounds.Height;
            this.Width = with;
            _allScreens = !_allScreens;
        }

        private void UpdateToolStripItem_Click(object sender, EventArgs e)
        {
            ExternalLinks.ShowReleasePage();
        }

        private void ClearHistoryToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.Cursor = Cursors.WaitCursor;
            _persistence.ConnectionHistory.Clear();
            this.Cursor = Cursors.Default;
        }

        private void ToolStripButtonReconnect_Click(object sender, EventArgs e)
        {
            this.Reconnect();
        }

        public void Reconnect()
        {
            IConnection currentConnection = _terminalsControler.CurrentConnection;
            if (currentConnection != null)
            {
                IFavorite favorite = currentConnection.Favorite;
                _tabControlRemover.Disconnect();
                _connectionsUiFactory.Connect(favorite);
            }
        }

        public bool CanExecute(IFavorite selected)
        {
            IFavorite selectedInTab = _terminalsControler.SelectedOriginFavorite;
            return selected != null && selected.StoreIdEquals(selectedInTab);
        }

        public void Disconnect()
        {
            _tabControlRemover.Disconnect();
        }

        private void SpecialCommandsToolStrip_MouseClick(object sender, EventArgs e)
        {

        }
    }
}
