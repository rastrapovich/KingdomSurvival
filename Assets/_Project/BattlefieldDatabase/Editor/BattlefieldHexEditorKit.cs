using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace KingdomSurvival.BattlefieldDatabase.Editor
{
    // Общие карточки настроек сетки и вида гекса: одни и те же в «Базе
    // полей боя» и в «Базе локаций» (бой на месте), чтобы настройки и их
    // вид не расходились. Поля привязываются к SerializedObject базы полей.
    public static class BattlefieldHexEditorKit
    {
        public const string HexImageFolder = "Assets/_Project/Art/Battlefields/Hexes";

        public static readonly Color CardBackground = new Color(0.20f, 0.205f, 0.225f, 1f);
        public static readonly Color CardBorder = new Color(0.10f, 0.10f, 0.11f, 1f);
        public static readonly Color MutedText = new Color(0.62f, 0.62f, 0.62f, 1f);
        public static readonly Color GridAccent = new Color(0.40f, 0.62f, 0.90f, 1f);
        public static readonly Color HexAccent = new Color(0.68f, 0.52f, 0.88f, 1f);

        private static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".psd", ".tga", ".tif", ".tiff", ".bmp", ".webp" };
        private static readonly Color DropHighlight = new Color(0.86f, 0.70f, 0.38f, 1f);

        public static SerializedProperty FieldProperty(SerializedObject serialized, int fieldIndex) =>
            serialized.FindProperty("battlefields").GetArrayElementAtIndex(fieldIndex);

        // ── Карточка «Сетка» ───────────────────────────────────────────────

        // Размер и сдвиг сетки поля. extraButtons — в ряд с «Сбросить положение».
        public static VisualElement GridCard(
            SerializedObject serialized,
            SerializedProperty field,
            string hint,
            Action changed,
            params VisualElement[] extraButtons)
        {
            VisualElement card = SectionCard("СЕТКА", GridAccent);
            if (!string.IsNullOrEmpty(hint))
                card.Add(Hint(hint, 0f, 4f));
            card.Add(BoundSlider("Размер", field.FindPropertyRelative("gridScale"), 0.5f, 1.5f));
            SerializedProperty offset = field.FindPropertyRelative("gridOffset");
            card.Add(BoundSlider("Сдвиг X", offset.FindPropertyRelative("x"), -0.3f, 0.3f));
            card.Add(BoundSlider("Сдвиг Y", offset.FindPropertyRelative("y"), -0.3f, 0.3f));

            VisualElement row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.Add(SmallButton("Сбросить положение", () => ResetValues(serialized, field, changed,
                ("gridScale", 1f), ("gridOffset.x", 0f), ("gridOffset.y", 0f))));
            foreach (VisualElement button in extraButtons)
                row.Add(button);
            card.Add(row);
            return card;
        }

        // ── Карточка «Вид гекса» ───────────────────────────────────────────

        // rebuild — пересобрать настройки (сменился общий/свой вид или сброс);
        // changed — значения изменились, обновить предпросмотр.
        public static VisualElement HexCard(
            BattlefieldDatabaseAsset database,
            SerializedObject serialized,
            int fieldIndex,
            EditorWindow owner,
            bool showStateSamples,
            Action<bool> stateSamplesChanged,
            Action rebuild,
            Action changed)
        {
            SerializedProperty field = FieldProperty(serialized, fieldIndex);
            VisualElement card = SectionCard("ВИД ГЕКСА", HexAccent);
            SerializedProperty useOwn = field.FindPropertyRelative("useOwnHexStyle");
            Toggle own = new Toggle("Свой вид у этого поля") { value = useOwn.boolValue };
            Compact(own);
            own.tooltip = "Выключено — общий вид для всех полей базы. Включено — вид только этого поля " +
                          "(при включении копируется общий).";
            own.RegisterValueChangedCallback(evt =>
            {
                SetOwnHexStyle(database, serialized, fieldIndex, evt.newValue);
                rebuild?.Invoke();
            });
            card.Add(own);

            Label scope = new Label(useOwn.boolValue
                ? "Правки ниже — только для этого поля."
                : "Правки ниже — для всех полей без своего вида.");
            scope.style.fontSize = 10f;
            scope.style.color = MutedText;
            scope.style.marginBottom = 4f;
            card.Add(scope);

            SerializedProperty style = useOwn.boolValue
                ? field.FindPropertyRelative("hexStyle")
                : serialized.FindProperty("hexStyle");

            card.Add(ImageSlot(owner, serialized, "Картинка гекса", style.FindPropertyRelative("hexImage"), HexImageFolder, changed));
            card.Add(BoundColor("Оттенок картинки", style.FindPropertyRelative("hexImageTint")));
            card.Add(ImageSlot(owner, serialized, "Картинка рамки", style.FindPropertyRelative("frameImage"), HexImageFolder, changed));
            card.Add(BoundColor("Оттенок рамки", style.FindPropertyRelative("frameImageTint")));
            card.Add(BoundSlider("Размер картинок", style.FindPropertyRelative("imageScale"), 0.5f, 1.5f));
            card.Add(BoundColor("Подложка", style.FindPropertyRelative("fillColor")));
            card.Add(BoundColor("Линия", style.FindPropertyRelative("lineColor")));
            card.Add(BoundSlider("Толщина линии", style.FindPropertyRelative("lineWidth"), 0f, 6f));
            card.Add(BoundSlider("Зазор", style.FindPropertyRelative("gap"), 0f, 0.3f));
            card.Add(BoundSlider("Непрозрачность", style.FindPropertyRelative("opacity"), 0f, 1f));

            Foldout states = new Foldout { text = "Цвета состояний в бою", value = false };
            states.Add(BoundColor("Трудный", style.FindPropertyRelative("difficultColor")));
            states.Add(BoundColor("Непроходимый", style.FindPropertyRelative("impassableColor")));
            states.Add(BoundColor("Доступный ход", style.FindPropertyRelative("reachableColor")));
            states.Add(BoundSlider("Толщина хода", style.FindPropertyRelative("reachableLineWidth"), 0.5f, 6f));
            states.Add(BoundColor("Цель", style.FindPropertyRelative("targetColor")));
            states.Add(BoundColor("Наведение: заливка", style.FindPropertyRelative("attackHoverFill")));
            states.Add(BoundColor("Наведение: линия", style.FindPropertyRelative("attackHoverLine")));
            card.Add(states);

            Toggle samples = new Toggle("Показать состояния на предпросмотре") { value = showStateSamples };
            samples.RegisterValueChangedCallback(evt => stateSamplesChanged?.Invoke(evt.newValue));
            card.Add(samples);

            card.Add(SmallButton("Сбросить вид гекса", () =>
            {
                ResetHexStyle(database, serialized, fieldIndex);
                rebuild?.Invoke();
            }));
            card.Add(Hint("Картинка гекса растягивается на прямоугольник гекса: ширина : высота ≈ 1,15 : 1. " +
                          "Изображения можно перетаскивать прямо на ячейки.", 4f, 0f));
            return card;
        }

        public static void SetOwnHexStyle(BattlefieldDatabaseAsset database, SerializedObject serialized, int fieldIndex, bool own)
        {
            Undo.RecordObject(database, own ? "Свой вид гекса" : "Общий вид гекса");
            BattlefieldDefinitionData field = database.Battlefields[fieldIndex];
            if (own)
                EditorJsonUtility.FromJsonOverwrite(EditorJsonUtility.ToJson(database.HexStyle), field.OwnHexStyle);
            serialized.Update();
            FieldProperty(serialized, fieldIndex).FindPropertyRelative("useOwnHexStyle").boolValue = own;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(database);
        }

        public static void ResetHexStyle(BattlefieldDatabaseAsset database, SerializedObject serialized, int fieldIndex)
        {
            Undo.RecordObject(database, "Сбросить вид гекса");
            BattlefieldHexStyle style = database.GetHexStyle(database.Battlefields[fieldIndex]);
            EditorJsonUtility.FromJsonOverwrite(EditorJsonUtility.ToJson(new BattlefieldHexStyle()), style);
            EditorUtility.SetDirty(database);
            serialized.Update();
        }

        public static void ResetValues(SerializedObject serialized, SerializedProperty field, Action changed,
            params (string Path, float Value)[] values)
        {
            serialized.Update();
            foreach ((string path, float value) in values)
                field.FindPropertyRelative(path).floatValue = value;
            serialized.ApplyModifiedProperties();
            changed?.Invoke();
        }

        // ── Элементы настроек ──────────────────────────────────────────────

        public static VisualElement SectionCard(string title, Color accent)
        {
            VisualElement card = new VisualElement();
            card.style.marginLeft = 4f;
            card.style.marginRight = 6f;
            card.style.marginBottom = 8f;
            card.style.paddingLeft = 10f;
            card.style.paddingRight = 10f;
            card.style.paddingTop = 7f;
            card.style.paddingBottom = 9f;
            card.style.backgroundColor = CardBackground;
            card.style.borderTopWidth = 1f;
            card.style.borderRightWidth = 1f;
            card.style.borderBottomWidth = 1f;
            card.style.borderLeftWidth = 3f;
            card.style.borderTopColor = CardBorder;
            card.style.borderRightColor = CardBorder;
            card.style.borderBottomColor = CardBorder;
            card.style.borderLeftColor = accent;
            card.style.borderTopLeftRadius = 5f;
            card.style.borderTopRightRadius = 5f;
            card.style.borderBottomLeftRadius = 5f;
            card.style.borderBottomRightRadius = 5f;
            Label header = new Label(title);
            header.style.unityFontStyleAndWeight = FontStyle.Bold;
            header.style.fontSize = 11f;
            header.style.letterSpacing = 1f;
            header.style.color = accent;
            header.style.marginBottom = 5f;
            card.Add(header);
            return card;
        }

        public static Label Hint(string text, float marginTop, float marginBottom)
        {
            Label hint = new Label(text);
            hint.style.whiteSpace = WhiteSpace.Normal;
            hint.style.fontSize = 10f;
            hint.style.color = MutedText;
            hint.style.marginTop = marginTop;
            hint.style.marginBottom = marginBottom;
            return hint;
        }

        public static void Compact(VisualElement field)
        {
            field.style.marginLeft = 0f;
            field.style.marginRight = 0f;
            Label label = field.Q<Label>(className: BaseField<int>.labelUssClassName);
            if (label == null)
                return;
            label.style.minWidth = 112f;
            label.style.width = 112f;
        }

        public static Slider BoundSlider(string label, SerializedProperty property, float min, float max)
        {
            Slider slider = new Slider(label, min, max) { showInputField = true, bindingPath = property.propertyPath };
            Compact(slider);
            return slider;
        }

        public static ColorField BoundColor(string label, SerializedProperty property)
        {
            ColorField field = new ColorField(label) { bindingPath = property.propertyPath, showAlpha = true };
            Compact(field);
            return field;
        }

        public static Button SmallButton(string text, Action action)
        {
            Button button = new Button(action) { text = text };
            button.style.height = 20f;
            button.style.marginLeft = 0f;
            button.style.marginRight = 4f;
            button.style.marginTop = 4f;
            button.style.fontSize = 11f;
            button.style.alignSelf = Align.FlexStart;
            return button;
        }

        // Ячейка картинки: миниатюра + поле выбора; принимает перетаскивание
        // спрайтов, текстур проекта и файлов из проводника.
        public static VisualElement ImageSlot(
            EditorWindow owner,
            SerializedObject serialized,
            string label,
            SerializedProperty property,
            string importFolder,
            Action changed)
        {
            VisualElement slot = new VisualElement();
            slot.style.flexDirection = FlexDirection.Row;
            slot.style.alignItems = Align.Center;
            slot.style.marginTop = 3f;
            slot.style.marginBottom = 3f;
            slot.style.paddingLeft = 3f;
            slot.style.paddingTop = 3f;
            slot.style.paddingBottom = 3f;
            slot.style.backgroundColor = new Color(0.16f, 0.165f, 0.18f, 1f);
            slot.style.borderTopLeftRadius = 3f;
            slot.style.borderTopRightRadius = 3f;
            slot.style.borderBottomLeftRadius = 3f;
            slot.style.borderBottomRightRadius = 3f;

            Image thumb = new Image { scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
            thumb.style.width = 58f;
            thumb.style.height = 40f;
            thumb.style.marginRight = 6f;
            thumb.style.backgroundColor = new Color(0.08f, 0.085f, 0.09f, 1f);
            thumb.sprite = property.objectReferenceValue as Sprite;
            slot.Add(thumb);

            VisualElement column = new VisualElement();
            column.style.flexGrow = 1f;
            column.style.flexShrink = 1f;
            Label caption = new Label(label);
            caption.style.fontSize = 10f;
            caption.style.color = MutedText;
            caption.style.whiteSpace = WhiteSpace.Normal;
            column.Add(caption);
            ObjectField picker = new ObjectField
            {
                objectType = typeof(Sprite),
                allowSceneObjects = false,
                bindingPath = property.propertyPath
            };
            picker.style.marginLeft = 0f;
            picker.RegisterValueChangedCallback(evt => thumb.sprite = evt.newValue as Sprite);
            column.Add(picker);
            slot.Add(column);

            string path = property.propertyPath;
            RegisterImageDrop(owner, slot, importFolder, false, sprites =>
            {
                serialized.Update();
                serialized.FindProperty(path).objectReferenceValue = sprites[0];
                serialized.ApplyModifiedProperties();
                thumb.sprite = sprites[0];
                changed?.Invoke();
            });
            return slot;
        }

        // ── Перетаскивание картинок ────────────────────────────────────────
        // Спрайты и текстуры проекта, файлы из проводника. Внешний файл
        // копируется в папку проекта, текстура переводится в спрайт.

        public static void RegisterImageDrop(
            EditorWindow owner,
            VisualElement target,
            string importFolder,
            bool multiple,
            Action<List<Sprite>> onDrop)
        {
            void Highlight(bool on)
            {
                Color color = on ? DropHighlight : Color.clear;
                float width = on ? 2f : 0f;
                target.style.borderTopWidth = width;
                target.style.borderBottomWidth = width;
                target.style.borderRightWidth = width;
                target.style.borderLeftWidth = width;
                target.style.borderTopColor = color;
                target.style.borderBottomColor = color;
                target.style.borderRightColor = color;
                target.style.borderLeftColor = color;
            }

            target.RegisterCallback<DragUpdatedEvent>(evt =>
            {
                if (!HasDraggedImage())
                    return;
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                Highlight(true);
                evt.StopPropagation();
            });
            target.RegisterCallback<DragLeaveEvent>(_ => Highlight(false));
            target.RegisterCallback<DragExitedEvent>(_ => Highlight(false));
            target.RegisterCallback<DragPerformEvent>(evt =>
            {
                Highlight(false);
                if (!HasDraggedImage())
                    return;
                DragAndDrop.AcceptDrag();
                evt.StopPropagation();
                List<Sprite> sprites = ImportDraggedSprites(importFolder, multiple);
                if (sprites.Count > 0)
                    onDrop(sprites);
                else if (owner != null)
                    owner.ShowNotification(new GUIContent("Не удалось получить картинку"));
            });
        }

        public static bool HasDraggedImage()
        {
            foreach (UnityEngine.Object dragged in DragAndDrop.objectReferences)
            {
                if (dragged is Sprite || dragged is Texture2D)
                    return true;
            }
            foreach (string path in DragAndDrop.paths)
            {
                if (IsImagePath(path))
                    return true;
            }
            return false;
        }

        private static bool IsImagePath(string path)
        {
            string extension = Path.GetExtension(path ?? string.Empty).ToLowerInvariant();
            return Array.IndexOf(ImageExtensions, extension) >= 0;
        }

        public static List<Sprite> ImportDraggedSprites(string importFolder, bool multiple)
        {
            List<Sprite> sprites = new List<Sprite>();
            HashSet<string> seen = new HashSet<string>();

            foreach (UnityEngine.Object dragged in DragAndDrop.objectReferences)
            {
                Sprite sprite = dragged as Sprite;
                if (sprite == null && dragged is Texture2D)
                    sprite = EnsureSprite(AssetDatabase.GetAssetPath(dragged));
                if (sprite != null && seen.Add(AssetDatabase.GetAssetPath(sprite)))
                    sprites.Add(sprite);
                if (sprites.Count > 0 && !multiple)
                    return sprites;
            }

            // Файлы из проводника: objectReferences пуст, есть только пути.
            foreach (string path in DragAndDrop.paths)
            {
                if (!IsImagePath(path))
                    continue;
                string assetPath = ToProjectAsset(path, importFolder);
                if (assetPath == null || !seen.Add(assetPath))
                    continue;
                Sprite sprite = EnsureSprite(assetPath);
                if (sprite != null)
                    sprites.Add(sprite);
                if (sprites.Count > 0 && !multiple)
                    break;
            }
            return sprites;
        }

        // Путь внутри проекта; внешний файл копируется в importFolder.
        private static string ToProjectAsset(string path, string importFolder)
        {
            string normalized = path.Replace('\\', '/');
            if (normalized.StartsWith("Assets/", StringComparison.Ordinal))
                return normalized;

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).Replace('\\', '/') + "/";
            string full = Path.GetFullPath(path).Replace('\\', '/');
            if (full.StartsWith(projectRoot + "Assets/", StringComparison.OrdinalIgnoreCase))
                return full.Substring(projectRoot.Length);
            if (!File.Exists(full))
                return null;

            Directory.CreateDirectory(Path.Combine(projectRoot, importFolder));
            string target = AssetDatabase.GenerateUniqueAssetPath(importFolder + "/" + Path.GetFileName(full));
            File.Copy(full, Path.Combine(projectRoot, target));
            AssetDatabase.ImportAsset(target);
            return target;
        }

        // Текстура как спрайт без мип-карт; фоны — до 4096 пикселей.
        private static Sprite EnsureSprite(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
                return null;
            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer != null && (importer.textureType != TextureImporterType.Sprite ||
                                     importer.spriteImportMode == SpriteImportMode.None))
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.mipmapEnabled = false;
                importer.alphaIsTransparency = true;
                importer.maxTextureSize = Mathf.Max(importer.maxTextureSize, 4096);
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
        }
    }
}
