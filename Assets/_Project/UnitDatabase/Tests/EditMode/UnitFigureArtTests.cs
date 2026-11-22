using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace KingdomSurvival.UnitDatabase.Tests
{
    // Рисунки существа и статичная фигура вместо анимаций без потери ссылки на набор.
    public sealed class UnitFigureArtTests
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
        public void ActiveArt_IsTheCreatureFields()
        {
            Sprite portrait = NewSprite("portrait");
            Sprite field = NewSprite("field");
            UnitDefinitionData unit = Creature(portrait, field, "set_classic");

            Assert.AreSame(portrait, unit.ActivePortrait);
            Assert.AreSame(field, unit.ActiveBattlefieldSprite);
            Assert.AreEqual("set_classic", unit.ActiveAnimationSetId);
            Assert.IsTrue(unit.UsesPortrait(portrait));
            Assert.IsFalse(unit.UsesPortrait(field));
        }

        [Test]
        public void StaticFigure_HidesAnimationSet_KeepsLink()
        {
            UnitDefinitionData unit = Creature(null, NewSprite("field"), "set_classic");

            unit.SetStaticFigureOnly(true);
            Assert.AreEqual(string.Empty, unit.ActiveAnimationSetId);
            Assert.AreEqual("set_classic", unit.AnimationSetId, "ссылка на набор сохраняется");

            unit.SetStaticFigureOnly(false);
            Assert.AreEqual("set_classic", unit.ActiveAnimationSetId);
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
