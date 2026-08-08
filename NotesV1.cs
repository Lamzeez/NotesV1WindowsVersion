using System;
using System.Drawing;
using System.Windows.Forms;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace NotesV1
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            bool createdNew;
            using (System.Threading.Mutex mutex = new System.Threading.Mutex(true, "NotesV1_SingleInstanceApp", out createdNew))
            {
                if (!createdNew)
                {
                    MessageBox.Show("Notes V1 is already running.", "Instance Already Running", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm(args));
            }
        }
    }

    public enum AppMode { VehicleTracker, NormalNotepad }

    public class StartupPrompt : Form
    {
        public AppMode SelectedMode { get; private set; }
        public StartupPrompt()
        {
            this.Text = "Notes V1 - Select Mode";
            this.Size = new Size(320, 150);
            this.StartPosition = FormStartPosition.CenterScreen;
            try { this.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;

            Label lbl = new Label() { Text = "How do you want to create/open .txt files?", Location = new Point(20, 20), AutoSize = true };
            
            Button btnTracker = new Button() { Text = "Vehicle Tracker", Location = new Point(20, 60), Width = 120, Height = 35 };
            btnTracker.Click += (s, e) => { SelectedMode = AppMode.VehicleTracker; this.DialogResult = DialogResult.OK; };
            
            Button btnNotepad = new Button() { Text = "Normal Notepad", Location = new Point(150, 60), Width = 120, Height = 35 };
            btnNotepad.Click += (s, e) => { SelectedMode = AppMode.NormalNotepad; this.DialogResult = DialogResult.OK; };

            this.Controls.Add(lbl);
            this.Controls.Add(btnTracker);
            this.Controls.Add(btnNotepad);
        }
    }

    public class MainForm : Form
    {
        private TabControl tabControl;
        private Panel findPanel;
        private TextBox txtFind;
        private string lastQuery = "";
        private int currentFindIndex = -1;
        public float CurrentFontSize { get; private set; }

        public MainForm(string[] args = null)
        {
            CurrentFontSize = 9f;
            this.Text = "Notes V1";
            this.Size = new Size(600, 700);
            this.MinimumSize = new Size(150, 150); // allow shrinking very small
            this.StartPosition = FormStartPosition.CenterScreen;
            try { this.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            this.Font = new Font(this.Font.FontFamily, CurrentFontSize);
            
            // Menu
            MenuStrip menu = new MenuStrip();
            ToolStripMenuItem fileMenu = new ToolStripMenuItem("File");
            
            ToolStripMenuItem newMenu = new ToolStripMenuItem("New Tab", null, (s, e) => {
                using (var prompt = new StartupPrompt()) {
                    if (prompt.ShowDialog() == DialogResult.OK) {
                        AddNewTab("Untitled", null, prompt.SelectedMode);
                    }
                }
            });
            newMenu.ShortcutKeys = Keys.Control | Keys.T;
            
            ToolStripMenuItem openMenu = new ToolStripMenuItem("Open...", null, MenuOpen_Click);
            openMenu.ShortcutKeys = Keys.Control | Keys.O;
            
            ToolStripMenuItem saveMenu = new ToolStripMenuItem("Save", null, MenuSave_Click);
            saveMenu.ShortcutKeys = Keys.Control | Keys.S;
            
            ToolStripMenuItem saveAsMenu = new ToolStripMenuItem("Save As...", null, MenuSaveAs_Click);
            
            ToolStripMenuItem closeTabMenu = new ToolStripMenuItem("Close Tab", null, (s, e) => {
                if (tabControl.SelectedTab != null) tabControl.TabPages.Remove(tabControl.SelectedTab);
            });
            closeTabMenu.ShortcutKeys = Keys.Control | Keys.W;

            fileMenu.DropDownItems.Add(newMenu);
            fileMenu.DropDownItems.Add(openMenu);
            fileMenu.DropDownItems.Add(saveMenu);
            fileMenu.DropDownItems.Add(saveAsMenu);
            fileMenu.DropDownItems.Add(new ToolStripSeparator());
            fileMenu.DropDownItems.Add(closeTabMenu);
            
            // 1. Add TabControl first so it takes the remaining space in the center
            tabControl = new TabControl() { Dock = DockStyle.Fill };
            this.Controls.Add(tabControl);

            // 2. Add Find Panel
            findPanel = new Panel() { Dock = DockStyle.Top, Height = 35, Visible = false, BackColor = Color.LightSteelBlue };
            Label lblFind = new Label() { Text = "Find:", Location = new Point(10, 10), AutoSize = true };
            txtFind = new TextBox() { Location = new Point(50, 7), Width = 250 };
            Label lblHints = new Label() { Text = "(Enter: Next, Shift+Enter: Prev, Esc: Close)", Location = new Point(310, 10), AutoSize = true };
            findPanel.Controls.Add(lblFind);
            findPanel.Controls.Add(txtFind);
            findPanel.Controls.Add(lblHints);
            this.Controls.Add(findPanel);

            // 3. Add Menu last so it docks to the very top edge
            menu.Items.Add(fileMenu);
            this.MainMenuStrip = menu;
            this.Controls.Add(menu);

            this.FormClosing += MainForm_FormClosing;
            LoadSession(args);
        }

        private string GetSessionFilePath()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string dir = Path.Combine(appData, "NotesV1");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            return Path.Combine(dir, "Session.txt");
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            try
            {
                string unsavedDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NotesV1", "Unsaved");
                if (Directory.Exists(unsavedDir)) Directory.Delete(unsavedDir, true);
                Directory.CreateDirectory(unsavedDir);

                List<string> paths = new List<string>();
                int unsavedIndex = 0;
                foreach (TabPage page in tabControl.TabPages)
                {
                    bool isNotepad = page.Controls.OfType<RichTextBox>().Any();
                    string path = page.Tag as string;
                    
                    string backupPath = Path.Combine(unsavedDir, string.Format("backup_{0}.txt", unsavedIndex++));
                    
                    if (isNotepad)
                    {
                        var tb = page.Controls.OfType<RichTextBox>().FirstOrDefault();
                        if (tb != null) File.WriteAllText(backupPath, tb.Text);
                    }
                    else
                    {
                        var tracker = page.Controls.OfType<TrackerControl>().FirstOrDefault();
                        if (tracker != null)
                        {
                            using (StreamWriter sw = new StreamWriter(backupPath))
                            {
                                foreach (TileControl tile in tracker.FlowPanel.Controls)
                                {
                                    for (int i = 0; i < tile.layout.RowCount; i++)
                                    {
                                        var lbl = tile.layout.GetControlFromPosition(0, i) as Label;
                                        var tb = tile.layout.GetControlFromPosition(1, i) as TextBox;
                                        if (lbl != null && tb != null)
                                            sw.WriteLine(string.Format("{0} {1}", lbl.Text, tb.Text));
                                    }
                                    sw.WriteLine();
                                }
                            }
                        }
                    }

                    string modeStr = isNotepad ? "Notepad" : "Tracker";
                    string originalPath = string.IsNullOrEmpty(path) ? "" : path;
                    paths.Add(string.Format("{0}|{1}|{2}", modeStr, originalPath, backupPath));
                }
                File.WriteAllLines(GetSessionFilePath(), paths.ToArray());
            }
            catch { }
        }

        private void LoadSession(string[] args)
        {
            try
            {
                string sessionFile = GetSessionFilePath();
                if (File.Exists(sessionFile))
                {
                    string[] lines = File.ReadAllLines(sessionFile);
                    bool loadedAny = false;
                    foreach (string line in lines)
                    {
                        string[] parts = line.Split('|');
                        if (parts.Length == 3)
                        {
                            AppMode mode = parts[0] == "Notepad" ? AppMode.NormalNotepad : AppMode.VehicleTracker;
                            string originalPath = parts[1];
                            string backupPath = parts[2];
                            
                            if (File.Exists(backupPath))
                            {
                                string title = string.IsNullOrEmpty(originalPath) ? "Untitled" : Path.GetFileName(originalPath);
                                string tagPath = string.IsNullOrEmpty(originalPath) ? null : originalPath;
                                
                                AddNewTab(title, tagPath, mode);
                                LoadFile(backupPath, tabControl.TabPages[tabControl.TabPages.Count - 1]);
                                loadedAny = true;
                            }
                        }
                    }
                    if (loadedAny)
                    {
                        if (args != null && args.Length > 0 && File.Exists(args[0]))
                        {
                            string content = File.ReadAllText(args[0]);
                            AppMode fileMode = content.Contains("Tag #:") ? AppMode.VehicleTracker : AppMode.NormalNotepad;
                            AddNewTab(Path.GetFileName(args[0]), args[0], fileMode);
                            LoadFile(args[0], tabControl.TabPages[tabControl.TabPages.Count - 1]);
                        }
                        return;
                    }
                }
            }
            catch { }
            
            if (args != null && args.Length > 0 && File.Exists(args[0]))
            {
                try {
                    string content = File.ReadAllText(args[0]);
                    AppMode fileMode = content.Contains("Tag #:") ? AppMode.VehicleTracker : AppMode.NormalNotepad;
                    AddNewTab(Path.GetFileName(args[0]), args[0], fileMode);
                    LoadFile(args[0], tabControl.TabPages[tabControl.TabPages.Count - 1]);
                    return;
                } catch { }
            }
            
            using (var prompt = new StartupPrompt()) {
                if (prompt.ShowDialog() == DialogResult.OK) {
                    AddNewTab("Untitled", null, prompt.SelectedMode);
                }
                else {
                    Environment.Exit(0);
                }
            }
        }

        private void AddNewTab(string title, string path, AppMode mode)
        {
            TabPage page = new TabPage(title);
            page.Tag = path;
            
            if (mode == AppMode.NormalNotepad)
            {
                RichTextBox tb = new RichTextBox() { Dock = DockStyle.Fill, ScrollBars = RichTextBoxScrollBars.Both, AcceptsTab = true, BorderStyle = BorderStyle.None };
                tb.Font = new Font(this.Font.FontFamily, CurrentFontSize);
                page.Controls.Add(tb);
            }
            else
            {
                TrackerControl tracker = new TrackerControl(this);
                page.Controls.Add(tracker);
            }
            
            tabControl.TabPages.Add(page);
            tabControl.SelectedTab = page;
        }

        private void MenuSave_Click(object sender, EventArgs e)
        {
            if (tabControl.SelectedTab == null) return;
            string path = tabControl.SelectedTab.Tag as string;
            if (string.IsNullOrEmpty(path))
            {
                MenuSaveAs_Click(sender, e);
            }
            else
            {
                SaveFile(path);
            }
        }
        
        private void MenuSaveAs_Click(object sender, EventArgs e)
        {
            if (tabControl.SelectedTab == null) return;
            using (SaveFileDialog sfd = new SaveFileDialog())
            {
                sfd.Filter = "Text Files (*.txt)|*.txt|All Files (*.*)|*.*";
                if (sfd.ShowDialog() == DialogResult.OK)
                {
                    tabControl.SelectedTab.Tag = sfd.FileName;
                    tabControl.SelectedTab.Text = Path.GetFileName(sfd.FileName);
                    SaveFile(sfd.FileName);
                }
            }
        }

        private void MenuOpen_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog ofd = new OpenFileDialog())
            {
                ofd.Filter = "Text Files (*.txt)|*.txt|All Files (*.*)|*.*";
                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    using (var prompt = new StartupPrompt()) {
                        if (prompt.ShowDialog() == DialogResult.OK) {
                            AddNewTab(Path.GetFileName(ofd.FileName), ofd.FileName, prompt.SelectedMode);
                            LoadFile(ofd.FileName, tabControl.SelectedTab);
                        }
                    }
                }
            }
        }

        public void SaveFile(string path)
        {
            if (tabControl.SelectedTab.Controls.OfType<RichTextBox>().Any())
            {
                var tb = tabControl.SelectedTab.Controls.OfType<RichTextBox>().FirstOrDefault();
                if (tb != null) File.WriteAllText(path, tb.Text);
            }
            else
            {
                var tracker = tabControl.SelectedTab.Controls.OfType<TrackerControl>().FirstOrDefault();
                if (tracker != null)
                {
                    using (StreamWriter sw = new StreamWriter(path))
                    {
                        foreach (TileControl tile in tracker.FlowPanel.Controls)
                        {
                            for (int i = 0; i < tile.layout.RowCount; i++)
                            {
                                var lbl = tile.layout.GetControlFromPosition(0, i) as Label;
                                var tb = tile.layout.GetControlFromPosition(1, i) as TextBox;
                                if (lbl != null && tb != null)
                                {
                                    sw.WriteLine(string.Format("{0} {1}", lbl.Text, tb.Text));
                                }
                            }
                            sw.WriteLine();
                        }
                    }
                }
            }
        }
        
        public void LoadFile(string path, TabPage page)
        {
            if (page.Controls.OfType<RichTextBox>().Any())
            {
                var tb = page.Controls.OfType<RichTextBox>().FirstOrDefault();
                if (tb != null) tb.Text = File.ReadAllText(path);
            }
            else
            {
                var tracker = page.Controls.OfType<TrackerControl>().FirstOrDefault();
                if (tracker != null)
                {
                    tracker.FlowPanel.Controls.Clear();
                    
                    string[] lines = File.ReadAllLines(path);
                    TileControl currentTile = null;
                    
                    foreach (string line in lines)
                    {
                        if (string.IsNullOrWhiteSpace(line))
                        {
                            currentTile = null;
                            continue;
                        }
                        
                        if (currentTile == null)
                        {
                            currentTile = new TileControl(tracker);
                            currentTile.ClearFields();
                            tracker.FlowPanel.Controls.Add(currentTile);
                            int w = tracker.FlowPanel.ClientSize.Width - 25;
                            if (w > 0) {
                                currentTile.MinimumSize = new Size(w, 0);
                                currentTile.MaximumSize = new Size(w, 0);
                            }
                        }
                        
                        int colonIdx = line.IndexOf(':');
                        if (colonIdx > 0)
                        {
                            string label = line.Substring(0, colonIdx + 1);
                            string val = line.Substring(colonIdx + 1).TrimStart();
                            currentTile.AddField(label);
                            var tbs = currentTile.GetTextBoxes();
                            tbs.Last().Text = val;
                        }
                    }
                    if (tracker.FlowPanel.Controls.Count == 0) tracker.AddTile();
                }
            }
        }

        public List<TextBoxBase> GetAllTextBoxes()
        {
            List<TextBoxBase> list = new List<TextBoxBase>();
            if (tabControl.SelectedTab == null) return list;
            
            if (tabControl.SelectedTab.Controls.OfType<RichTextBox>().Any())
            {
                var tb = tabControl.SelectedTab.Controls.OfType<RichTextBox>().FirstOrDefault();
                if (tb != null) list.Add(tb);
            }
            else
            {
                var tracker = tabControl.SelectedTab.Controls.OfType<TrackerControl>().FirstOrDefault();
                if (tracker != null)
                {
                    foreach (TileControl tile in tracker.FlowPanel.Controls)
                    {
                        foreach (var t in tile.GetTextBoxes()) list.Add(t);
                    }
                }
            }
            return list;
        }

        private void MoveFocus(TextBoxBase current, int direction)
        {
            var tbs = GetAllTextBoxes();
            int idx = tbs.IndexOf(current);
            if (idx >= 0)
            {
                int newIdx = idx + direction;
                if (newIdx >= 0 && newIdx < tbs.Count)
                {
                    tbs[newIdx].Focus();
                }
            }
        }

        private void FindNext(int direction, Control focusedControl)
        {
            if (string.IsNullOrEmpty(lastQuery)) return;
            
            if (tabControl.SelectedTab != null && tabControl.SelectedTab.Controls.OfType<RichTextBox>().Any())
            {
                var tb = tabControl.SelectedTab == null ? null : tabControl.SelectedTab.Controls.OfType<RichTextBox>().FirstOrDefault();
                if (tb != null)
                {
                    int startIdx = direction > 0 ? tb.SelectionStart + tb.SelectionLength : tb.SelectionStart - 1;
                    if (startIdx < 0) startIdx = tb.Text.Length - 1;
                    
                    StringComparison cmp = StringComparison.OrdinalIgnoreCase;
                    int idx = -1;
                    if (direction > 0)
                    {
                        idx = tb.Text.IndexOf(lastQuery, startIdx, cmp);
                        if (idx == -1) idx = tb.Text.IndexOf(lastQuery, 0, cmp);
                    }
                    else
                    {
                        idx = tb.Text.LastIndexOf(lastQuery, startIdx, cmp);
                        if (idx == -1) idx = tb.Text.LastIndexOf(lastQuery, tb.Text.Length - 1, cmp);
                    }
                    
                    if (idx != -1)
                    {
                        tb.Select(idx, lastQuery.Length);
                        tb.ScrollToCaret();
                        tb.Focus();
                    }
                }
            }
            else
            {
                var tbs = GetAllTextBoxes();
                if (tbs.Count == 0) return;
                
                int currIdx = tbs.IndexOf(focusedControl as TextBoxBase);
                if (currIdx == -1) currIdx = currentFindIndex;
                if (currIdx >= tbs.Count) currIdx = -1;
                if (currIdx == -1) currIdx = direction > 0 ? -1 : 0;
                
                for (int i = 1; i <= tbs.Count; i++)
                {
                    int checkIdx = (currIdx + i * direction) % tbs.Count;
                    if (checkIdx < 0) checkIdx += tbs.Count;
                    
                    if (tbs[checkIdx].Text.IndexOf(lastQuery, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        currentFindIndex = checkIdx;
                        tbs[checkIdx].Focus();
                        tbs[checkIdx].SelectAll();
                        return;
                    }
                }
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.Oemplus) || keyData == (Keys.Control | Keys.Add))
            {
                ChangeFontSize(1f);
                return true;
            }
            if (keyData == (Keys.Control | Keys.OemMinus) || keyData == (Keys.Control | Keys.Subtract))
            {
                ChangeFontSize(-1f);
                return true;
            }
            if (keyData == (Keys.Control | Keys.Tab))
            {
                if (tabControl.TabCount > 1)
                {
                    tabControl.SelectedIndex = (tabControl.SelectedIndex + 1) % tabControl.TabCount;
                }
                return true;
            }
            if (keyData == (Keys.Control | Keys.F))
            {
                findPanel.Visible = true;
                txtFind.Focus();
                txtFind.SelectAll();
                return true;
            }
            if (keyData == Keys.Escape && findPanel.Visible)
            {
                findPanel.Visible = false;
                if (tabControl.SelectedTab != null) tabControl.SelectedTab.Focus();
                return true;
            }

            if (keyData == (Keys.Control | Keys.Back))
            {
                TextBoxBase tb = Control.FromHandle(msg.HWnd) as TextBoxBase;
                if (tb != null && !tb.ReadOnly)
                {
                    int end = tb.SelectionStart;
                    if (tb.SelectionLength > 0)
                    {
                        tb.SelectedText = "";
                    }
                    else if (end > 0)
                    {
                        int start = end;
                        while (start > 0 && char.IsWhiteSpace(tb.Text[start - 1])) start--;
                        bool isPunct = start > 0 && char.IsPunctuation(tb.Text[start - 1]);
                        while (start > 0)
                        {
                            if (char.IsWhiteSpace(tb.Text[start - 1])) break;
                            if (char.IsPunctuation(tb.Text[start - 1]) != isPunct) break;
                            start--;
                        }
                        tb.Text = tb.Text.Remove(start, end - start);
                        tb.SelectionStart = start;
                    }
                    return true;
                }
            }

            if (findPanel.Visible)
            {
                if (keyData == Keys.Enter || keyData == (Keys.Shift | Keys.Enter))
                {
                    lastQuery = txtFind.Text;
                    FindNext(keyData == Keys.Enter ? 1 : -1, Control.FromHandle(msg.HWnd));
                    return true;
                }
            }
            
            if (tabControl.SelectedTab != null && tabControl.SelectedTab.Controls.OfType<TrackerControl>().Any())
            {
                TextBox tb = Control.FromHandle(msg.HWnd) as TextBox;
                if (tb != null && tb.Parent != null)
                {
                    TileControl tile = tb.Parent.Parent as TileControl;
                    if (tile != null)
                    {
                        if (keyData == Keys.Up || keyData == Keys.Down)
                        {
                            MoveFocus(tb, keyData == Keys.Up ? -1 : 1);
                            return true;
                        }
                        if (!findPanel.Visible && keyData == Keys.Enter)
                        {
                            TrackerControl tc = tile.ParentTracker;
                            tc.AddTile().FocusFirst();
                            return true;
                        }
                        if (keyData == (Keys.Alt | Keys.V))
                        {
                            tile.AddVisitor();
                            return true;
                        }
                        if (keyData == (Keys.Alt | Keys.C))
                        {
                            tile.AddCompany();
                            return true;
                        }
                        if (keyData == (Keys.Alt | Keys.P))
                        {
                            tile.AddPhone();
                            return true;
                        }
                        if (keyData == (Keys.Alt | Keys.E))
                        {
                            tile.AddEmail();
                            return true;
                        }
                        if (keyData == (Keys.Alt | Keys.S))
                        {
                            tile.ShowSaveAlert();
                            return true;
                        }
                    }
                }
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void ChangeFontSize(float delta)
        {
            CurrentFontSize += delta;
            if (CurrentFontSize < 5f) CurrentFontSize = 5f;
            if (CurrentFontSize > 72f) CurrentFontSize = 72f;
            
            Font newFont = new Font(this.Font.FontFamily, CurrentFontSize);
            Font newMono = new Font("Consolas", CurrentFontSize);
            Font newBold = new Font(this.Font.FontFamily, CurrentFontSize, FontStyle.Bold);
            
            this.Font = newFont;
            UpdateFontsRecursive(this, newFont, newMono, newBold);
        }

        private void UpdateFontsRecursive(Control parent, Font regular, Font mono, Font bold)
        {
            foreach (Control c in parent.Controls)
            {
                TextBoxBase tb = c as TextBoxBase;
                Label l = c as Label;

                if (tb != null && tb.Multiline && parent is TabPage)
                {
                    c.Font = mono;
                }
                else if (l != null && l.Text.EndsWith(":"))
                {
                    c.Font = bold;
                }
                else if (l != null && l.Text == "Save Tag")
                {
                    c.Font = bold;
                }
                else
                {
                    c.Font = regular;
                }
                
                if (c is TileControl)
                {
                    TileControl tile = (TileControl)c;
                    int labelWidth = TextRenderer.MeasureText("CompName:", bold).Width + 10;
                    if (tile.layout.ColumnStyles.Count > 0) {
                        tile.layout.ColumnStyles[0].Width = labelWidth;
                    }
                }
                
                if (c.HasChildren)
                {
                    UpdateFontsRecursive(c, regular, mono, bold);
                }
            }
        }
    }

    public class TrackerControl : UserControl
    {
        public FlowLayoutPanel FlowPanel { get; private set; }
        public new MainForm ParentForm { get; set; }
        private Point savedScroll;

        public TrackerControl(MainForm parent)
        {
            this.ParentForm = parent;
            this.Dock = DockStyle.Fill;
            FlowPanel = new FlowLayoutPanel();
            FlowPanel.Dock = DockStyle.Fill;
            FlowPanel.AutoScroll = true;
            FlowPanel.FlowDirection = FlowDirection.TopDown;
            FlowPanel.WrapContents = false;
            this.Controls.Add(FlowPanel);
            
            FlowPanel.SizeChanged += (s, e) => {
                int w = FlowPanel.ClientSize.Width - 25;
                if (w > 0) {
                    foreach (Control c in FlowPanel.Controls) {
                        TileControl tc = c as TileControl;
                        if (tc != null) {
                            tc.MinimumSize = new Size(w, 0);
                            tc.MaximumSize = new Size(w, 0);
                        }
                    }
                }
            };
            
            this.VisibleChanged += (s, e) => {
                if (this.Visible) {
                    FlowPanel.AutoScrollPosition = savedScroll;
                } else {
                    savedScroll = new Point(Math.Abs(FlowPanel.AutoScrollPosition.X), Math.Abs(FlowPanel.AutoScrollPosition.Y));
                }
            };
            
            AddTile();
        }
        
        public TileControl AddTile()
        {
            TileControl tile = new TileControl(this);
            FlowPanel.Controls.Add(tile);
            
            int w = FlowPanel.ClientSize.Width - 25;
            if (w > 0) {
                tile.MinimumSize = new Size(w, 0);
                tile.MaximumSize = new Size(w, 0);
            }
            return tile;
        }
    }

    public class TileControl : UserControl
    {
        public TrackerControl ParentTracker { get; set; }
        public TableLayoutPanel layout;
        
        public TileControl(TrackerControl parent)
        {
            this.ParentTracker = parent;
            this.BorderStyle = BorderStyle.FixedSingle;
            this.Margin = new Padding(10);
            this.Padding = new Padding(5);
            this.AutoSize = true;
            this.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            this.BackColor = Color.WhiteSmoke;
            
            layout = new TableLayoutPanel();
            layout.AutoSize = true;
            layout.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            layout.ColumnCount = 2;
            
            Font boldFont = new Font(ParentTracker.ParentForm.Font.FontFamily, ParentTracker.ParentForm.CurrentFontSize, FontStyle.Bold);
            int labelWidth = TextRenderer.MeasureText("CompName:", boldFont).Width + 10;
            
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, labelWidth));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.Dock = DockStyle.Top;
            
            this.Controls.Add(layout);
            
            AddField("Tag #:");
            AddField("Address:");
            AddField("ResName:");
        }
        
        public void ClearFields()
        {
            layout.Controls.Clear();
            layout.RowStyles.Clear();
            layout.RowCount = 0;
        }

        public void AddField(string labelText)
        {
            Font boldFont = new Font(ParentTracker.ParentForm.Font.FontFamily, ParentTracker.ParentForm.CurrentFontSize, FontStyle.Bold);
            Font regFont = new Font(ParentTracker.ParentForm.Font.FontFamily, ParentTracker.ParentForm.CurrentFontSize);
            
            Label lbl = new Label() { Text = labelText, AutoSize = true, Anchor = AnchorStyles.Left, Font = boldFont };
            TextBox txt = new TextBox() { Dock = DockStyle.Fill, Font = regFont };
            
            txt.Enter += (s, e) => {
                if (Control.MouseButtons == MouseButtons.None)
                {
                    txt.SelectAll();
                }
            };
            
            layout.RowCount++;
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(lbl, 0, layout.RowCount - 1);
            layout.Controls.Add(txt, 1, layout.RowCount - 1);
        }
        
        private void ReplaceOrAdd(string replaceWhat, string withWhat)
        {
            for (int i = 0; i < layout.RowCount; i++)
            {
                Label l = layout.GetControlFromPosition(0, i) as Label;
                if (l != null && l.Text == withWhat) return;
                if (l != null && l.Text == replaceWhat)
                {
                    l.Text = withWhat;
                    var tb = layout.GetControlFromPosition(1, i) as TextBox;
                    if (tb != null) tb.Focus();
                    return;
                }
            }
            AddField(withWhat);
            GetTextBoxes().Last().Focus();
        }

        public void AddVisitor()
        {
            ReplaceOrAdd("CompName:", "VisName:");
        }
        
        public void AddCompany()
        {
            ReplaceOrAdd("VisName:", "CompName:");
        }

        public void AddPhone()
        {
            foreach(Control c in layout.Controls) {
                Label l = c as Label;
                if (l != null && l.Text == "Phone:") return;
            }
            AddField("Phone:");
            GetTextBoxes().Last().Focus();
        }
        
        public void AddEmail()
        {
            foreach(Control c in layout.Controls) {
                Label l = c as Label;
                if (l != null && l.Text == "Email:") return;
            }
            AddField("Email:");
            GetTextBoxes().Last().Focus();
        }
        
        public List<TextBox> GetTextBoxes()
        {
            List<TextBox> list = new List<TextBox>();
            for (int i = 0; i < layout.RowCount; i++)
            {
                var tb = layout.GetControlFromPosition(1, i) as TextBox;
                if (tb != null) list.Add(tb);
            }
            return list;
        }

        public void FocusFirst()
        {
            var tbs = GetTextBoxes();
            if (tbs.Count > 0) tbs[0].Focus();
        }
        
        public void ShowSaveAlert()
        {
            Label alert = new Label();
            alert.Text = "Save Tag";
            alert.BackColor = Color.YellowGreen;
            alert.ForeColor = Color.White;
            alert.Font = new Font(ParentTracker.ParentForm.Font.FontFamily, ParentTracker.ParentForm.CurrentFontSize, FontStyle.Bold);
            alert.Padding = new Padding(3);
            alert.AutoSize = true;
            alert.Location = new Point(this.Width - 80, 5);
            this.Controls.Add(alert);
            alert.BringToFront();
            
            Timer t = new Timer();
            t.Interval = 1500;
            t.Tick += (s, e) => {
                this.Controls.Remove(alert);
                alert.Dispose();
                t.Stop();
                t.Dispose();
            };
            t.Start();
        }
    }
}
