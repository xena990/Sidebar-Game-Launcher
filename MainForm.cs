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
        private readonly Dictionary<string, Icon> displayIconCache;
        private readonly Dictionary<string, Image> launchBoxImageCache;
        private readonly Dictionary<string, string> shortcutTargetCache;
        private readonly Dictionary<string, ListViewItem> listItemByPath;
        private readonly object iconCacheSync;
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
        private int pressedItemIndex;
        private readonly bool lowEffectsMode;
        private static readonly int[] StandardIconSizes = new[] { 24, 32, 40, 48, 64, 72, 96 };
        private static readonly HashSet<string> LaunchableExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".exe", ".lnk", ".url", ".bat", ".cmd", ".com", ".msi"
        };
        private string visualStyle;
        private const string ThemeBlue = "blue";

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
            displayIconCache = new Dictionary<string, Icon>(StringComparer.OrdinalIgnoreCase);
            launchBoxImageCache = new Dictionary<string, Image>(StringComparer.OrdinalIgnoreCase);
            shortcutTargetCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            listItemByPath = new Dictionary<string, ListViewItem>(StringComparer.OrdinalIgnoreCase);
            iconCacheSync = new object();
            hoveredItemIndex = -1;
            pressedItemIndex = -1;
            bookcaseItems = new List<BookcaseItem>();
            contextMenuPath = string.Empty;
            visualStyle = ThemeBlue;
            lowEffectsMode = Environment.OSVersion.Version.Major < 6;

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
            launchListView.MouseUp += LaunchListView_MouseUp;
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
            showTitlesMenuItem = new ToolStripMenuItem("Remove Icon Title", null, ToggleShowTitles_Click);
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
            itemShowTitlesMenuItem = new ToolStripMenuItem("Remove Icon Title", null, ToggleShowTitles_Click);
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
            trayIcon.Icon = GetTrayIcon();
            trayIcon.Visible = true;
            trayIcon.ContextMenuStrip = trayMenu;
            trayIcon.DoubleClick += ToggleSidebar_Click;

            hoverAnimationTimer = new System.Windows.Forms.Timer();
            hoverAnimationTimer.Interval = 30;
            hoverAnimationTimer.Tick += HoverAnimationTimer_Tick;

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
            visualStyle = ThemeBlue;
            iconImageList.ImageSize = new Size(iconSize, iconSize);
            SyncIconSizeControls();
            UpdateListLayout();
            ApplyAccentToControls();
            ApplyVisualStyle();

            UpdatePinMenuText();
            UpdateStartupMenuState();
            UpdateLabelVisibilityMenuState();

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
            DisposeImageAndIconCaches();
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
            settings.SaveState(new WindowStateData(Bounds, currentFolder, TopMost, borderColor, iconSize, hideIconLabels, visualStyle));
        }

        private void TrayMenu_Opening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            UpdatePinMenuText();
            UpdateStartupMenuState();
            UpdateLabelVisibilityMenuState();
            SyncIconSizeControls();
        }

        private void ItemMenu_Opening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            UpdatePinMenuText();
            UpdateStartupMenuState();
            UpdateLabelVisibilityMenuState();
            SyncIconSizeControls();

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
            ListViewItem hitItem = launchListView.GetItemAt(e.X, e.Y);

            if (e.Button == MouseButtons.Left)
            {
                SetPressedItem(hitItem == null ? -1 : hitItem.Index);
                return;
            }

            if (e.Button != MouseButtons.Right)
            {
                return;
            }

            contextMenuItem = hitItem;
            if (contextMenuItem == null)
            {
                launchListView.SelectedItems.Clear();
                return;
            }

            contextMenuPath = contextMenuItem.Tag as string;
            contextMenuItem.Selected = true;
            contextMenuItem.Focused = true;
        }

        private void LaunchListView_MouseUp(object sender, MouseEventArgs e)
        {
            SetPressedItem(-1);
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
            InvalidateListItem(previous);
            InvalidateListItem(hoveredItemIndex);
        }

        private void LaunchListView_MouseLeave(object sender, EventArgs e)
        {
            int previous = hoveredItemIndex;
            SetPressedItem(-1);
            hoveredItemIndex = -1;
            InvalidateListItem(previous);
        }

        private void HoverAnimationTimer_Tick(object sender, EventArgs e)
        {
            // Hover animation intentionally disabled.
        }

        private void LaunchListView_DrawItem(object sender, DrawListViewItemEventArgs e)
        {
            bool hovered = e.ItemIndex == hoveredItemIndex;
            bool pressed = e.ItemIndex == pressedItemIndex;
            if (!hovered && !pressed)
            {
                e.DrawDefault = true;
                return;
            }

            e.DrawDefault = false;
            Rectangle bounds = e.Bounds;
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                return;
            }

            using (var back = new SolidBrush(launchListView.BackColor))
            {
                e.Graphics.FillRectangle(back, bounds);
            }

            DrawHoveredIconZoom(e.Graphics, e.Item, bounds, pressed);
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
            UpdateListLayout();
            ReloadCurrentFolder();
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
            UpdateListLayout();
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
            showTitlesMenuItem.Text = showTitles ? "Remove Icon Title" : "Show Icon Title";
            itemShowTitlesMenuItem.Text = showTitlesMenuItem.Text;
        }

        private void ApplyVisualStyle()
        {
            visualStyle = ThemeBlue;
            launchListView.Visible = true;
            bookcaseView.Visible = false;
            ApplyAccentToControls();
            Invalidate();
            UpdateListLayout();
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

            Icon icon = GetDisplayIconCached(filePath);
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
            pressedItemIndex = -1;
            listItemByPath.Clear();
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
                    iconImageList.Images.Add(imageKey, GetDisplayIconCached(file));

                    var item = new ListViewItem(hideIconLabels ? string.Empty : displayName, imageKey);
                    item.Name = displayName;
                    item.Tag = file;
                    item.ToolTipText = displayName + "\r\n" + file;
                    launchListView.Items.Add(item);
                    listItemByPath[file] = item;

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
                        string cachedLaunchBoxPath;
                        if (launchBoxIcons.TryGetCachedIconPath(displayName, out cachedLaunchBoxPath))
                        {
                            ApplyLaunchBoxImageToItem(item, file, cachedLaunchBoxPath);
                            continue;
                        }

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
                UpdateListLayout();
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

                    ApplyLaunchBoxImageToItem(resolvedItem, filePath, imagePath);
                });
            });
        }

        private void ApplyLaunchBoxImageToItem(ListViewItem item, string filePath, string imagePath)
        {
            if (item == null || string.IsNullOrEmpty(imagePath))
            {
                return;
            }

            string launchBoxKey = "lb::" + filePath;
            if (!iconImageList.Images.ContainsKey(launchBoxKey))
            {
                Image image = GetLaunchBoxImageCached(imagePath);
                if (image != null)
                {
                    iconImageList.Images.Add(launchBoxKey, image);
                }
            }

            if (iconImageList.Images.ContainsKey(launchBoxKey))
            {
                item.ImageKey = launchBoxKey;
            }
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
            ListViewItem cached;
            if (listItemByPath.TryGetValue(filePath, out cached) && cached != null && cached.ListView == launchListView)
            {
                return cached;
            }

            for (int i = 0; i < launchListView.Items.Count; i++)
            {
                ListViewItem item = launchListView.Items[i];
                string current = item.Tag as string;
                if (string.Equals(current, filePath, StringComparison.OrdinalIgnoreCase))
                {
                    listItemByPath[filePath] = item;
                    return item;
                }
            }

            return null;
        }

        private Icon GetDisplayIconCached(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return (Icon)SystemIcons.Application.Clone();
            }

            Icon cachedIcon;
            lock (iconCacheSync)
            {
                if (displayIconCache.TryGetValue(path, out cachedIcon))
                {
                    return (Icon)cachedIcon.Clone();
                }
            }

            string resolvedPath = ResolveShortcutTargetCached(path);
            Icon extracted = ExtractFileIcon(resolvedPath);
            if (extracted == null)
            {
                extracted = (Icon)SystemIcons.Application.Clone();
            }

            lock (iconCacheSync)
            {
                if (!displayIconCache.ContainsKey(path))
                {
                    displayIconCache[path] = (Icon)extracted.Clone();
                }
                else
                {
                    Icon existing = displayIconCache[path];
                    extracted.Dispose();
                    extracted = (Icon)existing.Clone();
                }

                if (displayIconCache.Count > 1800)
                {
                    ClearDisplayIconCache_NoThrow();
                }
            }

            return extracted;
        }

        private string ResolveShortcutTargetCached(string path)
        {
            if (!string.Equals(Path.GetExtension(path), ".lnk", StringComparison.OrdinalIgnoreCase))
            {
                return path;
            }

            string cached;
            lock (iconCacheSync)
            {
                if (shortcutTargetCache.TryGetValue(path, out cached) && !string.IsNullOrEmpty(cached) && File.Exists(cached))
                {
                    return cached;
                }
            }

            string targetPath;
            if (!TryResolveShortcutTargetPath(path, out targetPath))
            {
                targetPath = path;
            }

            lock (iconCacheSync)
            {
                shortcutTargetCache[path] = targetPath;
            }

            return targetPath;
        }

        private Image GetLaunchBoxImageCached(string imagePath)
        {
            if (string.IsNullOrEmpty(imagePath))
            {
                return null;
            }

            Image cached;
            lock (iconCacheSync)
            {
                if (launchBoxImageCache.TryGetValue(imagePath, out cached))
                {
                    return cached;
                }
            }

            Image loaded = LoadImageWithoutLock(imagePath);
            if (loaded == null)
            {
                return null;
            }

            lock (iconCacheSync)
            {
                if (!launchBoxImageCache.ContainsKey(imagePath))
                {
                    launchBoxImageCache[imagePath] = loaded;
                    cached = loaded;
                }
                else
                {
                    loaded.Dispose();
                    cached = launchBoxImageCache[imagePath];
                }

                if (launchBoxImageCache.Count > 900)
                {
                    ClearLaunchBoxImageCache_NoThrow();
                }
            }

            return cached;
        }

        private void DisposeImageAndIconCaches()
        {
            lock (iconCacheSync)
            {
                ClearDisplayIconCache_NoThrow();
                ClearLaunchBoxImageCache_NoThrow();
                shortcutTargetCache.Clear();
                listItemByPath.Clear();
            }
        }

        private void ClearDisplayIconCache_NoThrow()
        {
            foreach (KeyValuePair<string, Icon> entry in displayIconCache)
            {
                try
                {
                    if (entry.Value != null)
                    {
                        entry.Value.Dispose();
                    }
                }
                catch
                {
                }
            }

            displayIconCache.Clear();
        }

        private void ClearLaunchBoxImageCache_NoThrow()
        {
            foreach (KeyValuePair<string, Image> entry in launchBoxImageCache)
            {
                try
                {
                    if (entry.Value != null)
                    {
                        entry.Value.Dispose();
                    }
                }
                catch
                {
                }
            }

            launchBoxImageCache.Clear();
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
            if (rect.Width <= 0 || rect.Height <= 0)
            {
                return;
            }

            Color baseTone = Desaturate(borderColor, 0.22f);
            Color themeSky;
            Color themeField;
            GetThemeBiasColors(out themeSky, out themeField);
            Color frutigerBlue = Blend(baseTone, themeSky, 0.42f);
            Color frutigerGreen = Blend(baseTone, themeField, 0.34f);
            Color accentLight = MixWithWhite(frutigerBlue, 0.86f);
            Color accentMid = MixWithWhite(frutigerBlue, 0.66f);
            Color accentDeep = MixWithWhite(frutigerGreen, 0.50f);

            if (lowEffectsMode)
            {
                using (var bgBrush = new LinearGradientBrush(rect, Color.FromArgb(238, accentLight), Color.FromArgb(220, accentMid), 90f))
                {
                    e.Graphics.FillRectangle(bgBrush, rect);
                }

                using (var pen = new Pen(MixWithBlack(borderColor, 0.22f)))
                {
                    e.Graphics.DrawRectangle(pen, 0, 0, rect.Width - 1, rect.Height - 1);
                }

                return;
            }

            using (var bgBrush = new LinearGradientBrush(rect, Color.FromArgb(242, accentLight), Color.FromArgb(224, accentMid), 90f))
            {
                e.Graphics.FillRectangle(bgBrush, rect);
            }

            using (var diagonal = new HatchBrush(HatchStyle.LightDownwardDiagonal, Color.FromArgb(34, accentDeep), Color.Transparent))
            {
                e.Graphics.FillRectangle(diagonal, rect);
            }

            Rectangle topGlow = new Rectangle(0, 0, rect.Width, Math.Max(88, rect.Height / 4));
            using (var glowBrush = new LinearGradientBrush(topGlow, Color.FromArgb(150, Color.White), Color.FromArgb(10, Color.White), 90f))
            {
                e.Graphics.FillRectangle(glowBrush, topGlow);
            }

            Rectangle sweep = new Rectangle(-rect.Width / 3, -18, rect.Width + (rect.Width / 2), Math.Max(120, rect.Height / 2));
            using (var sweepBrush = new LinearGradientBrush(sweep, Color.FromArgb(96, MixWithWhite(frutigerBlue, 0.96f)), Color.FromArgb(10, accentMid), 145f))
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                e.Graphics.FillEllipse(sweepBrush, sweep);
                e.Graphics.SmoothingMode = SmoothingMode.Default;
            }

            Rectangle greenArc = new Rectangle(-rect.Width / 5, rect.Height / 3, rect.Width + (rect.Width / 2), rect.Height);
            using (var arcBrush = new LinearGradientBrush(greenArc, Color.FromArgb(72, MixWithWhite(frutigerGreen, 0.90f)), Color.FromArgb(10, accentDeep), 25f))
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                e.Graphics.FillEllipse(arcBrush, greenArc);
                e.Graphics.SmoothingMode = SmoothingMode.Default;
            }

            using (var pen = new Pen(MixWithBlack(borderColor, 0.22f)))
            {
                e.Graphics.DrawRectangle(pen, 0, 0, rect.Width - 1, rect.Height - 1);
            }

            using (var innerPen = new Pen(Color.FromArgb(120, Color.White)))
            {
                e.Graphics.DrawRectangle(innerPen, 1, 1, rect.Width - 3, rect.Height - 3);
            }
        }

        private void ApplyAccentToControls()
        {
            Color baseTone = Desaturate(borderColor, 0.22f);
            Color themeSky;
            Color themeField;
            GetThemeBiasColors(out themeSky, out themeField);
            Color themed = Blend(baseTone, Blend(themeSky, themeField, 0.35f), 0.32f);

            Color accentLight = MixWithWhite(themed, 0.90f);
            Color accentMid = MixWithWhite(themed, 0.78f);
            Color accentDark = MixWithBlack(baseTone, 0.62f);

            BackColor = MixWithWhite(baseTone, 0.94f);
            headerLabel.BackColor = Color.FromArgb(168, accentLight);
            headerLabel.ForeColor = accentDark;
            launchListView.BackColor = accentMid;
            launchListView.ForeColor = accentDark;
        }

        private void DrawHoveredIconZoom(Graphics g, ListViewItem item, Rectangle bounds, bool pressed)
        {
            Image image = null;
            if (!string.IsNullOrEmpty(item.ImageKey) && iconImageList.Images.ContainsKey(item.ImageKey))
            {
                image = iconImageList.Images[item.ImageKey];
            }
            else if (item.ImageIndex >= 0 && item.ImageIndex < iconImageList.Images.Count)
            {
                image = iconImageList.Images[item.ImageIndex];
            }

            if (image == null)
            {
                return;
            }

            float scale = pressed ? 1.10f : 1.16f;
            int zoom = (int)(iconSize * scale);
            int x = bounds.X + ((bounds.Width - zoom) / 2);
            int y = bounds.Y + 3;
            Rectangle zoomRect = new Rectangle(x, y, zoom, zoom);

            g.SmoothingMode = lowEffectsMode ? SmoothingMode.Default : SmoothingMode.HighQuality;
            g.InterpolationMode = lowEffectsMode ? InterpolationMode.Bilinear : InterpolationMode.HighQualityBicubic;
            g.DrawImage(image, zoomRect);
            g.InterpolationMode = InterpolationMode.Default;
            g.SmoothingMode = SmoothingMode.Default;

            if (!hideIconLabels && !string.IsNullOrEmpty(item.Text))
            {
                Rectangle textRect = new Rectangle(bounds.X + 2, zoomRect.Bottom + 4, Math.Max(1, bounds.Width - 4), Math.Max(1, bounds.Bottom - zoomRect.Bottom - 4));
                TextRenderer.DrawText(
                    g,
                    item.Text,
                    launchListView.Font,
                    textRect,
                    launchListView.ForeColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.EndEllipsis | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            }
        }

        private void GetThemeBiasColors(out Color sky, out Color field)
        {
            sky = Color.FromArgb(85, 195, 255);
            field = Color.FromArgb(145, 218, 140);
        }

        private void UpdateListLayout()
        {
            if (!launchListView.IsHandleCreated)
            {
                return;
            }

            for (int i = 0; i < launchListView.Items.Count; i++)
            {
                ListViewItem item = launchListView.Items[i];
                string displayName = item.Name;
                if (string.IsNullOrEmpty(displayName))
                {
                    string filePath = item.Tag as string;
                    displayName = string.IsNullOrEmpty(filePath) ? item.Text : GetDisplayNameFromPath(filePath);
                    item.Name = displayName;
                }

                string desiredText = hideIconLabels ? string.Empty : displayName;
                if (!string.Equals(item.Text, desiredText, StringComparison.Ordinal))
                {
                    item.Text = desiredText;
                }
            }

            int horizontalSpacing;
            int verticalSpacing;
            if (hideIconLabels)
            {
                horizontalSpacing = Math.Max(iconSize + 14, 38);
                verticalSpacing = Math.Max(iconSize + 14, 38);
            }
            else
            {
                horizontalSpacing = Math.Max(iconSize + 60, 92);
                verticalSpacing = Math.Max(iconSize + 34, 70);
            }

            launchListView.LabelWrap = !hideIconLabels;
            int spacing = (verticalSpacing << 16) | (horizontalSpacing & 0xFFFF);
            SendMessage(launchListView.Handle, LVM_SETICONSPACING, IntPtr.Zero, new IntPtr(spacing));
            launchListView.Invalidate();
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

        private void SetPressedItem(int itemIndex)
        {
            if (pressedItemIndex == itemIndex)
            {
                return;
            }

            int previous = pressedItemIndex;
            pressedItemIndex = itemIndex;
            InvalidateListItem(previous);
            InvalidateListItem(pressedItemIndex);
        }

        private static GraphicsPath CreateRoundedPath(Rectangle rect, int radius)
        {
            var path = new GraphicsPath();
            if (radius <= 0 || rect.Width <= 2 || rect.Height <= 2)
            {
                path.AddRectangle(rect);
                return path;
            }

            int diameter = radius * 2;
            if (diameter > rect.Width)
            {
                diameter = rect.Width;
            }

            if (diameter > rect.Height)
            {
                diameter = rect.Height;
            }

            Rectangle arc = new Rectangle(rect.X, rect.Y, diameter, diameter);
            path.AddArc(arc, 180, 90);
            arc.X = rect.Right - diameter;
            path.AddArc(arc, 270, 90);
            arc.Y = rect.Bottom - diameter;
            path.AddArc(arc, 0, 90);
            arc.X = rect.X;
            path.AddArc(arc, 90, 90);
            path.CloseFigure();
            return path;
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

        private static Icon GetTrayIcon()
        {
            try
            {
                Icon exeIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (exeIcon != null)
                {
                    return (Icon)exeIcon.Clone();
                }
            }
            catch
            {
            }

            return SystemIcons.Application;
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

            return LaunchableExtensions.Contains(ext);
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

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport("Shell32.dll", CharSet = CharSet.Auto)]
        private static extern uint ExtractIconEx(string szFileName, int nIconIndex, out IntPtr phiconLarge, out IntPtr phiconSmall, uint nIcons);

        private const uint SHGFI_ICON = 0x100;
        private const uint SHGFI_LARGEICON = 0x0;
        private const uint SHGFI_ICONLOCATION = 0x1000;
        private const int LVM_FIRST = 0x1000;
        private const int LVM_SETICONSPACING = LVM_FIRST + 53;
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

