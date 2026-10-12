using KingdomSurvival.BattlefieldDatabase;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace KingdomSurvival.LocationRendering
{
    // Поле боя, собранное в Базе локаций: рамка кадра поля на холсте (как фон
    // поля в бою: вписан с обрезкой, увеличен и сдвинут), проверка места вида
    // «Поле боя» без входов и карты, живой кадр вместо картинки в поле.
    public sealed class LocationBattlefieldSceneTests
    {
        [Test]
        public void FrameOnPicture_MatchesCroppedScaledBackground()
        {
            Rect whole = BattlefieldFrame.FrameOnPicture(new Vector2(1920, 1080), 1, Vector2.zero);
            Assert.That(whole.position, Is.EqualTo(Vector2.zero));
            Assert.That(whole.size.x, Is.EqualTo(1920).Within(.01f));
            Assert.That(whole.size.y, Is.EqualTo(1080).Within(.01f));

            Rect tall = BattlefieldFrame.FrameOnPicture(new Vector2(1600, 1200), 1, Vector2.zero);
            Assert.That(tall.width, Is.EqualTo(1600).Within(.01f), "4:3 — по ширине целиком,");
            Assert.That(tall.height, Is.EqualTo(900).Within(.01f), "по высоте обрезано.");
            Assert.That(tall.y, Is.EqualTo(150).Within(.01f), "Обрезка поровну сверху и снизу.");

            Rect zoomed = BattlefieldFrame.FrameOnPicture(new Vector2(1920, 1080), 2, Vector2.zero);
            Assert.That(zoomed.center.x, Is.EqualTo(960).Within(.01f));
            Assert.That(zoomed.width, Is.EqualTo(960).Within(.01f), "Увеличение ×2 — кадр вдвое меньше на рисунке.");

            Rect moved = BattlefieldFrame.FrameOnPicture(new Vector2(1920, 1080), 1, new Vector2(.1f, 0));
            Assert.That(moved.x, Is.EqualTo(-192).Within(.01f), "Фон сдвинут вправо — кадр на рисунке левее.");
        }

        [Test]
        public void BattlefieldKind_NeedsNoMapOrEntrances()
        {
            BattlefieldDatabaseAsset fields = Resources.Load<BattlefieldDatabaseAsset>(BattlefieldDatabaseAsset.ResourcesPath);
            BattlefieldDefinitionData field = fields.GetSandboxBattlefield();
            LocalLocationDefinition scene = new LocalLocationDefinition
            {
                Id = "zz_field", DisplayName = "Поле", Kind = LocalLocationKind.Battlefield, BattlefieldId = field.Id
            };
            Assert.That(LocalLocationValidator.Validate(scene, fields), Is.Empty, "Полю боя не нужны место карты, входы и противники.");
            LocalLocationDefinition place = new LocalLocationDefinition { Id = "zz_place", DisplayName = "Место", BattlefieldId = field.Id };
            Assert.That(LocalLocationValidator.Validate(place, fields), Is.Not.Empty, "Месту исследования — по-прежнему нужны.");
            LocalLocationDefinition old = JsonUtility.FromJson<LocalLocationDefinition>("{\"Id\":\"old\"}");
            Assert.That(old.IsBattlefield, Is.False, "Старые записи — места.");
        }

        [Test]
        public void BattlefieldView_ShowsSceneTexture_InsteadOfPicture()
        {
            BattlefieldView view = new BattlefieldView(true);
            RenderTexture texture = new RenderTexture(64, 36, 0);
            try
            {
                view.SetSceneTexture(texture);
                Image background = view.Q<Image>("battlefield-background");
                Assert.That(view.HasSceneTexture, Is.True);
                Assert.That(background.image, Is.SameAs(texture));
                Assert.That(background.style.display.value, Is.EqualTo(DisplayStyle.Flex));
                view.SetSceneTexture(null);
                Assert.That(background.image, Is.Null, "Снова картинка поля.");
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }
    }
}
