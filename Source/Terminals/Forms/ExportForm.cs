using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

using Terminals.Connections;
using Terminals.Data;
using Terminals.Forms.Controls;
using Terminals.Integration;
using Terminals.Integration.Export;

namespace Terminals.Forms
{
    internal partial class ExportForm : Form
    {
        private readonly IPersistence _persistence;
        private readonly FavoriteTreeListLoader _treeLoader;
        private readonly Exporters _exporters;

        private readonly ConnectionManager _connectionManager;

        private readonly FavoriteIcons _favoriteIcons;

        public ExportForm(IPersistence persistence, ConnectionManager connectionManager, FavoriteIcons favoriteIcons)
        {
            _persistence = persistence;
            InitializeComponent();

            _favoriteIcons = favoriteIcons;
            _treeLoader = new FavoriteTreeListLoader(_favsTree, _persistence, _favoriteIcons);
            _treeLoader.LoadRootNodes();
            _connectionManager = connectionManager;
            _exporters = new Exporters(_persistence, _connectionManager);
            _saveFileDialog.Filter = _exporters.GetProvidersDialogFilter();
        }

        private void ExportForm_Load(object sender, EventArgs e)
        {
            _favsTree.AssignServices(_persistence, _favoriteIcons, _connectionManager);
        }

        private void BtnCancel_Click(object sender, EventArgs e)
        {
            Close();
        }

        // ------------------------------------------------

        private void BtnExport_Click(object sender, EventArgs e)
        {
            if(_saveFileDialog.ShowDialog() == DialogResult.OK)
            {
                if(_favsTree.SelectedNode != null)
                {
                    RunExport();
                }

                string message = "Done exporting, you can find your exported file at " + _saveFileDialog.FileName;
                MessageBox.Show(message, "Terminals export");
                Close();
            }
        }

        // ------------------------------------------------

        private void RunExport()
        {
            List<FavoriteConfigurationElement> favorites = GetFavoritesToExport();
            
            // -----------------------
            // Filter index is 1 based

            int filterSplitIndex = (_saveFileDialog.FilterIndex - 1) * 2;
            string providerFilter = _saveFileDialog.Filter.Split('|')[filterSplitIndex];

            var options = new ExportOptions
            {
                ProviderFilter = providerFilter,
                Favorites = favorites,
                FileName = _saveFileDialog.FileName,
                IncludePasswords = _checkBox1.Checked
            };

            _exporters.Export(options);
        }

        // ------------------------------------------------

        private List<FavoriteConfigurationElement> GetFavoritesToExport()
        {
            List<IFavorite> favorites = TreeListNodes.FindAllCheckedFavorites(_favsTree.Nodes);
            return ConvertFavoritesToExport(favorites);
        }

        // ------------------------------------------------

        private List<FavoriteConfigurationElement> ConvertFavoritesToExport(List<IFavorite> favorites)
        {
            return favorites.Distinct()
                .Select(favorite => ModelConverterV2ToV1.ConvertToFavorite(favorite, _persistence, _connectionManager))
                .ToList();
        }

        private void FavsTree_AfterCheck(object sender, TreeViewEventArgs e)
        {
            var groupNode = e.Node as GroupTreeNode;
            if(groupNode != null)
                groupNode.CheckChildsByParent();
        }

        private void BtnSelect_Click(object sender, EventArgs e)
        {
            // dont expand only load complete subtree
            _treeLoader.LoadGroupNodesRecursive();
            TreeListNodes.CheckChildNodesRecursive(_favsTree.Nodes, true);
        }

        private void ExportForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            _treeLoader.UnregisterEvents();
        }
    }
}
