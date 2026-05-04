using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ProtoShape2D.Extensions;
using ProtoShape2D.Helpers;
using UnityEngine.Rendering;
using UnityEngine.Serialization;
using Random = UnityEngine.Random;

namespace ProtoShape2D
{
	[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer)), ExecuteInEditMode]
	public class ProtoShape2D : MonoBehaviour
	{

		#region Configurable parameters

		//Type of the curve
		public PS2DType type = PS2DType.Simple;

		//Fill parameters
		public PS2DFillType fillType = PS2DFillType.Color;

		//Texture
		public Texture2D texture = null;
		public float textureScale = 1f;
		public float textureRotation = 0f;
		public Vector2 textureOffset;
		public Color color1 = Color.red;

		public Color color2 = Color.red;

		//Gradient
		public float gradientScale = 1f;
		public float gradientRotation = 0;

		public float gradientOffset = 0;

		//Outline parameters
		public float outlineWidth = 0f;
		public Color outlineColor = Color.red;
		public bool outlineLoop = true;
		public int outlineConnect = 0;
		public bool outlineUseCustomMaterial = false;

		public Material outlineCustomMaterial;

		//Curve iterations
		public int curveIterations = 10;

		//For fake antialiasing
		public bool antialias = false;

		public float aaridge = 0.002f;

		//Snap settings
		public PS2DSnapType snapType = PS2DSnapType.Points;

		public float gridSize = 1f;

		//For collider type
		public PS2DColliderType colliderType = PS2DColliderType.None;
		public float colliderTopAngle = 90f;
		public float colliderOffsetTop = 0f;

		public bool showNormals = false;

		//For pivot adjustment
		public PS2DPivotType pivotType = PS2DPivotType.Manual;

		public PS2DPivotPosition PivotPosition = (PS2DPivotPosition)0;

		//HDR colors
		public bool HDRColors = false;

		//For sorting layers
		public int sortingLayer = 0;
		[SerializeField] private int _sortingLayer;
		public int orderInLayer = 0;

		[SerializeField] private int _orderInLayer;

		//For mask options
		public int selectedMaskOption = 0;
		[SerializeField] private int _selectedMaskOption;

		#endregion

		#region Internal parameters

		//Auto-generated unique name
		[SerializeField] public string uniqueName = "";

		//User-editable points
		public List<PS2DPoint> points = new(200);

		//Automatically generated points that include smoothing
		//The list is public so the editor script could draw the outline, move the pivot and place the marker for new points
		public List<Vector3> pointsFinal = new(500);

		//Points for a collider, generated automatically, include normals which we can use for edge collider
		public List<PS2DColliderPoint> cpoints = new(500);

		//Actual points to assign to a collider, also used to draw collider in the scene view
		public Vector2[] cpointsFinal;

		//For calculating normals
		public bool clockwise = true;

		//Mesh management
		[SerializeField] private MeshRenderer mr;
		[SerializeField] private MeshFilter mf;
		[SerializeField] private List<Vector3> vertices = new();
		[SerializeField] private List<Color> colors = new();
		[SerializeField] private List<Vector2> uvs = new();
		[SerializeField] private int[] tris;
		[SerializeField] private int[] trisOutline;

		[SerializeField] private int[] trisAntialiasing;

		//Just for displaying the number of triangles
		public int triangleCount = 0;

		//Collider management
		[SerializeField] private Collider2D col;

		[SerializeField] private MeshCollider mcol;

		//A width (depth) of mesh for the mesh collider
		public float cMeshDepth = 3f;

		//A mesh for mesh collider
		public Mesh cMesh;

		//To track the position of the object between every frame
		[SerializeField] private Vector2 lastPos;

		//Min and max point of the object
		public Vector2 minPoint;
		public Vector2 maxPoint;

		//For foldouts in inspector
		public bool showFillSettings = true;
		public bool showOutlineSettings = true;
		public bool showMeshSetting = true;
		public bool showSnapSetting = true;
		public bool showColliderSettings = true;
		public bool showTools = true;
		
		//Expose all points under Mesh section
		public bool exposePointsInInspector = false;

		//Materials
		public Material defaultMaterial = null;
		public Material spriteMaterial = null;
		public Material customMaterial = null;

		//Reference to a scriptable object with some global parameters
		private static PS2DGlobals _globals;

		#endregion
		
		#region Context menu
		
		[ContextMenu("Toggle raw points list in inspector")]
		private void ExposePointsInInspector()
		{
			exposePointsInInspector = !exposePointsInInspector;
			showMeshSetting = showMeshSetting || exposePointsInInspector;
		}
		
		[ContextMenu("Online help...")]
		private void OpenOnlineHelp()
		{
			Application.OpenURL("https://ax23w4.com/devlog/protoshape2d");
		}
		
		#endregion

		#region Awake, Update, Destroy

		private void Awake()
		{
			//Get all components if needed
			if (mr == null) mr = GetComponent<MeshRenderer>();
			if (mf == null) mf = GetComponent<MeshFilter>();
			if (col == null) col = GetComponent<Collider2D>();
			if (mcol == null) mcol = GetComponent<MeshCollider>();
			//Get reference to global ProtoShape2DSettings object if it's not set yet
			if (_globals == null) _globals = (PS2DGlobals)Resources.Load("ProtoShape2DGlobals");
			//If it's a new object, we define a unique name and set up renderer settings and material
			if (uniqueName == "")
			{
				uniqueName = "PS2D-"+Random.Range(1000, 999999).ToString();
				mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
				mr.receiveShadows = false;
				mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
				mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
				SetSpriteMaterial();
			}
			//It already has unique name, but is it really unique? Happens when you clone an object or instantiate a prefab
			else
			{
				var otherProtoShapes = GameObject.FindObjectsOfType<ProtoShape2D>();
				foreach (var otherProtoShape in otherProtoShapes)
				{
					if (otherProtoShape.uniqueName != uniqueName)
					{
						continue;
					}
					uniqueName = Random.Range(1000, 999999).ToString();
					if (fillType == PS2DFillType.Color || fillType == PS2DFillType.None)
					{
						SetSpriteMaterial();
					}
					else if (fillType == PS2DFillType.CustomMaterial)
					{
						SetCustomMaterial();
					}
					else
					{
						//Since it's a copy of an object, we unset the references to original object's materials
						this.defaultMaterial = null;
						mr.sharedMaterial = null;
						//This will create the new default material instead of using material of the original object
						SetDefaultMaterial();
					}
				}
			}
			var mesh = new Mesh { name = uniqueName };
			mf.sharedMesh = mesh;
			lastPos = transform.position;
			UpdateMaterialSettings();
			UpdateMesh();
		}

		private void Update()
		{
			if (mr.sharedMaterial != null && fillType != PS2DFillType.CustomMaterial &&
				fillType != PS2DFillType.Color && fillType != PS2DFillType.None)
			{
				if (!lastPos.Equals(transform.position))
				{
					lastPos = transform.position;
					mr.sharedMaterial.SetVector("_WPos", transform.position);
					mr.sharedMaterial.SetVector("_MinWPos", mr.bounds.min);
					mr.sharedMaterial.SetVector("_MaxWPos", mr.bounds.max);
				}
			}

			if (sortingLayer != _sortingLayer || orderInLayer != _orderInLayer)
			{
				mr.sortingLayerID = sortingLayer;
				mr.sortingOrder = orderInLayer;
				_sortingLayer = sortingLayer;
				_orderInLayer = orderInLayer;
			}

			//If mask setting changed we have to unset the material and force updating it or recreating it if needed
			if (selectedMaskOption != _selectedMaskOption)
			{
				UpdateMaterialSettings();
				_selectedMaskOption = selectedMaskOption;
			}
		}

		private void OnDestroy()
		{
			DestroyDefaultMaterialIfExists();
		}

		#endregion

		#region Setting and updating materials

		public void SetSpriteMaterial()
		{
			//Get reference to global ProtoShape2DSettings object if it's not set yet
			if (_globals == null)
			{
				_globals = (PS2DGlobals)Resources.Load("ProtoShape2DGlobals");
			}

			//Based on selected mask option, assign correct sprite material using reference to material from globals object
			//If it doesn't exist, we create it
			if (selectedMaskOption == 0)
			{
				if (_globals.colorMaterialNoStencil == null)
				{
					//Debug.Log("Creating new "+globals.colorMaterialNoStencil.name);
					_globals.colorMaterialNoStencil = new Material(Shader.Find("ProtoShape2D/Color"));
					_globals.colorMaterialNoStencil.SetFloat("_StencilComp", (int)UnityEngine.Rendering.CompareFunction.Disabled);
					_globals.colorMaterialNoStencil.name = "PS2DColor";
				}

				if (mr.sharedMaterial == null || !mr.sharedMaterial.Equals(_globals.colorMaterialNoStencil))
				{
					mr.sharedMaterial = _globals.colorMaterialNoStencil;
				}
			}
			else if (selectedMaskOption == 1)
			{
				if (_globals.colorMaterialStencilEqual == null)
				{
					_globals.colorMaterialStencilEqual = new Material(Shader.Find("ProtoShape2D/Color"));
					_globals.colorMaterialStencilEqual.SetFloat("_StencilComp",
						(int)UnityEngine.Rendering.CompareFunction.Equal);
					_globals.colorMaterialStencilEqual.name = "PS2DColorMaskInside";
				}

				if (mr.sharedMaterial == null || !mr.sharedMaterial.Equals(_globals.colorMaterialStencilEqual))
				{
					mr.sharedMaterial = _globals.colorMaterialStencilEqual;
				}
			}
			else if (selectedMaskOption == 2)
			{
				if (_globals.colorMaterialStencilNotEqual == null)
				{
					_globals.colorMaterialStencilNotEqual = new Material(Shader.Find("ProtoShape2D/Color"));
					_globals.colorMaterialStencilNotEqual.SetFloat("_StencilComp",
						(int)UnityEngine.Rendering.CompareFunction.NotEqual);
					_globals.colorMaterialStencilNotEqual.name = "PS2DColorMaskOutside";
				}

				if (mr.sharedMaterial == null || !mr.sharedMaterial.Equals(_globals.colorMaterialStencilNotEqual))
				{
					mr.sharedMaterial = _globals.colorMaterialStencilNotEqual;
				}
			}
		}

		public void SetDefaultMaterial()
		{
			if (defaultMaterial == null)
			{
				defaultMaterial = new Material(Shader.Find("ProtoShape2D/TextureAndColors"));
				//Setting mask compare function. It can only be set when material is first created, updating it dynamicallyd desn't do anything
				if (selectedMaskOption == 0)
				{
					defaultMaterial.SetFloat("_StencilComp", (int)UnityEngine.Rendering.CompareFunction.Disabled);
					defaultMaterial.name = "PS2DTextureAndColors";
				}
				else if (selectedMaskOption == 1)
				{
					defaultMaterial.SetFloat("_StencilComp", (int)UnityEngine.Rendering.CompareFunction.Equal);
					defaultMaterial.name = "PS2DTextureAndColorsMaskInside";
				}
				else if (selectedMaskOption == 2)
				{
					defaultMaterial.SetFloat("_StencilComp", (int)UnityEngine.Rendering.CompareFunction.NotEqual);
					defaultMaterial.name = "PS2DTextureAndColorsMaskOutside";
				}
			}

			//Set to default material only if it's not already set to default material
			if (mr.sharedMaterial == null || !mr.sharedMaterial.Equals(defaultMaterial))
			{
				mr.sharedMaterial = defaultMaterial;
			}
		}

		public void DestroyDefaultMaterialIfExists()
		{
			if (defaultMaterial != null)
			{
				if (Application.isEditor)
				{
					DestroyImmediate(defaultMaterial);
				}
				else
				{
					Destroy(defaultMaterial);
				}

				defaultMaterial = null;
			}
		}

		public void SetCustomMaterial()
		{
			if (customMaterial == null)
			{
				customMaterial = new Material(Shader.Find("ProtoShape2D/TextureAndColors"));
			}
			SetCustomMaterial(customMaterial);
		}

		public void SetCustomMaterial(Material mat)
		{
			if (mr.sharedMaterial == null || !mr.sharedMaterial.Equals(mat))
			{
				customMaterial = mat;
				mr.sharedMaterial = customMaterial;
			}
		}

		public void SetOutlineCustomMaterial()
		{
			if (outlineCustomMaterial != null)
			{
				SetOutlineCustomMaterial(outlineCustomMaterial);
			}
		}

		public void SetOutlineCustomMaterial(Material mat)
		{
			if (mr.sharedMaterials.Length > 1 && (mr.sharedMaterials[1] == null || !mr.sharedMaterials[1].Equals(mat)))
			{
				outlineCustomMaterial = mat;
				mr.sharedMaterials[1] = outlineCustomMaterial;
			}
		}

		public void UpdateMaterialSettings()
		{
			if (mf != null && mr != null)
			{
				//If mask setting changed we have to unset the material and force updating it or recreating it if needed
				if (selectedMaskOption != _selectedMaskOption)
				{
					DestroyDefaultMaterialIfExists();
					mr.sharedMaterial = null;
				}

				//If no material is set, we set one based on selected fill type
				if (mr.sharedMaterials[0] == null)
				{
					if (fillType == PS2DFillType.Color || fillType == PS2DFillType.None) SetSpriteMaterial();
					else if (fillType == PS2DFillType.CustomMaterial) SetCustomMaterial();
					else SetDefaultMaterial();
				}

				//In case we're using the gradient/texture material
				if (mr.sharedMaterials[0] != null && fillType != PS2DFillType.CustomMaterial &&
					fillType != PS2DFillType.Color && fillType != PS2DFillType.None)
				{
					if (fillType == PS2DFillType.Texture)
					{
						mr.sharedMaterials[0].SetVector("_Color1", Color.white);
						mr.sharedMaterials[0].SetVector("_Color2", Color.white);
					}

					if (fillType == PS2DFillType.Color || fillType == PS2DFillType.TextureWithColor)
					{
						mr.sharedMaterials[0].SetVector("_Color1", color1);
						mr.sharedMaterials[0].SetVector("_Color2", color1);
					}

					if (fillType == PS2DFillType.Gradient || fillType == PS2DFillType.TextureWithGradient)
					{
						mr.sharedMaterials[0].SetVector("_Color1", color1);
						mr.sharedMaterials[0].SetVector("_Color2", color2);
					}

					mr.sharedMaterials[0].SetFloat("_GradientAngle", gradientRotation);
					mr.sharedMaterials[0].SetFloat("_GradientScale", gradientScale);
					mr.sharedMaterials[0].SetFloat("_GradientOffset", gradientOffset);
					if (fillType == PS2DFillType.Texture || fillType == PS2DFillType.TextureWithColor ||
						fillType == PS2DFillType.TextureWithGradient)
					{
						mr.sharedMaterials[0].SetTexture("_Texture", texture);
						//Set texture size
						if (texture != null)
						{
							mr.sharedMaterials[0].SetTextureScale("_Texture",
								new Vector2(texture.width, texture.height) / 100f * textureScale);
							mr.sharedMaterials[0].SetFloat("_TextureAngle", textureRotation);
							mr.sharedMaterials[0].SetVector("_TextureOffset", textureOffset);
						}
					}
					else
					{
						mr.sharedMaterials[0].SetTexture("_Texture", null);
					}
				}

				//If the setting changed and number of materials does not match the settings
				if ((outlineUseCustomMaterial && mr.sharedMaterials.Length == 1) ||
					(!outlineUseCustomMaterial && mr.sharedMaterials.Length == 2))
				{
					Material[] materials = new Material[1 + (outlineUseCustomMaterial ? 1 : 0)];
					materials[0] = mr.sharedMaterials[0];
					if (outlineUseCustomMaterial) materials[1] = this.outlineCustomMaterial;
					mr.sharedMaterials = materials;
					UpdateMesh();
				}
			}
		}

		#endregion

		#region Generating mesh

		public void UpdateMesh()
		{
			if (mf != null && mr != null && mr.sharedMaterial != null)
			{

				//Find if polygon is drawn clockwise or counterclockwise
				float edgeSum = 0;
				for (int i = 0; i < points.Count; i++)
				{
					edgeSum += (points[i].position.x - points.Loop(i + 1).position.x) *
							   (points[i].position.y + points.Loop(i + 1).position.y);
				}

				clockwise = edgeSum <= 0;

				//Generate bezier handles
				if (type == PS2DType.Simple) GenerateHandles();

				//Clear mesh properties
				vertices.Clear();
				colors.Clear();
				uvs.Clear();
				minPoint = Vector2.one * 9999f;
				maxPoint = -Vector2.one * 9999f;

				//Counting how many outline points we want to connenct
				outlineConnect = 0;
				//Decide vertex color
				var vcolor = (fillType == PS2DFillType.Color ? color1 : Color.white);
				//Generate vertices and colors, get bounds
				for (var i = 0; i < points.Count; i++)
				{
					if (
						(type == PS2DType.Simple && (points[i].curve > 0f || points.Loop(i + 1).curve > 0f)) ||
						(type == PS2DType.Bezier && (points[i].pointType != PS2DPointType.None || points.Loop(i + 1).pointType != PS2DPointType.None)))
					{
						for (var j = 0; j < curveIterations; j++)
						{
							vertices.Add((Vector2)Bezier.CalculateBezierPoint(
								(float)j / (float)curveIterations,
								points[i].position,
								points[i].handleN,
								points.Loop(i + 1).handleP,
								points.Loop(i + 1).position
							));
							colors.Add(color1);
							if (vertices[vertices.Count - 1].x < minPoint.x)
								minPoint.x = vertices[vertices.Count - 1].x;
							if (vertices[vertices.Count - 1].y < minPoint.y)
								minPoint.y = vertices[vertices.Count - 1].y;
							if (vertices[vertices.Count - 1].x > maxPoint.x)
								maxPoint.x = vertices[vertices.Count - 1].x;
							if (vertices[vertices.Count - 1].y > maxPoint.y)
								maxPoint.y = vertices[vertices.Count - 1].y;
						}

						//Counting how many outline points we want to connenct
						if (outlineLoop || i < points.Count - 1) outlineConnect += curveIterations;
						if (!outlineLoop && i == points.Count - 1) outlineConnect++;
					}
					else
					{
						vertices.Add((Vector3)points[i].position);
						colors.Add(vcolor);
						//Get 
						if (points[i].position.x < minPoint.x) minPoint.x = points[i].position.x;
						if (points[i].position.y < minPoint.y) minPoint.y = points[i].position.y;
						if (points[i].position.x > maxPoint.x) maxPoint.x = points[i].position.x;
						if (points[i].position.y > maxPoint.y) maxPoint.y = points[i].position.y;
						//Counting how many outline points we want to connenct
						outlineConnect++;
					}
				}

				//Generate UVs based on bounds
				for (var i = 0; i < vertices.Count; i++)
				{
					//Preventing x or y from being equal to 1 because 1 is used to tell shader to use vertex color
					uvs.Add(new Vector2(
						Mathf.Min(0.99f, Mathf.InverseLerp(minPoint.x, maxPoint.x, vertices[i].x)),
						Mathf.Min(0.99f, Mathf.InverseLerp(minPoint.y, maxPoint.y, vertices[i].y))
					));
				}

				//Save shape's outline
				pointsFinal = new List<Vector3>(vertices);

				//Don't add any triangles if no-fill is selected
				if (fillType == PS2DFillType.None || points.Count < 3)
				{
					tris = Array.Empty<int>();
				}
				//If any fill is selected we do triangulation of the shape and add triangles
				else
				{
					var triangulator = new Triangulator(vertices);
					tris = triangulator.Triangulate();
				}

				//Outline
				if (outlineWidth > 0 && outlineConnect > 0)
				{
					var outlineVertices = new List<Vector2>(500);
					var vertCount = vertices.Count;
					for (var i = 0; i < outlineConnect; i++)
					{
						Vector2 normal1 = (vertices.Loop(i - 1) - vertices[i]).normalized;
						normal1 = new Vector2(normal1.y, -normal1.x) * (outlineWidth / 2);
						Vector2 normal2 = (vertices[i] - vertices.Loop(i + 1)).normalized;
						normal2 = new Vector2(normal2.y, -normal2.x) * (outlineWidth / 2);
						if (!outlineLoop && (i == 0 || i == outlineConnect - 1))
						{
							if (i == 0)
							{
								outlineVertices.Add((Vector2)vertices[i] - normal2);
								outlineVertices.Add((Vector2)vertices[i] + normal2);
							}

							if (i == outlineConnect - 1)
							{
								outlineVertices.Add((Vector2)vertices[i] - normal1);
								outlineVertices.Add((Vector2)vertices[i] + normal1);
							}
						}
						else
						{
							outlineVertices.Add(Geometry.LineIntersectionPoint(
								(Vector2)vertices.Loop(i - 1) - normal1,
								(Vector2)vertices[i] - normal1,
								(Vector2)vertices[i] - normal2,
								(Vector2)vertices.Loop(i + 1) - normal2
							));
							outlineVertices.Add(Geometry.LineIntersectionPoint(
								(Vector2)vertices.Loop(i - 1) + normal1,
								(Vector2)vertices[i] + normal1,
								(Vector2)vertices[i] + normal2,
								(Vector2)vertices.Loop(i + 1) + normal2
							));
						}
					}

					for (var i = 0; i < outlineVertices.Count; i++)
					{
						vertices.Add(outlineVertices[i]);
						colors.Add(outlineColor);
						uvs.Add(Vector2.one);
					}

					//Create triangles for all outline segments. If not looped, don't add the last segment
					trisOutline = new int[outlineVertices.Count * 3];
					for (var i = 0; i < outlineConnect - (outlineLoop ? 0 : 1); i++)
					{
						trisOutline[(i * 6) + 0] = vertCount + i * 2 + 0;
						trisOutline[(i * 6) + 1] = vertCount + i * 2 + 1;
						trisOutline[(i * 6) + 2] = (vertCount + i * 2 + 3 < vertices.Count
							? vertCount + i * 2 + 3
							: vertCount + 1);
						trisOutline[(i * 6) + 3] = vertCount + i * 2 + 0;
						trisOutline[(i * 6) + 4] = (vertCount + i * 2 + 3 < vertices.Count
							? vertCount + i * 2 + 3
							: vertCount + 1);
						trisOutline[(i * 6) + 5] = (vertCount + i * 2 + 3 < vertices.Count
							? vertCount + i * 2 + 2
							: vertCount + 0);
					}

					//Join outline triangles with all the other triangles unless we want them to be their own submesh
					if (!outlineUseCustomMaterial)
					{
						var originalTrisLength = tris.Length;
						System.Array.Resize(ref tris, tris.Length + trisOutline.Length);
						System.Array.Copy(trisOutline, 0, tris, originalTrisLength, trisOutline.Length);
					}
				}

				//Anti-aliasing
				if (antialias && tris.Length == (vertices.Count - 2) * 3)
				{
					var aaridgeVertices = new List<Vector2>(500);
					var vertCount = vertices.Count;
					for (var i = 0; i < vertices.Count; i++)
					{
						Vector2 normal1 = (vertices[i] - vertices.Loop(i + 1)).normalized;
						normal1 = new Vector2(normal1.y, -normal1.x) * aaridge;
						Vector2 normal2 = (vertices.Loop(i + 1) - vertices.Loop(i + 2)).normalized;
						normal2 = new Vector2(normal2.y, -normal2.x) * aaridge;
						if (!clockwise)
						{
							normal1 *= -1;
							normal2 *= -1;
						}
						aaridgeVertices.Add(Geometry.LineIntersectionPoint(
							(Vector2)vertices[i] + normal1,
							(Vector2)vertices.Loop(i + 1) + normal1,
							(Vector2)vertices.Loop(i + 1) + normal2,
							(Vector2)vertices.Loop(i + 2) + normal2
						));
					}

					var clear = new Color(vcolor.r, vcolor.g, vcolor.b, 0.0f);
					for (var i = 0; i < aaridgeVertices.Count; i++)
					{
						vertices.Add(aaridgeVertices[i]);
						colors.Add(clear);
						uvs.Add(Vector2.zero);
					}

					trisAntialiasing = new int[tris.Length + (aaridgeVertices.Count * 2 * 3)];
					for (var i = 0; i < tris.Length; i++)
					{
						trisAntialiasing[i] = tris[i];
					}

					for (var i = 0; i < aaridgeVertices.Count; i++)
					{
						trisAntialiasing[tris.Length + (i * 6) + 0] = i;
						trisAntialiasing[tris.Length + (i * 6) + 1] = (vertCount + i - 1) < vertCount
							? vertCount + aaridgeVertices.Count - 1
							: vertCount + i - 1;
						trisAntialiasing[tris.Length + (i * 6) + 2] = vertCount + i;
						trisAntialiasing[tris.Length + (i * 6) + 3] = i;
						trisAntialiasing[tris.Length + (i * 6) + 4] = vertCount + i;
						trisAntialiasing[tris.Length + (i * 6) + 5] = (i + 1 > vertCount - 1) ? 0 : i + 1;
					}

					tris = trisAntialiasing;
				}

				//Set mesh
				if (mf.sharedMesh == null)
				{
					mf.sharedMesh = new Mesh();
				}
				mf.sharedMesh.Clear();
				mf.sharedMesh.SetVertices(vertices);
				mf.sharedMesh.SetColors(colors);
				mf.sharedMesh.SetUVs(0, uvs);

				//Set triangles depending if we want an outline to be a submesh
				mf.sharedMesh.subMeshCount = 1 + (outlineUseCustomMaterial ? 1 : 0);
				mf.sharedMesh.SetTriangles(tris, 0);
				if (outlineUseCustomMaterial && outlineWidth > 0) mf.sharedMesh.SetTriangles(trisOutline, 1);

				List<Vector3> normals = new List<Vector3>(vertices.Count);
				for (int i = 0; i < vertices.Count; i++)
				{
					normals.Add(Vector3.back);
				}

				mf.sharedMesh.SetNormals(normals);
				mf.sharedMesh.RecalculateBounds();

				//Update triangle count
				triangleCount = (mf.sharedMesh.triangles.Length / 3);
				//Pass min and max positions to shader
				mr.sharedMaterial.SetVector("_WPos", transform.position);
				mr.sharedMaterial.SetVector("_MinWPos", mr.bounds.min);
				mr.sharedMaterial.SetVector("_MaxWPos", mr.bounds.max);
			}
			UpdateCollider();
		}

		public Mesh GetMesh()
		{
			return mf.sharedMesh;
		}

		#endregion

		#region Updating collider

		private void UpdateCollider()
		{
			col = GetComponent<Collider2D>();
			mcol = GetComponent<MeshCollider>();
			if (col != null || mcol != null)
			{
				//Create points for a collider
				cpoints.Clear();
				for (int i = 0; i < points.Count; i++)
				{
					if (points[i].curve > 0f || points.Loop(i + 1).curve > 0f)
					{
						for (int j = 0; j < curveIterations; j++)
						{
							cpoints.Add(new PS2DColliderPoint((Vector2)Bezier.CalculateBezierPoint(
								(float)j / (float)curveIterations,
								points[i].position,
								points[i].handleN,
								points.Loop(i + 1).handleP,
								points.Loop(i + 1).position
							)));
							cpoints[cpoints.Count - 1].wPosition =
								transform.TransformPoint(cpoints[cpoints.Count - 1].position);
						}
					}
					else
					{
						cpoints.Add(new PS2DColliderPoint((Vector2)points[i].position));
						cpoints[cpoints.Count - 1].wPosition =
							transform.TransformPoint(cpoints[cpoints.Count - 1].position);
					}
				}

				//Create normals and directions for every point
				for (var i = 0; i < cpoints.Count; i++)
				{
					//Setting normal
					cpoints[i].normal = cpoints[i].wPosition - cpoints.Loop(i + 1).wPosition;
					cpoints[i].normal = new Vector2(cpoints[i].normal.y, -cpoints[i].normal.x).normalized;
					if (!clockwise) cpoints[i].normal *= -1;
					//Deciding direction
					cpoints[i].signedAngle = Vector2Rotation.SignedAngle(Vector2.up, cpoints[i].normal);
					if (Mathf.Abs(cpoints[i].signedAngle) <= colliderTopAngle / 2)
						cpoints[i].direction = PS2DDirection.Up;
					else if (cpoints[i].signedAngle > (colliderTopAngle / 2) && cpoints[i].signedAngle < 135)
						cpoints[i].direction = PS2DDirection.Left;
					else if (cpoints[i].signedAngle < -(colliderTopAngle / 2) && cpoints[i].signedAngle > -135)
						cpoints[i].direction = PS2DDirection.Right;
					else if (Mathf.Abs(cpoints[i].signedAngle) >= 135) cpoints[i].direction = PS2DDirection.Down;
				}

				//Create array of points for collider
				if (col != null)
				{
					//Polygon collider
					if (col is PolygonCollider2D)
					{
						cpointsFinal = new Vector2[cpoints.Count];
						for (int i = 0; i < cpoints.Count; i++)
						{
							cpointsFinal[i] = cpoints[i].position;
							if (cpoints[i].direction == PS2DDirection.Up ||
								cpoints.Loop(i - 1).direction == PS2DDirection.Up)
								cpointsFinal[i] += (Vector2.up * colliderOffsetTop);
						}

						GetComponent<PolygonCollider2D>().points = cpointsFinal;
					}
					//Full edge collider
					else if (col is EdgeCollider2D && colliderType == PS2DColliderType.Edge)
					{
						cpointsFinal = new Vector2[cpoints.Count];
						for (int i = 0; i < cpoints.Count; i++)
						{
							cpointsFinal[i] = cpoints[i].position;
							if (cpoints[i].direction == PS2DDirection.Up ||
								cpoints.Loop(i - 1).direction == PS2DDirection.Up)
								cpointsFinal[i] += (Vector2.up * colliderOffsetTop);
						}

						GetComponent<EdgeCollider2D>().points = cpointsFinal;
					}
					//Top edge collider
					else if (col is EdgeCollider2D && colliderType == PS2DColliderType.TopEdge)
					{
						var lowestWPoint = 0;
						for (var i = 0; i < cpoints.Count; i++)
						{
							if (i == 0 || cpoints[i].wPosition.y < cpoints[lowestWPoint].wPosition.y)
							{
								lowestWPoint = i;
							}
						}

						var edgeStartPoint = -1;
						for (var i = lowestWPoint; i < lowestWPoint + cpoints.Count; i++)
						{
							if (cpoints.Loop(i).direction == PS2DDirection.Up)
							{
								edgeStartPoint = cpoints.LoopID(i);
								break;
							}
						}

						var edgeEndPoint = -1;
						for (var i = lowestWPoint; i > lowestWPoint - cpoints.Count; i--)
						{
							if (cpoints.Loop(i).direction == PS2DDirection.Up)
							{
								edgeEndPoint = cpoints.LoopID(i + 1);
								break;
							}
						}

						if (edgeStartPoint >= 0 && edgeEndPoint >= 0)
						{
							//Find number of collider points
							var countPoints = 1;
							for (int i = edgeStartPoint; i != edgeEndPoint; i = cpoints.LoopID(i + 1))
							{
								countPoints++;
							}

							if (countPoints > 1)
							{
								//Create collider points
								cpointsFinal = new Vector2[countPoints];
								for (var i = 0; i < countPoints; i++)
								{
									cpointsFinal[i] = cpoints.Loop(edgeStartPoint + i).position +
													  (Vector2.up * colliderOffsetTop);
								}
								//Set collider points
								GetComponent<EdgeCollider2D>().enabled = true;
								GetComponent<EdgeCollider2D>().points = cpointsFinal;
							}
							else
							{
								GetComponent<EdgeCollider2D>().enabled = false;
							}
						}
						else
						{
							GetComponent<EdgeCollider2D>().enabled = false;
						}
					}
				}

				if (mcol != null)
				{
					//Create two sets of vertices for the mesh collier
					var mVertices = new Vector3[mf.sharedMesh.vertices.Length * 2];
					for (var i = 0; i < mf.sharedMesh.vertices.Length; i++)
					{
						mVertices[i] = mf.sharedMesh.vertices[i];
						mVertices[i].z -= cMeshDepth / 2f;
					}

					for (var i = mf.sharedMesh.vertices.Length; i < mf.sharedMesh.vertices.Length * 2; i++)
					{
						mVertices[i] = mf.sharedMesh.vertices[i - mf.sharedMesh.vertices.Length];
						mVertices[i].z += cMeshDepth / 2f;
					}

					//Create triangles for mesh collider
					var mTriangles = new int[mf.sharedMesh.triangles.Length * 2 + ((mf.sharedMesh.vertices.Length * 2) * 3)];
					for (var i = 0; i < mf.sharedMesh.triangles.Length; i++)
					{
						mTriangles[i] = mf.sharedMesh.triangles[i];
					}

					for (var i = mf.sharedMesh.triangles.Length * 2 - 1; i >= mf.sharedMesh.triangles.Length; i--)
					{
						var sharedMesh = mf.sharedMesh;
						mTriangles[(sharedMesh.triangles.Length * 2) + sharedMesh.triangles.Length - 1 - i] =
							sharedMesh.triangles[i - sharedMesh.triangles.Length] + sharedMesh.vertices.Length;
					}

					//Stitch the two sides together
					for (var i = 0; i < mf.sharedMesh.vertices.Length; i++)
					{
						mTriangles[mf.sharedMesh.triangles.Length * 2 + (i * 6) + 0] = i;
						mTriangles[mf.sharedMesh.triangles.Length * 2 + (i * 6) + 1] = mf.sharedMesh.vertices.Length + i;
						mTriangles[mf.sharedMesh.triangles.Length * 2 + (i * 6) + 2] = mf.sharedMesh.vertices.Length +
							i + 1 - (i == mf.sharedMesh.vertices.Length - 1 ? mf.sharedMesh.vertices.Length : 0);
						mTriangles[mf.sharedMesh.triangles.Length * 2 + (i * 6) + 3] = i;
						mTriangles[mf.sharedMesh.triangles.Length * 2 + (i * 6) + 4] = mf.sharedMesh.vertices.Length +
							i + 1 - (i == mf.sharedMesh.vertices.Length - 1 ? mf.sharedMesh.vertices.Length : 0);
						mTriangles[mf.sharedMesh.triangles.Length * 2 + (i * 6) + 5] = i + 1 -
							(i == mf.sharedMesh.vertices.Length - 1 ? mf.sharedMesh.vertices.Length : 0);
					}

					cMesh = new Mesh();
					cMesh.SetVertices(new List<Vector3>(mVertices));
					cMesh.SetTriangles(mTriangles, 0);
					cMesh.name = transform.name;
					mcol.sharedMesh = null;
					mcol.sharedMesh = cMesh;
				}
			}
		}

		#endregion

		#region Create and update bezier handles

		private void GenerateHandles()
		{
			for (var i = 0; i < points.Count; i++)
			{
				GenerateHandles(i);
			}
		}

		public void GenerateHandles(int i)
		{
			var nextPoint = points.Loop(i + 1);
			var prevPoint = points.Loop(i - 1);
			//Find a median angle
			var angle = Vector2Rotation.SignedAngle(
				(nextPoint.position - points[i].position).normalized,
				(prevPoint.position - points[i].position).normalized
			);
			if (angle > 0) angle = -(360f - angle);
			var median = ((nextPoint.position - points[i].position).normalized).Rotate(angle / 2);
			if (!clockwise) median *= -1;
			//Check for sudden angle inversions when clockwise order didn't change
			if (points[i].clockwise == clockwise && Mathf.Abs(Vector2Rotation.SignedAngle(points[i].median, median)) > 135)
			{
				median *= -1;
			}
			//Calculate bezier handles
			points[i].handleP = median.Rotate(90 * (clockwise ? -1 : 1)) + points[i].position;
			points[i].handleN = median.Rotate(90 * (clockwise ? 1 : -1)) + points[i].position;
			//Multiply points by half of the distance to neighboring point
			points[i].handleP =
				((points[i].handleP - points[i].position) *
				 (Vector2.Distance(prevPoint.position, points[i].position) * points[i].curve)) +
				points[i].position;
			points[i].handleN =
				((points[i].handleN - points[i].position) *
				 (Vector2.Distance(nextPoint.position, points[i].position) * points[i].curve)) +
				points[i].position;
			//Store the configuration
			points[i].median = median;
			points[i].clockwise = clockwise;
		}

		#endregion

	}
}
