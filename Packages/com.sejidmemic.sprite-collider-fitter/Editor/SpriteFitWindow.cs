using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using UnityEditor;
using UnityEngine;

namespace SpriteFit.Editor
{
    /// <summary>
    /// Tools → Sprite Collider Fitter → Open Window. Fits primitives to the selected sprite, previews them in the Scene view
    /// with an optional error heatmap (FR-05), shows the stats (FR-08), and writes them on Apply (FR-06).
    /// </summary>
    public sealed class SpriteFitWindow : EditorWindow
    {
        const double DebounceSeconds = 0.15;
        static readonly Color AccentColor = new Color(0.25f, 0.95f, 0.6f, 1f);

        [SerializeField] FitSettings _settings = new FitSettings();
        [SerializeField] OldColliderAction _oldColliderAction = OldColliderAction.Disable;
        [SerializeField] bool _showPreview = true;
        [SerializeField] bool _showHeatmap;
        [SerializeField] bool _collidersOnRoot;
        [SerializeField] bool _createAsTrigger;

        static readonly GUIContent CreateAsTriggerLabel = new GUIContent(
            "Create as trigger",
            "Make every generated collider a trigger. When off, the trigger setting is copied from the object's existing collider.");

        static readonly GUIContent CollidersOnRootLabel = new GUIContent(
            "Add colliders to root object",
            "Put all shapes in one CustomCollider2D on the selected object instead of a 'Colliders (generated)' child. " +
            "Rotated boxes and capsules need no child objects, but this option has limitations, listed below it when it's on.");

        SpriteRenderer _target;
        Sprite _fittedSprite;
        PixelMask _mask;
        Sprite _maskSprite;
        float _maskThreshold = -1f;
        FitResult _result;
        byte[] _classes;
        HeatmapOverlay _heatmap;
        double _maskMilliseconds;
        double _fitMilliseconds;
        string _message;
        MessageType _messageType;
        double _recomputeAt = -1;
        Vector2 _scroll;

        /// <summary>
        /// The tool's only entry under Tools. It must stay a submenu: a path that is both a clickable item and
        /// a submenu shows only one of the two. MenuPathTests checks this.
        /// </summary>
        public const string MenuRoot = "Tools/Sprite Collider Fitter/";

        const float DefaultWidth = 380f;
        const float MinWidth = 320f;
        const float MinHeight = 240f;

        // Space kept free around a new window inside the Editor's main window.
        const float EditorMargin = 40f;

        // Where the window last closed while floating, so it reopens there at the same size. EditorPrefs is
        // per user and shared by all projects, like the window's screen position itself.
        const string LastRectKey = "SpriteFit.Window.LastRect";

        // Set when the window is first created, so the first repaint can size it to its content.
        bool _fitHeightPending;

        // Set when the root warning appears. Scrolling to the bottom keeps the warning, the checkboxes and every
        // button in view without resizing or moving the window.
        bool _scrollToBottomPending;

        /// <summary>Height of everything in the window, measured on the last repaint.</summary>
        internal float ContentHeight { get; private set; }

        [MenuItem(MenuRoot + "Open Window", priority = 0)]
        static void Open()
        {
            bool isNew = !HasOpenInstances<SpriteFitWindow>();
            var window = GetWindow<SpriteFitWindow>("Sprite Collider Fitter");
            if (!isNew) return;

            if (TryParseRect(EditorPrefs.GetString(LastRectKey, string.Empty), out Rect last) &&
                last.width >= MinWidth && last.height >= MinHeight)
            {
                window.position = last;
                return;
            }

            // The first time, the window opens over the Editor (not wherever the OS puts new windows, which can
            // be another monitor) and is then sized to its content on the first repaint.
            Rect editor = EditorGUIUtility.GetMainWindowPosition();
            float width = Mathf.Min(DefaultWidth, editor.width - 2f * EditorMargin);
            window.position = new Rect(editor.center.x - width * 0.5f, editor.y + EditorMargin, width, MinHeight);
            window._fitHeightPending = true;
            window.Repaint();
        }

        void OnEnable()
        {
            minSize = new Vector2(MinWidth, MinHeight);
            if (_settings == null) _settings = new FitSettings();
            _heatmap = new HeatmapOverlay();
            Selection.selectionChanged += OnSelectionChanged;
            SceneView.duringSceneGui += OnSceneGUI;
            EditorApplication.update += OnEditorUpdate;
            Undo.undoRedoPerformed += Repaint;
            OnSelectionChanged();
        }

        void OnDisable()
        {
            // A docked window is placed by the Editor's layout, so only a floating window's rect is kept.
            if (!docked) EditorPrefs.SetString(LastRectKey, FormatRect(position));

            Selection.selectionChanged -= OnSelectionChanged;
            SceneView.duringSceneGui -= OnSceneGUI;
            EditorApplication.update -= OnEditorUpdate;
            Undo.undoRedoPerformed -= Repaint;
            _heatmap.Dispose();
            SceneView.RepaintAll();
        }

        void OnSelectionChanged()
        {
            // The preview follows the active object; batch apply uses the whole selection.
            GameObject selected = Selection.activeGameObject;
            _target = selected != null ? selected.GetComponent<SpriteRenderer>() : null;
            Recompute();
            Repaint();
        }

        void OnEditorUpdate()
        {
            // The sprite can change, or the target can be deleted, without a selection change.
            Sprite current = _target != null ? _target.sprite : null;
            if (current != _fittedSprite)
            {
                Recompute();
                Repaint();
                return;
            }

            if (_recomputeAt >= 0 && EditorApplication.timeSinceStartup >= _recomputeAt) RecomputeNow();
        }

        void ScheduleRecompute()
        {
            _recomputeAt = EditorApplication.timeSinceStartup + DebounceSeconds;
        }

        void RecomputeNow()
        {
            _recomputeAt = -1;
            Recompute();
            Repaint();
        }

        void Recompute(bool forceMaskRead = false)
        {
            _result = null;
            _message = null;
            _maskMilliseconds = 0;
            _fitMilliseconds = 0;

            Sprite sprite = _target != null ? _target.sprite : null;
            _fittedSprite = sprite;
            if (sprite == null)
            {
                _mask = null;
                _maskSprite = null;
                SceneView.RepaintAll();
                return;
            }

            try
            {
                Stopwatch stopwatch = Stopwatch.StartNew();
                // The threshold comes from a slider, so any change at all means re-read.
                if (forceMaskRead || _mask == null || sprite != _maskSprite || _settings.AlphaThreshold != _maskThreshold)
                {
                    _mask = SpriteMaskReader.ReadMask(sprite, _settings.AlphaThreshold);
                    _maskSprite = sprite;
                    _maskThreshold = _settings.AlphaThreshold;
                    _maskMilliseconds = stopwatch.Elapsed.TotalMilliseconds;
                }

                int pixels = _mask.Width * _mask.Height;
                if (!_showHeatmap) _classes = null;
                else if (_classes == null || _classes.Length != pixels) _classes = new byte[pixels];

                stopwatch.Restart();
                _result = SpriteFitter.Fit(_mask, _settings, _classes);
                _fitMilliseconds = stopwatch.Elapsed.TotalMilliseconds;

                if (_classes != null) _heatmap.Update(_mask.Width, _mask.Height, _classes);

                if (_mask.OpaqueCount == 0) SetMessage("No pixels are above the alpha threshold.", MessageType.Warning);
                else if (_settings.Allowed == AllowedShapes.None) SetMessage("Allow at least one shape.", MessageType.Warning);
                else if (_settings.Mode == FitMode.Compound && _result.IslandCount > _settings.MaxPrimitives)
                {
                    SetMessage($"The sprite has {_result.IslandCount} separate parts, so it gets at least one primitive each.", MessageType.Info);
                }
            }
            catch (Exception exception)
            {
                _mask = null;
                _maskSprite = null;
                _result = null;
                SetMessage($"Couldn't fit '{sprite.name}': {exception.Message}", MessageType.Error);
                UnityEngine.Debug.LogException(exception);
            }

            SceneView.RepaintAll();
        }

        void SetMessage(string message, MessageType type)
        {
            _message = message;
            _messageType = type;
        }

        void OnGUI()
        {
            // The scroll view clamps this to the end of the content.
            if (_scrollToBottomPending && Event.current.type == EventType.Layout)
            {
                _scrollToBottomPending = false;
                _scroll.y = float.MaxValue;
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            DrawTarget();
            EditorGUILayout.Space();
            DrawSettings();
            EditorGUILayout.Space();
            DrawStats();
            EditorGUILayout.Space();
            DrawActions();

            // Measured before the message, so the fitted height ends just below the buttons.
            Rect buttonsEnd = GUILayoutUtility.GetRect(0f, 0f);
            if (!string.IsNullOrEmpty(_message)) EditorGUILayout.HelpBox(_message, _messageType);
            EditorGUILayout.EndScrollView();

            if (Event.current.type == EventType.Repaint)
            {
                ContentHeight = buttonsEnd.yMax;
                if (_fitHeightPending)
                {
                    _fitHeightPending = false;
                    FitHeight(ContentHeight);
                }
            }
        }

        // Fits the window to its content, centred over the Editor's main window and never taller than it.
        void FitHeight(float contentHeight)
        {
            Rect editor = EditorGUIUtility.GetMainWindowPosition();
            float height = Mathf.Clamp(contentHeight + 8f, MinHeight, editor.height - 2f * EditorMargin);
            float y = editor.y + Mathf.Max(EditorMargin, (editor.height - height) * 0.5f);

            EditorApplication.delayCall += () =>
            {
                if (this == null) return;

                position = new Rect(position.x, y, position.width, height);
            };
        }

        /// <summary>The rect as "x;y;width;height", independent of the user's culture.</summary>
        public static string FormatRect(Rect rect)
        {
            CultureInfo c = CultureInfo.InvariantCulture;
            return $"{rect.x.ToString("R", c)};{rect.y.ToString("R", c)};{rect.width.ToString("R", c)};{rect.height.ToString("R", c)}";
        }

        /// <summary>Reads a rect written by <see cref="FormatRect"/>; false for anything else.</summary>
        public static bool TryParseRect(string text, out Rect rect)
        {
            rect = default;
            if (string.IsNullOrEmpty(text)) return false;

            string[] parts = text.Split(';');
            if (parts.Length != 4) return false;

            var values = new float[4];
            for (int i = 0; i < 4; i++)
            {
                if (!float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]) ||
                    float.IsNaN(values[i]) || float.IsInfinity(values[i]))
                {
                    return false;
                }
            }

            rect = new Rect(values[0], values[1], values[2], values[3]);
            return true;
        }


        // Every section always draws the same rows, so the window's height doesn't change with the selection.
        void DrawTarget()
        {
            EditorGUILayout.LabelField("Target", EditorStyles.boldLabel);
            Sprite sprite = _target != null ? _target.sprite : null;
            EditorGUILayout.LabelField("Object", _target != null ? _target.name : "Select a GameObject with a SpriteRenderer");

            string spriteText = "–";
            if (sprite != null) spriteText = $"{sprite.name} ({sprite.rect.width:0} × {sprite.rect.height:0} px)";
            else if (_target != null) spriteText = "None";
            EditorGUILayout.LabelField("Sprite", spriteText);
        }

        void DrawSettings()
        {
            EditorGUILayout.LabelField("Settings", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            _settings.Mode = (FitMode)EditorGUILayout.EnumPopup("Mode", _settings.Mode);
            using (new EditorGUI.DisabledScope(_settings.Mode != FitMode.Compound))
            {
                _settings.MaxPrimitives = EditorGUILayout.IntSlider("Max primitives", _settings.MaxPrimitives, 1, FitSettings.MaxPrimitivesLimit);
                float percent = EditorGUILayout.Slider("Error tolerance (%)", _settings.Tolerance * 100f, 0f, 25f);
                _settings.Tolerance = percent / 100f;
                _settings.MergePass = EditorGUILayout.Toggle("Merge pass", _settings.MergePass);
            }

            EditorGUILayout.LabelField("Allowed shapes");
            using (new EditorGUI.IndentLevelScope())
            {
                AllowedShapes allowed = AllowedShapes.None;
                if (EditorGUILayout.ToggleLeft("Circles", _settings.Allowed.Allows(PrimitiveShape.Circle))) allowed |= AllowedShapes.Circle;
                if (EditorGUILayout.ToggleLeft("Capsules", _settings.Allowed.Allows(PrimitiveShape.Capsule))) allowed |= AllowedShapes.Capsule;
                if (EditorGUILayout.ToggleLeft("Boxes", _settings.Allowed.Allows(PrimitiveShape.Box))) allowed |= AllowedShapes.Box;
                _settings.Allowed = allowed;
            }

            _settings.AlphaThreshold = EditorGUILayout.Slider("Alpha threshold", _settings.AlphaThreshold, 0.01f, 1f);
            _settings.OverWeight = Mathf.Max(0f, EditorGUILayout.FloatField("Overfill weight", _settings.OverWeight));
            _settings.UnderWeight = Mathf.Max(0f, EditorGUILayout.FloatField("Underfill weight", _settings.UnderWeight));
            if (EditorGUI.EndChangeCheck()) ScheduleRecompute();

            EditorGUI.BeginChangeCheck();
            _showPreview = EditorGUILayout.Toggle("Show preview", _showPreview);
            _showHeatmap = EditorGUILayout.Toggle("Show error heatmap", _showHeatmap);
            if (EditorGUI.EndChangeCheck()) RecomputeNow();

            _oldColliderAction = (OldColliderAction)EditorGUILayout.EnumPopup("Old polygon collider", _oldColliderAction);
        }

        void DrawStats()
        {
            EditorGUILayout.LabelField("Stats", EditorStyles.boldLabel);
            if (_mask == null || _result == null)
            {
                foreach (string label in new[] { "Islands", "Primitives", "Overfill", "Underfill", "Weighted error", "Replaces", "Time" })
                {
                    EditorGUILayout.LabelField(label, "–");
                }

                return;
            }

            int circles = 0, capsules = 0, boxes = 0;
            foreach (Primitive primitive in _result.Primitives)
            {
                switch (primitive.Shape)
                {
                    case PrimitiveShape.Circle: circles++; break;
                    case PrimitiveShape.Capsule: capsules++; break;
                    case PrimitiveShape.Box: boxes++; break;
                }
            }

            FitError error = _result.Error;
            double weighted = error.Weighted(_settings.OverWeight, _settings.UnderWeight);
            EditorGUILayout.LabelField("Islands", _result.IslandCount.ToString());
            EditorGUILayout.LabelField("Primitives", $"{_result.Primitives.Count} ({circles} circle, {capsules} capsule, {boxes} box)");
            EditorGUILayout.LabelField("Overfill", $"{error.OverRatio:P2} ({error.Over} px)");
            EditorGUILayout.LabelField("Underfill", $"{error.UnderRatio:P2} ({error.Under} px)");
            EditorGUILayout.LabelField("Weighted error", $"{weighted:P2} (tolerance {_settings.Tolerance:P1})");

            PolygonInfo polygon = PolygonInfo.For(_target);
            EditorGUILayout.LabelField("Replaces", polygon.Source == null
                ? "No polygon"
                : $"{polygon.Paths} paths, {polygon.Vertices} vertices ({polygon.Source})");
            EditorGUILayout.LabelField("Time", $"mask {_maskMilliseconds:0.0} ms, fit {_fitMilliseconds:0.0} ms");
        }

        void DrawActions()
        {
            EditorGUILayout.LabelField("Actions", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            _collidersOnRoot = EditorGUILayout.ToggleLeft(CollidersOnRootLabel, _collidersOnRoot);
            if (EditorGUI.EndChangeCheck() && _collidersOnRoot)
            {
                _scrollToBottomPending = true;
                Repaint();
            }
            if (_collidersOnRoot) DrawRootPlacementWarning();
            _createAsTrigger = EditorGUILayout.ToggleLeft(CreateAsTriggerLabel, _createAsTrigger);

            using (new EditorGUILayout.HorizontalScope())
            {
                bool canApply = _target != null && _result != null && _result.Primitives.Count > 0;
                using (new EditorGUI.DisabledScope(!canApply))
                {
                    if (GUILayout.Button("Apply")) Apply();
                }

                using (new EditorGUI.DisabledScope(_target == null))
                {
                    if (GUILayout.Button("Revert to polygon")) Revert();
                }
            }

            int selected = BatchFitter.CollectTargets(Selection.gameObjects).Count;
            using (new EditorGUI.DisabledScope(selected == 0 || _settings.Allowed == AllowedShapes.None))
            {
                if (GUILayout.Button($"Apply to selection ({selected})")) ApplyToSelection();
            }

            using (new EditorGUI.DisabledScope(_target == null || _target.sprite == null))
            {
                if (GUILayout.Button("Re-read sprite")) Recompute(true);
            }
        }

        ColliderPlacement Placement => _collidersOnRoot ? ColliderPlacement.Root : ColliderPlacement.Child;

        const string RootPlacementLimitations =
            "Root colliders have limitations; use them only if you need everything on one object:\n" +
            "• The shapes can't be edited in the Inspector.\n" +
            "• Needs the object's own Rigidbody2D (a Static one is added if missing), so not for parts of a parent's Rigidbody2D.\n" +
            "• The tool asks before replacing any CustomCollider2D, since it can't tell which ones it made.";

        // Shown while the root option is on: the general limitations, then what Apply would do to the current target.
        void DrawRootPlacementWarning()
        {
            EditorGUILayout.HelpBox(RootPlacementLimitations, MessageType.Warning);
            if (_target == null) return;

            Rigidbody2D parentBody = ColliderWriter.FindParentBody(_target);
            if (parentBody != null)
            {
                EditorGUILayout.HelpBox(
                    $"'{_target.name}' belongs to the Rigidbody2D on '{parentBody.name}', so Apply won't work for it with this option on.",
                    MessageType.Error);
                return;
            }

            if (ColliderWriter.NeedsStaticBody(_target, ColliderPlacement.Root))
            {
                EditorGUILayout.HelpBox($"Apply will add a Static Rigidbody2D to '{_target.name}'.", MessageType.Info);
            }

            if (ColliderWriter.HasCustomCollider(_target))
            {
                EditorGUILayout.HelpBox($"'{_target.name}' already has a CustomCollider2D. You'll be asked before it's replaced.", MessageType.Info);
            }
        }

        // The tool can't tell a CustomCollider2D it made from one it didn't, so it always asks before
        // replacing or removing one. Returns null when the user cancels.
        bool? ConfirmCustomCollider(string what, int count, bool forRevert)
        {
            if (count == 0) return false;

            if (!forRevert && Placement == ColliderPlacement.Root)
            {
                const string why = "\n\nThe tool can't tell whether it made it.";
                if (count == 1)
                {
                    bool confirmed = EditorUtility.DisplayDialog("Replace CustomCollider2D?",
                        $"{what} already has a CustomCollider2D. Replace its shapes with the fitted ones?" + why, "Replace", "Cancel");
                    return confirmed ? true : (bool?)null;
                }

                int replace = EditorUtility.DisplayDialogComplex("Replace CustomCollider2D?",
                    $"{count} of the selected objects already have a CustomCollider2D. Replace their shapes with the fitted ones?" + why,
                    "Replace", "Cancel", "Skip Those");
                return replace == 0 ? true : replace == 2 ? false : (bool?)null;
            }

            string remove = count == 1
                ? $"{what} has a CustomCollider2D, possibly from an earlier Apply with \"{CollidersOnRootLabel.text}\" on. Remove it?"
                : $"{count} of the selected objects have a CustomCollider2D, possibly from an earlier Apply with \"{CollidersOnRootLabel.text}\" on. Remove them?";
            string detail = forRevert
                ? "\n\nKeep it if you added it yourself."
                : "\n\nKeeping it means it adds to the new colliders. Keep it if you added it yourself.";
            int choice = EditorUtility.DisplayDialogComplex("Remove CustomCollider2D?", remove + detail, "Remove", "Cancel", "Keep");
            return choice == 0 ? true : choice == 2 ? false : (bool?)null;
        }

        void ApplyToSelection()
        {
            List<SpriteRenderer> targets = BatchFitter.CollectTargets(Selection.gameObjects);
            int withCustom = targets.FindAll(ColliderWriter.HasCustomCollider).Count;
            bool? removeCustom = ConfirmCustomCollider(null, withCustom, false);
            if (removeCustom == null) return;

            BatchReport report;
            try
            {
                report = BatchFitter.Apply(targets, _settings, _oldColliderAction, (index, total, name) =>
                    EditorUtility.DisplayCancelableProgressBar("Sprite Collider Fitter", $"{name} ({index + 1}/{total})", (float)index / total),
                    Placement, removeCustom.Value, _createAsTrigger);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            SetMessage(report.ToString(), report.Problems.Count > 0 ? MessageType.Warning : MessageType.Info);
            if (report.Problems.Count > 0) UnityEngine.Debug.LogWarning($"Sprite Collider Fitter: {report}");
        }

        void Apply()
        {
            // Make sure a pending settings change is in the result being written.
            if (_recomputeAt >= 0) RecomputeNow();
            if (_target == null || _result == null || _mask == null) return;

            var local = new List<Primitive>(_result.Primitives.Count);
            foreach (Primitive primitive in _result.Primitives) local.Add(primitive.ToLocal(_mask.Pivot, _mask.PixelsPerUnit));

            bool? removeCustom = ConfirmCustomCollider($"'{_target.name}'", ColliderWriter.HasCustomCollider(_target) ? 1 : 0, false);
            if (removeCustom == null) return;

            try
            {
                bool addsBody = ColliderWriter.NeedsStaticBody(_target, Placement);
                ColliderWriter.Apply(_target, local, _oldColliderAction, Placement, removeCustom.Value, _createAsTrigger);
                string where = Placement == ColliderPlacement.Root ? "a CustomCollider2D on" : "colliders under";
                string body = addsBody ? " Added a static Rigidbody2D so they move with the object." : "";
                SetMessage($"Applied {local.Count} shapes as {where} '{_target.name}'.{body} Ctrl+Z undoes it.", MessageType.Info);
            }
            catch (InvalidOperationException exception)
            {
                SetMessage(exception.Message, MessageType.Error);
            }
        }

        void Revert()
        {
            bool? removeCustom = ConfirmCustomCollider($"'{_target.name}'", ColliderWriter.HasCustomCollider(_target) ? 1 : 0, true);
            if (removeCustom == null) return;

            try
            {
                bool reverted = ColliderWriter.Revert(_target, removeCustom.Value);
                SetMessage(reverted ? $"Reverted '{_target.name}' to its polygon collider." : "Nothing to revert.", MessageType.Info);
            }
            catch (InvalidOperationException exception)
            {
                SetMessage(exception.Message, MessageType.Error);
            }
        }

        void OnSceneGUI(SceneView sceneView)
        {
            if (_target == null || _mask == null || _result == null) return;
            if (Event.current.type != EventType.Repaint) return;

            // SpriteRenderer flips mirror the sprite about the object's origin.
            var flip = new Vector3(_target.flipX ? -1f : 1f, _target.flipY ? -1f : 1f, 1f);
            Matrix4x4 matrix = _target.transform.localToWorldMatrix * Matrix4x4.Scale(flip);

            if (_showHeatmap && _classes != null) _heatmap.Draw(matrix, _mask);

            if (!_showPreview) return;

            using (new Handles.DrawingScope(AccentColor, matrix))
            {
                foreach (Primitive primitive in _result.Primitives)
                {
                    PrimitiveHandles.Draw(primitive.ToLocal(_mask.Pivot, _mask.PixelsPerUnit));
                }
            }
        }
    }
}
