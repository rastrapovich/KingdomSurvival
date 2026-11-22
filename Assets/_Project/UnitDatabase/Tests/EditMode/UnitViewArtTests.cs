using System.Reflection;
using KingdomSurvival.UnitDatabase;
using NUnit.Framework;
using UnityEngine;

namespace KingdomSurvival.UnitDatabase.Tests
{
    // Рисунки по видам (19.11.2026): свой слот вида, подстановка «Обычного»
    // и статичная фигура вместо анимаций без потери ссылок на наборы.
    public sealed class UnitViewArtTests
    {
        private Texture2D texture;

        [SetUp]
        public void SetUp()
        {
            texture = new Texture2D(4, 4);
        }

        [TearDown]
        public void TearDown()
        {
            UnitArtViews.ActiveViewProvider = null;
            Object.DestroyImmediate(texture);
        }

        private Sprite NewSprite(string name)
        {
            Sprite sprite = Sprite.Create(texture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));
            sprite.name = name;
            return sprite;
        }

        private static UnitDefinitionData Creature(Sprite portrait, Sprite field, string animationSetId)
        {
            UnitDefinitionData unit = UnitDefinitionData.CreateCreature("probe", "Проба", UnitSize.Medium, 10, 1, 1, 1, 3, 3, 1, null, null);
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(UnitDefinitionData).GetField("portrait", flags).SetValue(unit, portrait);
            typeof(UnitDefinitionData).GetField("battlefieldSprite", flags).SetValue(unit, field);
            typeof(UnitDefinitionData).GetField("animationSetId", flags).SetValue(unit, animationSetId);
            return unit;
        }

        [Test]
        public void EmptyViewSlot_FallsBackToClassic()
        {
            Sprite portrait = NewSprite("classic_portrait");
            Sprite field = NewSprite("classic_field");
            UnitDefinitionData unit = Creature(portrait, field, "set_classic");

            Assert.AreSame(portrait, unit.PortraitFor(UnitArtViews.GloomId));
            Assert.AreSame(field, unit.BattlefieldSpriteFor(UnitArtViews.SepiaId));
            Assert.AreEqual("set_classic", unit.AnimationSetIdFor(UnitArtViews.GloomId));

            unit.GetOrAddViewArt(UnitArtViews.GloomId);
            Assert.AreSame(portrait, unit.PortraitFor(UnitArtViews.GloomId), "пустой слот вида — «Обычный»");
            Assert.AreEqual("set_classic", unit.AnimationSetIdFor(UnitArtViews.GloomId));
        }

        [Test]
        public void OwnViewArt_UsedOnlyInItsView()
        {
            Sprite portrait = NewSprite("classic_portrait");
            Sprite field = NewSprite("classic_field");
            Sprite gloomPortrait = NewSprite("gloom_portrait");
            Sprite gloomField = NewSprite("gloom_field");
            UnitDefinitionData unit = Creature(portrait, field, "set_classic");
            UnitViewArtData gloom = unit.GetOrAddViewArt(UnitArtViews.GloomId);
            gloom.SetPortrait(gloomPortrait);
            gloom.SetBattlefieldSprite(gloomField);
            gloom.SetAnimationSetId("set_gloom");

            Assert.AreSame(gloom, unit.GetOrAddViewArt(UnitArtViews.GloomId), "один слот на вид");
            Assert.IsNull(unit.GetOrAddViewArt(UnitArtViews.ClassicId), "у «Обычного» слота нет — это поля существа");
            Assert.AreSame(gloomPortrait, unit.PortraitFor(UnitArtViews.GloomId));
            Assert.AreSame(gloomField, unit.BattlefieldSpriteFor(UnitArtViews.GloomId));
            Assert.AreEqual("set_gloom", unit.AnimationSetIdFor(UnitArtViews.GloomId));
            Assert.AreSame(portrait, unit.PortraitFor(UnitArtViews.ClassicId));
            Assert.AreSame(portrait, unit.PortraitFor(UnitArtViews.SepiaId));
            Assert.AreEqual("set_classic", unit.AnimationSetIdFor(UnitArtViews.SepiaId));
            Assert.IsTrue(unit.UsesPortrait(gloomPortrait));
            Assert.IsTrue(unit.UsesPortrait(portrait));
        }

        [Test]
        public void ActiveView_ComesFromProvider_DefaultClassic()
        {
            Sprite portrait = NewSprite("classic_portrait");
            Sprite sepiaPortrait = NewSprite("sepia_portrait");
            UnitDefinitionData unit = Creature(portrait, null, string.Empty);
            unit.GetOrAddViewArt(UnitArtViews.SepiaId).SetPortrait(sepiaPortrait);

            UnitArtViews.ActiveViewProvider = null;
            Assert.AreEqual(UnitArtViews.ClassicId, UnitArtViews.ActiveViewId);
            Assert.AreSame(portrait, unit.ActivePortrait);

            UnitArtViews.ActiveViewProvider = () => UnitArtViews.SepiaId;
            Assert.AreSame(sepiaPortrait, unit.ActivePortrait);

            UnitArtViews.ActiveViewProvider = () => "unknown_view";
            Assert.AreSame(portrait, unit.ActivePortrait, "незнакомый вид — «Обычный»");
        }

        [Test]
        public void StaticFigure_HidesAnimationSets_KeepsLinks()
        {
            UnitDefinitionData unit = Creature(null, NewSprite("field"), "set_classic");
            unit.GetOrAddViewArt(UnitArtViews.GloomId).SetAnimationSetId("set_gloom");

            unit.SetStaticFigureOnly(true);
            Assert.AreEqual(string.Empty, unit.AnimationSetIdFor(UnitArtViews.ClassicId));
            Assert.AreEqual(string.Empty, unit.AnimationSetIdFor(UnitArtViews.GloomId));
            Assert.AreEqual("set_classic", unit.AnimationSetId, "ссылка на набор сохраняется");
            Assert.AreEqual("set_gloom", unit.FindViewArt(UnitArtViews.GloomId).AnimationSetId);

            unit.SetStaticFigureOnly(false);
            Assert.AreEqual("set_classic", unit.AnimationSetIdFor(UnitArtViews.ClassicId));
            Assert.AreEqual("set_gloom", unit.AnimationSetIdFor(UnitArtViews.GloomId));
        }

        [Test]
        public void Militia_UsesAssignedAnimationSet()
        {
            UnitDatabaseAsset database = Resources.Load<UnitDatabaseAsset>(UnitDatabaseAsset.ResourcesPath);
            UnitDefinitionData militia = database.FindById("militia");
            Assert.IsNotNull(militia);
            Assert.IsFalse(militia.StaticFigureOnly, "подключённые анимации ополченца включены");
            Assert.AreEqual("militia", militia.AnimationSetId, "набор ополченца остаётся привязанным");
            Assert.AreEqual("militia", militia.ActiveAnimationSetId);
            Assert.IsNotNull(militia.BattlefieldSprite, "запасная статичная фигура сохранена");
        }
    }
}
