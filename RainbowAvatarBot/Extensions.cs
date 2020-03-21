using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Imazen.WebP;

namespace RainbowAvatarBot {
	internal static class Extensions {
		internal static void Overlay(this Image sourceImage, Image overlayImage) {
			Bitmap resized = ResizeImage(overlayImage, sourceImage.Width, sourceImage.Height);
			try {
				resized.SetResolution(sourceImage.HorizontalResolution, sourceImage.VerticalResolution);
			} catch (ArgumentException) {
				// ignored
			}

			sourceImage.OverlayHardLight(resized);
		}

		private static void OverlayHardLight(this Image sourceImage, Image overlayImage) {
			Bitmap sourceBitmap = (Bitmap) sourceImage;
			Bitmap overlayBitmap = (Bitmap) overlayImage;
			Bitmap newOverlayBitmap = new Bitmap(sourceBitmap.Width, sourceBitmap.Height);

			Rectangle rect = new Rectangle(0, 0, sourceBitmap.Width, sourceBitmap.Height);
			BitmapData sourceData = sourceBitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
			BitmapData overlayData = overlayBitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
			BitmapData newOverlayData = newOverlayBitmap.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);

			int sourceBytes = Math.Abs(sourceData.Stride) * sourceData.Height;
			byte[] sourceValues = new byte[sourceBytes];
			int overlayBytes = Math.Abs(overlayData.Stride) * overlayBitmap.Height;
			byte[] overlayValues = new byte[overlayBytes];
			int newOverlayBytes = Math.Abs(newOverlayData.Stride) * newOverlayBitmap.Height;
			byte[] newOverlayValues = new byte[newOverlayBytes];

			Marshal.Copy(sourceData.Scan0, sourceValues, 0, sourceBytes);
			Marshal.Copy(overlayData.Scan0, overlayValues, 0, overlayBytes);
			Marshal.Copy(newOverlayData.Scan0, newOverlayValues, 0, newOverlayBytes);
			// R channel
			for (int i = 0; i < newOverlayValues.Length; i += 4) {
				newOverlayValues[i] = ProcessHardLight(sourceValues[i], overlayValues[i]);
			}

			// G channel
			for (int i = 1; i < newOverlayValues.Length; i += 4) {
				newOverlayValues[i] = ProcessHardLight(sourceValues[i], overlayValues[i]);
			}

			// B channel
			for (int i = 2; i < newOverlayValues.Length; i += 4) {
				newOverlayValues[i] = ProcessHardLight(sourceValues[i], overlayValues[i]);
			}

			// A channel
			for (int i = 3; i < newOverlayValues.Length; i += 4) {
				newOverlayValues[i] = sourceValues[i];
			}

			Marshal.Copy(newOverlayValues, 0, newOverlayData.Scan0, newOverlayBytes);
			sourceBitmap.UnlockBits(sourceData);
			overlayBitmap.UnlockBits(overlayData);
			newOverlayBitmap.UnlockBits(newOverlayData);

			if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
				sourceBitmap.SetResolution(newOverlayBitmap.HorizontalResolution, newOverlayBitmap.VerticalResolution);
			}

			using Graphics graphics = Graphics.FromImage(sourceBitmap);
			graphics.DrawImage(newOverlayBitmap.SetOpacity(0.5f), 0, 0);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
		private static byte ProcessHardLight(byte source, byte overlay) => (byte) (overlay < 128 ? 2 * source * overlay / 255 : 255 - 2 * (255 - source) * (255 - overlay) / 255);

		private static Bitmap ResizeImage(Image image, int width, int height) {
			Bitmap destImage = new Bitmap(width, height);

			destImage.SetResolution(image.HorizontalResolution, image.VerticalResolution);

			using Graphics graphics = Graphics.FromImage(destImage);
			graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
			graphics.SmoothingMode = SmoothingMode.HighQuality;
			graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

			graphics.DrawImage(image, 0, 0, width, height);

			return destImage;
		}

		internal static MemoryStream SaveToPng(this Image image) {
			MemoryStream stream = new MemoryStream();
			image.Save(stream, ImageFormat.Png);
			stream.Position = 0;
			return stream;
		}

		internal static MemoryStream SaveToWebp(this Image image) {
			MemoryStream stream = new MemoryStream();
			SimpleEncoder encoder = new SimpleEncoder();
			encoder.Encode((Bitmap) image, stream, 1);
			stream.Position = 0;
			return stream;
		}

		private static Image SetOpacity(this Image image, float opacity) {
			ColorMatrix colorMatrix = new ColorMatrix {
				Matrix33 = opacity
			};
			ImageAttributes imageAttributes = new ImageAttributes();
			imageAttributes.SetColorMatrix(
				colorMatrix,
				ColorMatrixFlag.Default,
				ColorAdjustType.Bitmap);
			Bitmap output = new Bitmap(image.Width, image.Height);
			using Graphics gfx = Graphics.FromImage(output);
			gfx.SmoothingMode = SmoothingMode.AntiAlias;
			gfx.DrawImage(image, new Rectangle(0, 0, image.Width, image.Height), 0, 0, image.Width, image.Height, GraphicsUnit.Pixel, imageAttributes);

			return output;
		}
	}
}
