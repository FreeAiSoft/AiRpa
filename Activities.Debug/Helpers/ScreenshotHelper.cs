using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace Activities.Debug.Helpers
{
    internal class ScreenshotHelper : IDisposable
    {
        private static readonly Dictionary<string, ImageFormat> formats = new Dictionary<string, ImageFormat>
        {
            {"bmp", ImageFormat.Bmp },
            {"emf", ImageFormat.Emf },
            {"exif", ImageFormat.Exif },
            {"gif", ImageFormat.Gif },
            {"icon", ImageFormat.Icon },
            {"jpg", ImageFormat.Jpeg },
            {"jpeg", ImageFormat.Jpeg },
            {"png", ImageFormat.Png },
            {"tiff", ImageFormat.Tiff },
            {"wmf", ImageFormat.Wmf },
        };

        private readonly Bitmap bitmap;

        public ScreenshotHelper() : this(Screen.GetBounds(Point.Empty)) { }

        public ScreenshotHelper(Rectangle rect)
        {
            bitmap = new Bitmap(rect.Width, rect.Height, PixelFormat.Format32bppArgb);
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.CopyFromScreen(rect.Left, rect.Top, 0, 0, bitmap.Size, CopyPixelOperation.SourceCopy);
            }
        }

        public void Save(string fullFilePath)
        {
            if (string.IsNullOrEmpty(fullFilePath))
            {
                throw new ArgumentException($"empty {fullFilePath}");
            }

            string extension = Path.GetExtension(fullFilePath).TrimStart('.').ToLower();

            if (!formats.ContainsKey(extension))
            {
                throw new ArgumentException($"bad file format {extension}");
            }

            bitmap.Save(fullFilePath, formats[extension]);
        }

        public void Dispose()
        {
            bitmap?.Dispose();
        }
    }
}
