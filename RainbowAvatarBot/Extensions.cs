using System.IO;
#if SYSTEMDRAWING
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
#else
using System.Diagnostics.CodeAnalysis;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Bmp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Processing.Processors.Transforms;
using SixLabors.Primitives;
#endif

namespace RainbowAvatarBot {
	internal static class Extensions {
	#if SYSTEMDRAWING
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

		private static void OverlayHardLight(this Image sourceImage, Image overlayImage) {
			static float ProcessHardLight(float source, float overlay) => overlay < 0.5 ? 2 * source * overlay : 1 - 2 * (1 - source) * (1 - overlay);
			Bitmap sourceBitmap = (Bitmap) sourceImage;
			Bitmap overlayBitmap = (Bitmap) overlayImage;
			Bitmap newOverlayBitmap = new Bitmap(sourceBitmap.Width, sourceBitmap.Height);

			Rectangle rect = new Rectangle(0, 0, sourceBitmap.Width, sourceBitmap.Height);
			BitmapData sourceData = sourceBitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
			BitmapData overlayData = overlayBitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
			BitmapData newOverlayData = newOverlayBitmap.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);

			int sourceBytes = Math.Abs(sourceData.Stride) * sourceData.Height;
			byte[] sourceValues = new byte[sourceBytes];
			int overlayBytes = Math.Abs(overlayData.Stride) * overlayBitmap.Height;
			byte[] overlayValues = new byte[overlayBytes];
			int newOverlayBytes = Math.Abs(newOverlayData.Stride) * newOverlayBitmap.Height;
			byte[] newOverlayValues = new byte[newOverlayBytes];

			Marshal.Copy(sourceData.Scan0, sourceValues, 0, sourceBytes);
			Marshal.Copy(overlayData.Scan0, overlayValues, 0, overlayBytes);
			Marshal.Copy(newOverlayData.Scan0, newOverlayValues, 0, newOverlayBytes);
			for (int i = 0; i < newOverlayValues.Length; i++) {
				newOverlayValues[i] = (byte) (255 * ProcessHardLight(sourceValues[i] / 255.0f, overlayValues[i] / 255.0f));
			}

			Marshal.Copy(newOverlayValues, 0, newOverlayData.Scan0, newOverlayBytes);
			sourceBitmap.UnlockBits(sourceData);
			overlayBitmap.UnlockBits(overlayData);
			newOverlayBitmap.UnlockBits(newOverlayData);

			if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
				sourceBitmap.SetResolution(newOverlayBitmap.HorizontalResolution, newOverlayBitmap.VerticalResolution);
			}

			using Graphics graphics = Graphics.FromImage(sourceImage);
			graphics.DrawImage(newOverlayBitmap.SetOpacity(0.5f), 0, 0);
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

		internal static void Overlay(this Image sourceImage, Image overlayImage) {
			Bitmap resized = ResizeImage(overlayImage, sourceImage.Width, sourceImage.Height);
			try {
				resized.SetResolution(sourceImage.HorizontalResolution, sourceImage.VerticalResolution);
			} catch (ArgumentException) {
				// ignored
			}

			sourceImage.OverlayHardLight(resized);
		}

		internal static MemoryStream SaveToBmp(this Image image) {
			MemoryStream stream = new MemoryStream();
			image.Save(stream, ImageFormat.Bmp);
			stream.Position = 0;
			return stream;
		}
	#else
		[SuppressMessage("ReSharper", "AccessToDisposedClosure")]
		[SuppressMessage("ReSharper", "ImplicitlyCapturedClosure")]
		internal static void Overlay(this Image<Rgba32> sourceImage, Image<Rgba32> overlayImage) {
			using Image<Rgba32> resized = overlayImage.Clone(img => img.Resize(sourceImage.Width, sourceImage.Height, new NearestNeighborResampler()));
			sourceImage.Mutate(img => img.DrawImage(resized, Point.Empty, PixelColorBlendingMode.HardLight, 0.5f));
		}

		internal static MemoryStream SaveToBmp(this Image<Rgba32> image) {
			MemoryStream stream = new MemoryStream();
			image.Save(stream, new BmpEncoder());
			stream.Position = 0;
			return stream;
		}
	#endif
	}
}
