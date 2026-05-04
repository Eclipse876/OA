using UnityEngine;

namespace ProtoShape2D.Editor.Extensions
{
	public static class ColorHelper
	{
		public static Color RandomPleasantColor()
		{
			var hue = UnityEngine.Random.Range(0f, 1f);
			while (hue * 360f >= 236f && hue * 360f <= 246f)
			{
				hue = UnityEngine.Random.Range(0f, 1f);
			}
			return Color.HSVToRGB(hue, UnityEngine.Random.Range(0.2f, 0.7f), UnityEngine.Random.Range(0.8f, 1f));
		}
	}
}