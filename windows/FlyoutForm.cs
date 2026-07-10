using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace BitBar
{
    public class FlyoutForm : Form
    {
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        private const int DWMWCP_ROUNDSMALL = 3; // Smaller rounded corners like native tooltips

        private Queue<long> downHistory = new Queue<long>();
        private Queue<long> upHistory = new Queue<long>();
        
        private long currentDown = 0;
        private long currentUp = 0;
        private string topConsumer = "Calculating...";
        private bool isLightTheme = false;

        private System.Windows.Forms.Timer animTimer;
        private int startY, endY;
        private float currentOpacity;

        public FlyoutForm()
        {
            this.FormBorderStyle = FormBorderStyle.None;
            this.ShowInTaskbar = false;
            this.TopMost = true;
            this.Size = new Size(250, 160);
            this.DoubleBuffered = true;
            this.StartPosition = FormStartPosition.Manual;

            animTimer = new System.Windows.Forms.Timer();
            animTimer.Interval = 15;
            animTimer.Tick += AnimTimer_Tick;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                // Add Drop Shadow
                cp.ClassStyle |= 0x00020000; // CS_DROPSHADOW
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int preference = DWMWCP_ROUNDSMALL;
            DwmSetWindowAttribute(this.Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int));
        }

        public void UpdateData(Queue<long> down, Queue<long> up, long curDown, long curUp, string consumer, bool lightTheme)
        {
            downHistory = new Queue<long>(down);
            upHistory = new Queue<long>(up);
            currentDown = curDown;
            currentUp = curUp;
            topConsumer = consumer;
            isLightTheme = lightTheme;

            // Use a slightly lighter dark-grey to match Win11 native flyouts better
            this.BackColor = isLightTheme ? Color.FromArgb(240, 240, 240) : Color.FromArgb(43, 43, 43);
            this.Invalidate(); // Trigger repaint
        }

        public void ShowAnimated(int targetX, int targetY)
        {
            if (this.Visible && this.Opacity >= 0.99) return;
            
            endY = targetY;
            startY = targetY + 15; // slide up 15px
            
            this.Opacity = 0;
            this.Location = new Point(targetX, startY);
            currentOpacity = 0;
            
            if (!this.Visible) this.Show();
            
            animTimer.Start();
        }

        private void AnimTimer_Tick(object sender, EventArgs e)
        {
            currentOpacity += 0.15f;
            if (currentOpacity >= 1.0f)
            {
                currentOpacity = 1.0f;
                this.Opacity = 1.0;
                this.Location = new Point(this.Location.X, endY);
                animTimer.Stop();
            }
            else
            {
                this.Opacity = currentOpacity;
                int newY = startY - (int)((startY - endY) * currentOpacity);
                this.Location = new Point(this.Location.X, newY);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Color textColor = isLightTheme ? Color.Black : Color.White;
            Color subTextColor = isLightTheme ? Color.FromArgb(100, 100, 100) : Color.FromArgb(180, 180, 180);
            Color graphBg = isLightTheme ? Color.FromArgb(220, 220, 220) : Color.FromArgb(30, 30, 30);

            // Draw Win11 native 1px border
            Color borderColor = isLightTheme ? Color.FromArgb(200, 200, 200) : Color.FromArgb(75, 75, 75);
            using (Pen borderPen = new Pen(borderColor, 1))
            {
                Rectangle borderRect = new Rectangle(0, 0, this.Width - 1, this.Height - 1);
                g.DrawRoundedRectangle(borderPen, borderRect, 4);
            }

            using (Font labelFont = new Font("Segoe UI Variable Text", 9, FontStyle.Regular))
            using (Font valueFont = new Font("Segoe UI Variable Text", 9, FontStyle.Bold))
            using (SolidBrush textBrush = new SolidBrush(textColor))
            using (SolidBrush subTextBrush = new SolidBrush(subTextColor))
            using (SolidBrush greenBrush = new SolidBrush(Color.FromArgb(52, 199, 89))) // Win11/iOS Green
            using (SolidBrush redBrush = new SolidBrush(Color.FromArgb(255, 59, 48)))   // Win11/iOS Red
            using (Pen sepPen = new Pen(isLightTheme ? Color.FromArgb(225, 225, 225) : Color.FromArgb(55, 55, 55), 1))
            using (StringFormat rightAlign = new StringFormat { Alignment = StringAlignment.Far })
            {
                int startX = 12;
                int endX = this.Width - 12;

                // Row 1: Consumer
                g.DrawString("Consumer", labelFont, subTextBrush, startX, 8);
                g.DrawString(topConsumer, valueFont, textBrush, endX, 8, rightAlign);

                // Separator 1
                g.DrawLine(sepPen, startX, 26, endX, 26);

                // Row 2: Download
                g.DrawString("Download", labelFont, subTextBrush, startX, 29);
                g.DrawString(FormatSpeed(currentDown), valueFont, greenBrush, endX, 29, rightAlign);

                // Separator 2
                g.DrawLine(sepPen, startX, 47, endX, 47);

                // Row 3: Upload
                g.DrawString("Upload", labelFont, subTextBrush, startX, 50);
                g.DrawString(FormatSpeed(currentUp), valueFont, redBrush, endX, 50, rightAlign);

                // Separator 3
                g.DrawLine(sepPen, startX, 68, endX, 68);

                // Draw Graph Box
                Rectangle graphRect = new Rectangle(10, 78, this.Width - 20, 72);
                using (SolidBrush bgBrush = new SolidBrush(graphBg))
                {
                    g.FillRoundedRectangle(bgBrush, graphRect, 5);
                }

                DrawGraph(g, graphRect, downHistory.ToList(), Color.FromArgb(52, 199, 89));
                DrawGraph(g, graphRect, upHistory.ToList(), Color.FromArgb(255, 59, 48));
            }
        }

        private void DrawGraph(Graphics g, Rectangle rect, List<long> data, Color color)
        {
            if (data.Count < 2) return;

            long maxVal = data.Max();
            if (maxVal == 0) maxVal = 1; // Prevent div by zero

            float stepX = (float)rect.Width / (data.Count - 1);
            PointF[] points = new PointF[data.Count];

            for (int i = 0; i < data.Count; i++)
            {
                float x = rect.X + (i * stepX);
                // Invert Y so 0 is at bottom
                float y = rect.Bottom - 5 - (((float)data[i] / maxVal) * (rect.Height - 10)); 
                points[i] = new PointF(x, y);
            }

            // Draw Gradient Fill
            using (GraphicsPath path = new GraphicsPath())
            {
                path.AddLines(points);
                path.AddLine(points.Last().X, points.Last().Y, points.Last().X, rect.Bottom);
                path.AddLine(points.Last().X, rect.Bottom, points.First().X, rect.Bottom);
                path.AddLine(points.First().X, rect.Bottom, points.First().X, points.First().Y);
                path.CloseFigure();

                Color gradTop = Color.FromArgb(100, color);
                Color gradBot = Color.FromArgb(0, color);
                
                using (LinearGradientBrush brush = new LinearGradientBrush(rect, gradTop, gradBot, LinearGradientMode.Vertical))
                {
                    g.FillPath(brush, path);
                }
            }

            using (Pen pen = new Pen(color, 2f))
            {
                pen.LineJoin = LineJoin.Round;
                g.DrawLines(pen, points);
            }
        }

        private string FormatSpeed(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B/s";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB/s";
            return $"{bytes / (1024.0 * 1024.0):F1} MB/s";
        }
    }

    public static class GraphicsExtensions
    {
        public static void FillRoundedRectangle(this Graphics g, Brush brush, Rectangle rect, int radius)
        {
            using (GraphicsPath path = new GraphicsPath())
            {
                path.AddArc(rect.X, rect.Y, radius * 2, radius * 2, 180, 90);
                path.AddArc(rect.Right - radius * 2, rect.Y, radius * 2, radius * 2, 270, 90);
                path.AddArc(rect.Right - radius * 2, rect.Bottom - radius * 2, radius * 2, radius * 2, 0, 90);
                path.AddArc(rect.X, rect.Bottom - radius * 2, radius * 2, radius * 2, 90, 90);
                path.CloseFigure();
                g.FillPath(brush, path);
            }
        }

        public static void DrawRoundedRectangle(this Graphics g, Pen pen, Rectangle rect, int radius)
        {
            using (GraphicsPath path = new GraphicsPath())
            {
                path.AddArc(rect.X, rect.Y, radius * 2, radius * 2, 180, 90);
                path.AddArc(rect.Right - radius * 2, rect.Y, radius * 2, radius * 2, 270, 90);
                path.AddArc(rect.Right - radius * 2, rect.Bottom - radius * 2, radius * 2, radius * 2, 0, 90);
                path.AddArc(rect.X, rect.Bottom - radius * 2, radius * 2, radius * 2, 90, 90);
                path.CloseFigure();
                g.DrawPath(pen, path);
            }
        }
    }
}
