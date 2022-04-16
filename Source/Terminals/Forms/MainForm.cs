using System;
using System.IO;
using System.Drawing;
using System.Windows.Forms;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using TabControl;

using Terminals.Data;
using Terminals.Forms;
using Terminals.Native;
using Terminals.Updates;
using Terminals.Services;
using Terminals.Connections;
using Terminals.Credentials;
using Terminals.CommandLine;
using Terminals.Configuration;
using Terminals.Forms.Controls;
using Terminals.Forms.Rendering;

using Settings = Terminals.Configuration.Settings;

namespace Terminals
{
    internal partial class MainForm : Form, IConnectionMainView, IConnectionCommands
    {
        private readonly IPersistence _persistence;

        private readonly Settings _settings = Settings.Instance;

        private const String FULLSCREEN_ERROR_MSG = "Screen properties not available for RDP";

        private FavsList _favsList1;
        private Boolean _allScreens;
        private ToolTip _currentToolTip;
        private TabControlItem _currentToolTipItem;
        private readonly FormSettings _formSettings;
        private readonly FavoritesMenuLoader _menuLoader;
        private readonly TerminalTabsSelectionControler _terminalsControler;
        private readonly MainFormFullScreenSwitch _fullScreenSwitch;

        private FavoriteIcons _favoriteIcons;

        private readonly TabControlFilter _tabsFilter;

        private readonly ToolTipBuilder _toolTipBuilder;

        private readonly TabControlRemover _tabControlRemover;

        private readonly IToolbarExtender[] _toolbarExtenders;

        private readonly ConnectionManager _connectionManager;

        private readonly ConnectionsUiFactory _connectionsUiFactory;

        /// -----------------------------------------------

        private IFavorites PersistedFavorites
        {
            get { return _persistence.Favorites; }
        }

        /// -----------------------------------------------
        /// <summary>
        ///     Get or set wether the MainForm window is in fullscreen mode.
        /// </summary>

        internal bool FullScreen
        {
            private get { return _fullScreenSwitch.FullScreen; }
            set { _fullScreenSwitch.FullScreen = value; }
        }

        /// -----------------------------------------------
        /// <summary>
        ///     Gets wether the MainForm window is switching 
        ///     to or from fullscreen mode.
        /// </summary>

        internal Boolean SwitchingFullScreen
        {
            get { return _fullScreenSwitch.SwitchingFullScreen; }
        }

        /// -----------------------------------------------

        private IConnectionExtra CurrentTerminal
        {
            get
            {
                return _terminalsControler.CurrentConnection as IConnectionExtra;
            }
        }

        /// -----------------------------------------------

        protected override void SetVisibleCore(Boolean value)
        {
            _formSettings.LoadFormSize();
            base.SetVisibleCore(value);
        }

        /// -----------------------------------------------

        public void OnLeavingFullScreen()
        {
            if(CurrentTerminal != null)
            {
                if(CurrentTerminal.ContainsFocus)
                    tscConnectTo.Focus();
            }
            else
            {
                BringToFront();
            }
        }

        /// -----------------------------------------------

        public MainForm(IPersistence persistence, ConnectionManager connectionManager, FavoriteIcons favoriteIcons)
        {
            try
            {
                _persistence = persistence;
                _connectionManager = connectionManager;
                _favoriteIcons = favoriteIcons;

                _toolTipBuilder = new ToolTipBuilder(_persistence.Security);
                _settings.StartDelayedUpdate();

                // --------------------------------------
                // Set default font type by Windows theme
                // to use for all controls on form

                Font = SystemFonts.IconTitleFont;

                InitializeComponent(); // main designer procedure

                _formSettings = new FormSettings(this);
                _tabsFilter = new TabControlFilter(tcTerminals);
                _terminalsControler = new TerminalTabsSelectionControler(tcTerminals, _persistence);
                _connectionsUiFactory = new ConnectionsUiFactory(this, _terminalsControler, _persistence, _connectionManager, _favoriteIcons);
                _terminalsControler.AssingUiFactory(_connectionsUiFactory);
                _toolbarExtenders = _connectionManager.CreateToolbarExtensions(_terminalsControler);

                // --------------------------------------------------
                // Initialize FavsList outside of InitializeComponent
                // Inside InitializeComponent it sometimes caused the
                // design view in VS to return errors

                InitializeFavsListControl();

                // -------------------------------------------
                // Set notifyicon icon from embedded png image

                MainWindowNotifyIcon.Icon = Icon.FromHandle(Properties.Resources.terminalsicon.GetHicon());
                _menuLoader = new FavoritesMenuLoader(this, _persistence);
                favoriteToolBar.Visible = toolStripMenuItemShowHideFavoriteToolbar.Checked;
                _fullScreenSwitch = new MainFormFullScreenSwitch(this);
                _tabControlRemover = new TabControlRemover(_settings, this, _terminalsControler, tcTerminals);
                _favsList1.AssignServices(_persistence, _connectionManager, favoriteIcons, this);
                AssignToolStripsToContainer();
                ApplyControlsEnableAndVisibleState();

                _menuLoader.LoadGroups();
                UpdateControls();
                LoadWindowState();
                CheckForMultiMonitorUse();

                tcTerminals.TabControlItemDetach += new TabControlItemChangedHandler(TcTerminals_TabDetach);
                tcTerminals.MouseClick += new MouseEventHandler(TcTerminals_MouseClick);

                QuickContextMenu.ItemClicked += new ToolStripItemClickedEventHandler(QuickContextMenu_ItemClicked);
                LoadSpecialCommands();

                ProtocolHandler.Register();
                _persistence.AssignSynchronizationObject(this);
            }
            catch(Exception exc)
            {
                Logging.Error("Error loading the Main Form", exc);
                throw;
            }
        }

        /// -----------------------------------------------

        private void InitializeFavsListControl()
        {
            _favsList1 = new FavsList()
            {
                TabIndex = 2,
                Name = "favsList1",
                Dock = DockStyle.Fill,
                Size = new Size(200, 497),
                Location = new Point(5, 0),
                Padding = new Padding(4, 4, 4, 4),
                ConnectionsUiFactory = _connectionsUiFactory,
            };

            pnlTagsFavorites.Controls.Add(_favsList1);
        }

        /// -----------------------------------------------

        private void ApplyControlsEnableAndVisibleState()
        {
            MainWindowNotifyIcon.Visible = _settings.MinimizeToTray;

            if(!_settings.MinimizeToTray && !Visible)
            {
                Visible = true;
            }

            lockToolbarsToolStripMenuItem.Checked = _settings.ToolbarsLocked;
            MainMenuStrip.GripStyle = _settings.ToolbarsLocked ? ToolStripGripStyle.Hidden : ToolStripGripStyle.Visible;

            tcTerminals.ShowToolTipOnTitle = _settings.ShowInformationToolTips;
            IFavorite selectedFavorite = _terminalsControler.SelectedFavorite;

            // TODO this should update all favorites

            if(selectedFavorite != null)
            {
                _terminalsControler.Selected.ToolTipText = _toolTipBuilder.BuildTooTip(selectedFavorite);
            }

            groupsToolStripMenuItem.Visible = _settings.EnableGroupsMenu;
            tsbTags.Checked = _settings.ShowFavoritePanel;
            pnlTagsFavorites.Width = 7;

            HideShowFavoritesPanel(_settings.ShowFavoritePanel);
            UpdateCaptureButtonEnabled();
            ApplyTheme();
        }

        /// -----------------------------------------------

        private void ApplyTheme()
        {
            if(_settings.Office2007BlueFeel)
            {
                ToolStripManager.Renderer = Office2007Renderer.GetRenderer(RenderColors.Blue);
            }
            else if(_settings.Office2007BlackFeel)
            {
                ToolStripManager.Renderer = Office2007Renderer.GetRenderer(RenderColors.Black);
            }
            else
            {
                ToolStripManager.Renderer = new ToolStripProfessionalRenderer();
            }

            // Update the old treeview theme to the new theme from Win Vista and up

            Methods.SetWindowTheme(menuStrip.Handle, "Explorer", null);
        }

        /// -----------------------------------------------
        /// <summary>
        /// Assignes toolbars and menu items to toolstrip container.
        /// They arent moved to the container because of designer
        /// </summary>

        private void AssignToolStripsToContainer()
        {
            toolStripContainer.ToolbarStd = toolbarStd;
            toolStripContainer.StandardToolbarToolStripMenuItem = standardToolbarToolStripMenuItem;
            toolStripContainer.FavoriteToolBar = favoriteToolBar;
            toolStripContainer.ToolStripMenuItemShowHideFavoriteToolbar = toolStripMenuItemShowHideFavoriteToolbar;
            toolStripContainer.SpecialCommandsToolStrip = SpecialCommandsToolStrip;
            toolStripContainer.ShortcutsToolStripMenuItem = shortcutsToolStripMenuItem;
            toolStripContainer.MenuStrip = menuStrip;
            toolStripContainer.TsRemoteToolbar = tsRemoteToolbar;
            toolStripContainer.AssignToolStripsLocationChangedEventHandler();
        }

        /// -----------------------------------------------

        private void LoadWindowState()
        {
            AssingTitle();
            HideShowFavoritesPanel(_settings.ShowFavoritePanel);
            toolStripContainer.LoadToolStripsState();
        }

        /// -----------------------------------------------

        internal void UpdateControls()
        {
            tcTerminals.ShowToolTipOnTitle = _settings.ShowInformationToolTips;
            UpdateCommandsByActiveConnection();
            UpgadeGrabInput();
            UpdateQuickConnectCommands();

            foreach(IToolbarExtender extender in _toolbarExtenders)
            {
                extender.Visit(toolbarStd);
            }
        }

        /// -----------------------------------------------

        private void UpgadeGrabInput()
        {
            try
            {
                var currentConnection = _terminalsControler.CurrentConnection as IHandleKeyboardInput;
                bool canGrab = currentConnection != null;
                tsbGrabInput.Checked = canGrab && currentConnection.GrabInput;
                tsbGrabInput.Enabled = canGrab;
                grabInputToolStripMenuItem.Checked = tsbGrabInput.Checked;
                grabInputToolStripMenuItem.Enabled = canGrab;
            }
            catch(Exception exc)
            {
                Logging.Error(FULLSCREEN_ERROR_MSG, exc);
            }
        }

        /// -----------------------------------------------

        private void UpdateQuickConnectCommands()
        {
            bool quickConnectEnabled = !string.IsNullOrEmpty(tscConnectTo.Text);
            tsbConnect.Enabled = quickConnectEnabled;
            tsbConnectToConsole.Enabled = quickConnectEnabled;
            tsbConnectAs.Enabled = quickConnectEnabled;
        }

        /// -----------------------------------------------

        public String GetDesktopShare()
        {
            // ------------------------------------
            // it is safe to ask for favorite here,
            // is only called from connection supporting this feature

            String currentDesktopShare = _terminalsControler.SelectedFavorite.DesktopShare;
            var desktopShares = new DesktopShares(CurrentTerminal, _settings.DefaultDesktopShare);
            return desktopShares.EvaluateDesktopShare(currentDesktopShare);
        }

        /// -----------------------------------------------

        internal void SendNativeMessageToFocus()
        {
            if(!Visible)
            {
                Show();

                if(WindowState == FormWindowState.Minimized)
                {
                    Methods.ShowWindow(new HandleRef(this, Handle), 9);
                }

                Methods.SetForegroundWindow(new HandleRef(this, Handle));
            }
        }

        /// -----------------------------------------------

        private void ToggleGrabInput()
        {
            var currentConnection = _terminalsControler.CurrentConnection as IHandleKeyboardInput;

            if(currentConnection != null)
            {
                currentConnection.GrabInput = !currentConnection.GrabInput;
                UpgadeGrabInput();
            }
        }

        /// -----------------------------------------------

        private void CheckForMultiMonitorUse()
        {
            if(Screen.AllScreens.Length > 1)
            {
                showInDualScreensToolStripMenuItem.Enabled = true;

                // ----------------------------------------------
                // Lazy check to see if we are using dual screens

                int w = Width / Screen.PrimaryScreen.Bounds.Width;

                if(w > 2)
                {
                    _allScreens = true;
                    showInDualScreensToolStripMenuItem.Text = "Show in single screens";
                }
            }
            else
            {
                showInDualScreensToolStripMenuItem.ToolTipText = "You only have one screen";
                showInDualScreensToolStripMenuItem.Enabled = false;
            }
        }

        /// -----------------------------------------------

        internal void AssignEventsToConnectionTab(TerminalTabControlItem terminalTabPage)
        {
            terminalTabPage.DragOver += TerminalTabPage_DragOver;
            terminalTabPage.DragEnter += new DragEventHandler(terminalTabPage_DragEnter);
            terminalTabPage.Resize += new EventHandler(terminalTabPage_Resize);
        }

        /// -----------------------------------------------

        internal void AssingDoubleClickEventHandler(TerminalTabControlItem terminalTabPage)
        {
            terminalTabPage.DoubleClick += new EventHandler(TerminalTabPage_DoubleClick);
        }

        /// -----------------------------------------------

        private void LoadSpecialCommands()
        {
            SpecialCommandsToolStrip.Items.Clear();

            foreach(SpecialCommandConfigurationElement cmd in _settings.SpecialCommands)
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

        /// -----------------------------------------------

        private void ShowCredentialsManager()
        {
            using(var mgr = new CredentialManager(_persistence))
            {
                mgr.ShowDialog();
            }
        }

        /// -----------------------------------------------

        private void OpenSavedConnections()
        {
            _connectionsUiFactory.ConnectByFavoriteNames(_settings.SavedConnections);
            _settings.ClearSavedConnectionsList();
        }

        /// -----------------------------------------------

        private void HideShowFavoritesPanel(bool show)
        {
            if(_settings.EnableFavoritesPanel)
            {
                if(show)
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
                // just hide it completely

                splitContainer1.Panel1Collapsed = true;
                splitContainer1.Panel1MinSize = 0;
                splitContainer1.SplitterDistance = 0;
            }
        }

        /// -----------------------------------------------

        internal void FocusFavoriteInQuickConnectCombobox(string favoriteName)
        {
            tscConnectTo.SelectedIndex = tscConnectTo.Items.IndexOf(favoriteName);
        }

        /// -----------------------------------------------

        private void QuickConnect(String server, Int32 port, Boolean connectToConsole)
        {
            IFavorite favorite = FavoritesFactory.GetOrCreateQuickConnectFavorite(_persistence, server, connectToConsole, port);
            _connectionsUiFactory.Connect(favorite);
        }

        /// -----------------------------------------------

        internal void HandleCommandLineActions(CommandLineArgs commandLineArgs)
        {
            Boolean connectToConsole = commandLineArgs.console;
            FullScreen = commandLineArgs.fullscreen;

            if(commandLineArgs.HasUrlDefined)
            {
                QuickConnect(commandLineArgs.UrlServer, commandLineArgs.UrlPort, connectToConsole);
            }

            if(commandLineArgs.HasMachineDefined)
            {
                QuickConnect(commandLineArgs.MachineName, commandLineArgs.Port, connectToConsole);
            }

            ConnectToFavorites(commandLineArgs, connectToConsole);
        }

        /// -----------------------------------------------

        private void ConnectToFavorites(CommandLineArgs commandLineArgs, bool connectToConsole)
        {
            if(commandLineArgs.Favorites.Length > 0)
            {
                _connectionsUiFactory.ConnectByFavoriteNames(commandLineArgs.Favorites, connectToConsole);
            }
        }

        /// -----------------------------------------------

        private void SaveActiveConnections()
        {
            var activeConnections = new List<string>();

            foreach(TabControlItem item in tcTerminals.Items)
            {
                activeConnections.Add(item.Title);
            }

            _settings.CreateSavedConnectionsList(activeConnections.ToArray());
        }

        /// -----------------------------------------------

        private void CheckForNewRelease()
        {
            var updateManager = new UpdateManager();
            Task<ReleaseInfo> downloadTask = updateManager.CheckForUpdates(false);
            downloadTask.ContinueWith(CheckForNewRelease, TaskScheduler.FromCurrentSynchronizationContext());
        }

        /// -----------------------------------------------

        private void CheckForNewRelease(Task<ReleaseInfo> downloadTask)
        {
            ReleaseInfo downloaded = downloadTask.Result;

            if(downloaded.NewAvailable && !_settings.NeverShowTerminalsWindow)
            {
                ExternalLinks.AskIfShowReleasePage(_settings, downloaded);
            }

            UpdateReleaseToolStripItem(downloaded);
        }

        /// -----------------------------------------------

        private void MainForm_Load(object sender, EventArgs e)
        {
            AssingTitle();
            CheckForNewRelease();
            OpenSavedConnections();
        }

        /// -----------------------------------------------

        private void MainForm_Shown(object sender, EventArgs e)
        {
            // Get initial window state, location and
            // after the form has finished loading

            SetWindowState();
        }

        /// -----------------------------------------------

        private void MainForm_KeyUp(object sender, KeyEventArgs e)
        {
            if((tsbGrabInput.Checked || FullScreen) &&
                e.KeyCode != Keys.Cancel &&
                CurrentTerminal != null)
            {
                CurrentTerminal.Focus();
                return;
            }

            if(e.KeyCode == Keys.Cancel)
            {
                ToggleGrabInput();
            }
            else if(e.Control && e.KeyCode == Keys.F12)
            {
                _terminalsControler.CaptureScreen();
            }
            else if(e.KeyCode == Keys.F4)
            {
                if(!tscConnectTo.Focused)
                {
                    tscConnectTo.Focus();
                }
            }
            else if(e.KeyCode == Keys.F3)
            {
                ShowQuickConnect();
            }
        }

        /// -----------------------------------------------

        private void ShowQuickConnect()
        {
            using(var qc = new QuickConnect(_persistence))
            {
                if(qc.ShowDialog(this) == DialogResult.OK && !string.IsNullOrEmpty(qc.ConnectionName))
                {
                    _connectionsUiFactory.ConnectByFavoriteNames(new List<string>() { qc.ConnectionName });
                }
            }
        }

        /// -----------------------------------------------

        private void MainForm_Activated(object sender, EventArgs e)
        {
            _formSettings.EnsureVisibleScreenArea();

            if(FullScreen)
            {
                tcTerminals.ShowTabs = false;
            }
        }

        /// -----------------------------------------------

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            _favsList1.SaveState();

            if(FullScreen)
            {
                FullScreen = false;
            }

            MainWindowNotifyIcon.Visible = false;
            CloseOpenedConnections(e);
            toolStripContainer.SaveLayout();

            if(!e.Cancel)
            {
                SingleInstanceApplication.Instance.Close();
            }
        }

        /// -----------------------------------------------

        private void CloseOpenedConnections(FormClosingEventArgs args)
        {
            if(_terminalsControler.HasSelected)
            {
                if(_settings.ShowConfirmDialog)
                {
                    SaveConnectonsIfRequested(args);
                }

                if(_settings.SaveConnectionsOnClose)
                {
                    SaveActiveConnections();
                }
            }
        }

        /// -----------------------------------------------

        private void SaveConnectonsIfRequested(FormClosingEventArgs args)
        {
            var frmSaveActiveConnections = new SaveActiveConnectionsForm();

            if(frmSaveActiveConnections.ShowDialog() == DialogResult.OK)
            {
                _settings.ShowConfirmDialog = frmSaveActiveConnections.PromptNextTime;

                if(frmSaveActiveConnections.OpenConnectionsNextTime)
                {
                    SaveActiveConnections();
                }
            }
            else
            {
                args.Cancel = true;
            }
        }

        /// -----------------------------------------------

        private void MainForm_Resize(object sender, EventArgs e)
        {
            if(WindowState == FormWindowState.Minimized)
            {
                if(_settings.MinimizeToTray)
                {
                    Visible = false;
                }
            }
            else
            {
                SetWindowState();
            }
        }

        /// -----------------------------------------------

        private void MainForm_Move(object sender, EventArgs e)
        {
            if(!_fullScreenSwitch.SwitchingFullScreen && WindowState == FormWindowState.Normal)
            {
                _fullScreenSwitch.LastWindowStateNormalLocation = Location;
            }
        }

        /// -----------------------------------------------
        /// <summary>
        /// Save the MainForm windowstate, location and size for reference,
        /// when restoring from fullscreen mode.
        /// </summary>

        private void SetWindowState()
        {
            // In a higher DPI mode, the form resize event is called before
            // the fullScreenSwitch class is initialized.

            if(_fullScreenSwitch == null)
            {
                return;
            }

            // Save window state only when not switching to and from fullscreen mode

            if(_fullScreenSwitch.SwitchingFullScreen)
            {
                return;
            }

            _fullScreenSwitch.LastWindowState = WindowState;

            if(WindowState == FormWindowState.Normal)
            {
                _fullScreenSwitch.LastWindowStateNormalLocation = Location;
                _fullScreenSwitch.LastWindowStateNormalSize = Size;
            }
        }

        /// -----------------------------------------------

        private void TcTerminals_TabDetach(TabControlItemChangedEventArgs args)
        {
            tcTerminals.SelectedItem = args.Item;
            _terminalsControler.DetachTabToNewWindow();
        }

        /// -----------------------------------------------

        private void TcTerminals_MouseClick(object sender, MouseEventArgs e)
        {
            if(e.Button == MouseButtons.Right && tcTerminals != null && sender != null)
            {
                QuickContextMenu.Show(tcTerminals, e.Location);
            }
        }

        /// -----------------------------------------------

        private void QuickContextMenu_Opening(object sender, CancelEventArgs e)
        {
            TcTerminals_MouseClick(null, new MouseEventArgs(MouseButtons.Right, 1, 0, 0, 0));
            e.Cancel = false;
        }

        /// -----------------------------------------------

        private void QuickContextMenu_ItemClicked(object sender, ToolStripItemClickedEventArgs e)
        {
            ToolStripItem clickedItem = e.ClickedItem;

            if(clickedItem.Text == Program.Resources.GetString("Restore") ||
               clickedItem.Name == FavoritesMenuLoader.COMMAND_RESTORESCREEN ||
               clickedItem.Name == FavoritesMenuLoader.COMMAND_FULLSCREEN)
            {
                FullScreen = !FullScreen;
            }
            else
            {
                switch(clickedItem.Name)
                {
                    case FavoritesMenuLoader.COMMAND_CREDENTIALMANAGER:
                        ShowCredentialsManager();
                        break;

                    case FavoritesMenuLoader.COMMAND_ORGANIZEFAVORITES:
                        ManageConnectionsToolStripMenuItem_Click(null, null);
                        break;

                    case FavoritesMenuLoader.COMMAND_OPTIONS:
                        OptionsToolStripMenuItem_Click(null, null);
                        break;

                    case FavoritesMenuLoader.COMMAND_NETTOOLS:
                        ToolStripButton2_Click(null, null);
                        break;

                    case FavoritesMenuLoader.COMMAND_CAPTUREMANAGER:
                        _terminalsControler.FocusCaptureManager();
                        break;

                    case FavoritesMenuLoader.COMMAND_EXIT:
                        Close();
                        break;

                    case FavoritesMenuLoader.QUICK_CONNECT:
                        ShowQuickConnect();
                        break;

                    case FavoritesMenuLoader.COMMAND_SHOWMENU:
                        var visible = !menuStrip.Visible;
                        menuStrip.Visible = visible;
                        menubarToolStripMenuItem.Checked = visible;
                        break;

                    case FavoritesMenuLoader.COMMAND_SPECIAL:
                        return;

                    default:
                        OnFavoriteTrayToolsStripClick(e);
                        break;
                }
            }

            QuickContextMenu.Hide();
        }

        /// -----------------------------------------------

        private void OnFavoriteTrayToolsStripClick(ToolStripItemClickedEventArgs e)
        {
            var tag = e.ClickedItem.Tag as String;

            if(tag != null)
            {
                String itemName = e.ClickedItem.Text;

                if(tag == FavoritesMenuLoader.FAVORITE)
                {
                    _connectionsUiFactory.ConnectByFavoriteNames(new List<string>() { itemName });
                }
            }
        }

        /// -----------------------------------------------

        private void ExitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Close();
        }

        /// -----------------------------------------------

        private void GroupAddToolStripMenuItem_Click(object sender, EventArgs e)
        {
            // TODO Bug: doenst work, the menu doesnt refresh and the favorite isnt put into the group

            IGroup selectedGroup = ((GroupMenuItem) sender).Group;
            IFavorite selectedFavorite = _terminalsControler.SelectedOriginFavorite;

            if(selectedGroup != null && selectedFavorite != null)
            {
                selectedGroup.AddFavorite(selectedFavorite);
            }
        }

        /// -----------------------------------------------

        private void GroupToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var groupMenuItem = (GroupMenuItem) sender;

            foreach(IFavorite favorite in groupMenuItem.Favorites)
            {
                _connectionsUiFactory.Connect(favorite);
            }
        }

        /// -----------------------------------------------

        private void ServerToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var connectionName = ((ToolStripItem) sender).Text;
            var favorite = PersistedFavorites[connectionName];
            _connectionsUiFactory.Connect(favorite);
        }

        /// -----------------------------------------------

        private void terminalTabPage_Resize(object sender, EventArgs e)
        {
            var terminalTabControlItem = sender as TerminalTabControlItem;

            if(terminalTabControlItem != null)
            {
                // TODO Fix the smart sizing issue added on 7/27/2011

                //var rdpConnection = terminalTabControlItem.Connection as RDPConnection;

                //if(rdpConnection != null &&
                //    !rdpConnection.AxMsRdpClient.AdvancedSettings3.SmartSizing)
                //{
                //    //rdpConnection.AxMsRdpClient.DesktopWidth = terminalTabControlItem.Width;
                //    //rdpConnection.AxMsRdpClient.DesktopHeight = terminalTabControlItem.Height;
                //    //Debug.WriteLine("Tab size:" + terminalTabControlItem.Size.ToString() + ";" +
                //    //                rdpConnection.AxMsRdpClient.DesktopHeight.ToString() + "," +
                //    //                rdpConnection.AxMsRdpClient.DesktopWidth.ToString());
                //}
            }
        }

        /// -----------------------------------------------

        private void terminalTabPage_DragEnter(object sender, DragEventArgs e)
        {
            e.Effect = e.Data.GetDataPresent(DataFormats.FileDrop, false) ? DragDropEffects.Copy : DragDropEffects.None;
        }

        /// -----------------------------------------------

        private void TerminalTabPage_DragOver(object sender, DragEventArgs e)
        {
            _terminalsControler.Select(sender as TerminalTabControlItem);
        }

        /// -----------------------------------------------

        private void TerminalTabPage_DoubleClick(object sender, EventArgs e)
        {
            Disconnect();
        }

        /// -----------------------------------------------

        private void NewTerminalToolStripMenuItem_Click(object sender, EventArgs e)
        {
            _connectionsUiFactory.CreateFavorite();
        }

        /// -----------------------------------------------

        private void TsbConnect_Click(object sender, EventArgs e)
        {
            ConnectFromQuickCombobox(false);
        }

        /// -----------------------------------------------

        private void TsbConnectToConsole_Click(object sender, EventArgs e)
        {
            ConnectFromQuickCombobox(true);
        }

        /// -----------------------------------------------

        private void TsbConnectAs_Click(object sender, EventArgs e)
        {
            using(var usrForm = new ConnectExtraForm(_persistence))
            {
                if(usrForm.ShowDialog() != DialogResult.OK) { return; }

                ConnectFromQuickCombobox(usrForm.Console, usrForm.NewWindow, usrForm.Credentials);
            }
        }

        /// -----------------------------------------------

        private void ConnectFromQuickCombobox(bool forceConsole, bool forceNewWindow = false, ICredentialSet credentials = null)
        {
            string connectionName = tscConnectTo.Text;

            if(!string.IsNullOrEmpty(connectionName))
            {
                _connectionsUiFactory.ConnectByFavoriteNames(new List<string>() { connectionName }, forceConsole, forceNewWindow, credentials);
            }
        }

        /// -----------------------------------------------

        private void TscConnectTo_KeyDown(object sender, KeyEventArgs e)
        {
            if(e.KeyCode == Keys.Enter)
            {
                tsbConnect.PerformClick();
            }

            if(e.KeyCode == Keys.Delete && tscConnectTo.DroppedDown && tscConnectTo.SelectedIndex != -1)
            {
                String connectionName = tscConnectTo.Items[tscConnectTo.SelectedIndex].ToString();
                DeleteFavorite(connectionName);
            }
        }

        /// -----------------------------------------------

        private void DeleteFavorite(string name)
        {
            tscConnectTo.Items.Remove(name);
            var favorite = PersistedFavorites[name];
            PersistedFavorites.Delete(favorite);
            favoritesToolStripMenuItem.DropDownItems.RemoveByKey(name);
        }

        /// -----------------------------------------------

        private void TsbDisconnect_Click(object sender, EventArgs e)
        {
            Disconnect();
        }

        /// -----------------------------------------------

        private void NewTerminalToolStripMenuItem_Click_1(object sender, EventArgs e)
        {
            _connectionsUiFactory.CreateFavorite();
        }

        /// -----------------------------------------------

        private void TsbGrabInput_Click(object sender, EventArgs e)
        {
            ToggleGrabInput();
        }

        /// -----------------------------------------------

        internal void OnDisconnected(Connection connection)
        {
            _tabControlRemover.OnDisconnected(connection);
        }

        /// -----------------------------------------------

        private void TcTerminals_TabControlItemSelectionChanged(TabControlItemChangedEventArgs e)
        {
            UpdateControls();
            AssingTitle();
        }

        /// -----------------------------------------------

        private void UpdateCommandsByActiveConnection()
        {
            bool hasSelectedConnection = _terminalsControler.HasSelected;
            tsbDisconnect.Enabled = hasSelectedConnection;
            disconnectToolStripMenuItem.Enabled = hasSelectedConnection;
            toolStripButtonReconnect.Enabled = hasSelectedConnection;
            reconnectToolStripMenuItem.Enabled = hasSelectedConnection;
            addTerminalToGroupToolStripMenuItem.Enabled = hasSelectedConnection;
            saveTerminalsAsGroupToolStripMenuItem.Enabled = hasSelectedConnection;
        }

        /// -----------------------------------------------

        private void AssingTitle()
        {
            TabControlItem selectedTab = tcTerminals.SelectedItem;

            if(_settings.ShowInformationToolTips && selectedTab != null)
            {
                Text = selectedTab.ToolTipText.Replace("\r\n", "; ");
            }
            else
            {
                Text = Program.Info.GetAboutText(_persistence.Name);
            }
        }

        /// -----------------------------------------------

        private void TscConnectTo_TextChanged(object sender, EventArgs e)
        {
            UpdateControls();
        }

        /// -----------------------------------------------

        private void TcTerminals_MouseHover(object sender, EventArgs e)
        {
            if(tcTerminals != null && !tcTerminals.ShowTabs)
            {
                timerHover.Enabled = true;
            }
        }

        /// -----------------------------------------------

        private void TcTerminals_MouseLeave(object sender, EventArgs e)
        {
            timerHover.Enabled = false;

            if(FullScreen && tcTerminals.ShowTabs && !tcTerminals.MenuOpen)
            {
                tcTerminals.ShowTabs = false;
            }

            if(_currentToolTipItem != null)
            {
                _currentToolTip.Hide(_currentToolTipItem);
                _currentToolTip.Active = false;
            }
        }

        /// -----------------------------------------------

        private void TcTerminals_DoubleClick(object sender, EventArgs e)
        {
            FullScreen = !FullScreen;
        }

        /// -----------------------------------------------

        private void TsbFullScreen_Click(object sender, EventArgs e)
        {
            FullScreen = !FullScreen;
            UpdateControls();
        }

        /// -----------------------------------------------

        private void TcTerminals_MenuItemsLoaded(object sender, EventArgs e)
        {
            UpdateTabControlMenuItemIcons();

            if(FullScreen)
            {
                var sep = new ToolStripSeparator();
                tcTerminals.Menu.Items.Add(sep);
                var item = new ToolStripMenuItem(Program.Resources.GetString("Restore"), null, TcTerminals_DoubleClick);
                tcTerminals.Menu.Items.Add(item);
                item = new ToolStripMenuItem(Program.Resources.GetString("Minimize"), null, Minimize);
                tcTerminals.Menu.Items.Add(item);
            }
        }

        /// -----------------------------------------------

        private void UpdateTabControlMenuItemIcons()
        {
            foreach(ToolStripItem menuItem in tcTerminals.Menu.Items)
            {
                // ------------------------------------------
                // the menu item always has name of connected
                // favorite, so search by name works

                IFavorite favorite = _tabsFilter.FindFavoriteByTabTitle(menuItem.Text);

                if(favorite != null)
                {
                    menuItem.Image = PersistedFavorites.LoadFavoriteIcon(favorite);
                }
            }
        }

        /// -----------------------------------------------

        private void SaveTerminalsAsGroupToolStripMenuItem_Click(object sender, EventArgs e)
        {
            string newGroupName = NewGroupForm.AskFroGroupName(_persistence);

            if(string.IsNullOrEmpty(newGroupName))
            {
                return;
            }

            IGroup group = FavoritesFactory.GetOrAddNewGroup(_persistence, newGroupName);

            foreach(IFavorite favorite in _tabsFilter.SelectTabsWithFavorite())
            {
                group.AddFavorite(favorite);
            }

            _menuLoader.LoadGroups();
        }

        /// -----------------------------------------------

        private void OrganizeGroupsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            using(var frmOrganizeGroups = new OrganizeGroupsForm(_persistence, _favoriteIcons))
            {
                frmOrganizeGroups.ShowDialog();
                _menuLoader.LoadGroups();
            }
        }

        /// -----------------------------------------------

        private void TcTerminals_TabControlMouseOnTitle(TabControlMouseOnTitleEventArgs e)
        {
            if(_settings.ShowInformationToolTips)
            {
                if(_currentToolTip == null)
                {
                    _currentToolTip = new ToolTip();
                    _currentToolTip.Active = false;
                }
                else if((_currentToolTipItem != null) && (_currentToolTipItem != e.Item))
                {
                    _currentToolTip.Hide(_currentToolTipItem);
                    _currentToolTip.Active = false;
                }

                if(!_currentToolTip.Active)
                {
                    _currentToolTip = new ToolTip();
                    _currentToolTip.ToolTipTitle = Program.Resources.GetString("ConnectionInformation");
                    _currentToolTip.ToolTipIcon = ToolTipIcon.Info;
                    _currentToolTip.UseFading = true;
                    _currentToolTip.UseAnimation = true;
                    _currentToolTip.IsBalloon = false;
                    _currentToolTip.Show(e.Item.ToolTipText, e.Item, (int) e.Item.StripRect.X, 2);
                    _currentToolTipItem = e.Item;
                    _currentToolTip.Active = true;
                }
            }
        }

        /// -----------------------------------------------

        private void TcTerminals_TabControlMouseLeftTitle(TabControlMouseOnTitleEventArgs e)
        {
            if(_currentToolTipItem != null)
            {
                _currentToolTip.Hide(_currentToolTipItem);
                _currentToolTip.Active = false;
            }
        }

        /// -----------------------------------------------

        private void TimerHover_Tick(object sender, EventArgs e)
        {
            if(timerHover.Enabled)
            {
                timerHover.Enabled = false;
                tcTerminals.ShowTabs = true;
            }
        }

        /// -----------------------------------------------

        private void OrganizeFavoritesToolbarToolStripMenuItem_Click(object sender, EventArgs e)
        {
            using(var frmOrganizeFavoritesToolbar = new OrganizeFavoritesToolbarForm(_persistence))
            {
                frmOrganizeFavoritesToolbar.ShowDialog();
            }
        }

        /// -----------------------------------------------

        private void OptionsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            using(var frmOptions = new OptionDialog(CurrentTerminal, _persistence))
            {
                if(frmOptions.ShowDialog() == DialogResult.OK)
                {
                    ApplyControlsEnableAndVisibleState();
                }
            }
        }

        /// -----------------------------------------------
        /// <summary>
        ///     Disable capture button when function is 
        ///     disabled in options
        /// </summary>

        private void UpdateCaptureButtonEnabled()
        {
            Boolean enableCapture = _settings.EnabledCaptureToFolderAndClipBoard;
            CaptureScreenToolStripButton.Enabled = enableCapture;
            captureTerminalScreenToolStripMenuItem.Enabled = enableCapture;
            _terminalsControler.UpdateCaptureButtonOnDetachedPopUps();
        }

        /// -----------------------------------------------

        private void AboutToolStripMenuItem_Click(object sender, EventArgs e)
        {
            using(var frmAbout = new AboutForm(_persistence.Name))
            {
                frmAbout.ShowDialog();
            }
        }

        /// -----------------------------------------------

        private void TsbTags_Click(object sender, EventArgs e)
        {
            HideShowFavoritesPanel(tsbTags.Checked);
        }

        /// -----------------------------------------------

        private void PbShowTags_Click(object sender, EventArgs e)
        {
            HideShowFavoritesPanel(true);
        }

        /// -----------------------------------------------

        private void PbHideTags_Click(object sender, EventArgs e)
        {
            HideShowFavoritesPanel(false);
        }

        /// -----------------------------------------------

        private void TsbFavorites_Click(object sender, EventArgs e)
        {
            _settings.EnableFavoritesPanel = tsbFavorites.Checked;
            HideShowFavoritesPanel(_settings.ShowFavoritePanel);
        }

        /// -----------------------------------------------

        private void Minimize(object sender, EventArgs e)
        {
            WindowState = FormWindowState.Minimized;
        }

        /// -----------------------------------------------

        private void MainWindowNotifyIcon_MouseClick(object sender, MouseEventArgs e)
        {
            if(e.Button == MouseButtons.Left)
            {
                if(_settings.MinimizeToTray)
                {
                    Visible = !Visible;
                    if(Visible && WindowState == FormWindowState.Minimized)
                        WindowState = _fullScreenSwitch.LastWindowState;
                }
                else
                {
                    if(WindowState == FormWindowState.Normal)
                    {
                        WindowState = FormWindowState.Minimized;
                    }
                    else
                    {
                        WindowState = _fullScreenSwitch.LastWindowState;
                    }
                }
            }
        }

        /// -----------------------------------------------

        private void CaptureScreenToolStripButton_Click(object sender, EventArgs e)
        {
            _terminalsControler.CaptureScreen();
        }

        /// -----------------------------------------------

        private void CaptureTerminalScreenToolStripMenuItem_Click(object sender, EventArgs e)
        {
            _terminalsControler.CaptureScreen();
        }

        /// -----------------------------------------------

        private void ToolStripButton1_Click(object sender, EventArgs e)
        {
            ExternalLinks.OpenTerminalServiceCommandPrompt(CurrentTerminal, _settings.PsexecLocation);
        }

        /// -----------------------------------------------

        private void TsbCmd_Click(object sender, EventArgs e)
        {
            ExternalLinks.OpenTerminalServiceCommandPrompt(CurrentTerminal, _settings.PsexecLocation);
        }

        /// -----------------------------------------------

        private void StandardToolbarToolStripMenuItem_Click(object sender, EventArgs e)
        {
            AddShowStrip(toolbarStd, standardToolbarToolStripMenuItem, !toolbarStd.Visible);
        }

        /// -----------------------------------------------

        private void ToolStripMenuItemShowHideFavoriteToolbar_Click(object sender, EventArgs e)
        {
            AddShowStrip(favoriteToolBar, toolStripMenuItemShowHideFavoriteToolbar, !favoriteToolBar.Visible);
        }

        /// -----------------------------------------------

        private void ShortcutsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            AddShowStrip(SpecialCommandsToolStrip, shortcutsToolStripMenuItem, !SpecialCommandsToolStrip.Visible);
        }

        /// -----------------------------------------------

        private void MenubarToolStripMenuItem_Click(object sender, EventArgs e)
        {
            AddShowStrip(menuStrip, menubarToolStripMenuItem, !menuStrip.Visible);
        }

        /// -----------------------------------------------

        private void AddShowStrip(ToolStrip strip, ToolStripMenuItem menu, Boolean visible)
        {
            strip.Visible = visible;
            menu.Checked = visible;
            toolStripContainer.SaveLayout();
        }

        /// -----------------------------------------------

        private void ToolsToolStripMenuItem_DropDownOpening(object sender, EventArgs e)
        {
            shortcutsToolStripMenuItem.Checked = SpecialCommandsToolStrip.Visible;
            toolStripMenuItemShowHideFavoriteToolbar.Checked = favoriteToolBar.Visible;
            standardToolbarToolStripMenuItem.Checked = toolbarStd.Visible;
        }

        /// -----------------------------------------------

        private void ToolStripMenuItem3_Click(object sender, EventArgs e)
        {
            using(var org = new OrganizeShortcuts())
            {
                org.ShowDialog(this);
            }

            LoadSpecialCommands();
        }

        /// -----------------------------------------------

        private void ShortcutsContextMenu_MouseClick(object sender, MouseEventArgs e)
        {
            ToolStripMenuItem3_Click(null, null);
        }

        /// -----------------------------------------------

        private void SpecialCommandsToolStrip_MouseClick(object sender, MouseEventArgs e)
        {
            if(e.Button == MouseButtons.Right)
            {
                ShortcutsContextMenu.Show(e.X, e.Y);
            }
        }

        /// -----------------------------------------------

        private void SpecialCommandsToolStrip_ItemClicked(object sender, ToolStripItemClickedEventArgs e)
        {
            var command = e.ClickedItem.Tag as SpecialCommandConfigurationElement;

            if(command != null)
            {
                ExternalLinks.Launch(command);
            }
        }

        /// -----------------------------------------------

        private void ToolStripButton2_Click(object sender, EventArgs e)
        {
            _connectionsUiFactory.OpenNetworkingTools(NettworkingTools.None, string.Empty);
        }

        /// -----------------------------------------------

        private void NetworkingToolsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ToolStripButton2_Click(null, null);
        }

        /// -----------------------------------------------

        private void ToolStripMenuItemCaptureManager_Click(object sender, EventArgs e)
        {
            _terminalsControler.FocusCaptureManager();
        }

        /// -----------------------------------------------

        private void ToolStripButtonCaptureManager_Click(object sender, EventArgs e)
        {
            _terminalsControler.FocusCaptureManager();
        }

        /// -----------------------------------------------

        private void ToolStripButton4_Click(object sender, EventArgs e)
        {
            IConnection connection = _terminalsControler.CurrentConnection;

            if(connection != null && connection.Favorite != null)
            {
                DesktopSize desktop = connection.Favorite.Display.DesktopSize;
                connection.ChangeDesktopSize(desktop);
            }
        }

        /// -----------------------------------------------

        private void PbShowTagsFavorites_MouseMove(object sender, MouseEventArgs e)
        {
            if(_settings.AutoExapandTagsPanel)
            {
                HideShowFavoritesPanel(true);
            }
        }

        /// -----------------------------------------------

        private void LockToolbarsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            toolStripContainer.SaveLayout();
            lockToolbarsToolStripMenuItem.Checked = !lockToolbarsToolStripMenuItem.Checked;
            _settings.ToolbarsLocked = lockToolbarsToolStripMenuItem.Checked;
            toolStripContainer.ChangeLockState();
        }

        /// -----------------------------------------------

        private void UpdateReleaseToolStripItem(ReleaseInfo downloaded)
        {
            if(updateToolStripItem != null && !updateToolStripItem.Visible && downloaded.NewAvailable)
            {
                updateToolStripItem.Visible = true;
                string newText = String.Format("{0} - {1}", updateToolStripItem.Text, downloaded.Version);
                updateToolStripItem.Text = newText;
            }
        }

        /// -----------------------------------------------

        private void OpenLocalComputeManagement_Click(object sender, EventArgs e)
        {
            ExternalLinks.OpenLocalComputerManagement();
        }

        /// -----------------------------------------------

        private void RebuildTagsIndexToolStripMenuItem_Click(object sender, EventArgs e)
        {
            _persistence.Groups.Rebuild();
            _menuLoader.LoadGroups();
            UpdateControls();
        }

        /// -----------------------------------------------

        private void ViewInNewWindowToolStripMenuItem_Click(object sender, EventArgs e)
        {
            _terminalsControler.DetachTabToNewWindow();
        }

        /// -----------------------------------------------

        private void RebuildShortcutsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            _settings.SpecialCommands.Clear();
            _settings.SpecialCommands = Wizard.SpecialCommandsWizard.LoadSpecialCommands();
            LoadSpecialCommands();
        }

        /// -----------------------------------------------

        private void RebuildToolbarsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            LoadWindowState();
        }

        /// -----------------------------------------------
        // todo Move openig putty tools to putty plugin as menu extender

        private void OpenSshAgentToolStripMenuItem_Click(object sender, EventArgs e)
        {
            OpenPuttyTool("pageant.exe");
        }

        /// -----------------------------------------------

        private void OpenSshKeygenToolStripMenuItem_Click(object sender, EventArgs e)
        {
            OpenPuttyTool("puttygen.exe");
        }

        /// -----------------------------------------------

        private void OpenPuttyTool(string name)
        {
            string path = Path.Combine(PluginsLoader.FindBasePluginDirectory(), "Putty", "Resources", name);
            ExternalLinks.OpenPath(path);
        }

        /// -----------------------------------------------

        private void OpenLogFolderToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ExternalLinks.OpenPath(FileLocations.LogDirectory);
        }

        /// -----------------------------------------------

        private void SplitContainer1_SplitterMoved(object sender, SplitterEventArgs e)
        {
            if(splitContainer1.Panel1.Width > 15)
            {
                _settings.FavoritePanelWidth = splitContainer1.Panel1.Width;
            }
        }

        /// -----------------------------------------------

        private void CredentialManagementToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ShowCredentialsManager();
        }

        /// -----------------------------------------------

        private void CredentialManagementToolStripButton_Click(object sender, EventArgs e)
        {
            ShowCredentialsManager();
        }

        /// -----------------------------------------------

        private void ExportConnectionsListToolStripMenuItem_Click(object sender, EventArgs e)
        {
            using(var frm = new ExportForm(_persistence, _connectionManager, _favoriteIcons))
            {
                frm.ShowDialog();
            }
        }

        /// -----------------------------------------------

        private void ManageConnectionsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            using(OrganizeFavoritesForm conMgr = CreateOrganizeFavoritesForm())
            {
                conMgr.ShowDialog();
            }
        }

        /// -----------------------------------------------

        private void ToolStripMenuItemImport_Click(object sender, EventArgs e)
        {
            using(OrganizeFavoritesForm conMgr = CreateOrganizeFavoritesForm())
            {
                conMgr.CallImport();
                conMgr.ShowDialog();
            }
        }

        /// -----------------------------------------------

        private OrganizeFavoritesForm CreateOrganizeFavoritesForm()
        {
            var organizeForm = new OrganizeFavoritesForm(_persistence, _connectionManager, _favoriteIcons);
            organizeForm.AssignConnectionsUiFactory(_connectionsUiFactory);
            return organizeForm;
        }

        /// -----------------------------------------------

        private void ShowInDualScreensToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Screen[] screenArr = Screen.AllScreens;
            Int32 with = 0;
            
            if(!_allScreens)
            {
                if(WindowState == FormWindowState.Maximized)
                {
                    WindowState = FormWindowState.Normal;
                }

                foreach(Screen screen in screenArr)
                {
                    with += screen.Bounds.Width;
                }

                showInDualScreensToolStripMenuItem.Text = "Show in Single Screen";
                BringToFront();
            }
            else
            {
                with = Screen.PrimaryScreen.Bounds.Width;
                showInDualScreensToolStripMenuItem.Text = "Show In Multi Screens";
            }

            Top = 0;
            Left = 0;
            Height = Screen.PrimaryScreen.Bounds.Height;
            Width = with;
            _allScreens = !_allScreens;
        }

        /// -----------------------------------------------

        private void UpdateToolStripItem_Click(object sender, EventArgs e)
        {
            ExternalLinks.ShowReleasePage();
        }

        /// -----------------------------------------------

        private void ClearHistoryToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Cursor = Cursors.WaitCursor;
            _persistence.ConnectionHistory.Clear();
            Cursor = Cursors.Default;
        }

        /// -----------------------------------------------

        private void ToolStripButtonReconnect_Click(object sender, EventArgs e)
        {
            Reconnect();
        }

        /// -----------------------------------------------

        public void Reconnect()
        {
            IConnection currentConnection = _terminalsControler.CurrentConnection;

            if(currentConnection != null)
            {
                IFavorite favorite = currentConnection.Favorite;
                _tabControlRemover.Disconnect();
                _connectionsUiFactory.Connect(favorite);
            }
        }

        /// -----------------------------------------------

        public bool CanExecute(IFavorite selected)
        {
            IFavorite selectedInTab = _terminalsControler.SelectedOriginFavorite;
            return selected != null && selected.StoreIdEquals(selectedInTab);
        }

        /// -----------------------------------------------

        public void Disconnect()
        {
            _tabControlRemover.Disconnect();
        }

        /// -----------------------------------------------

        private void SpecialCommandsToolStrip_MouseClick(object sender, EventArgs e)
        {

        }

        // ------------------------------------------------

        private void OnMainFormKeyDown(object sender, KeyEventArgs e)
        {
            if(e.Modifiers == Keys.Alt && e.KeyCode == Keys.M && menuStrip.Visible == false)
            {
                menuStrip.Visible = true;
                menubarToolStripMenuItem.Checked = true;
            }
        }
    }
}
