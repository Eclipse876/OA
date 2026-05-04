namespace ProtoShape2D.Editor
{
	public static class Strings
	{
		public static class Curve
		{
			public const string CurveType = "Curve type";
			public const string CurveTypeDesc = "Select how the curves work";
			public const string Simple = "Simple";
			public const string SimpleDesc = "Create simple curves by rounding the corners using a slider in point's properties window";
			public const string Bezier = "Bezier";
			public const string BezierDesc = "Create more precise curves by using Bezier controls similar to vector editors";
			public const string ToSimple = "Convert to Simple";
			public const string ToSimpleDesc = "Convert the selected Bezier curve to a simple curve";
		}
		public static class Fill
		{
			public const string Title = "Fill";
			public const string Type = "Fill type";
			public const string TypeDesc = "Which fill to use for this object. Single color is optimal for mobile games since it uses built-in sprite shader";
			public const string Color = "Color";
			public const string ColorDesc = "Color to fill the object with";
			public const string Texture = "Texture";
			public const string TextureDesc = "An image for tiling. Needs to have \"Wrap Mode\" property set to \"Repeat\"";
			public const string TextureScale = "Texture scale";
			public const string TextureScaleDesc = "Change size of the texture";
			public const string TextureRotation = "Texture rotation";
			public const string TextureRotationDesc = "Set angle of rotation for texture";
			public const string TextureOffsetX = "Texture offset X";
			public const string TextureOffsetXDesc = "Offset texture on X axis";
			public const string TextureOffsetY = "Texture offset Y";
			public const string TextureOffsetYDesc = "Offset texture on Y axis";
			public const string ColorOne = "Color one";
			public const string ColorOneDesc = "Top color for the gradient";
			public const string ColorTwo = "Color two";
			public const string ColorTwoDesc = "Bottom color for the gradient";
			public const string GradientScale = "Gradient scale";
			public const string GradientScaleDesc = "Zoom gradient in and out relatively to height of the object";
			public const string GradientRotation = "Gradient rotation";
			public const string GradientRotationDesc = "Set angle of rotation for gradient";
			public const string GradientOffset = "Gradient offset";
			public const string GradientOffsetDesc = "Offset gradient up or down";
			public const string CustomMaterial = "Custom material";
			public const string CustomMaterialDesc = "If you provide same material for multiple objects, it will lower the number of DrawCalls therefor optimizing the rendering process";
		}
		public static class Outline
		{
			public const string Title = "Outline";
			public const string Width = "Width";
			public const string WidthDesc = "Thickness of the outline. Outline is disabled when this number is zero.";
			public const string Color = "Color";
			public const string ColorDesc = "Color of the outline";
			public const string Loop = "Loop outline";
			public const string LoopDesc = "Connect start and end of the outline";
			public const string CustomMaterialToggle = "Use custom material";
			public const string CustomMaterialToggleDesc = "Allows to assign your own material to the outline";
			public const string CustomMaterial = "Custom material";
			public const string CustomMaterialDesc = "If you provide same material for multiple objects, it will lower the number of DrawCalls therefore optimizing the rendering process";
		}
		public static class Mesh
		{
			public const string Title = "Mesh";
			public const string CurveIterations = "Curve iterations";
			public const string CurveIterationsDesc = "How many points a curved line should have";
			public const string Antialiasing = "Anti-aliasing";
			public const string AntialiasingDesc = "Create an anti-aliasing effect by adding a thin transparent gradient outline to the mesh. Doesn't work with outline";
			public const string AARidge = "Anti-aliasing ridge";
			public const string AARidgeDesc = "Width of anti-aliasing border";
			public const string TriangleCount = "The mesh has {0} triangles";
			public const string OneTriangle = "The mesh is just one triangle";
			public const string NoTriangles = "The mesh has no triangles";
		}
		public static class Snap
		{
			public const string Title = "Snap";
			public const string Type = "Snap type";
			public const string TypeDesc = "Which type of snapping is used when Shift is pressed";
			public const string GridSize = "Grid size";
			public const string GridSizeDesc = "Distance between grid lines";
		}
		public static class Collider
		{
			public const string Title = "Collider";
			public const string AutoCollider2D = "Auto collider 2D";
			public const string AutoCollider2DDesc = "Automatically create a collider. Set to \"None\" if you want to create your collider by hand";
			public const string TopEdgeArc = "Top edge arc";
			public const string TopEdgeArcDesc = "Decides which edges are considered to be facing up";
			public const string OffsetTop = "Offset top";
			public const string OffsetTopDesc = "Displace part of collider that is considered to be facing up";
			public const string ShowNormals = "Show normals";
			public const string ShowNormalsDesc = "Visually shows which edges are facing which side. Just to better understand how \"Top edge arc\" works";
			public const string Depth = "Collider Depth";
			public const string DepthDesc = "The distance between two sides of the collider";
			public const string MeshDynamicNote = "Note that dynamic mesh colliders can only be convex even if they appear to be not";
		}
		public static class Tools
		{
			public const string Title = "Tools";
			public const string ZSorting = "Z-sorting";
			public const string ZSortingDesc = "Adds and subtracts 0.01 on Z axis for very basic sorting. Nothing fancy."; 
			public const string Pull = "Pull"; 
			public const string PullDesc = "Subtract 0.01 on Z axis"; 
			public const string Push = "Push"; 
			public const string PushDesc = "Add 0.01 on Z axis"; 
			public const string Pivot = "Pivot";
			public const string PivotDesc = "Move and configure object's pivot";
			public const string MovePivotManually = "Move pivot manually";
			public const string HdrColors = "HDR colors";
			public const string HdrColorsDesc = "Treat colors as HDR values. Works with some post-processing effects";
			public const string Export = "Export";
			public const string ExportDesc = "Export the shape to another form of object";
			public const string ExportMesh = "Mesh";
			public const string ExportMeshDesc = "Save current object as a Mesh asset in the root of your project";
			public const string ExportPng = "PNG";
			public const string ExportPngDesc = "Save current object as a PNG file in the root of your project";
			public const string AddSortingLayer = "Add Sorting Layer...";
			public const string SortingLayer = "Sorting Layer";
			public const string SortingLayerDesc = "Name of the Renderer's sorting layer";
			public const string OrderInLayer = "Order in Layer";
			public const string OrderInLayerDesc = "Renderer's order within a sorting layer";
			public const string MaskNone = "None";
			public const string MaskVisibleInside = "Visible Inside Mask";
			public const string MaskVisibleOutside = "Visible Outside Mask";
			public const string MaskInteraction = "Mask Interaction";
			public const string MaskInteractionDesc = "Interaction with a Sprite Mask";

		}
		public static class Undo
		{
			public const string SimpleBezierConvert = "Simple/Bezier convert";
			public const string ChangeFillType = "Change fill type";
			public const string ChangeColor = "Change color";
			public const string ChangeTexture = "Change texture";
			public const string ChangeTextureSize = "Change texture size";
			public const string ChangeTextureRotation = "Change texture rotation";
			public const string ChangeTextureOffset = "Change texture offset";
			public const string ChangeColorOne = "Change color one";
			public const string ChangeColorTwo = "Change color two";
			public const string ChangeGradientScale = "Change gradient scale";
			public const string ChangeGradientRotation = "Change gradient rotation";
			public const string ChangeGradientOffset = "Change gradient offset";
			public const string ChangeCustomMaterial = "Change custom material";
			public const string ChangeOutlineWidth = "Change outline width";
			public const string ChangeOutlineColor = "Change outline color";
			public const string ChangeOutlineLoop = "Change outline looping";
			public const string ChangeOutlineCustomMaterialToggle = "Change outline custom material setting";
			public const string ChangeOutlineCustomMaterial = "Change outline custom material";
			public const string ChangeCurveIterations = "Change curve iterations";
			public const string ChangeAntialiasing = "Change anti-aliasing";
			public const string ChangeAntialiasingWidth = "Changed anti-aliasing width";
			public const string ChangeSnapType = "Change snap type";
			public const string ChangeGridSize = "Change grid size";
			public const string ChangeColliderType = "Change collider type";
			public const string ChangeTopEdgeArc = "Change top edge arc";
			public const string ChangeOffsetTop = "Change offset top";
			public const string ChangeShowNormals = "Change show normals";
			public const string ChangeColliderDepth = "Change collider depth";
			public const string ZSortingPull = "Z-sorting pull";
			public const string ZSortingPush = "Z-sorting push";
			public const string ChangeHdrColors = "Change HDR colors";
			public const string ChangeSortingLayer = "Change sorting layer";
			public const string ChangeOrderInLayer = "Change order in layer";
			public const string ChangeMaskInteraction = "Change mask interaction";
			public const string DeletePoint = "Delete point";
			public const string EditPoint = "Edit point";
			public const string GradientMove = "Gradient move";
			public const string GradientScale = "Gradient scale";
			public const string GradientRotate = "Gradient rotate";
			public const string TextureMove = "Texture move";
			public const string TextureScale = "Texture scale";
			public const string TextureRotate = "Texture rotate";
			public const string MovePivot = "Move pivot";
		}
		public const string MeshOverwriteWarning = "Asset with this name already exists in root of your project.";
		public const string RemoveCollidersWarning = "This will remove existing Collider and RigidBody with all their settings.";
		public const string RecreateCollidersWarning = "This will remove existing Collider and RigidBody with all their settings and create new Collider and RigidBody in their place.";
		public const string KeepCollider = "Keep existing collider";
		public const string Warning = "Warning";
		public const string Overwrite = "Overwrite";
		public const string Remove = "Remove";
		public const string Continue = "Continue";
		public const string Cancel = "Cancel";
	}
}