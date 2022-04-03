using System;
using System.Linq;
using System.Windows.Forms;
using System.Collections.Generic;

using Terminals.Data;
using Terminals.Wizard;
using Terminals.Connections;
using Terminals.Configuration;
using Terminals.Forms.Controls;

namespace Terminals
{
    internal enum WizardForms
    {
        Intro,
        MasterPassword,
        DefaultCredentials,
        Options,
        Scanner
    }

    ///  --------------------------------------------------

    internal partial class FirstRunWizard : Form
    {
        private MethodInvoker _miv;
        private readonly IPersistence _persistence;
        private CommonOptions _co = new CommonOptions();
        private MasterPassword _mp = new MasterPassword();
        private WizardForms _selectedForm = WizardForms.Intro;
        private readonly Settings _settings = Settings.Instance;
        private DefaultCredentials _dc = new DefaultCredentials();
        private AddExistingRDPConnections _rdp = new AddExistingRDPConnections();

        private readonly ConnectionManager _connectionManager;

        ///  ----------------------------------------------

        public FirstRunWizard(IPersistence persistence, ConnectionManager connectionManager)
        {
            InitializeComponent();
            _rdp.OnDiscoveryCompleted += new AddExistingRDPConnections.DiscoveryCompleted(rdp_OnDiscoveryCompleted);
            _miv = new MethodInvoker(DiscoComplete);
            _persistence = persistence;
            _connectionManager = connectionManager;
            _mp.AssignPersistence(persistence);
        }

        ///  ----------------------------------------------

        private void FirstRunWizard_Load(object sender, EventArgs e)
        {
            var frm = new IntroForm();
            frm.Dock = DockStyle.Fill;
            panel1.Controls.Add(frm);
            _settings.StartDelayedUpdate();
        }

        ///  ----------------------------------------------

        private void NextButton_Click(object sender, EventArgs e)
        {
            if(_selectedForm == WizardForms.Intro)
            {
                SwitchToMasterPassword();
            }
            else if(_selectedForm == WizardForms.MasterPassword)
            {
                if(_mp.StorePassword)
                {
                    SwitchToDefaultCredentials();
                }
                else
                {
                    SwitchToOptions();
                }
            }
            else if(_selectedForm == WizardForms.DefaultCredentials)
            {
                SwitchToOptionsFromCredentials();
            }
            else if(_selectedForm == WizardForms.Options)
            {
                FinishOptions();
            }
            else if(_selectedForm == WizardForms.Scanner)
            {
                Hide();
            }
        }

        ///  ----------------------------------------------

        private void FinishOptions()
        {
            try
            {
                ApplySettings();
                StartImportIfRequested();
            }
            catch(Exception exc)
            {
                Logging.Error("Apply settings in the first run wizard failed.", exc);
            }
        }

        ///  ----------------------------------------------

        private void StartImportIfRequested()
        {
            if(_co.ImportRDPConnections)
            {
                nextButton.Enabled = false;
                nextButton.Text = "Finished!";
                panel1.Controls.Clear();
                _rdp.Dock = DockStyle.Fill;
                panel1.Controls.Add(_rdp);
                _rdp.StartImport(_connectionManager);
                _selectedForm = WizardForms.Scanner;
            }
            else
            {
                _rdp.CancelDiscovery();
                Hide();
            }
        }

        ///  ----------------------------------------------

        private void ApplySettings()
        {
            _settings.MinimizeToTray = _co.MinimizeToTray;
            _settings.ShowConfirmDialog = _co.WarnOnDisconnect;
            _settings.SingleInstance = _co.AllowOnlySingleInstance;
            _settings.AutoSwitchOnCapture = _co.AutoSwitchOnCapture;
            _settings.EnableCaptureToFolder = _co.EnableCaptureToFolder;
            _settings.EnableCaptureToClipboard = _co.EnableCaptureToClipboard;

            if(_co.LoadDefaultShortcuts)
            {
                _settings.SpecialCommands = SpecialCommandsWizard.LoadSpecialCommands();
            }
        }

        ///  ----------------------------------------------

        private void SwitchToOptionsFromCredentials()
        {
            _settings.DefaultDomain = _dc.DefaultDomain;
            _settings.DefaultPassword = _dc.DefaultPassword;
            _settings.DefaultUsername = _dc.DefaultUsername;

            nextButton.Enabled = true;
            panel1.Controls.Clear();
            panel1.Controls.Add(_co);
            _selectedForm = WizardForms.Options;
        }

        ///  ----------------------------------------------

        private void SwitchToOptions()
        {
            nextButton.Enabled = true;
            panel1.Controls.Clear();
            panel1.Controls.Add(_co);
            _selectedForm = WizardForms.Options;
        }

        ///  ----------------------------------------------

        private void SwitchToDefaultCredentials()
        {
            _persistence.Security.UpdateMasterPassword(_mp.Password);
            nextButton.Enabled = true;
            panel1.Controls.Clear();
            panel1.Controls.Add(_dc);
            _selectedForm = WizardForms.DefaultCredentials;
        }

        ///  ----------------------------------------------

        private void SwitchToMasterPassword()
        {
            nextButton.Enabled = true;
            panel1.Controls.Clear();
            panel1.Controls.Add(_mp);
            _selectedForm = WizardForms.MasterPassword;
        }

        ///  ----------------------------------------------

        private void CancelButton_Click(object sender, EventArgs e)
        {
            _rdp.CancelDiscovery();
            Hide();
        }

        ///  ----------------------------------------------

        private void DiscoComplete()
        {
            nextButton.Enabled = true;
            cancelButton.Enabled = false;
            Hide();
        }

        ///  ----------------------------------------------

        private void rdp_OnDiscoveryCompleted()
        {
            Invoke(_miv);
        }

        ///  ----------------------------------------------

        private void FirstRunWizard_FormClosing(object sender, FormClosingEventArgs e)
        {
            _settings.ShowWizard = false;
            _settings.SaveAndFinishDelayedUpdate();
            ImportDiscoveredFavorites();
        }

        ///  ----------------------------------------------

        private void ImportDiscoveredFavorites()
        {
            if(_rdp.DiscoveredConnections.Count > 0)
            {
                var message = String.Format("Automatic Discovery was able to find {0} connections.\r\n" +
                                            "Would you like to add them to your connections list?",
                                            _rdp.DiscoveredConnections.Count);

                if(MessageBox.Show(message, "Terminals Confirmation", MessageBoxButtons.YesNo) == DialogResult.Yes)
                {
                    List<FavoriteConfigurationElement> favoritesToImport = _rdp.DiscoveredConnections.ToList();
                    var managedImport = new ImportWithDialogs(this, _persistence, _connectionManager);
                    managedImport.Import(favoritesToImport);
                }
            }
        }
    }
}