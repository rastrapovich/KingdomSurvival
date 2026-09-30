using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace KingdomSurvival.AnimationDatabase.Editor
{
    // Тестовые кадры в структуре экспорта KS Sprite Renderer:
    // <папка>/<Действие>/<Ракурс>/<Действие>_<Ракурс>_<номер>.png.
    // Нужны, чтобы проверить импорт, предпросмотр и бой до настоящих спрайтов.
    // Номера — как у Step 3 с отрицательным стартом и добавленным последним.
    public static class CreatureAnimationTestFrames
    {
        public const int Width = 96;
        public const int Height = 128;

        private static readonly Vector2[] FacingVectors =
        {
            new Vector2(1.732f, 0f),
            new Vector2(0.866f, 1.125f),
            new Vector2(-0.866f, 1.125f),
            new Vector2(-1.732f, 0f),
            new Vector2(-0.866f, -1.125f),
            new Vector2(0.866f, -1.125f)
        };

        [MenuItem("Kingdom Survival/База анимаций: создать тестовые кадры…", priority = 2001)]
        private static void GenerateFromMenu()
        {
            string parent = EditorUtility.OpenFolderPanel("Куда положить тестовые кадры", string.Empty, string.Empty);
            if (string.IsNullOrEmpty(parent))
                return;
            string root = Path.Combine(parent, "KS_TestCreature").Replace('\\', '/');
            CreatureAnimationDatabaseAsset database = CreatureAnimationEditorData.LoadOrCreate();
            int count = Generate(root, database, true);
            EditorUtility.DisplayDialog(
                "Тестовые кадры",
                "Создано " + count + " PNG в\n" + root + "\n\nПеретащите эту папку в «Базу анимаций» на любое существо.",
                "Хорошо");
        }

        // Номера кадров экспорта: start..end через step, последний всегда входит.
        public static List<int> ExportNumbers(int start, int end, int step)
        {
            List<int> numbers = new List<int>();
            for (int value = start; value <= end; value += Math.Max(1, step))
                numbers.Add(value);
            if (numbers.Count == 0 || numbers[numbers.Count - 1] != end)
                numbers.Add(end);
            return numbers;
        }

        public static int Generate(string root, CreatureAnimationDatabaseAsset database, bool withShoot)
        {
            int count = 0;
            List<(CreatureAnimationAction action, List<int> numbers)> actions = new List<(CreatureAnimationAction, List<int>)>
            {
                (CreatureAnimationAction.Idle, ExportNumbers(1, 20, 3)),
                (CreatureAnimationAction.Walk, ExportNumbers(-1, 22, 3)),
                (CreatureAnimationAction.Attack, ExportNumbers(1, 24, 3)),
                (CreatureAnimationAction.Hit, ExportNumbers(1, 12, 3)),
                (CreatureAnimationAction.Death, ExportNumbers(1, 30, 3))
            };
            if (withShoot)
                actions.Add((CreatureAnimationAction.Shoot, ExportNumbers(1, 18, 3)));

            foreach ((CreatureAnimationAction action, List<int> numbers) in actions)
            {
                foreach (CreatureAnimationDirection direction in CreatureAnimationLabels.Directions)
                {
                    string folder = Path.Combine(root, action.ToString(), CreatureAnimationLabels.DirectionFolder(direction));
                    Directory.CreateDirectory(folder);
                    HexFacing facing = database != null
                        ? database.GetFacing(direction)
                        : CreatureAnimationDatabaseAsset.DefaultDirectionMap()[(int)direction].Facing;
                    for (int i = 0; i < numbers.Count; i++)
                    {
                        float phase = numbers.Count > 1 ? i / (float)(numbers.Count - 1) : 0f;
                        byte[] png = DrawFrame(action, facing, phase, numbers[i]);
                        string name = action + "_" + CreatureAnimationLabels.DirectionFolder(direction) + "_" +
                                      (numbers[i] < 0 ? "-" + Math.Abs(numbers[i]).ToString("000") : numbers[i].ToString("0000")) + ".png";
                        File.WriteAllBytes(Path.Combine(folder, name), png);
                        count++;
                    }
                }
            }
            return count;
        }

        private static byte[] DrawFrame(CreatureAnimationAction action, HexFacing facing, float phase, int number)
        {
            Color32[] pixels = new Color32[Width * Height];
            Vector2 facingVector = FacingVectors[(int)facing].normalized;
            Vector2 feet = new Vector2(Width * 0.5f, Height * 0.15f);
            Color32 body = BodyColor(action);
            float bob = 0f;
            float lean = 0f;
            float fall = 0f;
            float flash = 0f;

            switch (action)
            {
                case CreatureAnimationAction.Idle:
                    bob = Mathf.Sin(phase * Mathf.PI * 2f) * 2f;
                    break;
                case CreatureAnimationAction.Walk:
                    bob = Mathf.Abs(Mathf.Sin(phase * Mathf.PI * 4f)) * 4f;
                    break;
                case CreatureAnimationAction.Attack:
                case CreatureAnimationAction.Shoot:
                    lean = Mathf.Sin(Mathf.Clamp01(phase * 1.4f) * Mathf.PI) * 14f;
                    break;
                case CreatureAnimationAction.Hit:
                    lean = -Mathf.Sin(phase * Mathf.PI) * 8f;
                    flash = 1f - phase;
                    break;
                case CreatureAnimationAction.Death:
                    fall = Mathf.Clamp01(phase * 1.2f);
                    break;
            }

            // Тень под ногами — точка опоры.
            FillEllipse(pixels, feet, 22f, 5f, new Color32(0, 0, 0, 90));

            Vector2 torso = feet + new Vector2(0f, 38f + bob) + facingVector * lean;
            if (fall > 0f)
                torso = Vector2.Lerp(torso, feet + new Vector2(facingVector.x >= 0f ? -28f : 28f, 8f), fall);
            float radiusX = Mathf.Lerp(18f, 32f, fall);
            float radiusY = Mathf.Lerp(30f, 12f, fall);
            Color32 torsoColor = flash > 0f ? Color32.Lerp(body, new Color32(255, 255, 255, 255), flash * 0.7f) : body;
            FillEllipse(pixels, torso, radiusX, radiusY, torsoColor);

            // «Голова» и стрелка смотрят туда же, куда ракурс на поле.
            Vector2 head = torso + new Vector2(0f, radiusY + 6f) * (1f - fall) + facingVector * 10f * (1f - fall);
            FillEllipse(pixels, head, 10f, 10f, Color32.Lerp(torsoColor, new Color32(255, 240, 210, 255), 0.4f));
            if (fall < 0.8f)
                DrawArrow(pixels, torso, facingVector, 26f, new Color32(255, 255, 255, 255));

            if (action == CreatureAnimationAction.Shoot && phase > 0.45f)
            {
                Vector2 projectile = torso + facingVector * (20f + (phase - 0.45f) * 60f);
                FillEllipse(pixels, projectile, 3f, 3f, new Color32(255, 230, 90, 255));
            }

            DrawNumber(pixels, number, 4, Height - 12);
            Texture2D texture = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
            try
            {
                texture.SetPixels32(pixels);
                texture.Apply(false);
                return texture.EncodeToPNG();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        private static Color32 BodyColor(CreatureAnimationAction action)
        {
            switch (action)
            {
                case CreatureAnimationAction.Walk: return new Color32(70, 140, 200, 255);
                case CreatureAnimationAction.Attack: return new Color32(200, 90, 60, 255);
                case CreatureAnimationAction.Hit: return new Color32(220, 170, 60, 255);
                case CreatureAnimationAction.Death: return new Color32(110, 110, 120, 255);
                case CreatureAnimationAction.Shoot: return new Color32(150, 90, 190, 255);
                default: return new Color32(80, 160, 90, 255);
            }
        }

        private static void FillEllipse(Color32[] pixels, Vector2 center, float radiusX, float radiusY, Color32 color)
        {
            int minX = Mathf.Max(0, Mathf.FloorToInt(center.x - radiusX));
            int maxX = Mathf.Min(Width - 1, Mathf.CeilToInt(center.x + radiusX));
            int minY = Mathf.Max(0, Mathf.FloorToInt(center.y - radiusY));
            int maxY = Mathf.Min(Height - 1, Mathf.CeilToInt(center.y + radiusY));
            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    float dx = (x - center.x) / Mathf.Max(0.5f, radiusX);
                    float dy = (y - center.y) / Mathf.Max(0.5f, radiusY);
                    if (dx * dx + dy * dy <= 1f)
                        Blend(pixels, x, y, color);
                }
            }
        }

        private static void DrawArrow(Color32[] pixels, Vector2 from, Vector2 direction, float length, Color32 color)
        {
            Vector2 tip = from + direction * length;
            DrawLine(pixels, from, tip, color);
            Vector2 side = new Vector2(-direction.y, direction.x);
            DrawLine(pixels, tip, tip - direction * 7f + side * 5f, color);
            DrawLine(pixels, tip, tip - direction * 7f - side * 5f, color);
        }

        private static void DrawLine(Color32[] pixels, Vector2 from, Vector2 to, Color32 color)
        {
            int steps = Mathf.CeilToInt(Vector2.Distance(from, to) * 2f) + 1;
            for (int i = 0; i <= steps; i++)
            {
                Vector2 point = Vector2.Lerp(from, to, i / (float)steps);
                for (int oy = 0; oy <= 1; oy++)
                    for (int ox = 0; ox <= 1; ox++)
                        Blend(pixels, Mathf.RoundToInt(point.x) + ox, Mathf.RoundToInt(point.y) + oy, color);
            }
        }

        // Цифры 3×5 пикселей, масштаб 2.
        private static readonly string[] Digits =
        {
            "111101101101111", "010110010010111", "111001111100111", "111001111001111", "101101111001001",
            "111100111001111", "111100111101111", "111001001001001", "111101111101111", "111101111001111"
        };

        private static void DrawNumber(Color32[] pixels, int number, int x, int top)
        {
            string text = number.ToString();
            int cursor = x;
            foreach (char symbol in text)
            {
                if (symbol == '-')
                {
                    for (int px = 0; px < 6; px++)
                        for (int py = 0; py < 2; py++)
                            Blend(pixels, cursor + px, top + 4 + py, new Color32(255, 255, 255, 255));
                    cursor += 8;
                    continue;
                }
                string mask = Digits[symbol - '0'];
                for (int row = 0; row < 5; row++)
                {
                    for (int column = 0; column < 3; column++)
                    {
                        if (mask[row * 3 + column] != '1')
                            continue;
                        for (int sy = 0; sy < 2; sy++)
                            for (int sx = 0; sx < 2; sx++)
                                Blend(pixels, cursor + column * 2 + sx, top + (4 - row) * 2 + sy, new Color32(255, 255, 255, 255));
                    }
                }
                cursor += 8;
            }
        }

        private static void Blend(Color32[] pixels, int x, int y, Color32 color)
        {
            if (x < 0 || y < 0 || x >= Width || y >= Height)
                return;
            int index = y * Width + x;
            Color32 below = pixels[index];
            float alpha = color.a / 255f;
            float outAlpha = alpha + below.a / 255f * (1f - alpha);
            if (outAlpha <= 0f)
                return;
            pixels[index] = new Color32(
                (byte)((color.r * alpha + below.r * (below.a / 255f) * (1f - alpha)) / outAlpha),
                (byte)((color.g * alpha + below.g * (below.a / 255f) * (1f - alpha)) / outAlpha),
                (byte)((color.b * alpha + below.b * (below.a / 255f) * (1f - alpha)) / outAlpha),
                (byte)(outAlpha * 255f));
        }
    }
}
