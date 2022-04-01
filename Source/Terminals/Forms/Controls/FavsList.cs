using System;
using System.Drawing;
using System.Management;
using System.Windows.Forms;
using System.Threading.Tasks;
using System.Collections.Generic;

using Terminals.Data;
using Terminals.Forms;
using Terminals.Network;
using Terminals.Services;
using Terminals.Connections;
using Terminals.Credentials;
using Terminals.Configuration;
using Terminals.Forms.Controls;
using Terminals.Data.Validation;

using TreeView = Terminals.Forms.Controls.TreeView;

namespace Terminals
{
    internal partial class FavsList : UserControl
    {
        private readonly Settings settings = Settings.Instance;
        private FavoriteTreeListLoader _treeLoader;
        private static readonly string _shutdownFailMessage = Program.Resources.GetString("UnableToRemoteShutdown");
        internal ConnectionsUiFactory ConnectionsUiFactory { private get; set; }

        private FavoriteRenameCommand _renameCommand;

        private bool _isRenaming;

        private FavoriteIcons _favoriteIcons;

        private ConnectionManager _connectionManager;

        private IPersistence _persistence;

        private IConnectionCommands _connectionCommands;

        // ------------------------------------------------

        private IFavorites PersistedFavorites
        {
            get { return _persistence.Favorites; }
        }

        // ------------------------------------------------

        public FavsList()
        {
            InitializeComponent();
            InitializeSearchAndFavsPanels();
            
            // Update the old treeview theme to the new theme from Win Vista and up
            
            Native.Methods.SetWindowTheme(favsTree.Handle, "Explorer", null);
            Native.Methods.SetWindowTheme(historyTreeView.Handle, "Explorer", null);

            historyTreeView.DoubleClick += new EventHandler(HistoryTreeView_DoubleClick);
        }

        // ------------------------------------------------

        internal void AssignServices(IPersistence persistence, ConnectionManager connectionManager,
                                     FavoriteIcons favoriteIcons, IConnectionCommands connectionCommands)
        {
            this._persistence = persistence;
            this._connectionManager = connectionManager;
            this._favoriteIcons = favoriteIcons;
            this._connectionCommands = connectionCommands;
        }

        // ------------------------------------------------

        private void CloseMenuStrips()
        {
            favoritesContextMenu.Close();
            groupsContextMenu.Close();
        }

        // ------------------------------------------------

        private void InitializeSearchAndFavsPanels()
        {
            favsTree.Visible = true;
            favsTree.Dock = DockStyle.Fill;
            searchPanel1.Visible = false;
            searchPanel1.Dock = DockStyle.Fill;
        }

        // ------------------------------------------------

        private void FavsList_Load(object sender, EventArgs e)
        {
            favsTree.AssignServices(_persistence, _favoriteIcons, _connectionManager);
            _treeLoader = new FavoriteTreeListLoader(favsTree, _persistence, _favoriteIcons);
            _treeLoader.LoadRootNodes();
            historyTreeView.Load(_persistence, _favoriteIcons);
            LoadState();
            favsTree.MouseUp += new MouseEventHandler(FavsTree_MouseUp);
            searchTextBox.LoadEvents(_persistence);
            // hadle events
            searchPanel1.LoadEvents(_persistence, _favoriteIcons);
            _renameCommand = new FavoriteRenameCommand(_persistence, new RenameService(_persistence.Favorites));
        }

        // ------------------------------------------------

        private void HistoryTreeView_DoubleClick(object sender, EventArgs e)
        {
            StartConnectionByDoubleClick(historyTreeView, e);
        }

        // ------------------------------------------------

        private void PingToolStripMenuItem_Click(object sender, EventArgs e)
        {
            OpenNetworkingTool(NettworkingTools.Ping);
        }

        // ------------------------------------------------

        private void DNsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            OpenNetworkingTool(NettworkingTools.Dns);
        }

        // ------------------------------------------------

        private void TraceRouteToolStripMenuItem_Click(object sender, EventArgs e)
        {
            OpenNetworkingTool(NettworkingTools.Trace);
        }

        // ------------------------------------------------

        private void TsAdminToolStripMenuItem_Click(object sender, EventArgs e)
        {
            OpenNetworkingTool(NettworkingTools.TsAdmin);
        }

        // ------------------------------------------------

        private void OpenNetworkingTool(NettworkingTools toolName)
        {
            IFavorite fav = GetSelectedFavorite();

            if(fav != null)
            {
                ConnectionsUiFactory.OpenNetworkingTools(toolName, fav.ServerName);
            }
        }

        // ------------------------------------------------

        private void CreateFavoriteToolStripMenuItem_Click(object sender, EventArgs e)
        {
            GroupTreeNode groupNode = favsTree.SelectedGroupNode;
            ConnectionsUiFactory.CreateFavorite(string.Empty, groupNode);
        }

        // ------------------------------------------------

        private void PropertiesToolStripMenuItem_Click(object sender, EventArgs e)
        {
            IFavorite fav = GetSelectedFavorite();

            if(fav != null)
            {
                ConnectionsUiFactory.EditFavorite(fav, dr => { });
            }
        }

        // ------------------------------------------------

        private void RebootToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Reboot();
        }

        // ------------------------------------------------

        private void Reboot()
        {
            ProcessRemoteShutdownOpearation("reboot", ShutdownCommands.ForcedReboot);
        }

        // ------------------------------------------------

        private void ShutdownToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ProcessRemoteShutdownOpearation("shutdown", ShutdownCommands.ForcedShutdown);
        }

        // ------------------------------------------------

        private void ProcessRemoteShutdownOpearation(string operationName, ShutdownCommands shutdownStyle)
        {
            IFavorite fav = GetSelectedFavorite();

            if(fav == null) { return; }

            const string QUESTION_TEMPLATE = "Are you sure you want to {0} this machine: {1}\r\n" +
                                             "Operation requires administrator priviledges and can take a while.";
            var question = String.Format(QUESTION_TEMPLATE, operationName, fav.ServerName);
            string title = Program.Resources.GetString("Confirmation");
            var confirmResult = MessageBox.Show(question, title, MessageBoxButtons.YesNo, MessageBoxIcon.Question);

            if(confirmResult != DialogResult.Yes) { return; }

            var options = new Tuple<ShutdownCommands, IFavorite>(shutdownStyle, fav);
            var shutdownTask = Task.Factory.StartNew(new Func<object, string>(TryPerformRemoteShutdown), options);
            shutdownTask.ContinueWith(new Action<Task<string>>(ShowRebootResult), TaskScheduler.FromCurrentSynchronizationContext());
        }

        // ------------------------------------------------

        private void ShowRebootResult(Task<string> shutdownTask)
        {
            MessageBox.Show(shutdownTask.Result, "Remote action result", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // ------------------------------------------------

        private string TryPerformRemoteShutdown(object state)
        {
            try
            {
                var options = state as Tuple<ShutdownCommands, IFavorite>;

                if(options != null && RemoteManagement.ForceShutdown(_persistence, options.Item2, options.Item1))
                {
                    return "Terminals successfully sent the shutdown command.";
                }

                return _shutdownFailMessage;
            }
            catch(ManagementException ex)
            {
                Logging.Info(ex.ToString(), ex);
                return _shutdownFailMessage + "\r\nPlease check the log file for more details.";
            }
            catch(UnauthorizedAccessException)
            {
                return _shutdownFailMessage + "\r\n\r\nAccess is Denied.";
            }
        }

        // ------------------------------------------------

        private void EnableRDPToolStripMenuItem_Click(object sender, EventArgs e)
        {
            IFavorite fav = GetSelectedFavorite();
            Task<bool?> enableRdpTask = Task.Factory.StartNew(new Func<object, bool?>(TryEnableRdp), fav);
            enableRdpTask.ContinueWith(ShowEnableRdpResult, TaskScheduler.FromCurrentSynchronizationContext());
        }

        // ------------------------------------------------

        private void ShowEnableRdpResult(Task<bool?> enableRdpTask)
        {
            bool? operationResult = enableRdpTask.Result;

            if(operationResult.HasValue)
            {
                ShowEnableRdpResult(operationResult.Value);
            }
            else
            {
                MessageBox.Show("Terminals was not able to enable RDP remotely.");
            }
        }

        // ------------------------------------------------

        private void ShowEnableRdpResult(bool operationResult)
        {
            if(operationResult)
            {
                const string MESSAGE = "Terminals enabled the RDP on the remote machine, " +
                                       "would you like to reboot that machine for the change to take effect?";

                if(MessageBox.Show(MESSAGE, "Reboot Required", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.OK)
                {
                    Reboot();
                }
            }
            else
            {
                MessageBox.Show("RDP is already enabled.");
            }
        }

        // ------------------------------------------------

        private static bool? TryEnableRdp(object state)
        {
            try
            {
                var fav = state as IFavorite;

                if(fav == null) { return null; }
                
                return RemoteManagement.EnableRdp(fav);
            }
            catch // dont need to recover (registry problem, RPC problem, UAC not happy)
            {
                return null;
            }
        }

        // ------------------------------------------------

        private void ConnectFromContextMenu(List<IFavorite> favorites)
        {
            CloseMenuStrips();
            var definition = new ConnectionDefinition(favorites);
            ConnectionsUiFactory.Connect(definition);
        }

        // ------------------------------------------------

        private void ExtraConnectToolStripMenuItem_Click(object sender, EventArgs e)
        {
            CloseMenuStrips();
            var favorite = GetSelectedFavorite();

            if(favorite == null) { return; }

            var favorites = new List<IFavorite>() { favorite };
            ConnectToFavoritesExtra(favorites);
        }

        // ------------------------------------------------

        private void ConnectToAllExtraToolStripMenuItem_Click(object sender, EventArgs e)
        {
            CloseMenuStrips();
            List<IFavorite> selectedFavorites = favsTree.SelectedGroupFavorites;
            ConnectToFavoritesExtra(selectedFavorites);
        }

        // ------------------------------------------------

        private void ConnectToFavoritesExtra(List<IFavorite> selectedFavorites)
        {
            using(var usrForm = new ConnectExtraForm(_persistence))
            {
                if(usrForm.ShowDialog() != DialogResult.OK) { return; }

                var definition = new ConnectionDefinition(selectedFavorites, usrForm.Console, usrForm.NewWindow, usrForm.Credentials);
                ConnectionsUiFactory.Connect(definition);
            }
        }

        // ------------------------------------------------

        private void ComputerManagementMmcToolStripMenuItem_Click(object sender, EventArgs e)
        {
            IFavorite favorite = GetSelectedFavorite();
            ExternalLinks.StartMsManagementConsole(favorite);
        }

        // ------------------------------------------------

        private void SystemInformationToolStripMenuItem_Click(object sender, EventArgs e)
        {
            IFavorite favorite = GetSelectedFavorite();
            ExternalLinks.StartMsInfo32(favorite);
        }

        // ------------------------------------------------

        private void SetCredentialByTagToolStripMenuItem_Click(object sender, EventArgs e)
        {
            const string VARIABLE = "Credential";
            InputBoxResult result = PromptForVariableChange(VARIABLE);
            
            if(result.ReturnCode == DialogResult.OK)
            {
                ICredentialSet credential = _persistence.Credentials[result.Text];
                
                if(credential == null)
                {
                    MessageBox.Show("The credential you specified does not exist.");
                    return;
                }

                List<IFavorite> selectedFavorites = StartBatchUpdate();
                PersistedFavorites.ApplyCredentialsToAllFavorites(selectedFavorites, credential);
                FinishBatchUpdate(VARIABLE);
            }
        }

        // ------------------------------------------------

        private void SetPasswordByTagToolStripMenuItem_Click(object sender, EventArgs e)
        {
            const string VARIABLE = "Password";
            InputBoxResult result = PromptForVariableChange(VARIABLE, '*');
            
            if(result.ReturnCode == DialogResult.OK)
            {
                List<IFavorite> selectedFavorites = StartBatchUpdate();
                PersistedFavorites.SetPasswordToAllFavorites(selectedFavorites, result.Text);
                FinishBatchUpdate(VARIABLE);
            }
        }

        // ------------------------------------------------

        private void SetDomainByTagToolStripMenuItem_Click(object sender, EventArgs e)
        {
            const string VARIABLE = "Domain name";
            InputBoxResult result = PromptForVariableChange(VARIABLE);
            
            if(result.ReturnCode == DialogResult.OK)
            {
                List<IFavorite> selectedFavorites = StartBatchUpdate();
                PersistedFavorites.ApplyDomainNameToAllFavorites(selectedFavorites, result.Text);
                FinishBatchUpdate(VARIABLE);
            }
        }

        // ------------------------------------------------

        private void SetUsernameByTagToolStripMenuItem_Click(object sender, EventArgs e)
        {
            const string VARIABLE = "User name";
            InputBoxResult result = PromptForVariableChange(VARIABLE);
            
            if(result.ReturnCode == DialogResult.OK)
            {
                List<IFavorite> selectedFavorites = StartBatchUpdate();
                PersistedFavorites.ApplyUserNameToAllFavorites(selectedFavorites, result.Text);
                FinishBatchUpdate(VARIABLE);
            }
        }

        // ------------------------------------------------

        private List<IFavorite> StartBatchUpdate()
        {
            if(ParentForm != null)
            {
                ParentForm.Cursor = Cursors.WaitCursor;
            }

            return favsTree.SelectedGroupFavorites;
        }

        // ------------------------------------------------

        private void FinishBatchUpdate(string variable)
        {
            if(ParentForm != null)
            {
                ParentForm.Cursor = Cursors.Default;
            }
            
            string message = string.Format("Set {0} by group Complete.", variable);
            MessageBox.Show(message);
        }

        // ------------------------------------------------

        private InputBoxResult PromptForVariableChange(string variable, char passwordChar = '\0')
        {
            var groupName = favsTree.SelectedNode.Text;
            var prompt = String.Format("This will replace the {0} for all Favorites within this group.\r\nUse at your own risk!\r\n\r\nEnter new {0}:",
                                            variable);
            var title = string.Format("Change {0} - {1}", variable, groupName);

            if(passwordChar != '\0')
            {
                return InputBox.Show(prompt, title, passwordChar);
            }

            return InputBox.Show(prompt, title);
        }

        // ------------------------------------------------

        private void DeleteAllFavoritesByTagToolStripMenuItem_Click(object sender, EventArgs e)
        {
            string groupName = favsTree.SelectedNode.Text;
            string title = "Delete all Favorites by group - " + groupName;
            const string PROMPT = "This will DELETE all Favorites within this group.\r\nDo you realy want to delete them?";
            DialogResult result = MessageBox.Show(PROMPT, title, MessageBoxButtons.YesNo, MessageBoxIcon.Question);

            if(result == DialogResult.Yes)
            {
                List<IFavorite> selectedFavorites = StartBatchUpdate();
                _persistence.Favorites.Delete(selectedFavorites);

                if(ParentForm != null)
                {
                    ParentForm.Cursor = Cursors.Default;
                }

                MessageBox.Show("Delete all Favorites by group Complete.");
            }
        }

        // ------------------------------------------------

        private void RemoveSelectedToolStripMenuItem_Click(object sender, EventArgs e)
        {
            IFavorite favorite = GetSelectedFavorite();

            if(favorite != null && OrganizeFavoritesForm.AskIfRealyDelete("favorite"))
            {
                PersistedFavorites.Delete(favorite);
            }
        }

        // ------------------------------------------------

        private void FavsTree_MouseUp(object sender, MouseEventArgs e)
        {
            if(e.Button != MouseButtons.Right) { return; }

            var clickedPoint = new Point(e.X, e.Y);
            TreeNode clickedNode = favsTree.GetNodeAt(clickedPoint);
            favsTree.SelectedNode = clickedNode;

            if(clickedNode != null)
            {
                FavsTreeNodeMenuOpening(clickedPoint);
            }
            else
            {
                defaultContextMenu.Show(favsTree, clickedPoint);
            }
        }

        // ------------------------------------------------

        private void FavsTreeNodeMenuOpening(Point clickedPoint)
        {
            if(favsTree.SelectedFavorite != null)
            {
                ShowFavoritesContextMenu(clickedPoint);
            }
            else
            {
                groupsContextMenu.Show(favsTree, clickedPoint);
            }
        }

        // ------------------------------------------------

        private void ShowFavoritesContextMenu(Point clickedPoint)
        {
            IFavorite selected = GetSelectedFavorite();
            bool canExecute = _connectionCommands.CanExecute(selected);
            reconnectToolStripMenuItem.Visible = canExecute;
            disconnectToolStripMenuItem.Visible = canExecute;
            favoritesContextMenu.Show(favsTree, clickedPoint);
        }

        // ------------------------------------------------

        private void FavsTree_DoubleClick(object sender, EventArgs e)
        {
            StartConnectionByDoubleClick(favsTree, e);
        }

        // ------------------------------------------------

        private void StartConnectionByDoubleClick(TreeView treeView, EventArgs e)
        {
            Point doubleClickLocation = ((MouseEventArgs)e).Location;
            TreeNode doubleClickedNode = treeView.GetNodeAt(doubleClickLocation);

            if(doubleClickedNode == treeView.SelectedNode)
            {
                StartConnection(treeView);
            }
        }

        // ------------------------------------------------

        private void StartConnection(TreeView tv)
        {
            // dont connect in rename in favorites tree

            var favoriteNode = tv.SelectedNode as FavoriteTreeNode;

            if(favoriteNode != null && !tv.SelectedNode.IsEditing)
            {
                var definition = new ConnectionDefinition(favoriteNode.Favorite);
                ConnectionsUiFactory.Connect(definition);
                tv.Parent.Focus();
            }
        }

        // ------------------------------------------------

        private void HistoryTreeView_KeyUp(object sender, KeyEventArgs e)
        {
            if(e.KeyCode == Keys.Enter)
            {
                StartConnection(historyTreeView);
            }
        }

        // ------------------------------------------------

        private void DeleteGroupToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var groupNode = favsTree.SelectedGroupNode;

            if(groupNode != null && OrganizeFavoritesForm.AskIfRealyDelete("group"))
            {
                _persistence.Groups.Delete(groupNode.Group);
            }
        }

        // ------------------------------------------------

        private void DuplicateToolStripMenuItem_Click(object sender, EventArgs e)
        {
            IFavorite selected = GetSelectedFavorite();

            if(selected == null)
            {
                return;
            }

            var copyCommand = new CopyFavoriteCommand(_persistence);
            copyCommand.Copy(selected);
        }

        // ------------------------------------------------

        private void ConnectButton_Click(object sender, EventArgs e)
        {
            ConnectToSelectedFavorites();
        }

        // ------------------------------------------------

        private List<IFavorite> GetSelectedFavorites()
        {
            var selected = GetSelectedFavorite();

            if(selected != null)
            {
                return new List<IFavorite>() { selected };
            }

            return favsTree.SelectedGroupFavorites;
        }

        // ------------------------------------------------
        /// <summary>
        ///     Because the favorite context menu is shared 
        ///     between tree view and search results list,
        ///     we have to distinguish, from where to obtain 
        ///     the selected favorite.
        /// </summary>

        private IFavorite GetSelectedFavorite()
        {
            if(searchPanel1.Visible)
            {
                return searchPanel1.SelectedFavorite;
            }

            // favorites tree view is selected

            return favsTree.SelectedFavorite;
        }

        // ------------------------------------------------

        private void CollapseAllToolStripMenuItem_Click(object sender, EventArgs e)
        {
            favsTree.CollapseAll();
        }

        // ------------------------------------------------

        private void CollpseHistoryButton_Click(object sender, EventArgs e)
        {
            historyTreeView.CollapseAll();
        }

        // ------------------------------------------------

        private void ClearHistoryButton_Click(object sender, EventArgs e)
        {
            Cursor = Cursors.WaitCursor;
            _persistence.ConnectionHistory.Clear();
            Cursor = Cursors.Default;
        }

        // ------------------------------------------------

        private void FavsTree_KeyUp(object sender, KeyEventArgs e)
        {
            switch(e.KeyCode)
            {
                case Keys.F2:
                    BeginRenameInFavsTree();
                    break;

                case Keys.Enter:

                    if(!_isRenaming)
                    {
                        StartConnection(favsTree);
                    }

                    break;
            }
        }

        // ------------------------------------------------

        private void RenameToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if(searchPanel1.Visible)
            {
                searchPanel1.BeginRename();
            }
            else
            {
                BeginRenameInFavsTree();
            }
        }

        // ------------------------------------------------

        private void BeginRenameInFavsTree()
        {
            if(tabControl1.SelectedTab == FavoritesTabPage && favsTree.SelectedNode != null)
            {
                favsTree.SelectedNode.BeginEdit();
            }
        }

        // ------------------------------------------------

        private void FavsTree_AfterLabelEdit(object sender, NodeLabelEditEventArgs e)
        {
            if(string.IsNullOrEmpty(e.Label))
            {
                e.CancelEdit = true;
                return;
            }

            // following methods are called with the beginInvoke to realy perform the action. 
            // This is a trick how to deal with the WinForms fact, that in this event handler the action isnt realy finished.

            TryRenameFavoriteNode(e);
            TryRenameGroupNode(e);
        }

        // ------------------------------------------------

        private void TryRenameFavoriteNode(NodeLabelEditEventArgs e)
        {
            IFavorite favorite = favsTree.SelectedFavorite;
            e.CancelEdit = ValidateAndRename(favorite, e.Label);
        }

        // ------------------------------------------------

        private void TryRenameGroupNode(NodeLabelEditEventArgs e)
        {
            var groupNode = favsTree.SelectedGroupNode;

            if(groupNode == null) { return; }

            SheduleRename(groupNode.Group, e);
        }

        // ------------------------------------------------

        private void SheduleRename(IGroup group, NodeLabelEditEventArgs e)
        {
            var groupValidator = new GroupNameValidator(_persistence);
            string errorMessage = groupValidator.ValidateCurrent(group, e.Label);

            if(string.IsNullOrEmpty(errorMessage))
            {
                var groupArguments = new object[] { group, e.Label };
                favsTree.BeginInvoke(new Action<IGroup, string>(RenameGroup), groupArguments);
            }
            else
            {
                e.CancelEdit = true;
                MessageBox.Show(errorMessage, "Group name is not valid", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ------------------------------------------------

        private void RenameGroup(IGroup group, string newName)
        {
            group.Name = newName;
            _persistence.Groups.Update(group);
        }

        // ------------------------------------------------

        private void SearchPanel1_ResultListAfterLabelEdit(object sender, LabelEditEventArgs e)
        {
            IFavorite favorite = GetSelectedFavorite();

            // ------------------------
            // user canceled the rename

            if(string.IsNullOrEmpty(e.Label))
            {
                e.CancelEdit = true;
            }
            else
            {
                e.CancelEdit = ValidateAndRename(favorite, e.Label);
            }
        }

        // ------------------------------------------------
        /// <summary>
        ///     Because of incompatible event arguments class 
        ///     in tree and search panel, we have to return 
        ///     the cancelation result, to be able cancel the 
        ///     edit in UI. Returns true, if rename should be 
        ///     canceled.
        /// </summary>

        private bool ValidateAndRename(IFavorite favorite, string newName)
        {
            if(favorite == null)
                return true;

            bool isValid = _renameCommand.ValidateNewName(favorite, newName);
            if(isValid)
            {
                _isRenaming = true;
                SheduleRename(favorite, newName);
            }

            return !isValid;
        }

        // ------------------------------------------------

        private void SheduleRename(IFavorite favorite, string newName)
        {
            var favoriteArguments = new object[] { favorite, newName };
            favsTree.BeginInvoke(new Action<IFavorite, string>(ApplyRename), favoriteArguments);
        }

        // ------------------------------------------------

        private void ApplyRename(IFavorite favorite, string newName)
        {
            _renameCommand.ApplyRename(favorite, newName);
            _isRenaming = false;
        }

        // ------------------------------------------------

        private void SearchPanel_ResultListKeyUp(object sender, KeyEventArgs e)
        {
            switch(e.KeyCode)
            {
                case Keys.F2:
                    searchPanel1.BeginRename();
                    break;

                case Keys.Enter:
                    if(!_isRenaming)
                        ConnectToSelectedFavorites();
                    break;
            }
        }

        // ------------------------------------------------

        private void ConnectToSelectedFavorites()
        {
            List<IFavorite> favorites = GetSelectedFavorites();
            ConnectFromContextMenu(favorites);
        }

        // ------------------------------------------------

        public void SaveState()
        {
            settings.StartDelayedUpdate();
            settings.ExpandedFavoriteNodes = favsTree.ExpandedNodes;
            settings.ExpandedHistoryNodes = historyTreeView.ExpandedNodes;
            settings.SaveAndFinishDelayedUpdate();

            searchTextBox.UnloadEvents();
        }

        // ------------------------------------------------

        private void LoadState()
        {
            favsTree.ExpandedNodes = settings.ExpandedFavoriteNodes;
            historyTreeView.ExpandedNodes = settings.ExpandedHistoryNodes;
        }

        // ------------------------------------------------

        private void NewGroupToolStripMenuItem_Click(object sender, EventArgs e)
        {
            // -------------------------------------------------
            // backup the selected tree node, because it will be
            // replaced later by focus of NewGroupForm

            GroupTreeNode parentGroupNode = favsTree.SelectedGroupNode;
            string newGroupName = NewGroupForm.AskFroGroupName(_persistence);

            if(string.IsNullOrEmpty(newGroupName))
            {
                return;
            }

            IGroup newGroup = _persistence.Factory.CreateGroup(newGroupName);

            if(parentGroupNode != null)
            {
                newGroup.Parent = parentGroupNode.Group;
            }

            _persistence.Groups.Add(newGroup);
        }

        // ------------------------------------------------

        private void SearchTextBox_Found(object sender, FavoritesFoundEventArgs args)
        {
            searchPanel1.LoadFromFavorites(args.Favorites);
            searchPanel1.Visible = true;
            favsTree.Visible = false;
        }

        // ------------------------------------------------

        private void SearchTextBox_Canceled(object sender, EventArgs e)
        {
            searchPanel1.Visible = false;
            favsTree.Visible = true;
        }

        // ------------------------------------------------

        private void ReconnectToolStripMenuItem_Click(object sender, EventArgs e)
        {
            CloseMenuStrips();
            _connectionCommands.Reconnect();
        }

        // ------------------------------------------------

        private void DisconnectToolStripMenuItem_Click(object sender, EventArgs e)
        {
            CloseMenuStrips();
            _connectionCommands.Disconnect();
        }
    }
}
