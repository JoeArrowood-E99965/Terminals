using System;
using System.Collections.Generic;
using System.Linq;
using TabControl;
using Terminals.CaptureManager;
using Terminals.Configuration;
using Terminals.Connections;
using Terminals.Data;
using Terminals.Forms;

namespace Terminals
{
    /// ---------------------------------------------------
    /// <summary>
    ///     Adapter between all windows (including main window) 
    ///     and TabControl
    /// </summary>
    
    internal class TerminalTabsSelectionControler : ICurrenctConnectionProvider
    {
        private readonly Settings _settings = Settings.Instance;
        private readonly List<PopupTerminal> _detachedWindows = new List<PopupTerminal>();
        private readonly TabControl.TabControl _mainTabControl;
        private ConnectionsUiFactory _connectionsUiFactory;

        private readonly TabControlFilter _filter;

        public IConnection CurrentConnection
        {
            get { return _filter.SelectedConnection; }
        }

        internal TerminalTabControlItem Selected
        {
            get { return _filter.Selected; }
        }

        internal IFavorite SelectedOriginFavorite
        {
            get { return _filter.SelectedOriginFavorite; }
        }

        internal IFavorite SelectedFavorite
        {
            get { return _filter.SelectedFavorite; }
        }

        internal bool HasSelected
        {
            get { return _filter.HasSelected; }
        }

        internal TerminalTabsSelectionControler(TabControl.TabControl tabControl, IPersistence persistence)
        {
            _mainTabControl = tabControl;
            _filter = new TabControlFilter(tabControl);
            persistence.Dispatcher.FavoritesChanged += new FavoritesChangedEventHandler(OnFavoritesChanged);
        }

        internal void AssingUiFactory(ConnectionsUiFactory connectionsUiFactory)
        {
            _connectionsUiFactory = connectionsUiFactory;
        }

        private void OnFavoritesChanged(FavoritesChangedEventArgs args)
        {
            foreach (IFavorite updated in args.Updated)
            {
                // -----------------------------------
                // dont update the rest of properties,
                // because it doesnt reflect opened session

                UpdateDetachedWindowTitle(updated);
                UpdateAttachedTabTitle(updated);
            }
        }

        private void UpdateAttachedTabTitle(IFavorite updated)
        {
            TabControlItem attachedTab = _filter.FindAttachedTab(updated);
            if (attachedTab != null)
                attachedTab.Title = updated.Name;
        }

        private void UpdateDetachedWindowTitle(IFavorite updated)
        {
            PopupTerminal detached = FindDetachedWindowByFavorite(updated);
            if (detached != null)
                detached.UpdateTitle();
        }

        private PopupTerminal FindDetachedWindowByFavorite(IFavorite updated)
        {
            return _detachedWindows.FirstOrDefault(window => window.HasFavorite(updated));
        }

        /// <summary>
        /// Markes the selected terminal as selected. If it is in mainTabControl,
        /// then directly selects it, otherwise marks the selected window
        /// </summary>
        /// <param name="toSelect">new terminal tabControl to assign as selected</param>
        internal void Select(TerminalTabControlItem toSelect)
        {
            _mainTabControl.SelectedItem = toSelect;
        }

        /// <summary>
        /// Clears the selection of currently manipulated TabControl.
        /// This has the same result like to call Select(null).
        /// </summary>
        internal void UnSelect()
        {
            Select(null);
        }

        internal void AddAndSelect(TerminalTabControlItem toAdd)
        {
            _mainTabControl.Items.Add(toAdd);
            Select(toAdd);
        }

        internal void RemoveAndUnSelect(TerminalTabControlItem toRemove)
        {
            _mainTabControl.Items.Remove(toRemove);
            UnSelect();
        }

        /// <summary>
        /// Releases actualy selected tab to the new window
        /// </summary>
        internal void DetachTabToNewWindow()
        {
            if (Selected != null)
                DetachTabToNewWindow(Selected);
        }

        internal void DetachTabToNewWindow(TerminalTabControlItem tabControlToOpen)
        {
            if (tabControlToOpen != null)
            {
                _mainTabControl.Items.SuspendEvents();

                PopupTerminal pop = new PopupTerminal(this);
                _mainTabControl.RemoveTab(tabControlToOpen);
                pop.AddTerminal(tabControlToOpen);

                _mainTabControl.Items.ResumeEvents();
                _detachedWindows.Add(pop);
                pop.Show();
            }
        }

        internal void AttachTabFromWindow(TerminalTabControlItem tabControlToAttach)
        {
            _mainTabControl.AddTab(tabControlToAttach);
            PopupTerminal popupTerminal = tabControlToAttach.FindForm() as PopupTerminal;

            if (popupTerminal != null)
            {
                UnRegisterPopUp(popupTerminal);
            }
        }

        internal void UnRegisterPopUp(PopupTerminal popupTerminal)
        {
            if (_detachedWindows.Contains(popupTerminal))
            {
                _detachedWindows.Remove(popupTerminal);
            }
        }

        internal void CaptureScreen()
        {
            CaptureScreen(_mainTabControl);
        }

        /// -----------------------------------------------
        /// <summary>
        ///     We need to provide the tab from outside, 
        ///     because it may be tab from PopUp window
        /// </summary>
        
        internal void CaptureScreen(TabControl.TabControl tabControl)
        {
            CaptureManager.CaptureManager.PerformScreenCapture(tabControl, SelectedOriginFavorite);
            RefreshCaptureManagerAndCreateItsTab(false);
        }

        internal void FocusCaptureManager()
        {
            RefreshCaptureManagerAndCreateItsTab(true);
        }

        private void RefreshCaptureManagerAndCreateItsTab(bool openManagerTab)
        {
            Boolean refreshed = RefreshCaptureManager(openManagerTab);

            if(!refreshed && NeedsFocusCaptureManagerTab(openManagerTab))
            {
                _connectionsUiFactory.CreateCaptureManagerTab();
            }
        }

        /// <summary>
        /// Updates the CaptureManager tabcontrol, focuses it and updates its content.
        /// </summary>
        /// <param name="openManagerTab"></param>
        /// <returns>true, Tab exists and was updated, otherwise false.</returns>
        
        private bool RefreshCaptureManager(bool openManagerTab)
        {
            CaptureManagerLayout captureManager = FindCaptureManagerControl();

            if (captureManager != null)
            {
                captureManager.RefreshView();
                FocusCaptureManager(captureManager, openManagerTab);
                return true;
            }

            return false;
        }

        private void FocusCaptureManager(CaptureManagerLayout connectionManager, bool openManagerTab)
        {
            if (NeedsFocusCaptureManagerTab(openManagerTab))
            {
                connectionManager.BringToFront();
                connectionManager.Update();
                
                // ---------------------------------------------------------
                // the connection manager was resolved as control on the tab

                var tab = connectionManager.Parent as TerminalTabControlItem;
                Select(tab);
            }
        }

        private bool NeedsFocusCaptureManagerTab(bool openManagerTab)
        {
            return openManagerTab || (_settings.EnableCaptureToFolder && _settings.AutoSwitchOnCapture);
        }

        private CaptureManagerLayout FindCaptureManagerControl()
        {
            TerminalTabControlItem tab = _filter.FindCaptureManagerTab();

            if (tab != null)
            {
                // ---------------------------------------------------------
                // after the connection is removed, this index moves to zero

                return tab.Controls[CaptureManagerLayout.ControlName] as CaptureManagerLayout;
            }

            return null;
        }

        internal void UpdateCaptureButtonOnDetachedPopUps()
        {
            bool newEnable = _settings.EnabledCaptureToFolderAndClipBoard;

            foreach (PopupTerminal detachedWindow in _detachedWindows)
            {
                detachedWindow.UpdateCaptureButtonEnabled(newEnable);
            }
        }
    }
}
