using System;
using System.Collections.Generic;
using KingdomSurvival.BattlefieldDatabase;
using UnityEngine;

namespace KingdomSurvival.LocationRendering
{
    // ПР-12М: бой на месте — фигуры рисует рендерер места. Поле боя по-прежнему
    // ведёт бой и считает кадр анимации, позицию, удар и отражение каждой
    // фигуры; здесь та же картинка встаёт на рисунок места как обычный спрайт,
    // поэтому на неё действуют общий свет, костры, тени и нормали.
    public sealed partial class LocationWorldRenderer
    {
        public struct BattleFigureFrame
        {
            public string Id;
            public Sprite Sprite;
            // Прямоугольник картинки и точка земли — пиксели рисунка места (Y вниз).
            public Rect Rect;
            public Vector2 Ground;
            public bool Mirrored;
            // Миниатюра вписана в прямоугольник с сохранением пропорций.
            public bool FitInside;
            public Color Tint;
            public bool Corpse;
        }

        private sealed class BattleFigure
        {
            public Transform Anchor;
            public SpriteRenderer Image;
            public bool Seen;
            public Caster Caster;
            public Vector2 Ground;
            public bool Corpse;
            public string SourceId;
            // Когда фигура пала (секунды рендерера) — растворение павших.
            public float DiedAt = float.NaN;
        }

        private readonly Dictionary<string, BattleFigure> battleFigures = new Dictionary<string, BattleFigure>(StringComparer.Ordinal);
        private Transform battleLayer;

        public int BattleFigureCount => battleFigures.Count;

        public void SetBattleFigures(IReadOnlyList<BattleFigureFrame> frames)
        {
            if (battleLayer == null)
                battleLayer = Child("Фигуры боя").transform;
            foreach (BattleFigure figure in battleFigures.Values) figure.Seen = false;
            foreach (BattleFigureFrame frame in frames ?? Array.Empty<BattleFigureFrame>())
            {
                if (string.IsNullOrEmpty(frame.Id) || frame.Sprite == null) continue;
                if (!battleFigures.TryGetValue(frame.Id, out BattleFigure figure))
                {
                    Transform anchor = new GameObject(frame.Id).transform;
                    anchor.SetParent(battleLayer, false);
                    GameObject imageObject = new GameObject("Фигура");
                    imageObject.transform.SetParent(anchor, false);
                    figure = new BattleFigure { Anchor = anchor, Image = Image(imageObject, frame.Sprite, false) };
                    ApplyPersonStyle(figure.Image);
                    battleFigures[frame.Id] = figure;
                    figure.Caster = AddCaster(anchor, figure.Image, null, 0, Vector2.zero, false, 1, frame.Id, true);
                }
                figure.Seen = true;
                PlaceBattleFigure(figure, frame);
            }
            List<string> gone = null;
            foreach (KeyValuePair<string, BattleFigure> entry in battleFigures)
            {
                if (entry.Value.Seen) continue;
                (gone ??= new List<string>()).Add(entry.Key);
            }
            if (gone != null)
            {
                foreach (string id in gone)
                {
                    Destroy(battleFigures[id].Anchor.gameObject);
                    battleFigures.Remove(id);
                }
            }
            UpdateShadows(peopleOnly: true);
        }

        public void ClearBattleFigures() => SetBattleFigures(Array.Empty<BattleFigureFrame>());

        // Растворение павшего (0..доля «до какой растворяется») по времени
        // с гибели; выключено или не пал — 0.
        private float DissolveAmount(float diedAt)
        {
            LocationSpriteEffects s = PostActive ? Sky.Post.Sprites : null;
            if (s == null || !s.Dissolve || float.IsNaN(diedAt)) return 0;
            float t = Mathf.Clamp01((Seconds - diedAt) / Mathf.Max(.05f, s.DissolveDuration));
            return Mathf.Clamp01(s.DissolveAmount) * t;
        }

        // Пробное растворение фигур исследования (окно «Обработка кадра»): 0..1.
        public void PreviewDissolve(float amount)
        {
            foreach (Actor actor in actors.Values) SetDissolve(actor.Image, Mathf.Clamp01(amount));
        }

        private void PlaceBattleFigure(BattleFigure figure, BattleFigureFrame frame)
        {
            SpriteRenderer image = figure.Image;
            image.sprite = frame.Sprite;
            image.flipX = frame.Mirrored;
            image.color = frame.Tint;

            Rect rect = frame.Rect;
            if (frame.FitInside)
            {
                // Как Image.ScaleToFit: картинка по центру, пропорции сохранены.
                float aspect = frame.Sprite.rect.width / Mathf.Max(1f, frame.Sprite.rect.height);
                float width = Mathf.Min(rect.width, rect.height * aspect);
                float height = width / Mathf.Max(.0001f, aspect);
                rect = new Rect(rect.center.x - width / 2, rect.center.y - height / 2, width, height);
            }

            figure.Ground = frame.Ground;
            if (frame.Corpse && !figure.Corpse) figure.DiedAt = Seconds;
            if (!frame.Corpse) figure.DiedAt = float.NaN;
            figure.Corpse = frame.Corpse;
            SetDissolve(image, DissolveAmount(figure.DiedAt));
            Vector2 ground = LocationVisualGeometry.PixelToWorld(Location, frame.Ground);
            figure.Anchor.localPosition = ground;
            Vector2 center = LocationVisualGeometry.PixelToWorld(Location, rect.center);
            Vector2 size = rect.size / LocationVisualGeometry.PixelsPerUnit;
            Vector2 bounds = frame.Sprite.bounds.size;
            Vector3 scale = new Vector3(size.x / Mathf.Max(.0001f, bounds.x), size.y / Mathf.Max(.0001f, bounds.y), 1);
            Vector2 boundsCenter = frame.Sprite.bounds.center;
            if (frame.Mirrored) boundsCenter.x = -boundsCenter.x;
            image.transform.localScale = scale;
            image.transform.localPosition = center - ground - new Vector2(boundsCenter.x * scale.x, boundsCenter.y * scale.y);
            image.sortingOrder = frame.Corpse
                ? LocationVisualGeometry.SortOrder(LocationVisualBand.GroundDetail, 0, 500)
                : LocationVisualGeometry.SortOrder(LocationVisualBand.World, ground.y);
            // Павший лежит — его тень не нужна.
            if (figure.Caster != null) figure.Caster.Disabled = frame.Corpse;
            figure.Anchor.gameObject.name = frame.Corpse ? frame.Id + " (пал)" : frame.Id;
        }
    }
}
