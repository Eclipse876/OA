using UnityEngine;

namespace ProtoShape2D.Editor.Helpers
{
	public static class ImageHelper
	{
		public static Texture2D CropUsingColor(Texture2D image, Color cropColor)
		{
			var cropRect = new[] { 0, 0, image.width, image.height };
			var cropSet = new[] { false, false, false, false };
			for (var x = 0; x < image.width; x++)
			{
				for (var y = 0; y < image.height; y++)
				{
					if (image.GetPixel(x, y) == cropColor)
					{
						continue;
					}
					if (!cropSet[0] || x < cropRect[0])
					{
						cropRect[0] = x;
						cropSet[0] = true;
					}
					if (!cropSet[1] || y < cropRect[1])
					{
						cropRect[1] = y;
						cropSet[1] = true;
					}
					if (cropSet[0] && (!cropSet[2] || x + 1 > cropRect[2]))
					{
						cropRect[2] = x + 1;
						cropSet[2] = true;
					}
					if (cropSet[1] && (!cropSet[3] || y + 1 > cropRect[3]))
					{
						cropRect[3] = y + 1;
						cropSet[3] = true;
					}
				}
			}
			var cropImage = new Texture2D(cropRect[2] - cropRect[0], cropRect[3] - cropRect[1], TextureFormat.RGBA32, false);
			cropImage.SetPixels(image.GetPixels(cropRect[0], cropRect[1], cropRect[2] - cropRect[0], cropRect[3] - cropRect[1]));
			return cropImage;
		}
	}
}