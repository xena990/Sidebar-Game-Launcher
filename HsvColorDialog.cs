using System;
using System.Drawing;
using System.Windows.Forms;

namespace XpSidebarLauncher
{
    internal sealed class HsvColorDialog : Form
    {
        private readonly TrackBar hueTrack;
        private readonly TrackBar satTrack;
        private readonly TrackBar valTrack;
        private readonly NumericUpDown hueValue;
        private readonly NumericUpDown satValue;
        private readonly NumericUpDown valValue;
        private readonly Panel previewPanel;
        private bool isUpdating;

        public Color SelectedColor { get; private set; }

        public HsvColorDialog(Color initialColor)
        {
            Text = "Border Color (HSV)";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(370, 220);

            var table = new TableLayoutPanel();
            table.Dock = DockStyle.Fill;
            table.Padding = new Padding(10);
            table.ColumnCount = 3;
            table.RowCount = 5;
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 26));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 56));
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            table.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            table.Controls.Add(CreateLabel("H:"), 0, 0);
            hueTrack = CreateTrack(0, 360, 30);
            table.Controls.Add(hueTrack, 1, 0);
            hueValue = CreateNumber(0, 360);
            table.Controls.Add(hueValue, 2, 0);

            table.Controls.Add(CreateLabel("S:"), 0, 1);
            satTrack = CreateTrack(0, 100, 10);
            table.Controls.Add(satTrack, 1, 1);
            satValue = CreateNumber(0, 100);
            table.Controls.Add(satValue, 2, 1);

            table.Controls.Add(CreateLabel("V:"), 0, 2);
            valTrack = CreateTrack(0, 100, 10);
            table.Controls.Add(valTrack, 1, 2);
            valValue = CreateNumber(0, 100);
            table.Controls.Add(valValue, 2, 2);

            previewPanel = new Panel();
            previewPanel.Dock = DockStyle.Fill;
            previewPanel.Margin = new Padding(0, 6, 0, 0);
            table.Controls.Add(previewPanel, 1, 3);
            table.SetColumnSpan(previewPanel, 2);

            var buttonPanel = new FlowLayoutPanel();
            buttonPanel.FlowDirection = FlowDirection.RightToLeft;
            buttonPanel.Dock = DockStyle.Fill;

            var okButton = new Button();
            okButton.Text = "OK";
            okButton.DialogResult = DialogResult.OK;
            okButton.Width = 80;
            okButton.Click += OkButton_Click;
            buttonPanel.Controls.Add(okButton);

            var cancelButton = new Button();
            cancelButton.Text = "Cancel";
            cancelButton.DialogResult = DialogResult.Cancel;
            cancelButton.Width = 80;
            buttonPanel.Controls.Add(cancelButton);

            table.Controls.Add(buttonPanel, 0, 4);
            table.SetColumnSpan(buttonPanel, 3);

            Controls.Add(table);
            AcceptButton = okButton;
            CancelButton = cancelButton;

            hueTrack.Scroll += Track_Scroll;
            satTrack.Scroll += Track_Scroll;
            valTrack.Scroll += Track_Scroll;
            hueValue.ValueChanged += Numeric_ValueChanged;
            satValue.ValueChanged += Numeric_ValueChanged;
            valValue.ValueChanged += Numeric_ValueChanged;

            int h;
            int s;
            int v;
            RgbToHsv(initialColor, out h, out s, out v);
            SetAllValues(h, s, v);
            ApplyPreview();
        }

        private static Label CreateLabel(string text)
        {
            var label = new Label();
            label.Text = text;
            label.Dock = DockStyle.Fill;
            label.TextAlign = ContentAlignment.MiddleLeft;
            return label;
        }

        private static TrackBar CreateTrack(int min, int max, int tick)
        {
            var track = new TrackBar();
            track.Minimum = min;
            track.Maximum = max;
            track.TickFrequency = tick;
            track.Dock = DockStyle.Fill;
            return track;
        }

        private static NumericUpDown CreateNumber(int min, int max)
        {
            var num = new NumericUpDown();
            num.Minimum = min;
            num.Maximum = max;
            num.Dock = DockStyle.Fill;
            return num;
        }

        private void Track_Scroll(object sender, EventArgs e)
        {
            if (isUpdating)
            {
                return;
            }

            isUpdating = true;
            hueValue.Value = hueTrack.Value;
            satValue.Value = satTrack.Value;
            valValue.Value = valTrack.Value;
            isUpdating = false;
            ApplyPreview();
        }

        private void Numeric_ValueChanged(object sender, EventArgs e)
        {
            if (isUpdating)
            {
                return;
            }

            isUpdating = true;
            hueTrack.Value = (int)hueValue.Value;
            satTrack.Value = (int)satValue.Value;
            valTrack.Value = (int)valValue.Value;
            isUpdating = false;
            ApplyPreview();
        }

        private void OkButton_Click(object sender, EventArgs e)
        {
            SelectedColor = ColorFromHsv((int)hueValue.Value, (int)satValue.Value, (int)valValue.Value);
        }

        private void SetAllValues(int h, int s, int v)
        {
            isUpdating = true;
            hueTrack.Value = h;
            satTrack.Value = s;
            valTrack.Value = v;
            hueValue.Value = h;
            satValue.Value = s;
            valValue.Value = v;
            isUpdating = false;
        }

        private void ApplyPreview()
        {
            previewPanel.BackColor = ColorFromHsv((int)hueValue.Value, (int)satValue.Value, (int)valValue.Value);
        }

        private static Color ColorFromHsv(int hue, int saturation, int value)
        {
            double h = hue;
            double s = saturation / 100.0;
            double v = value / 100.0;

            if (s <= 0.0)
            {
                int gray = (int)Math.Round(v * 255.0);
                return Color.FromArgb(gray, gray, gray);
            }

            double hh = h / 60.0;
            int i = (int)Math.Floor(hh);
            double ff = hh - i;
            double p = v * (1.0 - s);
            double q = v * (1.0 - (s * ff));
            double t = v * (1.0 - (s * (1.0 - ff)));

            double r;
            double g;
            double b;

            switch (i)
            {
                case 0: r = v; g = t; b = p; break;
                case 1: r = q; g = v; b = p; break;
                case 2: r = p; g = v; b = t; break;
                case 3: r = p; g = q; b = v; break;
                case 4: r = t; g = p; b = v; break;
                default: r = v; g = p; b = q; break;
            }

            return Color.FromArgb(ClampByte((int)Math.Round(r * 255.0)), ClampByte((int)Math.Round(g * 255.0)), ClampByte((int)Math.Round(b * 255.0)));
        }

        private static void RgbToHsv(Color color, out int hue, out int saturation, out int value)
        {
            double r = color.R / 255.0;
            double g = color.G / 255.0;
            double b = color.B / 255.0;

            double max = Math.Max(r, Math.Max(g, b));
            double min = Math.Min(r, Math.Min(g, b));
            double delta = max - min;

            double h;
            if (delta < 0.00001)
            {
                h = 0.0;
            }
            else if (max == r)
            {
                h = 60.0 * (((g - b) / delta) % 6.0);
            }
            else if (max == g)
            {
                h = 60.0 * (((b - r) / delta) + 2.0);
            }
            else
            {
                h = 60.0 * (((r - g) / delta) + 4.0);
            }

            if (h < 0.0)
            {
                h += 360.0;
            }

            double s = max <= 0.0 ? 0.0 : delta / max;
            double v = max;

            hue = ClampRange((int)Math.Round(h), 0, 360);
            saturation = ClampRange((int)Math.Round(s * 100.0), 0, 100);
            value = ClampRange((int)Math.Round(v * 100.0), 0, 100);
        }

        private static int ClampRange(int value, int min, int max)
        {
            if (value < min)
            {
                return min;
            }

            if (value > max)
            {
                return max;
            }

            return value;
        }

        private static int ClampByte(int value)
        {
            return ClampRange(value, 0, 255);
        }
    }
}
