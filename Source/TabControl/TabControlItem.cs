using System;
using System.Drawing;
using System.Windows.Forms;
using System.ComponentModel;

namespace TabControl
{
    [ToolboxItem(false)]
    [DefaultEvent("Changed")]
    [DefaultProperty("Title")]
    [Designer(typeof(TabControlItemDesigner))]
    public class TabControlItem : Panel
    {
        private RectangleF stripRect = Rectangle.Empty;
        private bool canClose = true;
        private bool selected = false;
        private bool visible = true;
        private bool isDrawn = false;
        private string title = string.Empty;
        private string toolTipText = string.Empty;

        public event EventHandler Changed;

        // ------------------------------------------------

        [DefaultValue(true)]
        public new bool Visible
        {
            get { return visible; }
            set
            {
                if(visible == value)
                    return;

                visible = value;
                OnChanged();
            }
        }

        // ------------------------------------------------

        [Browsable(false)]
        [DefaultValue(false)]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public bool IsDrawn
        {
            get { return isDrawn; }
            set
            {
                if(isDrawn == value)
                    return;

                isDrawn = value;
            }
        }

        [DefaultValue(true)]
        // ------------------------------------------------

        public bool CanClose
        {
            get { return canClose; }
            set { canClose = value; }
        }

        // ------------------------------------------------

        public RectangleF StripRect
        {
            get { return stripRect; }
            internal set { stripRect = value; }
        }

        // ------------------------------------------------

        [DefaultValue("Name")]
        public string Title
        {
            get
            {
                return title;
            }
            set
            {
                if(title == value)
                    return;

                title = value;
                OnChanged();
            }
        }

        // ------------------------------------------------

        [DefaultValue("Name")]
        public string ToolTipText
        {
            get { return toolTipText; }
            set
            {
                toolTipText = value;
            }
        }

        // ------------------------------------------------
        /// <summary>
        ///     Gets and sets a value indicating if the 
        ///     page is selected.
        /// </summary>

        [DefaultValue(false)]
        [Browsable(false)]
        public bool Selected
        {
            get { return selected; }
            set
            {
                if(selected == value)
                    return;

                selected = value;
            }
        }

        // ------------------------------------------------

        public TabControlItem() : this(string.Empty, null)
        {
        }

        // ------------------------------------------------

        public TabControlItem(Control displayControl) : this(string.Empty, displayControl)
        {
        }

        // ------------------------------------------------

        public TabControlItem(string caption, Control displayControl)
        {
            this.selected = false;
            this.Visible = true;
            this.BorderStyle = BorderStyle.None;

            UpdateText(caption, displayControl);

            // Add to controls

            this.Controls.Add(displayControl);
        }

        // ------------------------------------------------

        public bool ShouldSerializeIsDrawn()
        {
            return false;
        }

        // ------------------------------------------------

        public bool ShouldSerializeDock()
        {
            return false;
        }

        // ------------------------------------------------

        public bool ShouldSerializeVisible()
        {
            return true;
        }

        // ------------------------------------------------

        internal bool LocationIsInTitle(Point mouseLocation)
        {
            bool inTitle = (this.StripRect.X + this.StripRect.Width - 1) > mouseLocation.X &&
                            (this.StripRect.Y + this.StripRect.Height - 1) > mouseLocation.Y;
            return inTitle;
        }

        // ------------------------------------------------

        private void UpdateText(string caption, Control displayControl)
        {
            if(displayControl is ICaptionSupport)
            {
                ICaptionSupport capControl = displayControl as ICaptionSupport;
                this.Title = capControl.Caption;
            }
            else if(caption!=null && caption.Length <= 0 && displayControl != null)
            {
                this.Title = displayControl.Text;
            }
            else if(caption != null)
            {
                this.Title = caption;
            }
            else
            {
                this.Title = string.Empty;
            }
        }

        /// -----------------------------------------------
        /// <summary>
        ///     Return a string representation of page.
        /// </summary>
        /// <returns></returns>

        public override string ToString()
        {
            return $"TabControlItem:{Title}";
        }

        // ------------------------------------------------

        public void Assign(TabControlItem item)
        {
            this.Visible = item.Visible;
            this.Text = item.Text;
            this.CanClose = item.CanClose;
            this.Tag = item.Tag;
        }

        // ------------------------------------------------

        protected internal virtual void OnChanged()
        {
            if(Changed != null)
            {
                Changed(this, EventArgs.Empty);
            }
        }

        // ------------------------------------------------

        [Browsable(false)]
        public string Caption
        {
            get { return Text; }
        }
    }
}
