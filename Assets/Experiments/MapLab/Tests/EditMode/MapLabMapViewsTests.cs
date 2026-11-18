using NUnit.Framework;
using UnityEngine;

namespace KingdomSurvival.MapLab.Tests
{
    // Виды карты: встроенные виды, переключение по кругу, ключ пересборки,
    // свои рисунки по ID и шейдерный глобал цветокоррекции.
    public sealed class MapLabMapViewsTests
    {
        private string savedId;

        [SetUp]
        public void SetUp()
        {
            savedId = MapLabMapViews.ActiveId;
        }

        [TearDown]
        public void TearDown()
        {
            MapLabMapViews.ActiveId = savedId;
            MapLabViewGradeShader.Neutral();
        }

        [Test]
        public void BuiltInViews_ClassicFirstAndNeutral_GloomAndSepiaGraded()
        {
            var views = MapLabMapViews.CreateBuiltIn();
            Assert.AreEqual(3, views.Count);
            Assert.AreEqual(MapLabMapViews.ClassicId, views[0].Id);
            Assert.IsTrue(views[0].Grade.IsNeutral, "Обычный вид — прежний облик без коррекции.");
            Assert.IsNull(views[0].Palette);
            Assert.AreEqual(0, views[0].ArtCount);
            Assert.AreEqual(MapLabMapViews.GloomId, views[1].Id);
            Assert.AreEqual(MapLabMapViews.SepiaId, views[2].Id);
            Assert.IsFalse(views[1].Grade.IsNeutral);
            Assert.IsFalse(views[2].Grade.IsNeutral);
            Assert.AreEqual(1f, views[2].Grade.GradientMix, 1e-4f, "Сепия — полная градиентная карта.");
        }

        [Test]
        public void EnsureBuiltIn_AddsMissing_KeepsAuthorViews()
        {
            MapLabMapViews asset = ScriptableObject.CreateInstance<MapLabMapViews>();
            try
            {
                MapLabMapView custom = new MapLabMapView { Id = "winter", Name = "Зима" };
                asset.Views.Add(custom);
                Assert.IsTrue(asset.EnsureBuiltIn());
                Assert.AreEqual(4, asset.Views.Count, "Встроенные три + свой вид автора (видов может быть больше трёх).");
                Assert.AreSame(custom, asset.Find("winter"));
                Assert.IsFalse(asset.EnsureBuiltIn(), "Повторный вызов ничего не меняет.");
            }
            finally
            {
                Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void Cycle_WalksEnabledViewsAndWraps()
        {
            MapLabMapViews.ActiveId = MapLabMapViews.ClassicId;
            int count = MapLabMapViews.Load().EnabledViews().Count;
            Assert.GreaterOrEqual(count, 3);
            string first = MapLabMapViews.Active.Id;
            for (int i = 0; i < count; i++)
                MapLabMapViews.Cycle();
            Assert.AreEqual(first, MapLabMapViews.Active.Id, "Полный круг возвращает к исходному виду.");
            MapLabMapViews.Cycle();
            Assert.AreNotEqual(first, MapLabMapViews.Active.Id);
        }

        [Test]
        public void UnknownActiveId_FallsBackToFirstEnabled()
        {
            MapLabMapViews.ActiveId = "no-such-view";
            Assert.IsNotNull(MapLabMapViews.Active);
            Assert.IsTrue(MapLabMapViews.Active.Enabled);
        }

        [Test]
        public void BuildKey_IgnoresGrade_ChangesWithArt()
        {
            MapLabMapView v = MapLabMapViews.Sepia();
            string key = v.BuildKey;
            v.Grade.Brightness = 0.5f;
            v.Grade.Strength = 0.3f;
            Assert.AreEqual(key, v.BuildKey, "Цветокоррекция мгновенная — пересборка не нужна.");
            Texture2D texture = new Texture2D(4, 4) { name = "House_Thatch_A_P35" };
            try
            {
                v.Art.Add(new MapLabViewArtOverride { ArtId = "House_Thatch_A_P35", Texture = texture });
                v.InvalidateIndex();
                Assert.AreNotEqual(key, v.BuildKey, "Свой рисунок вида требует пересборки.");
                Assert.AreSame(texture, v.FindTexture("House_Thatch_A_P35"));
                Assert.IsNull(v.FindTexture("Barn_A_P35"));
                v.Art[0].Enabled = false;
                v.InvalidateIndex();
                Assert.IsNull(v.FindTexture("House_Thatch_A_P35"), "Выключенный свой рисунок не берётся.");
                v.Art[0].Enabled = true;
                v.InvalidateIndex();
                MapLabMapView other = v.Clone();
                other.Id = "other";
                Assert.AreNotEqual(v.BuildKey, other.BuildKey, "Те же имена PNG у другого вида — другой ключ пересборки.");
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }

        [Test]
        public void GradeShader_AppliesAndResets()
        {
            MapLabViewGradeShader.Apply(MapLabMapViews.Gloom());
            Assert.Greater(Shader.GetGlobalVector("_MapLabGrade").x, 0f);
            MapLabViewGradeShader.Neutral();
            Assert.AreEqual(0f, Shader.GetGlobalVector("_MapLabGrade").x);
            MapLabViewGradeShader.Apply(MapLabMapViews.Classic());
            Assert.AreEqual(0f, Shader.GetGlobalVector("_MapLabGrade").x, "Обычный вид не ставит коррекцию.");
        }

        [Test]
        public void Clone_IsIndependent()
        {
            MapLabMapView v = MapLabMapViews.Gloom();
            MapLabMapView copy = v.Clone();
            copy.Grade.Brightness = 1.9f;
            copy.Art.Add(new MapLabViewArtOverride { ArtId = "x" });
            Assert.AreNotEqual(1.9f, v.Grade.Brightness);
            Assert.AreEqual(0, v.Art.Count);
        }
    }
}
