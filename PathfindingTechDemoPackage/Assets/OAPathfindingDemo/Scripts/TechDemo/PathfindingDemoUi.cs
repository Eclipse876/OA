using System;
using System.Collections.Generic;
using System.Globalization;
using OA.Simulation.Units;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

namespace OA.TechDemo
{
    public sealed class PathfindingDemoUi : MonoBehaviour
    {
        private readonly List<TraitRow> _traitRows = new List<TraitRow>(16);

        private PathfindingDemoController _controller;
        private MovementProfileDefinition _movementProfile;

        private Font _font;
        private InputField _seedInput;
        private InputField _obstacleInput;
        private Text _statusText;

        public void Initialize(PathfindingDemoController controller, MovementProfileDefinition movementProfile)
        {
            _controller = controller;
            _movementProfile = movementProfile;
            _font = Resources.GetBuiltinResource<Font>("Arial.ttf");

            EnsureEventSystem();
            BuildUiTree();
        }

        public void SyncMapInputs(int seed, float obstacleChance)
        {
            if (_seedInput != null)
            {
                _seedInput.text = seed.ToString(CultureInfo.InvariantCulture);
            }

            if (_obstacleInput != null)
            {
                _obstacleInput.text = obstacleChance.ToString("0.00", CultureInfo.InvariantCulture);
            }
        }

        public void RefreshTraitInputs()
        {
            for (int i = 0; i < _traitRows.Count; i++)
            {
                TraitRow row = _traitRows[i];
                if (row.Input != null)
                {
                    row.Input.text = row.Getter().ToString("0.###", CultureInfo.InvariantCulture);
                }
            }
        }

        public void SetStatus(string message)
        {
            if (_statusText != null)
            {
                _statusText.text = message;
            }
        }

        private void BuildUiTree()
        {
            GameObject canvasObject = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);

            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 120;

            var canvasScaler = canvasObject.GetComponent<CanvasScaler>();
            canvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            canvasScaler.referenceResolution = new Vector2(1920f, 1080f);
            canvasScaler.matchWidthOrHeight = 0.55f;

            GameObject panelObject = new GameObject(
                "Panel",
                typeof(RectTransform),
                typeof(Image),
                typeof(VerticalLayoutGroup),
                typeof(ContentSizeFitter));
            panelObject.transform.SetParent(canvasObject.transform, false);

            var panelRect = panelObject.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0f, 1f);
            panelRect.anchorMax = new Vector2(0f, 1f);
            panelRect.pivot = new Vector2(0f, 1f);
            panelRect.anchoredPosition = new Vector2(12f, -12f);
            panelRect.sizeDelta = new Vector2(440f, 760f);

            var panelImage = panelObject.GetComponent<Image>();
            panelImage.color = new Color(0.05f, 0.09f, 0.14f, 0.9f);

            var layout = panelObject.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(12, 12, 12, 12);
            layout.spacing = 6f;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandHeight = false;

            var fitter = panelObject.GetComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

            CreateLabel(panelObject.transform, "OA Pathfinding Tech Demo", 19, FontStyle.Bold, TextAnchor.MiddleLeft);
            CreateLabel(panelObject.transform, "Generate map, left-click destination, tune ship traits live.", 13, FontStyle.Normal, TextAnchor.MiddleLeft);
            CreateSpacer(panelObject.transform, 4f);

            CreateMapControls(panelObject.transform);
            CreateSpacer(panelObject.transform, 8f);

            CreateLabel(panelObject.transform, "Ship Traits", 16, FontStyle.Bold, TextAnchor.MiddleLeft);
            CreateTraitRows(panelObject.transform);

            CreateSpacer(panelObject.transform, 6f);
            CreateButtonRow(panelObject.transform, "Apply All Traits", HandleApplyAllTraits, 188f, 30f);

            CreateSpacer(panelObject.transform, 8f);
            _statusText = CreateLabel(panelObject.transform, "Bootstrapping demo...", 12, FontStyle.Normal, TextAnchor.UpperLeft);
            _statusText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _statusText.verticalOverflow = VerticalWrapMode.Overflow;
            _statusText.GetComponent<RectTransform>().sizeDelta = new Vector2(390f, 68f);
        }

        private void CreateMapControls(Transform parent)
        {
            CreateLabel(parent, "Map Controls", 16, FontStyle.Bold, TextAnchor.MiddleLeft);

            GameObject seedRow = CreateRow(parent, 28f);
            CreateLabeledInput(seedRow.transform, "Seed", out _seedInput, 76f);
            CreateMiniButton(seedRow.transform, "Roll", () =>
            {
                _seedInput.text = _controller.GetRandomSeed().ToString(CultureInfo.InvariantCulture);
            });

            GameObject obstacleRow = CreateRow(parent, 28f);
            CreateLabeledInput(obstacleRow.transform, "Obstacle % (0.05-0.45)", out _obstacleInput, 76f);
            _obstacleInput.text = "0.20";

            CreateButtonRow(parent, "Generate Random Map", HandleGenerateMap, 188f, 30f);
        }

        private void CreateTraitRows(Transform parent)
        {
            AddTraitRow(parent, "Max Speed", () => _movementProfile.maxSpeed, value => _movementProfile.maxSpeed = Mathf.Max(0f, value));
            AddTraitRow(parent, "Acceleration", () => _movementProfile.acceleration, value => _movementProfile.acceleration = Mathf.Max(0f, value));
            AddTraitRow(parent, "Deceleration", () => _movementProfile.deceleration, value => _movementProfile.deceleration = Mathf.Max(0f, value));
            AddTraitRow(parent, "Turn Rate (deg/s)", () => _movementProfile.turnRateDegreesPerSecond, value => _movementProfile.turnRateDegreesPerSecond = Mathf.Max(0f, value));
            AddTraitRow(parent, "Turning Radius", () => _movementProfile.turningRadius, value => _movementProfile.turningRadius = Mathf.Max(0f, value));
            AddTraitRow(parent, "Safety Radius", () => _movementProfile.safetyRadius, value => _movementProfile.safetyRadius = Mathf.Max(0f, value));
            AddTraitRow(parent, "Stopping Distance", () => _movementProfile.stoppingDistance, value => _movementProfile.stoppingDistance = Mathf.Max(0.01f, value));
        }

        private void AddTraitRow(Transform parent, string label, Func<float> getter, Action<float> setter)
        {
            GameObject row = CreateRow(parent, 28f);
            CreateLabelWithFixedWidth(row.transform, label, 144f);

            InputField input = CreateInputField(row.transform, 76f);
            input.text = getter().ToString("0.###", CultureInfo.InvariantCulture);

            CreateMiniButton(row.transform, "Set", () =>
            {
                if (!TryParseFloat(input.text, out float value))
                {
                    SetStatus($"Invalid number for {label}.");
                    return;
                }

                setter(value);
                _controller.OnMovementTraitsChanged();
                RefreshTraitInputs();
            });

            _traitRows.Add(new TraitRow
            {
                Label = label,
                Input = input,
                Getter = getter,
                Setter = setter
            });
        }

        private void HandleGenerateMap()
        {
            int seed;
            if (!int.TryParse(_seedInput.text, NumberStyles.Integer, CultureInfo.InvariantCulture, out seed))
            {
                seed = _controller.GetRandomSeed();
            }

            if (!TryParseFloat(_obstacleInput.text, out float obstacleChance))
            {
                SetStatus("Obstacle chance must be a valid decimal between 0.05 and 0.45.");
                return;
            }

            _controller.GenerateMap(seed, obstacleChance);
            SyncMapInputs(_controller.CurrentSeed, _controller.CurrentObstacleChance);
        }

        private void HandleApplyAllTraits()
        {
            for (int i = 0; i < _traitRows.Count; i++)
            {
                TraitRow row = _traitRows[i];
                if (!TryParseFloat(row.Input.text, out float value))
                {
                    SetStatus($"Invalid number for {row.Label}.");
                    return;
                }

                row.Setter(value);
            }

            _controller.OnMovementTraitsChanged();
            RefreshTraitInputs();
        }

        private void EnsureEventSystem()
        {
            if (EventSystem.current != null)
            {
                return;
            }

            var eventSystemObject = new GameObject("EventSystem", typeof(EventSystem));
#if ENABLE_INPUT_SYSTEM
            eventSystemObject.AddComponent<InputSystemUIInputModule>();
#else
            eventSystemObject.AddComponent<StandaloneInputModule>();
#endif
        }

        private GameObject CreateRow(Transform parent, float height)
        {
            var row = new GameObject("Row", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            row.transform.SetParent(parent, false);

            var layout = row.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 6f;
            layout.padding = new RectOffset(0, 0, 0, 0);
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = false;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = false;

            var rowLayout = row.GetComponent<LayoutElement>();
            rowLayout.minHeight = height;
            rowLayout.preferredHeight = height;
            rowLayout.flexibleWidth = 1f;

            return row;
        }

        private void CreateLabeledInput(Transform parent, string label, out InputField input, float inputWidth)
        {
            CreateLabelWithFixedWidth(parent, label, 144f);
            input = CreateInputField(parent, inputWidth);
        }

        private void CreateLabelWithFixedWidth(Transform parent, string label, float width)
        {
            Text text = CreateText(parent, label, 13, FontStyle.Normal, TextAnchor.MiddleLeft);
            var layout = text.gameObject.AddComponent<LayoutElement>();
            layout.preferredWidth = width;
            layout.minWidth = width;
        }

        private void CreateButtonRow(Transform parent, string text, Action onClick, float buttonWidth, float buttonHeight)
        {
            GameObject row = CreateRow(parent, buttonHeight);
            CreateMiniButton(row.transform, text, onClick, buttonWidth, buttonHeight);
        }

        private void CreateMiniButton(
            Transform parent,
            string text,
            Action onClick,
            float width = 58f,
            float height = 26f)
        {
            GameObject buttonObject = new GameObject("Button", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            buttonObject.transform.SetParent(parent, false);

            var image = buttonObject.GetComponent<Image>();
            image.color = new Color(0.11f, 0.35f, 0.54f, 1f);

            var layout = buttonObject.GetComponent<LayoutElement>();
            layout.preferredWidth = width;
            layout.minWidth = width;
            layout.preferredHeight = height;
            layout.minHeight = height;

            var button = buttonObject.GetComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(() => onClick?.Invoke());

            Text label = CreateText(buttonObject.transform, text, 12, FontStyle.Bold, TextAnchor.MiddleCenter);
            var labelRect = label.rectTransform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
        }

        private InputField CreateInputField(Transform parent, float width)
        {
            GameObject inputObject = new GameObject("InputField", typeof(RectTransform), typeof(Image), typeof(InputField), typeof(LayoutElement));
            inputObject.transform.SetParent(parent, false);

            var image = inputObject.GetComponent<Image>();
            image.color = new Color(0.96f, 0.97f, 1f, 0.95f);

            var layout = inputObject.GetComponent<LayoutElement>();
            layout.preferredWidth = width;
            layout.minWidth = width;
            layout.preferredHeight = 26f;
            layout.minHeight = 26f;

            Text text = CreateText(inputObject.transform, string.Empty, 12, FontStyle.Normal, TextAnchor.MiddleLeft);
            text.color = new Color(0.06f, 0.08f, 0.1f, 1f);

            Text placeholder = CreateText(inputObject.transform, "0", 12, FontStyle.Italic, TextAnchor.MiddleLeft);
            placeholder.color = new Color(0.32f, 0.36f, 0.4f, 0.8f);

            RectTransform textRect = text.rectTransform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(8f, 3f);
            textRect.offsetMax = new Vector2(-8f, -3f);

            RectTransform placeholderRect = placeholder.rectTransform;
            placeholderRect.anchorMin = Vector2.zero;
            placeholderRect.anchorMax = Vector2.one;
            placeholderRect.offsetMin = new Vector2(8f, 3f);
            placeholderRect.offsetMax = new Vector2(-8f, -3f);

            var input = inputObject.GetComponent<InputField>();
            input.textComponent = text;
            input.placeholder = placeholder;
            input.targetGraphic = image;
            input.contentType = InputField.ContentType.DecimalNumber;

            return input;
        }

        private Text CreateLabel(Transform parent, string message, int fontSize, FontStyle style, TextAnchor align)
        {
            return CreateText(parent, message, fontSize, style, align);
        }

        private Text CreateText(Transform parent, string message, int fontSize, FontStyle style, TextAnchor align)
        {
            GameObject textObject = new GameObject("Text", typeof(RectTransform), typeof(Text), typeof(LayoutElement));
            textObject.transform.SetParent(parent, false);

            var text = textObject.GetComponent<Text>();
            text.font = _font;
            text.text = message;
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.alignment = align;
            text.color = new Color(0.9f, 0.95f, 1f, 1f);

            var layout = textObject.GetComponent<LayoutElement>();
            layout.flexibleWidth = 1f;
            layout.minHeight = fontSize + 6f;

            return text;
        }

        private void CreateSpacer(Transform parent, float height)
        {
            var spacer = new GameObject("Spacer", typeof(RectTransform), typeof(LayoutElement));
            spacer.transform.SetParent(parent, false);
            var layout = spacer.GetComponent<LayoutElement>();
            layout.minHeight = height;
            layout.preferredHeight = height;
        }

        private static bool TryParseFloat(string text, out float value)
        {
            return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) ||
                   float.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
        }

        private sealed class TraitRow
        {
            public string Label;
            public InputField Input;
            public Func<float> Getter;
            public Action<float> Setter;
        }
    }
}
