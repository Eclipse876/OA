using System;
using UnityEditor;
using UnityEngine;

namespace ProtoShape2D.Editor.Helpers
{
	public static class UiHelper
	{
		//Convert any enum to array of GUIContent
		public static GUIContent[] EnumToGUI<TK>()
		{
			if (typeof(TK).BaseType != typeof(Enum)) throw new InvalidCastException();
			var strings = Enum.GetNames(typeof(TK));
			var buttons = new GUIContent[strings.Length];
			for (var i = 0; i < buttons.Length; i++)
			{
				buttons[i] = new GUIContent(strings[i]);
			}
			return buttons;
		}

		//Convert any enum to array of GUIContent with images and tooltips, given a path to images
		public static GUIContent[] EnumToGUI<TK>(string resourcePrefix)
		{
			if (typeof(TK).BaseType != typeof(Enum)) throw new InvalidCastException();
			var strings = Enum.GetNames(typeof(TK));
			var buttons = new GUIContent[strings.Length];
			for (var i = 0; i < buttons.Length; i++)
			{
				buttons[i] = new GUIContent((Texture)Resources.Load(resourcePrefix + strings[i]), strings[i]);
			}
			return buttons;
		}
		
		public static void SetCursor(MouseCursor cursor)
		{
			EditorGUIUtility.AddCursorRect(Camera.current.pixelRect, cursor);
		}
	}
}