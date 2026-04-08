using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace XpSidebarLauncher
{
    internal sealed class BookcaseItem
    {
        public string Title;
        public string FilePath;
        public Image SpineImage;
        public Image FrontImage;
        public float HoverProgress;
    }

    internal sealed class BookcaseItemEventArgs : EventArgs
    {
        public readonly BookcaseItem Item;

        public BookcaseItemEventArgs(BookcaseItem item)
        {
            Item = item;
        }
    }

    internal sealed class BookcaseView : Control
    {
        private readonly List<BookcaseItem> items;
        private readonly Timer animationTimer;
        private int hoveredIndex;
        private static readonly Color ShelfWoodLight = Color.FromArgb(210, 185, 149);
        private static readonly Color ShelfWoodDark = Color.FromArgb(150, 118, 82);
        private static readonly Color CaseEdge = Color.FromArgb(210, 236, 236, 238);
        private static readonly Color CaseShadow = Color.FromArgb(80, 0, 0, 0);

        public event EventHandler<BookcaseItemEventArgs> ItemActivated;
        public event MouseEventHandler ItemRightClick;

        public BookcaseView()
        {
            items = new List<BookcaseItem>();
            hoveredIndex = -1;

            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Color.FromArgb(228, 216, 198);

            animationTimer = new Timer();
            animationTimer.Interval = 30;
            animationTimer.Tick += AnimationTimer_Tick;
            animationTimer.Start();

            MouseMove += BookcaseView_MouseMove;
            MouseLeave += BookcaseView_MouseLeave;
            MouseDoubleClick += BookcaseView_MouseDoubleClick;
            MouseDown += BookcaseView_MouseDown;
        }

        public void SetItems(List<BookcaseItem> source)
        {
            items.Clear();
            if (source != null)
            {
                items.AddRange(source);
            }

            hoveredIndex = -1;
            Invalidate();
        }

        public BookcaseItem GetItemAt(Point point)
        {
            int index = HitTestIndex(point);
            if (index < 0 || index >= items.Count)
            {
                return null;
            }

            return items[index];
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                animationTimer.Stop();
                animationTimer.Dispose();
            }

            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            DrawBookcaseBackground(e.Graphics);

            if (items.Count == 0)
            {
                return;
            }

            int padding = 12;
            int availableWidth = Math.Max(1, Width - (padding * 2));
            int availableHeight = Math.Max(1, Height - (padding * 2));
            int spineWidth = 20;
            int bookHeight = Math.Max(84, Math.Min(140, availableHeight / 2));
            int rowHeight = bookHeight + 28;
            int columns = Math.Max(1, availableWidth / (spineWidth + 6));

            for (int i = 0; i < items.Count; i++)
            {
                int col = i % columns;
                int row = i / columns;
                int x = padding + (col * (spineWidth + 6));
                int y = padding + 18 + (row * rowHeight);
                Rectangle spineRect = new Rectangle(x, y, spineWidth, bookHeight);
                DrawBookItem(e.Graphics, items[i], spineRect, i == hoveredIndex);
            }
        }

        private void DrawBookcaseBackground(Graphics g)
        {
            Rectangle rect = ClientRectangle;
            using (var bgBrush = new LinearGradientBrush(rect, ShelfWoodLight, ShelfWoodDark, 90f))
            {
                g.FillRectangle(bgBrush, rect);
            }

            using (var grain = new HatchBrush(HatchStyle.Percent10, Color.FromArgb(54, 96, 68, 41), Color.Transparent))
            {
                g.FillRectangle(grain, rect);
            }

            using (var shelfPen = new Pen(Color.FromArgb(92, 108, 82, 52), 3f))
            {
                for (int y = 38; y < Height; y += 170)
                {
                    g.DrawLine(shelfPen, 6, y, Width - 6, y);
                    using (var topShelfGlow = new Pen(Color.FromArgb(80, 248, 228, 205), 1f))
                    {
                        g.DrawLine(topShelfGlow, 6, y - 2, Width - 6, y - 2);
                    }
                }
            }
        }

        private static void DrawBookItem(Graphics g, BookcaseItem item, Rectangle spineRect, bool hovered)
        {
            float p = item.HoverProgress;
            int push = (int)(44f * p);
            int lift = (int)(12f * p);
            float zoom = 1f + (0.12f * p);
            Rectangle movedSpine = new Rectangle(spineRect.X + push, spineRect.Y - lift, spineRect.Width, spineRect.Height);
            movedSpine = ScaleFromBottomLeft(movedSpine, zoom);

            using (var shadow = new SolidBrush(Color.FromArgb((int)(90f * p), 0, 0, 0)))
            {
                g.FillRectangle(shadow, movedSpine.X + 4, movedSpine.Y + 3, movedSpine.Width + 2, movedSpine.Height + 2);
            }

            DrawImageCover(g, item.SpineImage, movedSpine, Color.FromArgb(220, 64, 64, 64));
            DrawSpineGlass(g, movedSpine);

            using (var spinePen = new Pen(Color.FromArgb(164, 36, 36, 36)))
            {
                g.DrawRectangle(spinePen, movedSpine);
            }

            if (!hovered && p < 0.01f)
            {
                return;
            }

            int frontW = (int)(84 + (8 * p));
            int frontTilt = (int)(12 + (8 * p));
            Point p1 = new Point(movedSpine.Right - 1, movedSpine.Top + 1);
            Point p2 = new Point(movedSpine.Right + frontW, movedSpine.Top - frontTilt);
            Point p3 = new Point(movedSpine.Right + frontW, movedSpine.Bottom - frontTilt);
            Point[] destPoints = new[] { p1, p2, p3 };
            Point p4 = new Point(p1.X, movedSpine.Bottom);

            using (var frontShadow = new SolidBrush(Color.FromArgb((int)(80f * p), 0, 0, 0)))
            {
                g.FillPolygon(frontShadow, new[]
                {
                    new Point(p1.X + 4, p1.Y + 4),
                    new Point(p2.X + 4, p2.Y + 4),
                    new Point(p3.X + 4, p3.Y + 4),
                    new Point(p4.X + 4, p4.Y + 4)
                });
            }

            if (item.FrontImage != null)
            {
                g.DrawImage(item.FrontImage, destPoints);
            }
            else
            {
                using (var b = new SolidBrush(Color.FromArgb(212, 224, 224, 224)))
                {
                    g.FillPolygon(b, new[]
                    {
                        p1,
                        p2,
                        p3,
                        new Point(p1.X, movedSpine.Bottom)
                    });
                }
            }

            using (var frontPen = new Pen(Color.FromArgb((int)(180f * p), 44, 44, 44), 1.3f))
            {
                g.DrawLine(frontPen, p1, p2);
                g.DrawLine(frontPen, p2, p3);
                g.DrawLine(frontPen, p3, p4);
                g.DrawLine(frontPen, p4, p1);
            }

            DrawFrontPlasticOverlay(g, p1, p2, p3, p4, p);
            DrawCaseThickness(g, movedSpine, p2, p3, p);
        }

        private static void DrawSpineGlass(Graphics g, Rectangle bounds)
        {
            Rectangle topHalf = new Rectangle(bounds.X + 1, bounds.Y + 1, Math.Max(1, bounds.Width - 2), Math.Max(1, (bounds.Height / 3)));
            using (var gloss = new LinearGradientBrush(topHalf, Color.FromArgb(84, 255, 255, 255), Color.FromArgb(18, 255, 255, 255), 90f))
            {
                g.FillRectangle(gloss, topHalf);
            }

            using (var edgePen = new Pen(CaseEdge, 1f))
            {
                g.DrawLine(edgePen, bounds.X + 1, bounds.Y + 1, bounds.X + 1, bounds.Bottom - 2);
            }
        }

        private static void DrawFrontPlasticOverlay(Graphics g, Point p1, Point p2, Point p3, Point p4, float progress)
        {
            Point[] poly = new[] { p1, p2, p3, p4 };
            using (var faceGlow = new SolidBrush(Color.FromArgb((int)(10f + (16f * progress)), 245, 245, 255)))
            {
                g.FillPolygon(faceGlow, poly);
            }

            Point r1 = LerpPoint(p1, p2, 0.06f);
            Point r2 = LerpPoint(p1, p2, 0.26f);
            Point r3 = LerpPoint(p4, p3, 0.25f);
            Point r4 = LerpPoint(p4, p3, 0.05f);
            using (var reflection = new SolidBrush(Color.FromArgb((int)(24f * progress), 255, 255, 255)))
            {
                g.FillPolygon(reflection, new[] { r1, r2, r3, r4 });
            }
        }

        private static void DrawCaseThickness(Graphics g, Rectangle spineRect, Point p2, Point p3, float progress)
        {
            Point b1 = new Point(spineRect.Right, spineRect.Top);
            Point b2 = p2;
            Point b3 = p3;
            Point b4 = new Point(spineRect.Right, spineRect.Bottom - 1);
            using (var sideBrush = new LinearGradientBrush(spineRect, Color.FromArgb((int)(72f + (36f * progress)), 210, 210, 218), Color.FromArgb((int)(36f + (24f * progress)), 162, 162, 176), 90f))
            {
                g.FillPolygon(sideBrush, new[] { b1, b2, b3, b4 });
            }

            using (var sidePen = new Pen(Color.FromArgb((int)(120f + (70f * progress)), 98, 98, 108)))
            {
                g.DrawLine(sidePen, b1, b2);
                g.DrawLine(sidePen, b2, b3);
                g.DrawLine(sidePen, b3, b4);
            }
        }

        private static void DrawImageCover(Graphics g, Image image, Rectangle bounds, Color fallback)
        {
            if (image != null)
            {
                g.DrawImage(image, bounds);
                return;
            }

            using (var b = new SolidBrush(fallback))
            {
                g.FillRectangle(b, bounds);
            }
        }

        private void AnimationTimer_Tick(object sender, EventArgs e)
        {
            bool changed = false;
            for (int i = 0; i < items.Count; i++)
            {
                float target = i == hoveredIndex ? 1f : 0f;
                float current = items[i].HoverProgress;
                float next = EaseTowards(current, target, 0.22f, 0.03f);
                if (Math.Abs(next - current) > 0.001f)
                {
                    items[i].HoverProgress = next;
                    changed = true;
                }
            }

            if (changed)
            {
                Invalidate();
            }
        }

        private void BookcaseView_MouseMove(object sender, MouseEventArgs e)
        {
            int next = HitTestIndex(e.Location);
            if (next != hoveredIndex)
            {
                hoveredIndex = next;
                Invalidate();
            }
        }

        private void BookcaseView_MouseLeave(object sender, EventArgs e)
        {
            if (hoveredIndex != -1)
            {
                hoveredIndex = -1;
                Invalidate();
            }
        }

        private void BookcaseView_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            int index = HitTestIndex(e.Location);
            if (index < 0 || index >= items.Count)
            {
                return;
            }

            EventHandler<BookcaseItemEventArgs> handler = ItemActivated;
            if (handler != null)
            {
                handler(this, new BookcaseItemEventArgs(items[index]));
            }
        }

        private void BookcaseView_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right)
            {
                return;
            }

            MouseEventHandler rightHandler = ItemRightClick;
            if (rightHandler != null)
            {
                rightHandler(this, e);
            }
        }

        private int HitTestIndex(Point point)
        {
            int padding = 12;
            int availableWidth = Math.Max(1, Width - (padding * 2));
            int availableHeight = Math.Max(1, Height - (padding * 2));
            int spineWidth = 20;
            int bookHeight = Math.Max(84, Math.Min(140, availableHeight / 2));
            int rowHeight = bookHeight + 28;
            int columns = Math.Max(1, availableWidth / (spineWidth + 6));

            for (int i = 0; i < items.Count; i++)
            {
                int col = i % columns;
                int row = i / columns;
                int x = padding + (col * (spineWidth + 6));
                int y = padding + 18 + (row * rowHeight);
                Rectangle zone = new Rectangle(x, y - 12, spineWidth + 90, bookHeight + 18);
                if (zone.Contains(point))
                {
                    return i;
                }
            }

            return -1;
        }

        private static float EaseTowards(float current, float target, float factor, float minStep)
        {
            if (current < target)
            {
                current += Math.Max(minStep, (target - current) * factor);
                if (current > target)
                {
                    current = target;
                }
            }
            else if (current > target)
            {
                current -= Math.Max(minStep, (current - target) * factor);
                if (current < target)
                {
                    current = target;
                }
            }

            return current;
        }

        private static Point LerpPoint(Point a, Point b, float t)
        {
            int x = (int)(a.X + ((b.X - a.X) * t));
            int y = (int)(a.Y + ((b.Y - a.Y) * t));
            return new Point(x, y);
        }

        private static Rectangle ScaleFromBottomLeft(Rectangle rect, float scale)
        {
            if (scale <= 1f)
            {
                return rect;
            }

            int newW = (int)(rect.Width * scale);
            int newH = (int)(rect.Height * scale);
            return new Rectangle(
                rect.X,
                rect.Bottom - newH,
                newW,
                newH);
        }
    }
}
