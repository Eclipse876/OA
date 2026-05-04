using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using ProtoShape2D.Editor.Extensions;
using ProtoShape2D.Editor.Helpers;
using ProtoShape2D.Extensions;

namespace ProtoShape2D.Editor
{
	[CustomEditor(typeof(ProtoShape2D))]
	public class PS2DEditor : UnityEditor.Editor
	{
		private ProtoShape2D script;
		
		private bool isDraggingPoint = false; //If the point is being dragged
		private bool isDraggingAnything = false; //If point or handle is being dragged
		private Snap snap = new Snap(); //For snap detection
		private double lastClickTime = 0; //For double-click detection
		private Rect windowRect; //For point properties window
		private int markerID = 0; //To hold ID of add point marker
		private bool dontDrawAddPoint = false; //For disabling + point marker when needed
		private Plane objectPlane; //Mostly to position controls using plane's normal

		private int pointClicked = -1; //To keep number of point which was clicked on mousedown

		private Vector2 dragOrigin; //To keep the coordinates of the mouse cursor before you started to drag the point. Needed in older Unity versions

		private int lastControlID = 0; //Used to get control IDs for each control we create


		private bool pivotMoveMode = false;
		private bool draggingPivot = false;
		private Vector3 pivotStart;
		private Tool rememberTool;

		#region Adding and setting up new object

		[MenuItem("GameObject/2D Object/ProtoShape 2D")]
		private static void CreateSimple()
		{
			AddObject(PS2DType.Simple);
		}

		private static void AddObject(PS2DType type)
		{
			var go = new GameObject("ProtoShape 2D");
			var ps2d = go.AddComponent<ProtoShape2D>();
			ps2d.SetSpriteMaterial();
			ps2d.type = type;
			var position = SceneViewHelper.GetCurrentSceneViewCenter();
			go.transform.position = new Vector3(position.x, position.y, 0f);
			if (Selection.activeGameObject != null)
			{
				go.transform.parent = Selection.activeGameObject.transform;
			}
			Selection.activeGameObject = go;
			Tools.current = Tool.Move;
		}

		private void Awake()
		{
			script = (ProtoShape2D)target;
			if (script.points.Count == 0)
			{
				var size = SceneViewHelper.GetCurrentSceneViewSize() / 6f;
				script.AddPoint((new Vector2(-2f, 1f) + (UnityEngine.Random.insideUnitCircle * 0.1f)) * size);
				script.AddPoint((new Vector2(2f, 1f) + (UnityEngine.Random.insideUnitCircle * 0.1f)) * size);
				script.AddPoint((new Vector2(2f, -1f) + (UnityEngine.Random.insideUnitCircle * 0.1f)) * size);
				script.AddPoint((new Vector2(-2f, -1f) + (UnityEngine.Random.insideUnitCircle * 0.1f)) * size);
				script.color1 = ColorHelper.RandomPleasantColor();
				script.color2 = ColorHelper.RandomPleasantColor();
				script.outlineColor = ColorHelper.RandomPleasantColor();
				DeselectAllPoints();
				InternalEditorUtility.SetIsInspectorExpanded(script.GetComponent<MeshFilter>(), false);
				InternalEditorUtility.SetIsInspectorExpanded(script.GetComponent<MeshRenderer>(), false);
			}
			SceneView.RepaintAll();
			script.UpdateMaterialSettings();
			script.UpdateMesh();
		}

		private void OnDestroy()
		{
			PivotMoveModeOff();
		}

		#endregion

		#region Inspector controls

		public override void OnInspectorGUI()
		{
			var forceRepaint = Event.current.type == EventType.ValidateCommand && Event.current.commandName == "UndoRedoPerformed";

			EditorGUILayout.BeginHorizontal();
			EditorGUILayout.PrefixLabel(new GUIContent(Strings.Curve.CurveType, Strings.Curve.CurveTypeDesc));
			var typeCurrent = (script.type == PS2DType.Simple ? 0 : 1);
			var buttons = new[]
			{
				new GUIContent(Strings.Curve.Simple, Strings.Curve.SimpleDesc),
				new GUIContent(Strings.Curve.Bezier, Strings.Curve.BezierDesc)
			};
			var typeState = GUILayout.Toolbar(typeCurrent, buttons);
			if (typeState != typeCurrent)
			{
				var confirm = true;
				//If switching from Bezier to Simple, check if there are curves and warn user that details can be lost
				if (typeState == 0)
				{
					var hasCurves = false;
					for (int i = 0; i < script.points.Count; i++)
					{
						if (script.points[i].pointType != PS2DPointType.None)
						{
							hasCurves = true;
							break;
						}
					}
					if (hasCurves)
					{
						confirm = EditorUtility.DisplayDialog(Strings.Curve.ToSimple, Strings.Curve.ToSimpleDesc, 
							Strings.Continue, Strings.Cancel);
					}
				}

				if (confirm)
				{
					Undo.RecordObject(script, Strings.Undo.SimpleBezierConvert);
					if (typeState == 0)
					{
						//Converting to Simple
						script.type = PS2DType.Simple;
					}
					else
					{
						//Converting to Bezier
						script.type = PS2DType.Bezier;
						for (var i = 0; i < script.points.Count; i++)
						{
							if (script.points[i].curve == 0)
							{
								script.points[i].pointType = PS2DPointType.None;
							}
							else if (script.points[i].curve > 0)
							{
								script.points[i].pointType = PS2DPointType.Rounded;
							}
						}
					}
					EditorUtility.SetDirty(script);
				}
			}
			EditorGUILayout.EndHorizontal();

			script.showFillSettings = EditorGUILayout.Foldout(script.showFillSettings, Strings.Fill.Title, true);
			if (script.showFillSettings)
			{
				var fillType = (PS2DFillType)EditorGUILayout.EnumPopup(new GUIContent(Strings.Fill.Type, Strings.Fill.TypeDesc), script.fillType);
				//If fill type changed
				if (fillType != script.fillType)
				{
					Undo.RecordObject(script, Strings.Undo.ChangeFillType);
					//If setting changed to single color or to no-fill, we use Unity's built-in sprite material
					if (fillType == PS2DFillType.Color || fillType == PS2DFillType.None)
					{
						script.SetSpriteMaterial();
					}
					//If setting changed to custom material, we use a material provided by user
					else if (fillType == PS2DFillType.CustomMaterial)
					{
						script.SetCustomMaterial();
					}
					//Otherwise we use our own shader that supports gradient and texture
					else script.SetDefaultMaterial();
					//Update the object
					script.fillType = fillType;
					script.UpdateMaterialSettings();
					EditorUtility.SetDirty(script);
				}
				
				//Single color setup
				if (script.fillType == PS2DFillType.Color || script.fillType == PS2DFillType.TextureWithColor)
				{
					var color1 = EditorGUILayout.ColorField(new GUIContent(Strings.Fill.Color,Strings.Fill.ColorDesc), 
						script.color1, true, true, script.HDRColors);
					if (script.color1 != color1)
					{
						Undo.RecordObject(script, Strings.Undo.ChangeColor);
						script.color1 = color1;
						script.UpdateMaterialSettings();
						EditorUtility.SetDirty(script);
					}
				}

				//Texture
				if (script.fillType == PS2DFillType.Texture || script.fillType == PS2DFillType.TextureWithColor || script.fillType == PS2DFillType.TextureWithGradient)
				{
					var texture = (Texture2D)EditorGUILayout.ObjectField(new GUIContent(Strings.Fill.Texture, Strings.Fill.TextureDesc), 
						script.texture, typeof(Texture2D), false);
					if (script.texture != texture)
					{
						Undo.RecordObject(script, Strings.Undo.ChangeTexture);
						script.texture = texture;
						script.UpdateMaterialSettings();
						EditorUtility.SetDirty(script);
					}

					var textureScale = EditorGUILayout.FloatField(new GUIContent(Strings.Fill.TextureScale, Strings.Fill.TextureScaleDesc), 
						script.textureScale);
					if (textureScale != script.textureScale)
					{
						if (textureScale < 0) textureScale = 0;
						Undo.RecordObject(script, Strings.Undo.ChangeTextureSize);
						script.textureScale = textureScale;
						script.UpdateMaterialSettings();
						EditorUtility.SetDirty(script);
					}

					var textureRotation = EditorGUILayout.Slider(new GUIContent(Strings.Fill.TextureRotation, Strings.Fill.TextureRotationDesc), 
						script.textureRotation, -180f, 180f);
					if (textureRotation != script.textureRotation)
					{
						Undo.RecordObject(script, Strings.Undo.ChangeTextureRotation);
						script.textureRotation = textureRotation;
						script.UpdateMaterialSettings();
						EditorUtility.SetDirty(script);
					}

					var textureOffsetX = EditorGUILayout.Slider(new GUIContent(Strings.Fill.TextureOffsetX, Strings.Fill.TextureOffsetXDesc), 
						script.textureOffset.x, -1f, 1f);
					if (textureOffsetX != script.textureOffset.x)
					{
						Undo.RecordObject(script, Strings.Undo.ChangeTextureOffset);
						script.textureOffset.x = Mathf.Clamp(textureOffsetX, -1f, 1f);
						script.UpdateMaterialSettings();
						EditorUtility.SetDirty(script);
					}

					var textureOffsetY = EditorGUILayout.Slider(new GUIContent(Strings.Fill.TextureOffsetY, Strings.Fill.TextureOffsetYDesc), 
						script.textureOffset.y, -1f, 1f);
					if (textureOffsetY != script.textureOffset.y)
					{
						Undo.RecordObject(script, Strings.Undo.ChangeTextureOffset);
						script.textureOffset.y = Mathf.Clamp(textureOffsetY, -1f, 1f);
						script.UpdateMaterialSettings();
						EditorUtility.SetDirty(script);
					}
				}

				//Two color setup
				if (script.fillType == PS2DFillType.Gradient || script.fillType == PS2DFillType.TextureWithGradient)
				{
					var gcolor1 = EditorGUILayout.ColorField(new GUIContent(Strings.Fill.ColorOne, Strings.Fill.ColorOneDesc), 
						script.color1, true, true, script.HDRColors);
					if (script.color1 != gcolor1)
					{
						Undo.RecordObject(script, Strings.Undo.ChangeColorOne);
						script.color1 = gcolor1;
						script.UpdateMaterialSettings();
						EditorUtility.SetDirty(script);
					}
					var gcolor2 = EditorGUILayout.ColorField(new GUIContent(Strings.Fill.ColorTwo, Strings.Fill.ColorTwoDesc), 
						script.color2, true, true, script.HDRColors);
					if (script.color2 != gcolor2)
					{
						Undo.RecordObject(script, Strings.Undo.ChangeColorTwo);
						script.color2 = gcolor2;
						script.UpdateMaterialSettings();
						EditorUtility.SetDirty(script);
					}
					var gradientScale = EditorGUILayout.Slider(new GUIContent(Strings.Fill.GradientScale, Strings.Fill.GradientScaleDesc), 
						script.gradientScale, 0f, 2f);
					if (gradientScale != script.gradientScale)
					{
						Undo.RecordObject(script, Strings.Undo.ChangeGradientScale);
						script.gradientScale = gradientScale;
						script.UpdateMaterialSettings();
						EditorUtility.SetDirty(script);
					}

					var gradientRotation = EditorGUILayout.Slider(new GUIContent(Strings.Fill.GradientRotation, Strings.Fill.GradientRotationDesc), 
						script.gradientRotation, -180f, 180f);
					if (gradientRotation != script.gradientRotation)
					{
						Undo.RecordObject(script, Strings.Undo.ChangeGradientRotation);
						script.gradientRotation = gradientRotation;
						script.UpdateMaterialSettings();
						EditorUtility.SetDirty(script);
					}

					var gradientOffset = EditorGUILayout.Slider(new GUIContent(Strings.Fill.GradientOffset, Strings.Fill.GradientOffsetDesc), 
						script.gradientOffset, -1f, 1f);
					if (gradientOffset != script.gradientOffset)
					{
						Undo.RecordObject(script, Strings.Undo.ChangeGradientOffset);
						script.gradientOffset = gradientOffset;
						script.UpdateMaterialSettings();
						EditorUtility.SetDirty(script);
					}
				}

				//Custom material setup
				if (script.fillType == PS2DFillType.CustomMaterial)
				{
					Material material = (Material)EditorGUILayout.ObjectField(new GUIContent(Strings.Fill.CustomMaterial, Strings.Fill.CustomMaterialDesc),
						script.customMaterial, typeof(Material), false);
					if (script.customMaterial != material)
					{
						Undo.RecordObject(script, Strings.Undo.ChangeCustomMaterial);
						script.SetCustomMaterial(material);
						script.UpdateMaterialSettings();
						EditorUtility.SetDirty(script);
					}
				}
			}

			script.showOutlineSettings = EditorGUILayout.Foldout(script.showOutlineSettings, Strings.Outline.Title, true);
			if (script.showOutlineSettings)
			{
				var outlineWidth = EditorGUILayout.FloatField(new GUIContent(Strings.Outline.Width, Strings.Outline.WidthDesc), 
					script.outlineWidth);
				if (outlineWidth != script.outlineWidth)
				{
					outlineWidth = Mathf.Max(0f, outlineWidth);
					Undo.RecordObject(script, Strings.Undo.ChangeOutlineWidth);
					script.outlineWidth = outlineWidth;
					if (outlineWidth > 0)
					{
						script.antialias = false; //Turn off fake antialiasing
					}
					script.UpdateMesh();
					EditorUtility.SetDirty(script);
				}
				var outlineColor = EditorGUILayout.ColorField(new GUIContent(Strings.Outline.Color, Strings.Outline.ColorDesc), 
					script.outlineColor, true, true, script.HDRColors);
				if (outlineColor != script.outlineColor)
				{
					Undo.RecordObject(script, Strings.Undo.ChangeOutlineColor);
					script.outlineColor = outlineColor;
					script.UpdateMesh();
					EditorUtility.SetDirty(script);
				}

				var outlineLoop = EditorGUILayout.Toggle(new GUIContent(Strings.Outline.Loop, Strings.Outline.LoopDesc),
					script.outlineLoop);
				if (outlineLoop != script.outlineLoop)
				{
					Undo.RecordObject(script, Strings.Undo.ChangeOutlineLoop);
					script.outlineLoop = outlineLoop;
					EditorUtility.SetDirty(script);
				}

				var outlineUseCustomMaterial = EditorGUILayout.Toggle(new GUIContent(Strings.Outline.CustomMaterialToggle, Strings.Outline.CustomMaterialToggleDesc),
					script.outlineUseCustomMaterial);
				if (outlineUseCustomMaterial != script.outlineUseCustomMaterial)
				{
					Undo.RecordObject(script, Strings.Undo.ChangeOutlineCustomMaterialToggle);
					script.outlineUseCustomMaterial = outlineUseCustomMaterial;
					script.UpdateMaterialSettings();
					EditorUtility.SetDirty(script);
				}

				//Custom material setup
				if (script.outlineUseCustomMaterial)
				{
					var outlineCustomMaterial = (Material)EditorGUILayout.ObjectField(new GUIContent(Strings.Outline.CustomMaterial, Strings.Outline.CustomMaterialDesc),
						script.outlineCustomMaterial, typeof(Material), false);
					if (script.outlineCustomMaterial != outlineCustomMaterial)
					{
						Undo.RecordObject(script, Strings.Undo.ChangeOutlineCustomMaterial);
						script.SetOutlineCustomMaterial(outlineCustomMaterial);
						script.UpdateMaterialSettings();
						EditorUtility.SetDirty(script);
					}
				}
			}

			script.showMeshSetting = EditorGUILayout.Foldout(script.showMeshSetting, Strings.Mesh.Title, true);
			if (script.showMeshSetting)
			{
				var curveIterations = EditorGUILayout.IntSlider(new GUIContent(Strings.Mesh.CurveIterations, Strings.Mesh.CurveIterationsDesc),
					script.curveIterations, 1, 30);
				if (curveIterations != script.curveIterations)
				{
					Undo.RecordObject(script, Strings.Undo.ChangeCurveIterations);
					script.curveIterations = curveIterations;
					EditorUtility.SetDirty(script);
				}

				EditorGUI.BeginDisabledGroup(script.outlineWidth > 0);
				bool antialias = EditorGUILayout.Toggle(new GUIContent(Strings.Mesh.Antialiasing, Strings.Mesh.AntialiasingDesc),
					script.antialias);
				if (antialias != script.antialias)
				{
					Undo.RecordObject(script, Strings.Undo.ChangeAntialiasing);
					script.antialias = antialias;
					script.aaridge = 0.002f * (Camera.main != null ? Camera.main.orthographicSize * 2 : 10f);
					EditorUtility.SetDirty(script);
				}

				EditorGUI.EndDisabledGroup();
				//Edit antialiasing ridge
				if (antialias)
				{
					float aaridge = EditorGUILayout.FloatField(new GUIContent(Strings.Mesh.AARidge, Strings.Mesh.AARidgeDesc), 
						script.aaridge);
					if (aaridge != script.aaridge)
					{
						if (aaridge < 0f) aaridge = 0f;
						Undo.RecordObject(script, Strings.Undo.ChangeAntialiasingWidth);
						script.aaridge = aaridge;
						EditorUtility.SetDirty(script);
					}
				}

				var triangleCount = script.triangleCount switch
				{
					> 1 => string.Format(Strings.Mesh.TriangleCount, script.triangleCount),
					1 => Strings.Mesh.OneTriangle,
					_ => Strings.Mesh.NoTriangles
				};
				GUILayout.Box(new GUIContent(triangleCount), EditorStyles.helpBox);


				//Expose point positions in inspector
				if (script.exposePointsInInspector)
				{
					var pPositions = new Vector2[script.points.Count];
					for (var i = 0; i < script.points.Count; i++)
					{
						pPositions[i] = EditorGUILayout.Vector2Field(script.points[i].name, script.points[i].position);
						if (pPositions[i] != script.points[i].position)
						{
							script.points[i].position = pPositions[i];
							script.UpdateMesh();
						}
					}
				}
			}

			script.showSnapSetting = EditorGUILayout.Foldout(script.showSnapSetting, Strings.Snap.Title, true);
			if (script.showSnapSetting)
			{
				var snapType = (PS2DSnapType)EditorGUILayout.EnumPopup(new GUIContent(Strings.Snap.Type, Strings.Snap.TypeDesc),
					script.snapType);
				if (snapType != script.snapType)
				{
					Undo.RecordObject(script, Strings.Undo.ChangeSnapType);
					script.snapType = snapType;
					EditorUtility.SetDirty(script);
				}

				if (script.snapType != PS2DSnapType.Points)
				{
					var gridSize = EditorGUILayout.FloatField(new GUIContent(Strings.Snap.GridSize, Strings.Snap.GridSizeDesc),
							script.gridSize);
					if (gridSize != script.gridSize)
					{
						gridSize = Mathf.Max(0.01f, gridSize);
						Undo.RecordObject(script, Strings.Undo.ChangeGridSize);
						script.gridSize = gridSize;
						EditorUtility.SetDirty(script);
					}
				}
			}

			script.showColliderSettings = EditorGUILayout.Foldout(script.showColliderSettings, Strings.Collider.Title, true);
			if (script.showColliderSettings)
			{
				var colliderType = (PS2DColliderType)EditorGUILayout.EnumPopup(new GUIContent(Strings.Collider.AutoCollider2D, Strings.Collider.AutoCollider2DDesc),
					script.colliderType);
				if (colliderType != script.colliderType)
				{
					if (RemoveCollider(colliderType))
					{
						Undo.RecordObject(script, Strings.Undo.ChangeColliderType);
						script.colliderType = colliderType;
						AddCollider();
						script.UpdateMesh();
						EditorUtility.SetDirty(script);
					}

					EditorGUIUtility.ExitGUI();
				}

				if (script.colliderType != PS2DColliderType.None &&
					script.colliderType != PS2DColliderType.MeshStatic &&
					script.colliderType != PS2DColliderType.MeshDynamic)
				{
					var colliderTopAngle = EditorGUILayout.Slider(new GUIContent(Strings.Collider.TopEdgeArc, Strings.Collider.TopEdgeArcDesc),
						script.colliderTopAngle, 1, 180);
					if (colliderTopAngle != script.colliderTopAngle)
					{
						Undo.RecordObject(script, Strings.Undo.ChangeTopEdgeArc);
						script.colliderTopAngle = colliderTopAngle;
						EditorUtility.SetDirty(script);
					}

					var colliderOffsetTop = EditorGUILayout.Slider(new GUIContent(Strings.Collider.OffsetTop, Strings.Collider.OffsetTopDesc),
						script.colliderOffsetTop, -1, 1);
					if (colliderOffsetTop != script.colliderOffsetTop)
					{
						Undo.RecordObject(script, Strings.Undo.ChangeOffsetTop);
						script.colliderOffsetTop = colliderOffsetTop;
						EditorUtility.SetDirty(script);
					}

					var showNormals = EditorGUILayout.Toggle(new GUIContent(Strings.Collider.ShowNormals, Strings.Collider.ShowNormalsDesc),
						script.showNormals);
					if (showNormals != script.showNormals)
					{
						Undo.RecordObject(script, Strings.Undo.ChangeShowNormals);
						script.showNormals = showNormals;
						EditorUtility.SetDirty(script);
					}
				}

				if (script.colliderType == PS2DColliderType.MeshStatic || script.colliderType == PS2DColliderType.MeshDynamic)
				{
					var cMeshDepth = EditorGUILayout.Slider(new GUIContent(Strings.Collider.Depth, Strings.Collider.DepthDesc),
						script.cMeshDepth, 0, 10);
					if (cMeshDepth != script.cMeshDepth)
					{
						Undo.RecordObject(script, Strings.Undo.ChangeColliderDepth);
						script.cMeshDepth = cMeshDepth;
						script.UpdateMesh();
						EditorUtility.SetDirty(script);
					}
				}

				if (script.colliderType == PS2DColliderType.MeshDynamic)
				{
					GUILayout.Box(new GUIContent(Strings.Collider.MeshDynamicNote), EditorStyles.helpBox);
				}
			}

			script.showTools = EditorGUILayout.Foldout(script.showTools, Strings.Tools.Title, true);
			if (script.showTools)
			{
				EditorGUILayout.BeginHorizontal();
				EditorGUILayout.PrefixLabel(new GUIContent(Strings.Tools.ZSorting, Strings.Tools.ZSortingDesc));
				if (GUILayout.Button(new GUIContent(Strings.Tools.Pull, Strings.Tools.PullDesc)))
				{
					Undo.RecordObject(script.transform, Strings.Undo.ZSortingPull);
					script.transform.position -= Vector3.forward * 0.01f;
					EditorUtility.SetDirty(script.transform);
				}

				if (GUILayout.Button(new GUIContent(Strings.Tools.Push, Strings.Tools.PushDesc)))
				{
					Undo.RecordObject(script.transform, Strings.Undo.ZSortingPush);
					script.transform.position += Vector3.forward * 0.01f;
					EditorUtility.SetDirty(script.transform);
				}

				EditorGUILayout.EndHorizontal();

				//Storing padding for toolbar buttons so we can restore it later
				var oPaddingButtonLeft = GUI.skin.GetStyle("ButtonLeft").padding;
				var oPaddingButtonMid = GUI.skin.GetStyle("ButtonMid").padding;
				var oPaddingButtonRight = GUI.skin.GetStyle("ButtonRight").padding;
				//Changing padding for toolbar buttons to fit more stuff
				GUI.skin.GetStyle("ButtonLeft").padding = new RectOffset(0, 0, 3, 3);
				GUI.skin.GetStyle("ButtonMid").padding = new RectOffset(0, 0, 3, 3);
				GUI.skin.GetStyle("ButtonRight").padding = new RectOffset(0, 0, 3, 3);

				//Pivot type
				EditorGUILayout.BeginHorizontal();
				EditorGUILayout.PrefixLabel(new GUIContent(Strings.Tools.Pivot, Strings.Tools.PivotDesc));

				//Move pivot manually
				var pivotButtonStyle = new GUIStyle(GUI.skin.button)
				{
					padding = new RectOffset(3, 3, 3, 3),
					stretchWidth = false,
					fixedWidth = 21,
					fixedHeight = GUI.skin.GetStyle("ButtonMid").CalcHeight(new GUIContent("Text"), 10f)
				};
				var newPivotMoveMode = GUILayout.Toggle(pivotMoveMode, 
					new GUIContent((Texture)Resources.Load("Icons/movePivot"), Strings.Tools.MovePivotManually), pivotButtonStyle);
				if (newPivotMoveMode != pivotMoveMode)
				{
					pivotMoveMode = newPivotMoveMode;
					if (pivotMoveMode)
					{
						PivotMoveModeOn();
					}
					else
					{
						PivotMoveModeOff();
					}
				}

				//Pivot type switch
				int pivotType = GUILayout.Toolbar((int)script.pivotType, UiHelper.EnumToGUI<PS2DPivotType>());
				if (pivotType != (int)script.pivotType)
				{
					GUI.FocusControl(null);
					script.pivotType = (PS2DPivotType)pivotType;
					if (script.pivotType == PS2DPivotType.Auto)
					{
						MovePivot();
						if (pivotMoveMode) PivotMoveModeOff();
					}
				}

				EditorGUILayout.EndHorizontal();

				//Pivot position
				EditorGUILayout.BeginHorizontal();
				GUILayout.Space(EditorGUIUtility.labelWidth);
				int pivotPosition =
					GUILayout.Toolbar(script.pivotType == PS2DPivotType.Manual ? -1 : (int)script.PivotPosition,
						UiHelper.EnumToGUI<PS2DPivotPosition>("Icons/pivot"));
				if ((script.pivotType == PS2DPivotType.Manual && pivotPosition != -1) ||
					(script.pivotType == PS2DPivotType.Auto && pivotPosition != (int)script.PivotPosition))
				{
					script.PivotPosition = (PS2DPivotPosition)pivotPosition;
					MovePivot();
					pivotStart = script.transform.position;
				}

				EditorGUILayout.EndHorizontal();

				//Restoring original button padding
				GUI.skin.GetStyle("ButtonLeft").padding = oPaddingButtonLeft;
				GUI.skin.GetStyle("ButtonMid").padding = oPaddingButtonMid;
				GUI.skin.GetStyle("ButtonRight").padding = oPaddingButtonRight;

				var hdrColors = EditorGUILayout.Toggle(new GUIContent(Strings.Tools.HdrColors, Strings.Tools.HdrColorsDesc), 
					script.HDRColors);
				if (hdrColors != script.HDRColors)
				{
					Undo.RecordObject(script, Strings.Undo.ChangeHdrColors);
					script.HDRColors = hdrColors;
					EditorUtility.SetDirty(script);
				}

				EditorGUILayout.BeginHorizontal();
				EditorGUILayout.PrefixLabel(new GUIContent(Strings.Tools.Export, Strings.Tools.ExportDesc));
				if (GUILayout.Button(new GUIContent(Strings.Tools.ExportMesh, Strings.Tools.ExportMeshDesc)))
				{
					ExportMesh();
				}

				if (GUILayout.Button(new GUIContent(Strings.Tools.ExportPng, Strings.Tools.ExportPngDesc)))
				{
					ExportPNG();
				}

				EditorGUILayout.EndHorizontal();

				/*
				//Extrude
				if(AssetDatabase.FindAssets("ProtoShape2DExtruder").Length>0){
					EditorGUILayout.BeginHorizontal();
					EditorGUILayout.PrefixLabel(new GUIContent("Extrude"));
					if(script.gameObject.GetComponent<PS2DExtruder>()==null){
						if(GUILayout.Button(new GUIContent("Extrude","Extrude the shape into 3D space"))){
							script.gameObject.AddComponent<PS2DExtruder>();
						}
					}else{
						if(GUILayout.Button(new GUIContent("Remove","Remove the extrusion component and child objects"))){
							script.gameObject.GetComponent<PS2DExtruder>().Remove();
							DestroyImmediate(script.gameObject.GetComponent<PS2DExtruder>());
						}
					}
					EditorGUILayout.EndHorizontal();
				}
				*/

				//Sprite sorting
				GUILayout.Space(10);
				//Get sorting layers
				var layerIDs = SortingLayers.GetSortingLayerUniqueIDs();
				var layerNames = SortingLayers.GetSortingLayerNames();
				//Get selected sorting layer
				var selected = -1;
				for (var i = 0; i < layerIDs.Length; i++)
				{
					if (layerIDs[i] == script.sortingLayer)
					{
						selected = i;
					}
				}

				//Select Default layer if no other is selected
				if (selected == -1)
				{
					for (var i = 0; i < layerIDs.Length; i++)
					{
						if (layerIDs[i] == 0)
						{
							selected = i;
						}
					}
				}

				//Sorting layer dropdown
				EditorGUI.BeginChangeCheck();
				var dropdown = new GUIContent[layerNames.Length + 2];
				for (var i = 0; i < layerNames.Length; i++)
				{
					dropdown[i] = new GUIContent(layerNames[i]);
				}
				dropdown[layerNames.Length] = new GUIContent();
				dropdown[layerNames.Length + 1] = new GUIContent(Strings.Tools.AddSortingLayer);
				selected = EditorGUILayout.Popup(new GUIContent(Strings.Tools.SortingLayer, Strings.Tools.SortingLayerDesc),
					selected, dropdown);
				if (EditorGUI.EndChangeCheck())
				{
					Undo.RecordObject(script, Strings.Undo.ChangeSortingLayer);
					if (selected == layerNames.Length + 1)
					{
						EditorApplication.ExecuteMenuItem("Edit/Project Settings/Tags and Layers");
					}
					else
					{
						script.sortingLayer = layerIDs[selected];
					}
					EditorUtility.SetDirty(script);
				}

				//Order in layer field
				EditorGUI.BeginChangeCheck();
				var order = EditorGUILayout.IntField(new GUIContent(Strings.Tools.OrderInLayer, Strings.Tools.OrderInLayerDesc), 
					script.orderInLayer);
				if (EditorGUI.EndChangeCheck())
				{
					Undo.RecordObject(script, Strings.Undo.ChangeOrderInLayer);
					script.orderInLayer = order;
					EditorUtility.SetDirty(script);
				}

				//Mask dropdown
				EditorGUI.BeginChangeCheck();
				var dropdownMask = new GUIContent[3];
				dropdownMask[0] = new GUIContent(Strings.Tools.MaskNone);
				dropdownMask[1] = new GUIContent(Strings.Tools.MaskVisibleInside);
				dropdownMask[2] = new GUIContent(Strings.Tools.MaskVisibleOutside);
				var selectedMaskOption = EditorGUILayout.Popup(new GUIContent(Strings.Tools.MaskInteraction, Strings.Tools.MaskInteractionDesc),
						script.selectedMaskOption, dropdownMask);
				if (EditorGUI.EndChangeCheck())
				{
					Undo.RecordObject(script, Strings.Undo.ChangeMaskInteraction);
					script.selectedMaskOption = selectedMaskOption;
					//script.UpdateMaterialSettings();
					EditorUtility.SetDirty(script);
				}
			}

			if (GUI.changed || forceRepaint)
			{
				script.UpdateMesh();
				SceneView.RepaintAll();
			}
		}

		private void PivotMoveModeOn()
		{
			script.pivotType = PS2DPivotType.Manual;
			rememberTool = Tools.current;
			Tools.current = Tool.None;
			pivotStart = script.transform.position;
			pivotMoveMode = true;
		}

		private void PivotMoveModeOff()
		{
			if (rememberTool == Tool.None) rememberTool = Tool.Move;
			if (Tools.current == Tool.None) Tools.current = rememberTool;
			pivotMoveMode = false;
		}

		#endregion

		#region Scene GUI - drawing points, lines, grid in the scene view

		private void OnSceneGUI()
		{
			Tools.pivotMode = PivotMode.Pivot;
			var et = Event.current.type; //Need to save this because it can be changed to Used by other functions
			//Create an object plane
			objectPlane = new Plane(
				script.transform.TransformPoint(new Vector3(0, 0, 0)),
				script.transform.TransformPoint(new Vector3(0, 1, 0)),
				script.transform.TransformPoint(new Vector3(1, 0, 0))
			);
			//Detecting if object is being dragged
			if (et == EventType.MouseDrag) isDraggingAnything = true;
			if (isDraggingAnything && et == EventType.MouseUp) isDraggingAnything = false;
			//If current tool is none, we're probably in collider edit mode
			if (Tools.current != Tool.None && script.isActiveAndEnabled)
			{
				PivotMoveModeOff();
				//Draw a snapping grid
				if (script.snapType != PS2DSnapType.Points) DrawGrid();
				//Draw outline
				DrawLines();
				//Deselect all points on ESC
				if (et == EventType.KeyDown)
				{
					if (Event.current.keyCode == KeyCode.Escape)
					{
						DeselectAllPoints();
						SceneView.RepaintAll();
					}
				}

				//When CTRL is pressed, draw only deletable points
				if (Event.current.control || (Event.current.command && (Application.platform == RuntimePlatform.OSXEditor || Application.platform == RuntimePlatform.OSXPlayer)))
				{
					if (script.points.Count > 2)
					{
						for (int i = 0; i < script.points.Count; i++)
						{
							DrawDeletePoint(i);
						}
					}
				}
				else
				{
					//Controls for gradient
					if (script.fillType == PS2DFillType.Gradient || script.fillType == PS2DFillType.TextureWithGradient)
					{
						DrawGradientControls();
					}

					//Controls for texture
					if (script.fillType == PS2DFillType.Texture || script.fillType == PS2DFillType.TextureWithColor ||
						script.fillType == PS2DFillType.TextureWithGradient)
					{
						if (script.texture != null) DrawTextureControls();
					}

					//Draw a marker to add new points, but only if nothing is being dragged right now
					if (!dontDrawAddPoint && !isDraggingAnything)
					{
						DrawAddPointMarker(et);
					}

					//Draw draggable points
					for (int i = 0; i < script.points.Count; i++)
					{
						DrawPoint(i);
					}

					//This tracks if mouse is over bezier point
					dontDrawAddPoint = false;
					//Draw draggable bezier controls
					for (int i = 0; i < script.points.Count; i++)
					{
						DrawBezierControls(i);
					}

					//Remember mouse position on mouse down
					if (et == EventType.MouseDown)
					{
						dragOrigin = GUIUtility.GUIToScreenPoint(Event.current.mousePosition);
					}
					//On drag end check the distance of dragging and if it's zero, this was just a click, not a drag
					if (et == EventType.MouseUp && !Event.current.shift && pointClicked != -1 &&
						Mathf.Approximately(
							(GUIUtility.GUIToScreenPoint(Event.current.mousePosition) - dragOrigin).magnitude, 0f))
					{
						DeselectAllPoints();
						SelectPoint(pointClicked, true);
						pointClicked = -1;
					}

					//Select/deselect points
					if (et == EventType.MouseDown && GUIUtility.hotControl != 0)
					{
						pointClicked = -1;
						var PS2DControlsClicked = false; //Track if any point or bezier control was clicked
						for (int i = 0; i < script.points.Count; i++)
						{
							//Detect click on a point
							if (GUIUtility.hotControl == script.points[i].controlID)
							{
								PS2DControlsClicked = true;
								pointClicked = i;
								//Detect double click on a control and invert bezier handles in simple mode
								if (EditorApplication.timeSinceStartup - lastClickTime < 0.3f)
								{
									script.points[i].median *= -1;
									script.UpdateMesh();
									lastClickTime = 0f;
								}
								else
								{
									lastClickTime = EditorApplication.timeSinceStartup;
								}

								break;
							}

							//Check for a click on bezier controls
							if (!PS2DControlsClicked && (GUIUtility.hotControl == script.points[i].controlPID ||
														 GUIUtility.hotControl == script.points[i].controlNID))
							{
								PS2DControlsClicked = true;
							}
						}

						//If we found a point that corresponds to current hotControl
						if (pointClicked > -1)
						{
							//If shift is being held we toggle selection of this point
							if (Event.current.shift)
							{
								SelectPoint(pointClicked, !script.points[pointClicked].selected);
								//If shift is now down we just set this point to selected
							}
							else
							{
								//If this point wasn't selected we deselect all other poings
								if (!script.points[pointClicked].selected) DeselectAllPoints();
								SelectPoint(pointClicked, true);
							}
						}

						//If we found that no point or bezier control was clicked, we deselect all points
						if (!PS2DControlsClicked) DeselectAllPoints();
					}

					//Draw collider lines
					DrawCollider();
					//Draw properties of selected points
					DrawPointsProperties();
				}
			}
			//Move pivot mode
			else if (Tools.current == Tool.None && pivotMoveMode && script.isActiveAndEnabled)
			{
				Handles.color = Color.red;
				Handles.DrawSolidDisc(pivotStart, objectPlane.normal, HandleUtility.GetHandleSize(pivotStart) * 0.03f);
				Handles.color = Color.white;
				EditorGUI.BeginChangeCheck();

				#if UNITY_2022_1_OR_NEWER
				pivotStart = Handles.FreeMoveHandle(pivotStart,HandleUtility.GetHandleSize(pivotStart)*0.1f,Vector3.zero,Handles.CircleHandleCap);
				#else
				pivotStart = Handles.FreeMoveHandle(pivotStart, Quaternion.identity, HandleUtility.GetHandleSize(pivotStart) * 0.1f, Vector3.zero, Handles.CircleHandleCap);
				#endif

				var changed = EditorGUI.EndChangeCheck();
				if (changed && draggingPivot == false)
				{
					draggingPivot = true;
				}
				//React to drag stop
				if (et == EventType.MouseUp && draggingPivot)
				{
					draggingPivot = false;
					MovePivot(script.transform.InverseTransformPoint(pivotStart));
					EditorUtility.SetDirty(script);
					pivotStart = script.transform.position;
				}
			}
			else
			{
				DeselectAllPoints();
			}
			SceneView.RepaintAll();
		}
		
		private void DrawLines()
		{
			Handles.color = new Color(1f, 1f, 1f, 0.6f);
			for (var i = 0; i < script.outlineConnect - (script.outlineLoop ? 0 : 1); i++)
			{
				Handles.DrawLine(
					script.transform.TransformPoint(script.pointsFinal[i]),
					script.transform.TransformPoint(script.pointsFinal.Loop(i + 1))
				);
			}

			Handles.color = Color.white;
		}

		private void DrawPoint(int pointID)
		{
			var et = Event.current.type;
			var size = HandleUtility.GetHandleSize(script.points[pointID].position) * 0.1f;
			//Circle around drag point
			Handles.color = new Color(1, 1, 1, 0.5f);
			Handles.DrawWireDisc(script.transform.TransformPoint(script.points[pointID].position), objectPlane.normal,
				size);
			//Drag point
			Handles.color = Color.clear;
			EditorGUI.BeginChangeCheck();

			#if UNITY_2022_1_OR_NEWER
			var point = Handles.FreeMoveHandle(
				script.transform.TransformPoint(script.points[pointID].position),
				size,
				Vector3.zero,
				CircleHandleCapSaveIDWrapper
			);
			#else
			var point = Handles.FreeMoveHandle(
				script.transform.TransformPoint(script.points[pointID].position),
				script.transform.rotation,
				size,
				Vector3.zero,
				CircleHandleCapSaveIDWrapper
			);
			#endif

			var changed = EditorGUI.EndChangeCheck();
			//Assign last control id (retrieved by CircleHandleCapSaveID function)
			//Doing it during a layout event because that's when IDs are being set
			if (et == EventType.Layout) script.points[pointID].controlID = lastControlID;
			//Compensate for Unity's incorrect treatment of displays with higher pixel density
			if (changed && !isDraggingPoint)
			{
				//Select point if it's being dragged
				if (!script.points[pointID].selected) SelectPoint(pointID, true);
				isDraggingPoint = true;
			}

			//Change to default cursor if mouse is near the point
			Handles.color = Color.white;
			if (Vector2.Distance(script.transform.TransformPoint(script.points[pointID].position),
					objectPlane.GetMousePosition()) < size)
			{
				UiHelper.SetCursor(MouseCursor.Arrow);
			}

			//Snapping. Setting point's position depending on proximity to the grid
			if (script.points[pointID].controlID == GUIUtility.hotControl && isDraggingPoint && Event.current.shift)
			{
				if (script.snapType == PS2DSnapType.Points)
				{
					snap.Reset(size);
					for (var i = 0; i < script.points.Count; i++)
					{
						if (i == pointID || script.points[i].selected)
						{
							continue; //Don't snap to itself or to other selected points
						}
						snap.CheckPoint(i, point, script.transform.TransformPoint(script.points[i].position));
					}

					if (snap.GetClosestAxes() > 0)
					{
						point = snap.snapLocation;
						Vector3 ab;
						if (snap.snapPoint1 > -1)
						{
							ab = ((Vector3)snap.snapLocation -
								  script.transform.TransformPoint(script.points[snap.snapPoint1].position)).normalized *
								 (size * 8);
							Handles.DrawLine(
								(Vector3)snap.snapLocation + ab,
								script.transform.TransformPoint(script.points[snap.snapPoint1].position) - ab
							);
						}
						if (snap.snapPoint2 > -1)
						{
							ab = ((Vector3)snap.snapLocation -
								  script.transform.TransformPoint(script.points[snap.snapPoint2].position)).normalized *
								 (size * 8);
							Handles.DrawLine(
								(Vector3)script.transform.TransformPoint(script.points[pointID].position) + ab,
								script.transform.TransformPoint(script.points[snap.snapPoint2].position) - ab
							);
						}
					}
				}
				else if (script.snapType == PS2DSnapType.WorldGrid || script.snapType == PS2DSnapType.LocalGrid)
				{
					Vector2 pointTr = point;
					if (script.snapType == PS2DSnapType.LocalGrid)
						pointTr = script.transform.InverseTransformPoint(pointTr);
					Vector2[] snapPoints = new Vector2[4]
					{
						new Vector2(Mathf.Floor(pointTr.x / script.gridSize),
							Mathf.Floor(pointTr.y / script.gridSize)) * script.gridSize,
						new Vector2(Mathf.Floor(pointTr.x / script.gridSize), Mathf.Ceil(pointTr.y / script.gridSize)) *
						script.gridSize,
						new Vector2(Mathf.Ceil(pointTr.x / script.gridSize), Mathf.Ceil(pointTr.y / script.gridSize)) *
						script.gridSize,
						new Vector2(Mathf.Ceil(pointTr.x / script.gridSize), Mathf.Floor(pointTr.y / script.gridSize)) *
						script.gridSize
					};
					var snapPoint = -1;
					var closestDistance = script.gridSize;
					for (var j = 0; j < snapPoints.Length; j++)
					{
						if (script.snapType == PS2DSnapType.LocalGrid)
							snapPoints[j] = script.transform.TransformPoint(snapPoints[j]);
						var dist = Vector2.Distance(point, snapPoints[j]);
						if (dist < closestDistance)
						{
							snapPoint = j;
							closestDistance = dist;
						}
					}

					if (snapPoint > -1) point = snapPoints[snapPoint];
				}
			}
			//Actual dragging
			if (script.points[pointID].selected && (changed || (isDraggingPoint && Event.current.shift)))
			{
				Undo.RecordObject(script, "Move point");
				Vector3 diff = (Vector2)script.transform.InverseTransformPoint(point) - script.points[pointID].position;
				//Move all selected points
				for (var i = 0; i < script.points.Count; i++)
				{
					if (script.points[i].selected)
						script.points[i].Move(diff, script.type == PS2DType.Bezier ? true : false);
				}

				script.UpdateMesh();
			}
			//If point is selected, draw a circle
			if (script.points[pointID].selected == true)
			{
				Handles.DrawWireDisc(point, objectPlane.normal, size);
				Handles.DrawSolidDisc(script.transform.TransformPoint(script.points[pointID].position),
					objectPlane.normal, size * 0.75f);
			}
			//React to drag stop
			if (et == EventType.MouseUp && isDraggingPoint)
			{
				isDraggingPoint = false;
				EditorUtility.SetDirty(script);
				if (script.pivotType == PS2DPivotType.Auto)
				{
					MovePivot();
				}
			}
		}

		private void DrawDeletePoint(int pointID)
		{
			Handles.color = Color.white;
			var size = HandleUtility.GetHandleSize(script.points[pointID].position) * 0.1f;
			Handles.DrawWireDisc(script.transform.TransformPoint(script.points[pointID].position), objectPlane.normal,
				size);
			Handles.DrawLine(
				script.transform.TransformPoint(script.points[pointID].position + ((Vector2.up + Vector2.left) * (size * 0.5f))),
				script.transform.TransformPoint(script.points[pointID].position + ((Vector2.down + Vector2.right) * (size * 0.5f)))
			);
			Handles.DrawLine(
				script.transform.TransformPoint(script.points[pointID].position + ((Vector2.up + Vector2.right) * (size * 0.5f))),
				script.transform.TransformPoint(script.points[pointID].position + ((Vector2.down + Vector2.left) * (size * 0.5f)))
			);
			if (Vector2.Distance(script.transform.TransformPoint(script.points[pointID].position), objectPlane.GetMousePosition()) < size)
			{
				UiHelper.SetCursor(MouseCursor.ArrowMinus);
			}
			if (Handles.Button(script.transform.TransformPoint(script.points[pointID].position), Quaternion.identity, 0, size, Handles.CircleHandleCap))
			{
				Undo.RecordObject(script, Strings.Undo.DeletePoint);
				script.DeletePoint(pointID);
				script.UpdateMesh();
				EditorUtility.SetDirty(script);
				if (script.pivotType == PS2DPivotType.Auto)
				{
					MovePivot();
				}
			}
		}

		private void DrawAddPointMarker(EventType et)
		{
			var drawn = false;
			var size = HandleUtility.GetHandleSize(script.transform.position) * 0.05f;
			//Get position of cursor in the world
			//The cursor is the point where mouse ray intersects with object's plane
			var mRay = HandleUtility.GUIPointToWorldRay(Event.current.mousePosition);
			if (objectPlane.Raycast(mRay, out var mRayDist))
			{
				var cursor = mRay.GetPoint(mRayDist);
				//Get closest line
				Vector2 cursorIn = script.transform.InverseTransformPoint(cursor);
				var marker = Vector3.zero;
				var newMarker = Vector3.zero;
				var markerPoint = -1;
				for (var i = 0; i < script.outlineConnect - (script.outlineLoop ? 0 : 1); i++)
				{
					//Get point where perpendicular meets the line
					newMarker = EditorGeometry.GetBasePoint(
						script.pointsFinal[i],
						script.pointsFinal.Loop(i + 1),
						cursorIn
					);
					//If perpendicular doesn't meet the line, take closest end of the line
					if (newMarker == Vector3.zero)
					{
						if (Vector2.Distance(cursorIn, script.pointsFinal[i]) < Vector2.Distance(cursorIn, script.pointsFinal.Loop(i + 1)))
						{
							newMarker = script.pointsFinal[i];
						}
						else
						{
							newMarker = script.pointsFinal.Loop(i + 1);
						}
					}
					//Save shortest marker distance
					if (marker == Vector3.zero || Vector3.Distance(cursorIn, newMarker) < Vector3.Distance(cursorIn, marker))
					{
						markerPoint = i;
						marker = newMarker;
					}
				}

				//Check if cursor is not too close to the point handle
				var tooClose = false;
				for (var i = 0; i < script.points.Count; i++)
				{
					if (Vector3.Distance(script.points[i].position, marker) < size * 5)
					{
						tooClose = true;
						break;
					}
				}
				if (!tooClose && Vector3.Distance(cursorIn, marker) < size * 2.5)
				{
					marker = script.transform.TransformPoint(marker);
					Handles.color = Color.green;
					Handles.DrawSolidDisc(marker, objectPlane.normal, size);
					Handles.Button(marker, Quaternion.identity, 0, size * 2, CircleHandleCapSaveIDWrapper);
					if (et == EventType.Layout)
					{
						markerID = lastControlID; //Save control's ID if it's a layout event
					}
					if (et == EventType.MouseDown && markerID == GUIUtility.hotControl)
					{
						DeselectAllPoints();
						Undo.RecordObject(script, "Add point");
						//Find after which point we should add a new one by iterating through them
						int pointSum = 0;
						int pointAfter = -1;
						for (int i = 0; i < script.points.Count; i++)
						{
							if (
								(script.type == PS2DType.Simple &&
								 (script.points[i].curve > 0f || script.points.Loop(i + 1).curve > 0f)) ||
								(script.type == PS2DType.Bezier && (script.points[i].pointType != PS2DPointType.None ||
																	script.points.Loop(i + 1).pointType !=
																	PS2DPointType.None))
							)
							{
								pointSum += script.curveIterations;
							}
							else
							{
								pointSum++;
							}
							if (markerPoint < pointSum)
							{
								pointAfter = i;
								break;
							}
						}
						script.AddPoint(script.transform.InverseTransformPoint(marker), pointAfter);
						GUIUtility.hotControl = 0;
						SelectPoint(pointAfter + 1, true);
						EditorUtility.SetDirty(script);
						script.UpdateMesh();
					}
					Handles.color = Color.white;
					drawn = true;
				}
			}
			if (drawn)
			{
				UiHelper.SetCursor(MouseCursor.ArrowPlus);
			}
			else
			{
				markerID = 0;
			}
		}

		private void DrawBezierControls(int i)
		{
			var showAllControls = false;
			var drawControls = 2;
			var size = HandleUtility.GetHandleSize(script.points[i].position) * 0.1f;
			//Simple controls
			if (script.type == PS2DType.Simple)
			{
				//Draw a curve indicator circle
				if (script.points[i].curve > 0f)
				{
					if (script.points[i].selected == true)
					{
						Handles.color = new Color(1, 1, 0, 0.7f);
					}
					else
					{
						Handles.color = new Color(1, 1, 0, 0.3f);
					}
					Handles.DrawWireDisc(script.transform.TransformPoint(script.points[i].position), objectPlane.normal,
						size * 1.2f + (size * 2f) * script.points[i].curve);
					Handles.color = Color.white;
				}
			}
			//Bezier controls
			else if (script.type == PS2DType.Bezier && script.points[i].pointType != PS2DPointType.None &&
					 (script.points[i].selected || script.points.Loop(i - 1).selected ||
					  script.points.Loop(i + 1).selected || showAllControls))
			{
				//Bezier controls: Previous handle
				if (script.points.Loop(i - 1).selected || script.points[i].selected || showAllControls)
				{
					DrawBezierHandle(i, ref script.points[i].handleP);
					drawControls--;
				}
				//Bezier controls: Next handle
				if (script.points.Loop(i + 1).selected || script.points[i].selected || showAllControls)
				{
					DrawBezierHandle(i, ref script.points[i].handleN);
					drawControls--;
				}

				Handles.color = Color.white;
			}
			//Draw non-working controls to always keep same number of controls
			if (drawControls > 0)
			{
				Handles.color = Color.white;
				for (int j = 0; j < drawControls; j++)
				{
					#if UNITY_2022_1_OR_NEWER
						Handles.FreeMoveHandle(script.transform.TransformPoint(Vector3.zero),0.0f,Vector3.zero,Handles.DotHandleCap);
					#else
					Handles.FreeMoveHandle(script.transform.TransformPoint(Vector3.zero), Quaternion.identity, 0.0f,
						Vector3.zero, Handles.DotHandleCap);
					#endif
				}
			}
		}

		private void DrawBezierHandle(int i, ref Vector2 handlePosition)
		{
			var previous = script.points[i].handleP == handlePosition ? true : false;
			var next = script.points[i].handleN == handlePosition ? true : false;
			var size = HandleUtility.GetHandleSize(script.points[i].position) * 0.1f;
			var et = Event.current.type;
			Handles.color = new Color(1, 1, 1, 0.5f);
			Handles.DrawLine(script.transform.TransformPoint(script.points[i].position),
				script.transform.TransformPoint(handlePosition));
			Handles.color = Color.white;
			Handles.DrawSolidDisc(script.transform.TransformPoint(handlePosition), Vector3.back, size * 0.5f);
			Handles.color = Color.clear;
			EditorGUI.BeginChangeCheck();
			#if UNITY_2022_1_OR_NEWER
			var handlePoint = Handles.FreeMoveHandle(
				script.transform.TransformPoint(handlePosition),
				size*0.5f,
				Vector3.zero,
				CircleHandleCapSaveIDWrapper
			);
			#else
			var handlePoint = Handles.FreeMoveHandle(
				script.transform.TransformPoint(handlePosition),
				script.transform.rotation,
				size * 0.5f,
				Vector3.zero,
				CircleHandleCapSaveIDWrapper
			);
			#endif
			//Assign last control id (retrieved by CircleHandleCapSaveID function)
			//It looks like only within these two events controls are drawn and lastControlID is set
			//Otherwise we just get the last lastControlID each time
			if (et == EventType.Layout || et == EventType.Repaint)
			{
				if (previous)
				{
					script.points[i].controlPID = lastControlID;
				}
				else if (next)
				{
					script.points[i].controlNID = lastControlID;
				}
			}

			if (EditorGUI.EndChangeCheck())
			{
				if (!isDraggingPoint) isDraggingPoint = true;
				//Snapping. Setting point's position depending on proximity to the grid
				if ((script.points[i].controlPID == GUIUtility.hotControl ||
					 script.points[i].controlNID == GUIUtility.hotControl) && isDraggingPoint && Event.current.shift)
				{
					if (script.snapType == PS2DSnapType.Points)
					{
						snap.Reset(size);
						snap.CheckPoint(i, handlePoint, script.transform.TransformPoint(script.points[i].position));
						if (snap.GetClosestAxes() > 0)
						{
							handlePoint = snap.snapLocation;
						}
					}
					else if (script.snapType == PS2DSnapType.WorldGrid || script.snapType == PS2DSnapType.LocalGrid)
					{
						Vector2 pointTr = handlePoint;
						if (script.snapType == PS2DSnapType.LocalGrid)
							pointTr = script.transform.InverseTransformPoint(pointTr);
						var snapPoints = new Vector2[4]
						{
							new Vector2(Mathf.Floor(pointTr.x / script.gridSize),
								Mathf.Floor(pointTr.y / script.gridSize)) * script.gridSize,
							new Vector2(Mathf.Floor(pointTr.x / script.gridSize),
								Mathf.Ceil(pointTr.y / script.gridSize)) * script.gridSize,
							new Vector2(Mathf.Ceil(pointTr.x / script.gridSize),
								Mathf.Ceil(pointTr.y / script.gridSize)) * script.gridSize,
							new Vector2(Mathf.Ceil(pointTr.x / script.gridSize),
								Mathf.Floor(pointTr.y / script.gridSize)) * script.gridSize
						};
						var snapPoint = -1;
						var closestDistance = script.gridSize;
						for (var j = 0; j < snapPoints.Length; j++)
						{
							if (script.snapType == PS2DSnapType.LocalGrid)
								snapPoints[j] = script.transform.TransformPoint(snapPoints[j]);
							var dist = Vector2.Distance(handlePoint, snapPoints[j]);
							if (dist < closestDistance)
							{
								snapPoint = j;
								closestDistance = dist;
							}
						}

						if (snapPoint > -1) handlePoint = snapPoints[snapPoint];
					}
				}
				Undo.RecordObject(script, Strings.Undo.EditPoint);
				handlePosition = script.transform.InverseTransformPoint(handlePoint);
				//Move other bezier handle accordingly
				if (script.points[i].pointType == PS2DPointType.Rounded)
				{
					if (previous)
					{
						script.points[i].handleN = script.points[i].position -
												   (script.points[i].handleP - script.points[i].position).normalized *
												   (script.points[i].handleN - script.points[i].position).magnitude;
					}
					else
					{
						script.points[i].handleP = script.points[i].position -
												   (script.points[i].handleN - script.points[i].position).normalized *
												   (script.points[i].handleP - script.points[i].position).magnitude;
					}
				}
				script.UpdateMesh();
			}
			//Find if mouse is near this handle
			if (Vector2.Distance(script.transform.TransformPoint(handlePosition), objectPlane.GetMousePosition()) <= size * 1.5f)
			{
				dontDrawAddPoint = true;
			}
		}

		private void DrawCollider()
		{
			if (script.colliderType != PS2DColliderType.None && script.colliderType != PS2DColliderType.MeshStatic &&
				script.colliderType != PS2DColliderType.MeshDynamic)
			{
				var size = HandleUtility.GetHandleSize(script.transform.position) * 0.05f;
				Handles.color = Color.green;
				for (var i = 0; i < script.cpointsFinal.Length; i++)
				{
					if (i == 0 && script.colliderType == PS2DColliderType.TopEdge) continue;
					Handles.DrawLine(
						script.transform.TransformPoint(script.cpointsFinal.Loop(i - 1)),
						script.transform.TransformPoint(script.cpointsFinal[i])
					);
				}
				Handles.color = Color.white;
				//Debug edge normals
				if (script.showNormals)
				{
					for (int i = 0; i < script.cpoints.Count; i++)
					{
						if (script.cpoints[i].direction == PS2DDirection.Up) Handles.color = Color.green;
						if (script.cpoints[i].direction == PS2DDirection.Right) Handles.color = Color.magenta;
						if (script.cpoints[i].direction == PS2DDirection.Left) Handles.color = Color.blue;
						if (script.cpoints[i].direction == PS2DDirection.Down) Handles.color = Color.yellow;
						Handles.DrawLine(
							(Vector2)script.transform.TransformPoint(
								Vector2.Lerp(script.cpoints[i].position, script.cpoints.Loop(i + 1).position, 0.5f) +
								(Vector2)script.cpoints[i].normal * (size * 1)),
							(Vector2)script.transform.TransformPoint(
								Vector2.Lerp(script.cpoints[i].position, script.cpoints.Loop(i + 1).position, 0.5f) +
								(Vector2)script.cpoints[i].normal * (size * 7))
						);
					}
					Handles.color = Color.white;
				}
			}
			if (script.colliderType == PS2DColliderType.MeshStatic || script.colliderType == PS2DColliderType.MeshDynamic)
			{
				Handles.color = Color.green;
				var clength = script.cMesh.vertices.Length / 2;
				for (int i = 0; i < clength; i++)
				{
					Handles.DrawLine(
						script.transform.TransformPoint(script.cMesh.vertices[i]),
						script.transform.TransformPoint(script.cMesh.vertices[(i == clength - 1 ? 0 : i + 1)])
					);
					Handles.DrawLine(
						script.transform.TransformPoint(script.cMesh.vertices[clength + i]),
						script.transform.TransformPoint(
							script.cMesh.vertices[((clength + i == clength * 2 - 1) ? clength : clength + i + 1)])
					);
					Handles.DrawLine(
						script.transform.TransformPoint(script.cMesh.vertices[i]),
						script.transform.TransformPoint(script.cMesh.vertices[clength + i])
					);
				}
				Handles.color = Color.white;
			}
		}

		private void DrawGradientControls()
		{
			//Size of controls
			float size = HandleUtility.GetHandleSize(script.transform.position) * 0.1f;
			//Get bigger and smaller dimension of the shape
			float maxSize = Mathf.Max(script.maxPoint.x - script.minPoint.x, script.maxPoint.y - script.minPoint.y);
			float minSize = Mathf.Min(script.maxPoint.x - script.minPoint.x, script.maxPoint.y - script.minPoint.y);
			//Get geometric center of the shape
			Vector2 center = Vector2.LerpUnclamped(script.minPoint, script.maxPoint, 0.5f);
			//Get center of the gradient
			Vector2 gCenter = center +
							  (Vector2.up * (maxSize * script.gradientOffset)).Rotate(script.gradientRotation - 180f);
			//Get vectors needed to draw gradient rectangle control
			Vector2 gWidth = (Vector2.right * (minSize * 0.4f)).Rotate(script.gradientRotation);
			Vector2 gHeight = (Vector2.up * ((maxSize * script.gradientScale) / 2)).Rotate(script.gradientRotation);
			if (gHeight == Vector2.zero) gHeight = gWidth.Rotate(90).normalized * 0.001f;
			//Draw gradient bounds
			Handles.color = new Color(1, 1, 1, 0.5f);
			Handles.DrawLine(
				script.transform.TransformPoint(gCenter - gWidth + gHeight),
				script.transform.TransformPoint(gCenter + gWidth + gHeight)
			);
			Handles.DrawLine(
				script.transform.TransformPoint(gCenter - gWidth - gHeight),
				script.transform.TransformPoint(gCenter + gWidth - gHeight)
			);
			Handles.color = new Color(1, 1, 1, 1f);
			//Move handle
			Handles.color = Color.white;
			Handles.DrawWireDisc(script.transform.TransformPoint(gCenter), objectPlane.normal, size * 2f);
			Handles.color = Color.clear;
			EditorGUI.BeginChangeCheck();

			#if UNITY_2022_1_OR_NEWER
			var pointCenter = Handles.FreeMoveHandle(
				script.transform.TransformPoint(gCenter),
				size*2f,
				Vector3.zero,
				Handles.CircleHandleCap
			);
			#else
			var pointCenter = Handles.FreeMoveHandle(
				script.transform.TransformPoint(gCenter),
				script.transform.rotation,
				size * 2f,
				Vector3.zero,
				Handles.CircleHandleCap
			);
			#endif

			if (Vector2.Distance(pointCenter, objectPlane.GetMousePosition()) < size * 2)
			{
				dontDrawAddPoint = true;
				UiHelper.SetCursor(MouseCursor.MoveArrow);
			}
			if (EditorGUI.EndChangeCheck())
			{
				GUI.FocusControl(null);
				Undo.RecordObject(script, Strings.Undo.GradientMove);
				Vector2 pCenterLocal = (Vector2)script.transform.InverseTransformPoint(pointCenter);
				Vector2 middlePoint = EditorGeometry.NearestPointOnLine(center, gWidth, pCenterLocal);
				script.gradientOffset = ((pCenterLocal - middlePoint).magnitude / maxSize) *
										Mathf.Sign(-(pCenterLocal - middlePoint).y) * Mathf.Sign(gHeight.y);
				script.gradientOffset = Mathf.Clamp(script.gradientOffset, -1f, 1f);
				script.UpdateMaterialSettings();
			}
			//Scale handle
			Handles.color = Color.white;
			Handles.DrawAAConvexPolygon(new Vector3[]
				{
					script.transform.TransformPoint(gCenter - gHeight + (gHeight.normalized + gWidth.normalized) * (size * 0.5f)),
					script.transform.TransformPoint(gCenter - gHeight + (-gHeight.normalized + gWidth.normalized) * (size * 0.5f)),
					script.transform.TransformPoint(gCenter - gHeight + (-gHeight.normalized - gWidth.normalized) * (size * 0.5f)),
					script.transform.TransformPoint(gCenter - gHeight + (gHeight.normalized - gWidth.normalized) * (size * 0.5f))
				}
			);
			Handles.color = Color.clear;
			EditorGUI.BeginChangeCheck();

			#if UNITY_2022_1_OR_NEWER
			var pointScale = Handles.FreeMoveHandle(
				script.transform.TransformPoint(gCenter-gHeight),
				size*0.5f,
				Vector3.zero,
				Handles.CircleHandleCap
			);
			#else
			var pointScale = Handles.FreeMoveHandle(
				script.transform.TransformPoint(gCenter - gHeight),
				script.transform.rotation,
				size * 0.5f,
				Vector3.zero,
				Handles.CircleHandleCap
			);
			#endif

			if (Vector2.Distance(pointScale, objectPlane.GetMousePosition()) < size)
			{
				dontDrawAddPoint = true;
				var a = Vector2Rotation.SignedAngle(Vector2.right, -gHeight);
				if (Mathf.Abs(a) < 22.5f) UiHelper.SetCursor(MouseCursor.ResizeHorizontal);
				else if (Mathf.Abs(a) < 67.5f) UiHelper.SetCursor(Math.Sign(a) > 0 ? MouseCursor.ResizeUpRight : MouseCursor.ResizeUpLeft);
				else if (Mathf.Abs(a) < 112.5f) UiHelper.SetCursor(MouseCursor.ResizeVertical);
				else if (Mathf.Abs(a) < 157.5f) UiHelper.SetCursor(Math.Sign(a) > 0 ? MouseCursor.ResizeUpLeft : MouseCursor.ResizeUpRight);
				else UiHelper.SetCursor(MouseCursor.ResizeHorizontal);
			}
			if (EditorGUI.EndChangeCheck())
			{
				GUI.FocusControl(null);
				Undo.RecordObject(script, Strings.Undo.GradientScale);
				script.gradientScale =
					(((Vector2)script.transform.InverseTransformPoint(pointScale) -
					  EditorGeometry.NearestPointOnLine(gCenter, gWidth,
						  script.transform.InverseTransformPoint(pointScale))).magnitude / maxSize) * 2;
				if (script.gradientScale > 2f) script.gradientScale = 2f;
				script.UpdateMaterialSettings();
			}
			//Rotation handle
			Handles.color = Color.white;
			Handles.DrawSolidDisc(script.transform.TransformPoint(gCenter + gHeight + gWidth), objectPlane.normal, size * 0.2f);
			Handles.DrawWireDisc(script.transform.TransformPoint(gCenter + gHeight + gWidth), objectPlane.normal, size * 0.6f);
			Handles.color = Color.clear;
			EditorGUI.BeginChangeCheck();

			#if UNITY_2022_1_OR_NEWER
			var pointRotation = Handles.FreeMoveHandle(
				script.transform.TransformPoint(gCenter+gHeight+gWidth),
				size*0.6f,
				Vector3.zero,
				Handles.CircleHandleCap
			);
			#else
			var pointRotation = Handles.FreeMoveHandle(
				script.transform.TransformPoint(gCenter + gHeight + gWidth),
				script.transform.rotation,
				size * 0.6f,
				Vector3.zero,
				Handles.CircleHandleCap
			);
			#endif

			if (Vector2.Distance(pointRotation, objectPlane.GetMousePosition()) <= size)
			{
				dontDrawAddPoint = true;
				UiHelper.SetCursor(MouseCursor.RotateArrow);
			}
			if (EditorGUI.EndChangeCheck())
			{
				GUI.FocusControl(null);
				Undo.RecordObject(script, Strings.Undo.GradientRotate);
				var angle = Vector2Rotation.SignedAngle(gCenter + gHeight + gWidth - center, gHeight);
				script.gradientRotation = 360f - 90f + angle - Vector2Rotation.SignedAngle(
					(Vector2)script.transform.InverseTransformPoint(pointRotation) - center,
					Vector2.right
				);
				while (script.gradientRotation < -180f) script.gradientRotation += 360f;
				while (script.gradientRotation > 180f) script.gradientRotation -= 360f;
				script.UpdateMaterialSettings();
			}
		}

		private void DrawTextureControls()
		{
			//Size of controls
			var size = HandleUtility.GetHandleSize(script.transform.position) * 0.1f;
			//Calculate center and bounds based on texture size, scaling, rotation and offset
			var tCenter = (Vector2)script.transform.position +
						  new Vector2(script.textureOffset.x * ((float)script.texture.width / 100f),
							  (float)script.textureOffset.y * (script.texture.height / 100f)) * script.textureScale;
			var tWidth = (Vector2.right * (script.texture.width / 200f) * script.textureScale).Rotate(script.textureRotation);
			var tHeight = (Vector2.up * (script.texture.height / 200f) * script.textureScale).Rotate(script.textureRotation);
			//Draw texture outline
			Handles.color = Color.white;
			Handles.DrawDottedLines(new Vector3[]
				{
					(Vector3)(tCenter - tWidth + tHeight), (Vector3)(tCenter + tWidth + tHeight),
					(Vector3)(tCenter + tWidth + tHeight), (Vector3)(tCenter + tWidth - tHeight),
					(Vector3)(tCenter + tWidth - tHeight), (Vector3)(tCenter - tWidth - tHeight),
					(Vector3)(tCenter - tWidth - tHeight), (Vector3)(tCenter - tWidth + tHeight)
				},
				0.1f / size
			);
			//Move handle
			var handleSize = ((float)Mathf.Min(script.texture.width, script.texture.height) * script.textureScale) / 400f;
			Handles.color = Color.white;
			Handles.DrawWireDisc(tCenter, objectPlane.normal, handleSize);
			Handles.color = Color.clear;
			EditorGUI.BeginChangeCheck();

			#if UNITY_2022_1_OR_NEWER
			var pointCenter = Handles.FreeMoveHandle(
				tCenter,
				handleSize,
				Vector3.zero,
				Handles.CircleHandleCap
			);
			#else
			var pointCenter = Handles.FreeMoveHandle(
				tCenter,
				script.transform.rotation,
				handleSize,
				Vector3.zero,
				Handles.CircleHandleCap
			);
			#endif

			if (Vector2.Distance(pointCenter, objectPlane.GetMousePosition()) < handleSize)
			{
				dontDrawAddPoint = true;
				UiHelper.SetCursor(MouseCursor.MoveArrow);
			}

			if (EditorGUI.EndChangeCheck())
			{
				Undo.RecordObject(script, Strings.Undo.TextureMove);
				script.textureOffset = Vector2.Scale(
					(Vector2)(pointCenter - script.transform.position),
					new Vector2(
						1f / ((script.texture.width / 100f) * script.textureScale),
						1f / ((script.texture.height / 100f) * script.textureScale)
					)
				);
				script.textureOffset.x = Mathf.Clamp(script.textureOffset.x, -1f, 1f);
				script.textureOffset.y = Mathf.Clamp(script.textureOffset.y, -1f, 1f);
				script.UpdateMaterialSettings();
			}
			//Scale handle
			Handles.color = Color.white;
			Handles.DrawAAConvexPolygon(
				new Vector3[]
				{
					tCenter - tHeight + tWidth + (tHeight.normalized + tWidth.normalized) * (size * 0.5f),
					tCenter - tHeight + tWidth + (-tHeight.normalized + tWidth.normalized) * (size * 0.5f),
					tCenter - tHeight + tWidth + (-tHeight.normalized - tWidth.normalized) * (size * 0.5f),
					tCenter - tHeight + tWidth + (tHeight.normalized - tWidth.normalized) * (size * 0.5f)
				}
			);
			Handles.color = Color.clear;
			EditorGUI.BeginChangeCheck();

			#if UNITY_2022_1_OR_NEWER
			var pointScale = Handles.FreeMoveHandle(
				tCenter-tHeight+tWidth,
				size*0.5f,
				Vector3.zero,
				Handles.CircleHandleCap
			);
			#else
			var pointScale = Handles.FreeMoveHandle(
				tCenter - tHeight + tWidth,
				script.transform.rotation,
				size * 0.5f,
				Vector3.zero,
				Handles.CircleHandleCap
			);
			#endif

			if (Vector2.Distance(pointScale, objectPlane.GetMousePosition()) < size)
			{
				dontDrawAddPoint = true;
				var a = Vector2Rotation.SignedAngle(Vector2.right, -tHeight + tWidth);
				if (Mathf.Abs(a) < 22.5f) UiHelper.SetCursor(MouseCursor.ResizeHorizontal);
				else if (Mathf.Abs(a) < 67.5f) UiHelper.SetCursor(Math.Sign(a) > 0 ? MouseCursor.ResizeUpRight : MouseCursor.ResizeUpLeft);
				else if (Mathf.Abs(a) < 112.5f) UiHelper.SetCursor(MouseCursor.ResizeVertical);
				else if (Mathf.Abs(a) < 157.5f) UiHelper.SetCursor(Math.Sign(a) > 0 ? MouseCursor.ResizeUpLeft : MouseCursor.ResizeUpRight);
				else UiHelper.SetCursor(MouseCursor.ResizeHorizontal);
			}
			if (EditorGUI.EndChangeCheck())
			{
				GUI.FocusControl(null);
				Undo.RecordObject(script, Strings.Undo.TextureScale);
				script.textureScale = Vector2.Distance(tCenter, pointScale) / Vector2.Distance(Vector2.zero,
					new Vector2(script.texture.width / 200f, script.texture.height / 200f));
				script.UpdateMaterialSettings();
			}
			//Rotation handle
			Handles.color = Color.white;
			Handles.DrawSolidDisc(tCenter + tHeight + tWidth, objectPlane.normal, size * 0.2f);
			Handles.DrawWireDisc(tCenter + tHeight + tWidth, objectPlane.normal, size * 0.6f);
			Handles.color = Color.clear;
			EditorGUI.BeginChangeCheck();

			#if UNITY_2022_1_OR_NEWER
			var pointRotation = Handles.FreeMoveHandle(
				tCenter+tHeight+tWidth,
				size*0.6f,
				Vector3.zero,
				Handles.CircleHandleCap
			);
			#else
			var pointRotation = Handles.FreeMoveHandle(
				tCenter + tHeight + tWidth,
				script.transform.rotation,
				size * 0.6f,
				Vector3.zero,
				Handles.CircleHandleCap
			);
			#endif

			if (Vector2.Distance(pointRotation, objectPlane.GetMousePosition()) <= size)
			{
				dontDrawAddPoint = true;
				UiHelper.SetCursor(MouseCursor.RotateArrow);
			}
			if (EditorGUI.EndChangeCheck())
			{
				Undo.RecordObject(script, Strings.Undo.TextureRotate);
				var angle = Vector2Rotation.SignedAngle(tHeight + tWidth, tHeight);
				script.textureRotation = 360f - 90f + angle - Vector2Rotation.SignedAngle((Vector2)pointRotation - tCenter, Vector2.right);
				while (script.textureRotation < -180f) script.textureRotation += 360f;
				while (script.textureRotation > 180f) script.textureRotation -= 360f;
				script.UpdateMaterialSettings();
			}
		}

		private void DrawGrid()
		{
			Handles.color = new Color(1f, 1f, 1f, 0.3f);
			//Get start and end points for the grid
			Vector2 start = script.transform.TransformPoint(script.minPoint);
			Vector2 end = script.transform.TransformPoint(script.maxPoint);
			//If local grid is selected, we transform to local coordinates
			if (script.snapType == PS2DSnapType.LocalGrid)
			{
				start = script.transform.InverseTransformPoint(start);
				end = script.transform.InverseTransformPoint(end);
			}
			//Round start and end points to be on the grid
			start.x = Mathf.Floor(start.x / script.gridSize) * script.gridSize;
			start.y = Mathf.Floor(start.y / script.gridSize) * script.gridSize;
			end.x = Mathf.Ceil(end.x / script.gridSize) * script.gridSize;
			end.y = Mathf.Ceil(end.y / script.gridSize) * script.gridSize;
			//Now that we've rounded the coordinates, if it's a local grid, we convert them back to global
			if (script.snapType == PS2DSnapType.LocalGrid)
			{
				start = script.transform.TransformPoint(start);
				end = script.transform.TransformPoint(end);
			}
			//How many lines to draw
			var linesX = (int)Mathf.Round((end.x - start.x) / script.gridSize);
			var linesY = (int)Mathf.Round((end.y - start.y) / script.gridSize);
			//Draw vertical lines
			for (int i = 0; i <= linesX; i++)
			{
				Handles.DrawDottedLine(
					new Vector3(start.x + i * script.gridSize, start.y - script.gridSize, 0f),
					new Vector3(start.x + i * script.gridSize, end.y + script.gridSize, 0f),
					1f
				);
			}
			//Draw horizontal lines
			for (var i = 0; i <= linesY; i++)
			{
				Handles.DrawDottedLine(
					new Vector3(start.x - script.gridSize, start.y + i * script.gridSize, 0f),
					new Vector3(end.x + script.gridSize, start.y + i * script.gridSize, 0f),
					1f
				);
			}
			Handles.color = Color.white;
		}
		
		private void DeselectAllPoints()
		{
			script.DeselectAllPoints();
			Repaint();
		}

		private void SelectPoint(int i, bool state)
		{
			script.SelectPoint(i, state);
			Repaint();
		}

		//Move pivot of the object to do this, we rearrange all points around the new pivot and then just move the object
		private void MovePivot()
		{
			//Get min and max positions
			var min = Vector2.one * Mathf.Infinity;
			var max = -Vector2.one * 9999f;
			for (var i = 0; i < script.pointsFinal.Count; i++)
			{
				if (script.pointsFinal[i].x < min.x) min.x = script.pointsFinal[i].x;
				if (script.pointsFinal[i].y < min.y) min.y = script.pointsFinal[i].y;
				if (script.pointsFinal[i].x > max.x) max.x = script.pointsFinal[i].x;
				if (script.pointsFinal[i].y > max.y) max.y = script.pointsFinal[i].y;
			}
			//Calculate the difference
			var newPivot = new Vector2();
			if (script.PivotPosition == PS2DPivotPosition.Center) newPivot = Vector2.Lerp(min, max, 0.5f);
			if (script.PivotPosition == PS2DPivotPosition.Top) newPivot = new Vector2(Mathf.Lerp(min.x, max.x, 0.5f), max.y);
			if (script.PivotPosition == PS2DPivotPosition.Right) newPivot = new Vector2(max.x, Mathf.Lerp(min.y, max.y, 0.5f));
			if (script.PivotPosition == PS2DPivotPosition.Bottom) newPivot = new Vector2(Mathf.Lerp(min.x, max.x, 0.5f), min.y);
			if (script.PivotPosition == PS2DPivotPosition.Left) newPivot = new Vector2(min.x, Mathf.Lerp(min.y, max.y, 0.5f));
			//Do the moving
			MovePivot(newPivot);
		}

		private void MovePivot(Vector2 newPivot)
		{
			//Difference between projected and real pivots converted to lcoal scale
			var diff = newPivot - (Vector2)script.transform.InverseTransformPoint((Vector2)script.transform.position);
			//To record full state we need to use RegisterFullObjectHierarchyUndo
			Undo.RegisterFullObjectHierarchyUndo(script, Strings.Undo.MovePivot);
			//Use it to move the points and children
			for (var i = 0; i < script.points.Count; i++)
			{
				script.points[i].Move(-diff, true);
			}
			if (script.transform.childCount > 0)
			{
				for (var i = 0; i < script.transform.childCount; i++)
				{
					script.transform.GetChild(i).transform.localPosition -= (Vector3)diff;
				}
			}
			//Convert projected pivot to world coordinates and move object to it
			Vector2 projectedPivotWorld = script.transform.TransformPoint(newPivot);
			script.transform.position = new Vector3(projectedPivotWorld.x, projectedPivotWorld.y, script.transform.position.z);
			script.UpdateMesh();
			//For undo
			EditorUtility.SetDirty(script);
		}

		#endregion
		
		#region Point properties window

		private void DrawPointsProperties()
		{
			var et = Event.current.type;
			var selected = "";
			var selectedPoints = new List<int>();
			for (var i = 0; i < script.points.Count; i++)
			{
				if (script.points[i].selected)
				{
					if (selected.Length > 0) selected += ",";
					selected += " " + i.ToString();
					selectedPoints.Add(i);
				}
			}
			//EditorWindow
			if (selectedPoints.Count <= 0 || et == EventType.Repaint)
			{
				return;
			}
			var options = new GUILayoutOption[]
			{
				GUILayout.MaxWidth(200), // Max width to prevent overly wide windows
				GUILayout.MinHeight(50) // Minimum height to accommodate content
			};
			var currentViewRect = Camera.current.pixelRect;
			var windowSize = new Vector2(140, 66); // Desired window size
			var windowPosition = new Vector2(
				currentViewRect.width / EditorGUIUtility.pixelsPerPoint - windowSize.x -
				3, //Last number is the offset from the right edge
				currentViewRect.height / EditorGUIUtility.pixelsPerPoint - windowSize.y -
				2 //Last number is the offset from the bottom edge
			);
			var windowStyle = new GUIStyle(GUI.skin.window) { margin = new RectOffset(5, 5, 5, 5) };
			var pointWindowRect = new Rect(windowPosition.x, windowPosition.y, windowSize.x, windowSize.y);
			GUILayout.Window(0, pointWindowRect, (id) =>
			{
				//Working with temporary vars for undo
				var pos = script.points[selectedPoints[0]].position;
				var curve = script.points[selectedPoints[0]].curve;
				//Define the window
				EditorGUILayout.BeginHorizontal();
				EditorGUIUtility.labelWidth = 20;
				pos.x = EditorGUILayout.FloatField("X", pos.x, GUILayout.Width(64));
				pos.y = EditorGUILayout.FloatField("Y", pos.y, GUILayout.Width(64));
				EditorGUILayout.EndHorizontal();
				//Bezier types switch
				var type = script.points[selectedPoints[0]].pointType;
				var updateType = false;
				var updateCurve = false;
				var autoHandles = false;
				var straightenHandles = false;
				//See if all points have same curve radius and/or curve type
				var allPointsHaveSameCurve = true;
				var allPointsHaveSameType = true;
				for (int i = 0; i < selectedPoints.Count; i++)
				{
					if (script.points[selectedPoints[i]].pointType !=
						script.points[selectedPoints.Loop(i + 1)].pointType && allPointsHaveSameType)
					{
						allPointsHaveSameType = false;
					}
					if (script.points[selectedPoints[i]].curve != script.points[selectedPoints.Loop(i + 1)].curve &&
						allPointsHaveSameCurve)
					{
						allPointsHaveSameCurve = false;
					}
				}
				if (script.type == PS2DType.Simple)
				{
					EditorGUI.showMixedValue = !allPointsHaveSameCurve;
					var _curve = EditorGUILayout.Slider(curve, 0f, 1f);
					if (_curve != curve)
					{
						curve = _curve;
						updateCurve = true;
					}
					EditorGUI.showMixedValue = false;
				}
				else if (script.type == PS2DType.Bezier)
				{
					EditorGUILayout.BeginHorizontal();
					if (GUILayout.Toggle((type == PS2DPointType.None && allPointsHaveSameType),
							(Texture)Resources.Load("Icons/bezierNone"), "Button"))
					{
						curve = 0f;
						updateCurve = true;
						type = PS2DPointType.None;
						updateType = true;
					}

					if (GUILayout.Toggle((type == PS2DPointType.Sharp && allPointsHaveSameType),
							(Texture)Resources.Load("Icons/bezierSharp"), "Button"))
					{
						if (curve == 0)
						{
							curve = 0.5f;
							updateCurve = true;
							autoHandles = true;
						}

						type = PS2DPointType.Sharp;
						updateType = true;
					}

					if (GUILayout.Toggle((type == PS2DPointType.Rounded && allPointsHaveSameType),
							(Texture)Resources.Load("Icons/bezierRounded"), "Button"))
					{
						if (curve == 0)
						{
							curve = 0.5f;
							updateCurve = true;
							autoHandles = true;
						}
						else if (script.points[selectedPoints[0]].pointType == PS2DPointType.Sharp)
						{
							straightenHandles = true;
						}

						type = PS2DPointType.Rounded;
						updateType = true;
					}

					EditorGUILayout.EndHorizontal();
				}
				//React to change
				if (GUI.changed)
				{
					Undo.RecordObject(script, Strings.Undo.EditPoint);
					//Calculate the movement
					var move = pos - script.points[selectedPoints[0]].position;
					//Update each selected point
					foreach (var selectedPoint in selectedPoints)
					{
						script.points[selectedPoint].Move(move, script.type == PS2DType.Bezier ? true : false);
						if (updateCurve)
						{
							script.points[selectedPoint].curve = Mathf.Round(curve * 100f) / 100;
							if (script.points[selectedPoint].curve == 0)
							{
								script.points[selectedPoint].handleN = script.points[selectedPoint].position;
								script.points[selectedPoint].handleP = script.points[selectedPoint].position;
							}
						}

						if (updateType) script.points[selectedPoint].pointType = type;
						if (autoHandles) script.GenerateHandles(selectedPoint);
						if (straightenHandles) script.points[selectedPoint].StraightenHandles();
					}
					script.UpdateMesh();
					EditorUtility.SetDirty(script);
				}

				//Make window draggable but don't actually allow to drag it. It's a hack so a window wouldn't disappear on click.
				if (Event.current.type != EventType.MouseDrag)
				{
					GUI.DragWindow();
				}
			}, "Point" + selected, windowStyle, options);
		}

		#endregion

		#region Helper methods

		private void ExportMesh()
		{
			script.UpdateMesh();
			var mesh = script.GetMesh();
			if (System.IO.File.Exists("Assets/" + mesh.name.ToString() + ".asset") && !EditorUtility.DisplayDialog(
					Strings.Warning, Strings.MeshOverwriteWarning, Strings.Overwrite, Strings.Cancel))
			{
				return;
			}
			AssetDatabase.CreateAsset(UnityEngine.Object.Instantiate(mesh), "Assets/" + mesh.name.ToString() + ".asset");
			AssetDatabase.SaveAssets();
		}

		private void ExportPNG()
		{
			script.UpdateMesh();
			//Move current object to the root of the scene
			var prevParent = script.transform.parent;
			script.transform.parent = null;
			//Disable all root game objects except the current one and main camera
			var rootList = UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects();
			var disableList = new List<GameObject>(50);
			foreach (var go in rootList)
			{
				if (go.activeSelf && go != script.gameObject)
				{
					disableList.Add(go);
					go.SetActive(false);
				}
			}
			//Create a temporary camera
			var tempCameraGo = new GameObject { name = "Screenshot Camera" };
			var tempCamera = tempCameraGo.AddComponent<Camera>();
			tempCamera.cameraType = CameraType.Game;
			tempCamera.orthographic = true;
			tempCamera.enabled = false;
			tempCamera.clearFlags = CameraClearFlags.Color;
			//Center camera on the object and set the size
			var meshBounds = script.GetComponent<MeshRenderer>().bounds;
			tempCameraGo.transform.position = meshBounds.center + Vector3.back;
			tempCamera.orthographicSize = Mathf.Max(meshBounds.size.x / 2, meshBounds.size.y);
			//Crete render texture with antialiasing
			var rt = new RenderTexture((int)(meshBounds.size.x * 200), (int)(meshBounds.size.y * 200), 0)
			{
				antiAliasing = 8,
				autoGenerateMips = false
			};
			tempCamera.targetTexture = rt;
			RenderTexture.active = tempCamera.targetTexture;
			//Render image with white background
			tempCamera.backgroundColor = Color.white;
			tempCamera.Render();
			var imageWhite = new Texture2D(tempCamera.targetTexture.width, tempCamera.targetTexture.height, TextureFormat.RGB24, false);
			imageWhite.ReadPixels(new Rect(0, 0, tempCamera.targetTexture.width, tempCamera.targetTexture.height), 0, 0);
			//Render image with black background
			tempCamera.backgroundColor = Color.black;
			tempCamera.Render();
			var imageBlack = new Texture2D(tempCamera.targetTexture.width, tempCamera.targetTexture.height, TextureFormat.RGB24, false);
			imageBlack.ReadPixels(new Rect(0, 0, tempCamera.targetTexture.width, tempCamera.targetTexture.height), 0, 0);
			//Create image with alpha by comparing black and white bg images
			var imageTrans = new Texture2D(tempCamera.targetTexture.width, tempCamera.targetTexture.height, TextureFormat.RGBA32, false);
			for (var y = 0; y < imageTrans.height; ++y)
			{
				for (var x = 0; x < imageTrans.width; ++x)
				{
					var alpha = imageWhite.GetPixel(x, y).r - imageBlack.GetPixel(x, y).r;
					alpha = 1.0f - alpha;
					Color color;
					if (alpha == 0)
					{
						color = Color.clear;
					}
					else
					{
						color = imageBlack.GetPixel(x, y) / alpha;
					}
					color.a = alpha;
					imageTrans.SetPixel(x, y, color);
				}
			}
			//Crop excessive transparent color from image
			var cropImage = ImageHelper.CropUsingColor(imageTrans, Color.clear);
			//Come up with a unique name for an image
			string filename;
			var iterator = 0;
			do
			{
				filename = Application.dataPath + "/" + script.name + (iterator > 0 ? " (" + iterator.ToString() + ")" : "") + ".png";
				iterator++;
			} while (File.Exists(filename));
			//Save an image to PNG
			File.WriteAllBytes(filename, cropImage.EncodeToPNG());
			//Return things to their original state
			RenderTexture.active = null;
			DestroyImmediate(tempCameraGo);
			AssetDatabase.Refresh();
			//Enable the objects we disabled previously
			foreach (var disabledGo in disableList)
			{
				disabledGo.SetActive(true);
			}
			//Return object to its original parent
			script.transform.parent = prevParent;
		}

		private void AddCollider()
		{
			switch (script.colliderType)
			{
				case PS2DColliderType.PolygonStatic:
					script.gameObject.AddComponent<Rigidbody2D>();
					script.GetComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Static;
					script.gameObject.AddComponent<PolygonCollider2D>();
					break;
				case PS2DColliderType.PolygonDynamic:
					script.gameObject.AddComponent<Rigidbody2D>();
					script.GetComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Dynamic;
					script.gameObject.AddComponent<PolygonCollider2D>();
					break;
				case PS2DColliderType.Edge:
					script.gameObject.AddComponent<Rigidbody2D>();
					script.GetComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Static;
					script.gameObject.AddComponent<EdgeCollider2D>();
					break;
				case PS2DColliderType.TopEdge:
					script.gameObject.AddComponent<Rigidbody2D>();
					script.GetComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Static;
					script.gameObject.AddComponent<EdgeCollider2D>();
					script.GetComponent<EdgeCollider2D>().usedByEffector = true;
					script.gameObject.AddComponent<PlatformEffector2D>();
					script.GetComponent<PlatformEffector2D>().surfaceArc = 90f;
					break;
				case PS2DColliderType.MeshStatic:
					script.gameObject.AddComponent<MeshCollider>();
					break;
				case PS2DColliderType.MeshDynamic:
					script.gameObject.AddComponent<Rigidbody>();
					script.gameObject.AddComponent<MeshCollider>();
					script.GetComponent<MeshCollider>().convex = true;
					break;
			}
			InternalEditorUtility.SetIsInspectorExpanded(script.GetComponent<PolygonCollider2D>(), false);
			InternalEditorUtility.SetIsInspectorExpanded(script.GetComponent<EdgeCollider2D>(), false);
			InternalEditorUtility.SetIsInspectorExpanded(script.GetComponent<Rigidbody2D>(), false);
			InternalEditorUtility.SetIsInspectorExpanded(script.GetComponent<MeshCollider>(), false);
			InternalEditorUtility.SetIsInspectorExpanded(script.GetComponent<Rigidbody>(), false);
		}

		private bool RemoveCollider(PS2DColliderType nextType)
		{
			var ok = true;
			if (script.GetComponent<Collider2D>() != null || script.GetComponent<MeshCollider>())
			{
				if (nextType == PS2DColliderType.None)
				{
					ok = EditorUtility.DisplayDialog(Strings.Warning, Strings.RemoveCollidersWarning, Strings.Remove, Strings.KeepCollider);
				}
				else
				{
					ok = EditorUtility.DisplayDialog(Strings.Warning, Strings.RecreateCollidersWarning, Strings.Overwrite, Strings.KeepCollider);
				}
				if (ok)
				{
					while (script.GetComponent<Collider2D>() != null)
					{
						DestroyImmediate(script.GetComponent<Collider2D>());
					}
					while (script.GetComponent<MeshCollider>() != null)
					{
						DestroyImmediate(script.GetComponent<MeshCollider>());
					}
					while (script.GetComponent<PlatformEffector2D>() != null)
					{
						DestroyImmediate(script.GetComponent<PlatformEffector2D>());
					}
					while (script.GetComponent<Rigidbody2D>() != null)
					{
						DestroyImmediate(script.GetComponent<Rigidbody2D>());
					}
					while (script.GetComponent<Rigidbody>() != null)
					{
						DestroyImmediate(script.GetComponent<Rigidbody>());
					}
				}
			}
			return ok;
		}

		private void CircleHandleCapSaveIDWrapper(int controlID, Vector3 position, Quaternion rotation, float size, EventType et)
		{
			lastControlID = controlID;
			Handles.CircleHandleCap(controlID, position, rotation, size, et);
		}
		
		#endregion
	}
}