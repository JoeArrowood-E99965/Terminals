namespace Terminals.Forms
{
    partial class ExportForm
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
       
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        
        protected override void Dispose(bool disposing)
        {
            if (disposing && (this.components != null))
            {
                this.components.Dispose();
            }

            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            this._checkBox1 = new System.Windows.Forms.CheckBox();
            this._btnExport = new System.Windows.Forms.Button();
            this._btnCancel = new System.Windows.Forms.Button();
            this._saveFileDialog = new System.Windows.Forms.SaveFileDialog();
            this._btnSelect = new System.Windows.Forms.Button();
            this._label1 = new System.Windows.Forms.Label();
            this._favsTree = new Terminals.Forms.Controls.FavoritesTreeView();
            this.SuspendLayout();
            // 
            // checkBox1
            // 
            this._checkBox1.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this._checkBox1.AutoSize = true;
            this._checkBox1.Location = new System.Drawing.Point(15, 351);
            this._checkBox1.Name = "checkBox1";
            this._checkBox1.Size = new System.Drawing.Size(115, 17);
            this._checkBox1.TabIndex = 2;
            this._checkBox1.Text = "&Include Passwords";
            this._checkBox1.UseVisualStyleBackColor = true;
            // 
            // btnExport
            // 
            this._btnExport.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this._btnExport.Location = new System.Drawing.Point(224, 347);
            this._btnExport.Name = "btnExport";
            this._btnExport.Size = new System.Drawing.Size(75, 23);
            this._btnExport.TabIndex = 4;
            this._btnExport.Text = "&Export";
            this._btnExport.UseVisualStyleBackColor = true;
            this._btnExport.Click += new System.EventHandler(this.BtnExport_Click);
            // 
            // btnCancel
            // 
            this._btnCancel.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this._btnCancel.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this._btnCancel.Location = new System.Drawing.Point(305, 347);
            this._btnCancel.Name = "btnCancel";
            this._btnCancel.Size = new System.Drawing.Size(75, 23);
            this._btnCancel.TabIndex = 5;
            this._btnCancel.Text = "&Cancel";
            this._btnCancel.UseVisualStyleBackColor = true;
            this._btnCancel.Click += new System.EventHandler(this.BtnCancel_Click);
            // 
            // saveFileDialog
            // 
            this._saveFileDialog.Filter = "Terminals favorites XML *.xml|*.xml";
            this._saveFileDialog.Title = "Save export list as...";
            // 
            // btnSelect
            // 
            this._btnSelect.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this._btnSelect.Location = new System.Drawing.Point(143, 347);
            this._btnSelect.Name = "btnSelect";
            this._btnSelect.Size = new System.Drawing.Size(75, 23);
            this._btnSelect.TabIndex = 3;
            this._btnSelect.Text = "&Select all";
            this._btnSelect.UseVisualStyleBackColor = true;
            this._btnSelect.Click += new System.EventHandler(this.BtnSelect_Click);
            // 
            // label1
            // 
            this._label1.AutoSize = true;
            this._label1.Location = new System.Drawing.Point(12, 9);
            this._label1.Name = "label1";
            this._label1.Size = new System.Drawing.Size(79, 13);
            this._label1.TabIndex = 6;
            this._label1.Text = "Connection list:";
            // 
            // favsTree
            // 
            this._favsTree.AllowDrop = true;
            this._favsTree.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this._favsTree.CausesValidation = false;
            this._favsTree.CheckBoxes = true;
            this._favsTree.HideSelection = false;
            this._favsTree.ImageIndex = 0;
            this._favsTree.Location = new System.Drawing.Point(12, 31);
            this._favsTree.Name = "favsTree";
            this._favsTree.SelectedImageIndex = 0;
            this._favsTree.ShowNodeToolTips = true;
            this._favsTree.Size = new System.Drawing.Size(368, 310);
            this._favsTree.TabIndex = 1;
            this._favsTree.AfterCheck += new System.Windows.Forms.TreeViewEventHandler(this.FavsTree_AfterCheck);
            // 
            // ExportForm
            // 
            this.AcceptButton = this._btnExport;
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.CancelButton = this._btnCancel;
            this.ClientSize = new System.Drawing.Size(392, 376);
            this.Controls.Add(this._label1);
            this.Controls.Add(this._favsTree);
            this.Controls.Add(this._btnSelect);
            this.Controls.Add(this._btnCancel);
            this.Controls.Add(this._btnExport);
            this.Controls.Add(this._checkBox1);
            this.MinimumSize = new System.Drawing.Size(398, 397);
            this.Name = "ExportForm";
            this.ShowIcon = false;
            this.ShowInTaskbar = false;
            this.Text = "Terminals - Export";
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.ExportForm_FormClosing);
            this.Load += new System.EventHandler(this.ExportForm_Load);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Label _label1;
        private System.Windows.Forms.Button _btnExport;
        private System.Windows.Forms.Button _btnCancel;
        private System.Windows.Forms.Button _btnSelect;
        private System.Windows.Forms.CheckBox _checkBox1;
        private System.Windows.Forms.SaveFileDialog _saveFileDialog;
        private Terminals.Forms.Controls.FavoritesTreeView _favsTree;
    }
}