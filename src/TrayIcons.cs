using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace ZapretControl
{
    // Two tray variants: the application icon with a coloured state dot.
    // Drawn once at startup, because NotifyIcon keeps the Icon object and a
    // per-tick GetHicon would leak GDI handles.
    internal static class TrayIcons
    {
        private const int Size = 32;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr handle);

        internal static Icon Create(Icon source, Color badge)
        {
            using (Bitmap bitmap = new Bitmap(Size, Size))
            {
                using (Graphics graphics = Graphics.FromImage(bitmap))
                {
                    graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    graphics.Clear(Color.Transparent);
                    if (source != null)
                    {
                        try
                        {
                            graphics.DrawIcon(
                                source, new Rectangle(0, 0, Size, Size));
                        }
                        catch (Exception)
                        {
                            // A corrupt/oversized frame must not remove the
                            // whole icon: the badge dot below still works.
                        }
                    }
                    using (SolidBrush fill = new SolidBrush(badge))
                    using (Pen border = new Pen(Color.White, 2f))
                    {
                        Rectangle dot = new Rectangle(18, 18, 13, 13);
                        graphics.FillEllipse(fill, dot);
                        graphics.DrawEllipse(border, dot);
                    }
                }
                IntPtr handle = bitmap.GetHicon();
                try
                {
                    using (Icon temporary = Icon.FromHandle(handle))
                    {
                        return (Icon)temporary.Clone();
                    }
                }
                finally
                {
                    DestroyIcon(handle);
                }
            }
        }
    }
}
