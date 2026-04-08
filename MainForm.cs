using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Forms;

namespace SidebarGameLauncher
{
    public class MainForm : Form
    {
        private readonly BufferedListView launchListView;
        private readonly BookcaseView bookcaseView;
        private readonly ImageList iconImageList;
        private readonly Label headerLabel;
        private readonly NotifyIcon trayIcon;
        private readonly ContextMenuStrip trayMenu;
        private readonly ContextMenuStrip itemMenu;
        private readonly FolderSettings settings;
        private readonly ToolStripMenuItem pinMenuItem;
        private readonly ToolStripMenuItem startupMenuItem;
        private readonly ToolStripMenuItem showTitlesMenuItem;
        private readonly ToolStripMenuItem itemShowTitlesMenuItem;
        private readonly ToolStripMenuItem styleMenuItem;
        private readonly ToolStripMenuItem styleSidebarMenuItem;
        private readonly ToolStripMenuItem styleClassicMenuItem;
        private readonly ToolStripMenuItem itemStyleMenuItem;
        private readonly ToolStripMenuItem itemStyleSidebarMenuItem;
        private readonly ToolStripMenuItem itemStyleClassicMenuItem;
        private readonly ToolStripMenuItem iconSizeMenuItem;
        private readonly ToolStripMenuItem itemIconSizeMenuItem;
        private readonly ToolStripControlHost iconSizeHost;
        private readonly ToolStripControlHost itemIconSizeHost;
        private readonly TrackBar iconSizeTrackBar;
        private readonly TrackBar itemIconSizeTrackBar;
        private readonly ToolStripMenuItem itemPinMenuItem;
        private readonly ToolStripMenuItem itemStartupMenuItem;
        private readonly ToolStripMenuItem useLaunchBoxIconMenuItem;
        private readonly ToolStripMenuItem useFileIconMenuItem;
        private readonly LaunchBoxIconService launchBoxIcons;
        private readonly ItemIconPreferenceStore itemIconPreferences;
        private readonly System.Windows.Forms.Timer hoverAnimationTimer;
        private readonly Dictionary<int, int> hoverAlphaByIndex;
        private readonly List<BookcaseItem> bookcaseItems;

        private string currentFolder;
        private bool isInitializing;
        private bool isDragging;
        private Point dragStart;
        private Color borderColor;
        private int iconSize;
        private bool hideIconLabels;
        private int loadToken;
        private ListViewItem contextMenuItem;
        private string contextMenuPath;
        private bool syncingIconSizeControls;
        private int hoveredItemIndex;
        private const int HoverMaxAlpha = 72;
        private static readonly int[] StandardIconSizes = new[] { 24, 32, 40, 48, 64, 72, 96 };
        private string visualStyle;

        public MainForm()
        {
            settings = new FolderSettings();
            launchBoxIcons = new LaunchBoxIconService();
            itemIconPreferences = new ItemIconPreferenceStore();
            isInitializing = true;
            borderColor = Color.FromArgb(124, 167, 204);
            iconSize = 32;
            hideIconLabels = false;
            hoverAlphaByIndex = new Dictionary<int, int>();
            hoveredItemIndex = -1;
            bookcaseItems = new List<BookcaseItem>();
            contextMenuPath = string.Empty;
            visualStyle = "sidebar";

            Text = "Sidebar Game Launcher";
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            MinimumSize = new Size(180, 280);
            FormBorderStyle = FormBorderStyle.None;
            Padding = new Padding(6);
            BackColor = Color.FromArgb(225, 232, 243);
            Font = CreateUiFont();
            DoubleBuffered = true;

            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);

            headerLabel = new Label();
            headerLabel.Dock = DockStyle.Top;
            headerLabel.Height = 34;
            headerLabel.TextAlign = ContentAlignment.MiddleCenter;
            headerLabel.Padding = new Padding(10, 0, 10, 0);
            headerLabel.ForeColor = Color.FromArgb(34, 63, 95);
            headerLabel.BackColor = Color.FromArgb(105, 255, 255, 255);
            headerLabel.Text = "No folder selected";
            headerLabel.MouseDown += DragStart_MouseDown;
            headerLabel.MouseMove += DragStart_MouseMove;
            headerLabel.MouseUp += DragStart_MouseUp;

            iconImageList = new ImageList();
            iconImageList.ColorDepth = ColorDepth.Depth32Bit;
            iconImageList.ImageSize = new Size(iconSize, iconSize);

            launchListView = new BufferedListView();
            launchListView.Dock = DockStyle.Fill;
            launchListView.View = View.LargeIcon;
            launchListView.LargeImageList = iconImageList;
            launchListView.BorderStyle = BorderStyle.None;
            launchListView.BackColor = Color.FromArgb(238, 244, 250);
            launchListView.ForeColor = Color.FromArgb(20, 41, 64);
            launchListView.MultiSelect = false;
            launchListView.HideSelection = false;
            launchListView.FullRowSelect = true;
            launchListView.ShowItemToolTips = true;
            launchListView.OwnerDraw = true;
            launchListView.DoubleClick += LaunchListView_DoubleClick;
            launchListView.KeyDown += LaunchListView_KeyDown;
            launchListView.MouseDown += LaunchListView_MouseDown;
            launchListView.MouseMove += LaunchListView_MouseMove;
            launchListView.MouseLeave += LaunchListView_MouseLeave;
            launchListView.DrawItem += LaunchListView_DrawItem;

            bookcaseView = new BookcaseView();
            bookcaseView.Dock = DockStyle.Fill;
            bookcaseView.Visible = false;
            bookcaseView.ItemActivated += BookcaseView_ItemActivated;
            bookcaseView.ItemRightClick += BookcaseView_ItemRightClick;

            Controls.Add(bookcaseView);
            Controls.Add(launchListView);
            Controls.Add(headerLabel);

            trayMenu = new ContextMenuStrip();
            trayMenu.Opening += TrayMenu_Opening;
            trayMenu.Items.Add("Show / Hide Sidebar", null, ToggleSidebar_Click);
            trayMenu.Items.Add("Choose Folder", null, ChooseFolder_Click);
            trayMenu.Items.Add("Refresh", null, Refresh_Click);
            trayMenu.Items.Add("Border Color (HSV)", null, BorderColor_Click);
            styleMenuItem = new ToolStripMenuItem("Style");
            styleSidebarMenuItem = new ToolStripMenuItem("Sidebar", null, StyleSidebar_Click);
            styleClassicMenuItem = new ToolStripMenuItem("Classic", null, StyleClassic_Click);
            styleMenuItem.DropDownItems.Add(styleSidebarMenuItem);
            styleMenuItem.DropDownItems.Add(styleClassicMenuItem);
            styleMenuItem.Visible = false;
            trayMenu.Items.Add(styleMenuItem);
            showTitlesMenuItem = new ToolStripMenuItem("Show Icon Titles", null, ToggleShowTitles_Click);
            showTitlesMenuItem.CheckOnClick = false;
            trayMenu.Items.Add(showTitlesMenuItem);

            iconSizeTrackBar = CreateIconSizeTrackBar();
            iconSizeTrackBar.ValueChanged += IconSizeTrackBar_ValueChanged;
            iconSizeMenuItem = new ToolStripMenuItem("Icon Size");
            iconSizeMenuItem.Enabled = false;
            iconSizeHost = new ToolStripControlHost(iconSizeTrackBar);
            iconSizeHost.AutoSize = false;
            iconSizeHost.Size = new Size(164, 36);
            trayMenu.Items.Add(iconSizeMenuItem);
            trayMenu.Items.Add(iconSizeHost);

            startupMenuItem = new ToolStripMenuItem("Run At Startup", null, ToggleStartup_Click);
            startupMenuItem.CheckOnClick = false;
            trayMenu.Items.Add(startupMenuItem);

            pinMenuItem = new ToolStripMenuItem("Pin On Top", null, ToggleTopMost_Click);
            trayMenu.Items.Add(pinMenuItem);

            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add("Exit", null, Exit_Click);

            itemMenu = new ContextMenuStrip();
            itemMenu.Opening += ItemMenu_Opening;
            useLaunchBoxIconMenuItem = new ToolStripMenuItem("Use LaunchBox Icon", null, UseLaunchBoxIcon_Click);
            useLaunchBoxIconMenuItem.CheckOnClick = false;
            itemMenu.Items.Add(useLaunchBoxIconMenuItem);
            useFileIconMenuItem = new ToolStripMenuItem("Use File Icon", null, UseFileIcon_Click);
            useFileIconMenuItem.CheckOnClick = false;
            itemMenu.Items.Add(useFileIconMenuItem);
            itemMenu.Items.Add(new ToolStripSeparator());
            itemMenu.Items.Add("Show / Hide Sidebar", null, ToggleSidebar_Click);
            itemMenu.Items.Add("Choose Folder", null, ChooseFolder_Click);
            itemMenu.Items.Add("Refresh", null, Refresh_Click);
            itemMenu.Items.Add("Border Color (HSV)", null, BorderColor_Click);
            itemStyleMenuItem = new ToolStripMenuItem("Style");
            itemStyleSidebarMenuItem = new ToolStripMenuItem("Sidebar", null, StyleSidebar_Click);
            itemStyleClassicMenuItem = new ToolStripMenuItem("Classic", null, StyleClassic_Click);
            itemStyleMenuItem.DropDownItems.Add(itemStyleSidebarMenuItem);
            itemStyleMenuItem.DropDownItems.Add(itemStyleClassicMenuItem);
            itemStyleMenuItem.Visible = false;
            itemMenu.Items.Add(itemStyleMenuItem);
            itemShowTitlesMenuItem = new ToolStripMenuItem("Show Icon Titles", null, ToggleShowTitles_Click);
            itemShowTitlesMenuItem.CheckOnClick = false;
            itemMenu.Items.Add(itemShowTitlesMenuItem);
            itemIconSizeTrackBar = CreateIconSizeTrackBar();
            itemIconSizeTrackBar.ValueChanged += ItemIconSizeTrackBar_ValueChanged;
            itemIconSizeMenuItem = new ToolStripMenuItem("Icon Size");
            itemIconSizeMenuItem.Enabled = false;
            itemIconSizeHost = new ToolStripControlHost(itemIconSizeTrackBar);
            itemIconSizeHost.AutoSize = false;
            itemIconSizeHost.Size = new Size(164, 36);
            itemMenu.Items.Add(itemIconSizeMenuItem);
            itemMenu.Items.Add(itemIconSizeHost);
            itemStartupMenuItem = new ToolStripMenuItem("Run At Startup", null, ToggleStartup_Click);
            itemStartupMenuItem.CheckOnClick = false;
            itemMenu.Items.Add(itemStartupMenuItem);
            itemPinMenuItem = new ToolStripMenuItem("Pin On Top", null, ToggleTopMost_Click);
            itemMenu.Items.Add(itemPinMenuItem);
            itemMenu.Items.Add(new ToolStripSeparator());
            itemMenu.Items.Add("Exit", null, Exit_Click);

            ContextMenuStrip = trayMenu;
            headerLabel.ContextMenuStrip = trayMenu;
            launchListView.ContextMenuStrip = itemMenu;
            bookcaseView.ContextMenuStrip = itemMenu;

            trayIcon = new NotifyIcon();
            trayIcon.Text = "Sidebar Game Launcher";
            trayIcon.Icon = SystemIcons.Application;
            trayIcon.Visible = true;
            trayIcon.ContextMenuStrip = trayMenu;
            trayIcon.DoubleClick += ToggleSidebar_Click;

            hoverAnimationTimer = new System.Windows.Forms.Timer();
            hoverAnimationTimer.Interval = 30;
            hoverAnimationTimer.Tick += HoverAnimationTimer_Tick;
            hoverAnimationTimer.Start();

            Load += MainForm_Load;
            FormClosing += MainForm_FormClosing;
            Move += MainForm_MoveOrResize;
            Resize += MainForm_MoveOrResize;
        }

        private static Font CreateUiFont()
        {
            try
            {
                return new Font("Segoe UI", 9.0f, FontStyle.Regular);
            }
            catch
            {
                return new Font("Tahoma", 9.0f, FontStyle.Regular);
            }
        }

        private static TrackBar CreateIconSizeTrackBar()
        {
            var bar = new TrackBar();
            bar.Minimum = 0;
            bar.Maximum = StandardIconSizes.Length - 1;
            bar.TickFrequency = 1;
            bar.SmallChange = 1;
            bar.LargeChange = 1;
            bar.AutoSize = false;
            bar.Width = 156;
            bar.Height = 30;
            return bar;
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            WindowStateData state = settings.LoadState();
            Bounds = state.WindowBounds;
            TopMost = state.TopMost;
            borderColor = state.BorderColor;
            iconSize = NormalizeIconSize(state.IconSize);
            hideIconLabels = state.HideIconLabels;
            visualStyle = "sidebar";
            iconImageList.ImageSize = new Size(iconSize, iconSize);
            SyncIconSizeControls();
            ApplyAccentToControls();
            ApplyVisualStyle();

            UpdatePinMenuText();
            UpdateStartupMenuState();
            UpdateLabelVisibilityMenuState();
            UpdateStyleMenuState();

            currentFolder = state.LastFolder;
            if (!string.IsNullOrEmpty(currentFolder) && Directory.Exists(currentFolder))
            {
                LoadFolder(currentFolder);
            }
            else
            {
                currentFolder = string.Empty;
            }

            isInitializing = false;
            Invalidate();
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            SaveCurrentState();
            hoverAnimationTimer.Stop();
            hoverAnimationTimer.Dispose();
            trayIcon.Visible = false;
            trayIcon.Dispose();
        }

        private void MainForm_MoveOrResize(object sender, EventArgs e)
        {
            if (!isInitializing && WindowState == FormWindowState.Normal)
            {
                SaveCurrentState();
            }
        }

        private void SaveCurrentState()
        {
            settings.SaveState(new WindowStateData(Bounds, currentFolder, TopMost, borderColor, iconSize, hideIconLabels, "sidebar"));
        }

        private void TrayMenu_Opening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            UpdatePinMenuText();
            UpdateStartupMenuState();
            UpdateLabelVisibilityMenuState();
            SyncIconSizeControls();
            UpdateStyleMenuState();
        }

        private void ItemMenu_Opening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            UpdatePinMenuText();
            UpdateStartupMenuState();
            UpdateLabelVisibilityMenuState();
            SyncIconSizeControls();
            UpdateStyleMenuState();

            ListViewItem selected = contextMenuItem;
            if (selected == null && launchListView.SelectedItems.Count > 0)
            {
                selected = launchListView.SelectedItems[0];
            }

            bool hasItem = selected != null;
            useLaunchBoxIconMenuItem.Enabled = hasItem;
            useFileIconMenuItem.Enabled = hasItem;
            useLaunchBoxIconMenuItem.Checked = false;
            useFileIconMenuItem.Checked = false;

            if (!hasItem)
            {
                return;
            }

            string itemPath = selected.Tag as string;
            ItemIconMode mode = itemIconPreferences.GetMode(itemPath);
            useLaunchBoxIconMenuItem.Checked = mode == ItemIconMode.LaunchBox;
            useFileIconMenuItem.Checked = mode == ItemIconMode.FileDefault;
        }

        private void ToggleSidebar_Click(object sender, EventArgs e)
        {
            if (Visible)
            {
                Hide();
            }
            else
            {
                Show();
                WindowState = FormWindowState.Normal;
                Activate();
            }
        }

        private void ChooseFolder_Click(object sender, EventArgs e)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Select a folder with your games or shortcuts";
                dialog.ShowNewFolderButton = false;
                if (!string.IsNullOrEmpty(currentFolder) && Directory.Exists(currentFolder))
                {
                    dialog.SelectedPath = currentFolder;
                }

                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    LoadFolder(dialog.SelectedPath);
                    SaveCurrentState();
                }
            }
        }

        private void Refresh_Click(object sender, EventArgs e)
        {
            if (!string.IsNullOrEmpty(currentFolder) && Directory.Exists(currentFolder))
            {
                LoadFolder(currentFolder);
            }
        }

        private void BorderColor_Click(object sender, EventArgs e)
        {
            using (var dialog = new HsvColorDialog(borderColor))
            {
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    borderColor = dialog.SelectedColor;
                    ApplyAccentToControls();
                    Invalidate();
                    SaveCurrentState();
                }
            }
        }

        private void ToggleStartup_Click(object sender, EventArgs e)
        {
            bool enabled = StartupRegistration.IsEnabled();
            try
            {
                StartupRegistration.SetEnabled(!enabled);
                UpdateStartupMenuState();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not change startup setting:\r\n" + ex.Message, "Startup Setting", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void UpdateStartupMenuState()
        {
            bool enabled;
            try
            {
                enabled = StartupRegistration.IsEnabled();
            }
            catch
            {
                enabled = false;
            }

            startupMenuItem.Checked = enabled;
            itemStartupMenuItem.Checked = enabled;
        }

        private void ToggleTopMost_Click(object sender, EventArgs e)
        {
            TopMost = !TopMost;
            UpdatePinMenuText();
            SaveCurrentState();
        }

        private void UpdatePinMenuText()
        {
            pinMenuItem.Text = TopMost ? "Unpin From Top" : "Pin On Top";
            itemPinMenuItem.Text = pinMenuItem.Text;
        }

        private void Exit_Click(object sender, EventArgs e)
        {
            Close();
        }

        private void LaunchListView_DoubleClick(object sender, EventArgs e)
        {
            LaunchSelectedItem();
        }

        private void LaunchListView_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.Handled = true;
                LaunchSelectedItem();
            }
            else if (e.KeyCode == Keys.F5)
            {
                e.Handled = true;
                Refresh_Click(sender, EventArgs.Empty);
            }
        }

        private void LaunchListView_MouseDown(object sender, MouseEventArgs e)
        {
            contextMenuPath = string.Empty;
            if (e.Button != MouseButtons.Right)
            {
                return;
            }

            contextMenuItem = launchListView.GetItemAt(e.X, e.Y);
            if (contextMenuItem == null)
            {
                launchListView.SelectedItems.Clear();
                return;
            }

            contextMenuPath = contextMenuItem.Tag as string;
            contextMenuItem.Selected = true;
            contextMenuItem.Focused = true;
        }

        private void LaunchListView_MouseMove(object sender, MouseEventArgs e)
        {
            ListViewItem hovered = launchListView.GetItemAt(e.X, e.Y);
            int index = hovered == null ? -1 : hovered.Index;
            if (index == hoveredItemIndex)
            {
                return;
            }

            int previous = hoveredItemIndex;
            hoveredItemIndex = index;
            if (previous >= 0 && !hoverAlphaByIndex.ContainsKey(previous))
            {
                hoverAlphaByIndex[previous] = 0;
            }

            if (hoveredItemIndex >= 0 && !hoverAlphaByIndex.ContainsKey(hoveredItemIndex))
            {
                hoverAlphaByIndex[hoveredItemIndex] = 0;
            }
        }

        private void LaunchListView_MouseLeave(object sender, EventArgs e)
        {
            if (hoveredItemIndex >= 0 && !hoverAlphaByIndex.ContainsKey(hoveredItemIndex))
            {
                hoverAlphaByIndex[hoveredItemIndex] = 0;
            }

            hoveredItemIndex = -1;
        }

        private void HoverAnimationTimer_Tick(object sender, EventArgs e)
        {
            if (!Visible || launchListView.Items.Count == 0)
            {
                return;
            }

            bool changed = false;
            var keys = new List<int>(hoverAlphaByIndex.Keys);
            for (int i = 0; i < keys.Count; i++)
            {
                int index = keys[i];
                int current = hoverAlphaByIndex[index];
                int target = index == hoveredItemIndex ? HoverMaxAlpha : 0;
                int next = MoveTowards(current, target, 16);
                if (next != current)
                {
                    hoverAlphaByIndex[index] = next;
                    InvalidateListItem(index);
                    changed = true;
                }
                else if (next == 0 && target == 0)
                {
                    hoverAlphaByIndex.Remove(index);
                }
            }

            if (hoveredItemIndex >= 0 && !hoverAlphaByIndex.ContainsKey(hoveredItemIndex))
            {
                hoverAlphaByIndex[hoveredItemIndex] = 0;
                changed = true;
            }

            if (changed && hoveredItemIndex >= 0)
            {
                InvalidateListItem(hoveredItemIndex);
            }
        }

        private void LaunchListView_DrawItem(object sender, DrawListViewItemEventArgs e)
        {
            e.DrawDefault = true;

            int alpha;
            if (!hoverAlphaByIndex.TryGetValue(e.ItemIndex, out alpha) || alpha <= 0)
            {
                return;
            }

            Rectangle bounds = e.Bounds;
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                return;
            }

            using (var pen = new Pen(Color.FromArgb(Math.Min(150, alpha + 24), MixWithWhite(borderColor, 0.20f))))
            {
                Rectangle rect = new Rectangle(bounds.X + 2, bounds.Y + 1, Math.Max(1, bounds.Width - 4), Math.Max(1, bounds.Height - 3));
                e.Graphics.DrawRectangle(pen, rect);
            }
        }

        private void UseLaunchBoxIcon_Click(object sender, EventArgs e)
        {
            ApplySelectedItemIconMode(ItemIconMode.LaunchBox);
        }

        private void UseFileIcon_Click(object sender, EventArgs e)
        {
            ApplySelectedItemIconMode(ItemIconMode.FileDefault);
        }

        private void ToggleShowTitles_Click(object sender, EventArgs e)
        {
            hideIconLabels = !hideIconLabels;
            UpdateLabelVisibilityMenuState();
            ReloadCurrentFolder();
            SaveCurrentState();
        }

        private void StyleSidebar_Click(object sender, EventArgs e)
        {
            if (string.Equals(visualStyle, "sidebar", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            visualStyle = "sidebar";
            ApplyVisualStyle();
            UpdateStyleMenuState();
            SaveCurrentState();
        }

        private void StyleClassic_Click(object sender, EventArgs e)
        {
            visualStyle = "sidebar";
            ApplyVisualStyle();
            UpdateStyleMenuState();
            SaveCurrentState();
        }

        private void IconSizeTrackBar_ValueChanged(object sender, EventArgs e)
        {
            if (syncingIconSizeControls)
            {
                return;
            }

            SetIconSize(StandardIconSizes[((TrackBar)sender).Value]);
        }

        private void ItemIconSizeTrackBar_ValueChanged(object sender, EventArgs e)
        {
            if (syncingIconSizeControls)
            {
                return;
            }

            SetIconSize(StandardIconSizes[((TrackBar)sender).Value]);
        }

        private void SetIconSize(int requestedSize)
        {
            int normalized = NormalizeIconSize(requestedSize);
            if (normalized == iconSize)
            {
                SyncIconSizeControls();
                return;
            }

            iconSize = normalized;
            iconImageList.ImageSize = new Size(iconSize, iconSize);
            SyncIconSizeControls();
            ReloadCurrentFolder();
            SaveCurrentState();
        }

        private void SyncIconSizeControls()
        {
            syncingIconSizeControls = true;
            int index = GetIconSizeIndex(iconSize);
            iconSizeTrackBar.Value = index;
            itemIconSizeTrackBar.Value = index;
            iconSizeMenuItem.Text = "Icon Size: " + iconSize.ToString();
            itemIconSizeMenuItem.Text = "Icon Size: " + iconSize.ToString();
            syncingIconSizeControls = false;
        }

        private void UpdateLabelVisibilityMenuState()
        {
            bool showTitles = !hideIconLabels;
            showTitlesMenuItem.Checked = showTitles;
            itemShowTitlesMenuItem.Checked = showTitles;
        }

        private void UpdateStyleMenuState()
        {
            bool classic = string.Equals(visualStyle, "classic", StringComparison.OrdinalIgnoreCase);
            styleSidebarMenuItem.Checked = !classic;
            styleClassicMenuItem.Checked = classic;
            itemStyleSidebarMenuItem.Checked = !classic;
            itemStyleClassicMenuItem.Checked = classic;
        }

        private void ApplyVisualStyle()
        {
            visualStyle = "sidebar";
            launchListView.Visible = true;
            bookcaseView.Visible = false;
        }

        private void BookcaseView_ItemActivated(object sender, BookcaseItemEventArgs e)
        {
            if (e == null || e.Item == null)
            {
                return;
            }

            contextMenuPath = e.Item.FilePath;
            LaunchItemPath(e.Item.FilePath);
        }

        private void BookcaseView_ItemRightClick(object sender, MouseEventArgs e)
        {
            BookcaseItem item = bookcaseView.GetItemAt(e.Location);
            contextMenuPath = item == null ? string.Empty : item.FilePath;
        }

        private void ApplySelectedItemIconMode(ItemIconMode mode)
        {
            string filePath = GetContextItemPath();
            if (string.IsNullOrEmpty(filePath))
            {
                return;
            }

            itemIconPreferences.SetMode(filePath, mode);
            string displayName = GetDisplayNameFromPath(filePath);

            if (mode == ItemIconMode.FileDefault)
            {
                ApplyFileIconToCurrentViews(filePath);
            }
            else
            {
                QueueLaunchBoxImageLoad(displayName, filePath, null, loadToken);
            }
        }

        private void ReloadCurrentFolder()
        {
            if (!string.IsNullOrEmpty(currentFolder) && Directory.Exists(currentFolder))
            {
                LoadFolder(currentFolder);
            }
        }

        private void ApplyFileIconToCurrentViews(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                return;
            }

            for (int i = 0; i < launchListView.Items.Count; i++)
            {
                ListViewItem lvItem = launchListView.Items[i];
                string p = lvItem.Tag as string;
                if (string.Equals(p, filePath, StringComparison.OrdinalIgnoreCase))
                {
                    lvItem.ImageKey = filePath;
                    break;
                }
            }

            Icon icon = ExtractDisplayIcon(filePath);
            Image img = icon == null ? null : icon.ToBitmap();
            if (img == null)
            {
                return;
            }

            for (int i = 0; i < bookcaseItems.Count; i++)
            {
                if (string.Equals(bookcaseItems[i].FilePath, filePath, StringComparison.OrdinalIgnoreCase))
                {
                    bookcaseItems[i].SpineImage = img;
                    bookcaseItems[i].FrontImage = img;
                    bookcaseView.Invalidate();
                    break;
                }
            }
        }

        private void LaunchSelectedItem()
        {
            string path = GetContextItemPath();
            if (string.IsNullOrEmpty(path) && launchListView.SelectedItems.Count > 0)
            {
                path = launchListView.SelectedItems[0].Tag as string;
            }

            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            LaunchItemPath(path);
        }

        private void LaunchItemPath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            try
            {
                var startInfo = new ProcessStartInfo();
                startInfo.FileName = path;
                startInfo.WorkingDirectory = Path.GetDirectoryName(path);
                startInfo.UseShellExecute = true;
                Process.Start(startInfo);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not launch item:\r\n" + ex.Message, "Launch Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private string GetContextItemPath()
        {
            if (!string.IsNullOrEmpty(contextMenuPath))
            {
                return contextMenuPath;
            }

            ListViewItem item = contextMenuItem;
            if (item != null)
            {
                string p = item.Tag as string;
                if (!string.IsNullOrEmpty(p))
                {
                    return p;
                }
            }

            if (launchListView.SelectedItems.Count > 0)
            {
                return launchListView.SelectedItems[0].Tag as string;
            }

            return string.Empty;
        }

        private void LoadFolder(string folderPath)
        {
            int currentToken = unchecked(++loadToken);
            currentFolder = folderPath;
            headerLabel.Text = Path.GetFileName(folderPath);
            if (string.IsNullOrEmpty(headerLabel.Text))
            {
                headerLabel.Text = folderPath;
            }

            launchListView.BeginUpdate();
            launchListView.Items.Clear();
            iconImageList.Images.Clear();
            hoverAlphaByIndex.Clear();
            hoveredItemIndex = -1;
            bookcaseItems.Clear();
            contextMenuPath = string.Empty;

            try
            {
                string[] files = Directory.GetFiles(folderPath, "*", SearchOption.TopDirectoryOnly);
                Array.Sort(files, StringComparer.OrdinalIgnoreCase);

                foreach (string file in files)
                {
                    if (!IsLaunchable(file))
                    {
                        continue;
                    }

                    string displayName = GetDisplayNameFromPath(file);

                    string imageKey = file;
                    iconImageList.Images.Add(imageKey, ExtractDisplayIcon(file));

                    var item = new ListViewItem(hideIconLabels ? string.Empty : displayName, imageKey);
                    item.Tag = file;
                    item.ToolTipText = displayName + "\r\n" + file;
                    launchListView.Items.Add(item);

                    Image fallbackCaseImage = iconImageList.Images[imageKey];
                    bookcaseItems.Add(new BookcaseItem
                    {
                        Title = displayName,
                        FilePath = file,
                        SpineImage = fallbackCaseImage,
                        FrontImage = fallbackCaseImage,
                        HoverProgress = 0f
                    });

                    if (itemIconPreferences.GetMode(file) == ItemIconMode.LaunchBox)
                    {
                        QueueLaunchBoxImageLoad(displayName, file, item, currentToken);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not load folder:\r\n" + ex.Message, "Load Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                launchListView.EndUpdate();
                bookcaseView.SetItems(new List<BookcaseItem>(bookcaseItems));
            }
        }

        private void QueueLaunchBoxImageLoad(string displayName, string filePath, ListViewItem item, int token)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                string imagePath;
                if (!launchBoxIcons.TryGetIconPath(displayName, out imagePath))
                {
                    return;
                }

                if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath))
                {
                    return;
                }

                if (IsDisposed || Disposing)
                {
                    return;
                }

                BeginInvoke((MethodInvoker)delegate
                {
                    if (IsDisposed || Disposing || token != loadToken)
                    {
                        return;
                    }

                    ListViewItem resolvedItem = item;
                    if (resolvedItem == null || resolvedItem.ListView != launchListView)
                    {
                        resolvedItem = FindListItemByPath(filePath);
                        if (resolvedItem == null)
                        {
                            return;
                        }
                    }

                    string currentPath = resolvedItem.Tag as string;
                    if (!string.Equals(currentPath, filePath, StringComparison.OrdinalIgnoreCase))
                    {
                        return;
                    }

                    string launchBoxKey = "lb::" + filePath;
                    if (!iconImageList.Images.ContainsKey(launchBoxKey))
                    {
                        Image image = LoadImageWithoutLock(imagePath);
                        if (image != null)
                        {
                            iconImageList.Images.Add(launchBoxKey, image);
                        }
                    }

                    if (iconImageList.Images.ContainsKey(launchBoxKey))
                    {
                        resolvedItem.ImageKey = launchBoxKey;
                    }
                });
            });
        }

        private void QueueBookcaseArtLoad(string displayName, string filePath, int token)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                string spinePath;
                string frontPath;
                if (!launchBoxIcons.TryGetCaseArtPaths(displayName, out spinePath, out frontPath))
                {
                    return;
                }

                if (IsDisposed || Disposing)
                {
                    return;
                }

                Image spineImage = LoadImageWithoutLock(spinePath);
                Image frontImage = LoadImageWithoutLock(frontPath);
                if (spineImage == null && frontImage == null)
                {
                    return;
                }

                if (spineImage == null)
                {
                    spineImage = frontImage;
                }

                if (frontImage == null)
                {
                    frontImage = spineImage;
                }

                BeginInvoke((MethodInvoker)delegate
                {
                    if (IsDisposed || Disposing || token != loadToken)
                    {
                        return;
                    }

                    for (int i = 0; i < bookcaseItems.Count; i++)
                    {
                        BookcaseItem entry = bookcaseItems[i];
                        if (string.Equals(entry.FilePath, filePath, StringComparison.OrdinalIgnoreCase))
                        {
                            entry.SpineImage = spineImage;
                            entry.FrontImage = frontImage;
                            bookcaseView.Invalidate();
                            break;
                        }
                    }
                });
            });
        }

        private ListViewItem FindListItemByPath(string filePath)
        {
            for (int i = 0; i < launchListView.Items.Count; i++)
            {
                ListViewItem item = launchListView.Items[i];
                string current = item.Tag as string;
                if (string.Equals(current, filePath, StringComparison.OrdinalIgnoreCase))
                {
                    return item;
                }
            }

            return null;
        }

        private static Image LoadImageWithoutLock(string imagePath)
        {
            try
            {
                using (Image original = Image.FromFile(imagePath))
                {
                    return new Bitmap(original);
                }
            }
            catch
            {
                return null;
            }
        }

        private void DragStart_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            isDragging = true;
            dragStart = e.Location;
        }

        private void DragStart_MouseMove(object sender, MouseEventArgs e)
        {
            if (!isDragging)
            {
                return;
            }

            Point screen = PointToScreen(e.Location);
            Location = new Point(screen.X - dragStart.X, screen.Y - dragStart.Y);
        }

        private void DragStart_MouseUp(object sender, MouseEventArgs e)
        {
            isDragging = false;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            Rectangle rect = ClientRectangle;
            Color baseTone = Desaturate(borderColor, 0.22f);
            Color accentLight = MixWithWhite(baseTone, 0.86f);
            Color accentMid = MixWithWhite(baseTone, 0.66f);
            Color accentDeep = MixWithWhite(baseTone, 0.42f);

            using (var bgBrush = new LinearGradientBrush(rect, Color.FromArgb(235, accentLight), Color.FromArgb(220, accentMid), 90f))
            {
                e.Graphics.FillRectangle(bgBrush, rect);
            }

            using (var diagonal = new HatchBrush(HatchStyle.LightDownwardDiagonal, Color.FromArgb(48, accentDeep), Color.Transparent))
            {
                e.Graphics.FillRectangle(diagonal, rect);
            }

            Rectangle glow = new Rectangle(0, 0, rect.Width, 72);
            using (var glowBrush = new LinearGradientBrush(glow, Color.FromArgb(160, accentLight), Color.FromArgb(24, accentMid), 90f))
            {
                e.Graphics.FillRectangle(glowBrush, glow);
            }

            Rectangle orb = new Rectangle(rect.Width - 124, 12, 96, 72);
            using (var orbBrush = new LinearGradientBrush(orb, Color.FromArgb(120, accentLight), Color.FromArgb(12, accentMid), 90f))
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                e.Graphics.FillEllipse(orbBrush, orb);
                e.Graphics.SmoothingMode = SmoothingMode.Default;
            }

            using (var pen = new Pen(borderColor))
            {
                e.Graphics.DrawRectangle(pen, 0, 0, rect.Width - 1, rect.Height - 1);
            }
        }

        private void ApplyAccentToControls()
        {
            Color baseTone = Desaturate(borderColor, 0.22f);
            Color accentLight = MixWithWhite(baseTone, 0.90f);
            Color accentMid = MixWithWhite(baseTone, 0.78f);
            Color accentDark = MixWithBlack(baseTone, 0.62f);

            BackColor = MixWithWhite(baseTone, 0.94f);
            headerLabel.BackColor = accentLight;
            headerLabel.ForeColor = accentDark;
            launchListView.BackColor = accentMid;
            launchListView.ForeColor = accentDark;
        }

        private static Color Desaturate(Color color, float saturationFactor)
        {
            if (saturationFactor < 0f)
            {
                saturationFactor = 0f;
            }
            else if (saturationFactor > 1f)
            {
                saturationFactor = 1f;
            }

            float gray = (color.R * 0.30f) + (color.G * 0.59f) + (color.B * 0.11f);
            int r = (int)(gray + ((color.R - gray) * saturationFactor));
            int g = (int)(gray + ((color.G - gray) * saturationFactor));
            int b = (int)(gray + ((color.B - gray) * saturationFactor));
            return Color.FromArgb(ClampByte(r), ClampByte(g), ClampByte(b));
        }

        private static Color MixWithWhite(Color color, float amount)
        {
            return Blend(color, Color.White, amount);
        }

        private static Color MixWithBlack(Color color, float amount)
        {
            return Blend(color, Color.Black, amount);
        }

        private static Color Blend(Color from, Color to, float amount)
        {
            if (amount < 0f)
            {
                amount = 0f;
            }
            else if (amount > 1f)
            {
                amount = 1f;
            }

            int r = (int)(from.R + ((to.R - from.R) * amount));
            int g = (int)(from.G + ((to.G - from.G) * amount));
            int b = (int)(from.B + ((to.B - from.B) * amount));
            return Color.FromArgb(r, g, b);
        }

        private static int ClampByte(int value)
        {
            if (value < 0)
            {
                return 0;
            }

            if (value > 255)
            {
                return 255;
            }

            return value;
        }

        private static int NormalizeIconSize(int value)
        {
            int best = StandardIconSizes[0];
            int bestDistance = Math.Abs(value - best);
            for (int i = 1; i < StandardIconSizes.Length; i++)
            {
                int size = StandardIconSizes[i];
                int distance = Math.Abs(value - size);
                if (distance < bestDistance)
                {
                    best = size;
                    bestDistance = distance;
                }
            }

            return best;
        }

        private static int GetIconSizeIndex(int normalizedSize)
        {
            for (int i = 0; i < StandardIconSizes.Length; i++)
            {
                if (StandardIconSizes[i] == normalizedSize)
                {
                    return i;
                }
            }

            return 1;
        }

        private static int MoveTowards(int current, int target, int step)
        {
            if (current < target)
            {
                return Math.Min(target, current + step);
            }

            if (current > target)
            {
                return Math.Max(target, current - step);
            }

            return current;
        }

        private void InvalidateListItem(int index)
        {
            if (index < 0 || index >= launchListView.Items.Count)
            {
                return;
            }

            launchListView.Invalidate(launchListView.Items[index].Bounds);
        }

        private static string GetDisplayNameFromPath(string filePath)
        {
            string displayName = Path.GetFileNameWithoutExtension(filePath);
            if (string.IsNullOrEmpty(displayName))
            {
                displayName = Path.GetFileName(filePath);
            }

            return displayName;
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_NCHITTEST)
            {
                base.WndProc(ref m);
                if ((int)m.Result == HTCLIENT)
                {
                    int lParam = m.LParam.ToInt32();
                    int x = (short)(lParam & 0xFFFF);
                    int y = (short)((lParam >> 16) & 0xFFFF);
                    Point client = PointToClient(new Point(x, y));
                    m.Result = (IntPtr)GetResizeHitTest(client);
                }

                return;
            }

            base.WndProc(ref m);
        }

        private int GetResizeHitTest(Point clientPoint)
        {
            const int grip = 10;
            bool left = clientPoint.X <= grip;
            bool right = clientPoint.X >= ClientSize.Width - grip;
            bool top = clientPoint.Y <= grip;
            bool bottom = clientPoint.Y >= ClientSize.Height - grip;

            if (left && top)
            {
                return HTTOPLEFT;
            }

            if (right && top)
            {
                return HTTOPRIGHT;
            }

            if (left && bottom)
            {
                return HTBOTTOMLEFT;
            }

            if (right && bottom)
            {
                return HTBOTTOMRIGHT;
            }

            if (left)
            {
                return HTLEFT;
            }

            if (right)
            {
                return HTRIGHT;
            }

            if (top)
            {
                return HTTOP;
            }

            if (bottom)
            {
                return HTBOTTOM;
            }

            return HTCLIENT;
        }

        private static bool IsLaunchable(string path)
        {
            string ext = Path.GetExtension(path);
            if (string.IsNullOrEmpty(ext))
            {
                return false;
            }

            ext = ext.ToLowerInvariant();
            return ext == ".exe" || ext == ".lnk" || ext == ".url" || ext == ".bat" || ext == ".cmd" || ext == ".com" || ext == ".msi";
        }

        private static Icon ExtractDisplayIcon(string path)
        {
            string ext = Path.GetExtension(path);
            if (string.Equals(ext, ".lnk", StringComparison.OrdinalIgnoreCase))
            {
                string targetPath;
                if (TryResolveShortcutTargetPath(path, out targetPath))
                {
                    Icon targetIcon = ExtractFileIcon(targetPath);
                    if (targetIcon != null)
                    {
                        return targetIcon;
                    }
                }
            }

            return ExtractFileIcon(path);
        }

        private static bool TryResolveShortcutTargetPath(string shortcutPath, out string targetPath)
        {
            targetPath = string.Empty;
            object shell = null;
            object shortcut = null;

            try
            {
                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null)
                {
                    return false;
                }

                shell = Activator.CreateInstance(shellType);
                shortcut = shellType.InvokeMember(
                    "CreateShortcut",
                    BindingFlags.InvokeMethod,
                    null,
                    shell,
                    new object[] { shortcutPath },
                    CultureInfo.InvariantCulture);

                if (shortcut == null)
                {
                    return false;
                }

                Type shortcutType = shortcut.GetType();
                object targetObj = shortcutType.InvokeMember(
                    "TargetPath",
                    BindingFlags.GetProperty,
                    null,
                    shortcut,
                    null,
                    CultureInfo.InvariantCulture);

                string resolved = Convert.ToString(targetObj, CultureInfo.InvariantCulture);
                if (string.IsNullOrWhiteSpace(resolved))
                {
                    return false;
                }

                resolved = resolved.Trim();
                if (!File.Exists(resolved))
                {
                    return false;
                }

                if (string.Equals(resolved, shortcutPath, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                targetPath = resolved;
                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (shortcut != null && Marshal.IsComObject(shortcut))
                {
                    Marshal.ReleaseComObject(shortcut);
                }

                if (shell != null && Marshal.IsComObject(shell))
                {
                    Marshal.ReleaseComObject(shell);
                }
            }
        }

        private static Icon ExtractFileIcon(string path)
        {
            SHFILEINFO locationInfo;
            IntPtr locationRet = SHGetFileInfo(path, 0, out locationInfo, (uint)Marshal.SizeOf(typeof(SHFILEINFO)), SHGFI_ICONLOCATION);
            if (locationRet != IntPtr.Zero && !string.IsNullOrEmpty(locationInfo.szDisplayName))
            {
                IntPtr largeIcon;
                IntPtr smallIcon;
                uint extracted = ExtractIconEx(locationInfo.szDisplayName, locationInfo.iIcon, out largeIcon, out smallIcon, 1);
                if (extracted > 0 && largeIcon != IntPtr.Zero)
                {
                    try
                    {
                        return (Icon)Icon.FromHandle(largeIcon).Clone();
                    }
                    finally
                    {
                        DestroyIcon(largeIcon);
                        if (smallIcon != IntPtr.Zero)
                        {
                            DestroyIcon(smallIcon);
                        }
                    }
                }
            }

            SHFILEINFO shinfo;
            IntPtr ret = SHGetFileInfo(path, 0, out shinfo, (uint)Marshal.SizeOf(typeof(SHFILEINFO)), SHGFI_ICON | SHGFI_LARGEICON | SHGFI_USEFILEATTRIBUTES);
            if (ret != IntPtr.Zero && shinfo.hIcon != IntPtr.Zero)
            {
                try
                {
                    return (Icon)Icon.FromHandle(shinfo.hIcon).Clone();
                }
                finally
                {
                    DestroyIcon(shinfo.hIcon);
                }
            }

            return SystemIcons.Application;
        }

        [DllImport("Shell32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, out SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport("Shell32.dll", CharSet = CharSet.Auto)]
        private static extern uint ExtractIconEx(string szFileName, int nIconIndex, out IntPtr phiconLarge, out IntPtr phiconSmall, uint nIcons);

        private const uint SHGFI_ICON = 0x100;
        private const uint SHGFI_LARGEICON = 0x0;
        private const uint SHGFI_ICONLOCATION = 0x1000;
        private const uint SHGFI_USEFILEATTRIBUTES = 0x10;
        private const int WM_NCHITTEST = 0x84;
        private const int HTCLIENT = 1;
        private const int HTLEFT = 10;
        private const int HTRIGHT = 11;
        private const int HTTOP = 12;
        private const int HTTOPLEFT = 13;
        private const int HTTOPRIGHT = 14;
        private const int HTBOTTOM = 15;
        private const int HTBOTTOMLEFT = 16;
        private const int HTBOTTOMRIGHT = 17;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
            public string szTypeName;
        }
    }
}

