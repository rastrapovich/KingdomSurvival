using System.Collections.Generic;
using System.IO;
using System.Linq;
using KingdomSurvival.ArtAssets.Editor;
using NUnit.Framework;

namespace KingdomSurvival.ArtAssets.Tests
{
    // ПР-12Н: разбор пакета файлов. Ракурс — по папке или имени файла
    // (английские токены экспорта и русские имена по таблице), нормаль — по
    // слову normal. Неоднозначное — на ручное назначение; нормаль без рисунка
    // не становится предметом.
    public sealed class ArtAssetImportParserTests
    {
        private string root;

        [SetUp]
        public void SetUp()
        {
            root = Path.Combine(Path.GetTempPath(), "ks_art_parser_" + System.Guid.NewGuid().ToString("N").Substring(0, 8)).Replace('\\', '/');
            Directory.CreateDirectory(root);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }

        private string Touch(string relative)
        {
            string path = Path.Combine(root, relative).Replace('\\', '/');
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, new byte[] { 0 });
            return path;
        }

        private static readonly string[] Folders = { "Front", "Front_Right", "Back_Right", "Back", "Back_Left", "Front_Left" };

        [Test]
        public void ObjectFolder_SixViewsAndSixNormals_GivesOneAssetWithPairs()
        {
            foreach (string folder in Folders)
            {
                Touch("Телега/" + folder + "/color.png");
                Touch("Телега/" + folder + "/normal.png");
            }
            ArtAssetImportPlan plan = ArtAssetImportParser.ParsePaths(new[] { root + "/Телега" });
            Assert.That(plan.Groups.Count, Is.EqualTo(1));
            ArtAssetImportGroup group = plan.Groups[0];
            Assert.That(group.Name, Is.EqualTo("Телега"));
            Assert.That(group.MainViewCount, Is.EqualTo(6));
            Assert.That(group.NormalCount, Is.EqualTo(6));
            foreach (ArtAssetImportSlot slot in group.Slots)
            {
                string folder = ArtAssetLabels.ViewFolder(slot.View);
                StringAssert.EndsWith("/" + folder + "/color.png", slot.ColorPath);
                StringAssert.EndsWith("/" + folder + "/normal.png", slot.NormalPath, "Нормаль — к рисунку своего ракурса.");
            }
            Assert.That(plan.Unresolved, Is.Empty);
        }

        [Test]
        public void ParentFolder_CreatesSeveralAssets_NormalsAreNotSeparateObjects()
        {
            foreach (string name in new[] { "Телега", "Бочка", "Забор" })
                foreach (string folder in Folders.Take(3))
                {
                    Touch("Пакет/" + name + "/" + folder + "/color.png");
                    Touch("Пакет/" + name + "/" + folder + "/normal.png");
                }
            ArtAssetImportPlan plan = ArtAssetImportParser.ParsePaths(new[] { root + "/Пакет" });
            Assert.That(plan.Groups.Select(group => group.Name), Is.EquivalentTo(new[] { "Телега", "Бочка", "Забор" }));
            Assert.That(plan.Groups.All(group => group.ColorCount == 3 && group.NormalCount == 3));
            Assert.That(plan.Groups.Any(group => group.Name.ToLowerInvariant().Contains("normal")), Is.False);
        }

        [Test]
        public void FlatPairs_AreRecognized_ByViewTokenInFileName()
        {
            Touch("export/cart_Front.png");
            Touch("export/cart_Front_normal.png");
            Touch("export/cart_Back_Left.png");
            Touch("export/cart_Back_Left_normal.png");
            Touch("export/backpack_Front_Right.png");
            ArtAssetImportPlan plan = ArtAssetImportParser.ParsePaths(Directory.GetFiles(root + "/export"));
            ArtAssetImportGroup cart = plan.Groups.Single(group => group.Name == "cart");
            Assert.That(cart.Slots.Select(slot => slot.View), Is.EquivalentTo(new[] { ArtAssetView.Front, ArtAssetView.BackLeft }));
            Assert.That(cart.Slots.All(slot => slot.ColorPath != null && slot.NormalPath != null));
            ArtAssetImportGroup backpack = plan.Groups.Single(group => group.Name == "backpack");
            Assert.That(backpack.Slots.Single().View, Is.EqualTo(ArtAssetView.FrontRight), "«backpack» не путается с «back».");
        }

        [Test]
        public void RussianViewNames_AreRecognizedByExplicitTable()
        {
            Touch("Дом/Спереди справа/цвет.png");
            Touch("Дом/Спереди справа/нормаль.png");
            Touch("Дом/Сзади_слева/цвет.png");
            ArtAssetImportPlan plan = ArtAssetImportParser.ParsePaths(new[] { root + "/Дом" });
            ArtAssetImportGroup house = plan.Groups.Single();
            Assert.That(house.Slots.Select(slot => slot.View), Is.EquivalentTo(new[] { ArtAssetView.FrontRight, ArtAssetView.BackLeft }));
            Assert.That(house.Slots.Single(slot => slot.View == ArtAssetView.FrontRight).NormalPath, Is.Not.Null);
        }

        [Test]
        public void LoneNormalWithoutContext_IsNotAnAsset()
        {
            string normal = Touch("normal.png");
            ArtAssetImportPlan plan = ArtAssetImportParser.ParsePaths(new[] { normal });
            Assert.That(plan.Groups, Is.Empty, "Внешний normal.png без контекста не становится отдельным предметом.");
            Assert.That(plan.Unresolved.Single().Path, Is.EqualTo(normal));

            string paired = Touch("Бочка/Front/normal.png");
            plan = ArtAssetImportParser.ParsePaths(new[] { root + "/Бочка" });
            Assert.That(plan.Groups, Is.Empty, "Нормаль без своего рисунка — не пара и не предмет.");
            StringAssert.Contains("нормаль без рисунка", plan.Unresolved.Single(item => item.Path == paired).Reason);
        }

        [Test]
        public void AmbiguousFiles_AreLeftForManualAssignment()
        {
            Touch("Ящик/Front/color.png");
            Touch("Ящик/Front/основа.png");
            Touch("Ящик/Back/color.png");
            Touch("Ящик/картинка.png");
            ArtAssetImportPlan plan = ArtAssetImportParser.ParsePaths(new[] { root + "/Ящик" });
            ArtAssetImportGroup crate = plan.Groups.Single();
            Assert.That(crate.Slots.Single(slot => slot.Part.Length == 0).View, Is.EqualTo(ArtAssetView.Back));
            Assert.That(plan.Unresolved.Any(item => item.Path.EndsWith("картинка.png") && item.Reason.Contains("ракурс")), Is.True,
                "Направление по картинке не угадывается.");
        }

        [Test]
        public void PartsInViewFolder_BecomeNamedParts()
        {
            foreach (string folder in Folders.Take(2))
            {
                Touch("Изба/" + folder + "/Основа.png");
                Touch("Изба/" + folder + "/Основа_normal.png");
                Touch("Изба/" + folder + "/Крыша.png");
                Touch("Изба/" + folder + "/Крыша_normal.png");
            }
            ArtAssetImportGroup house = ArtAssetImportParser.ParsePaths(new[] { root + "/Изба" }).Groups.Single();
            Assert.That(house.Parts, Is.EquivalentTo(new[] { "", "крыша" }));
            Assert.That(house.MainViewCount, Is.EqualTo(2));
            Assert.That(house.NormalCount, Is.EqualTo(4));
        }

        [Test]
        public void ForcedAssetName_PutsEveryFileIntoOneRecord()
        {
            Touch("x/barrel_Front.png");
            Touch("x/cask_Back.png");
            ArtAssetImportPlan plan = ArtAssetImportParser.ParsePaths(Directory.GetFiles(root + "/x"), "Бочка");
            Assert.That(plan.Groups.Single().Name, Is.EqualTo("Бочка"));
            Assert.That(plan.Groups.Single().ColorCount, Is.EqualTo(2));
        }

        // Перетащили просто PNG (без ракурса в имени) — новый ассет в ракурсе по
        // умолчанию, его пара *_normal — нормаль; нормаль без рисунка — нет.
        [Test]
        public void LoosePng_WithDefaultView_BecomesNewAsset_WithItsNormal()
        {
            string color = Touch("Загрузки/дом.png");
            string normal = Touch("Загрузки/дом_normal.png");
            string lone = Touch("Загрузки/normal.png");
            ArtAssetImportPlan plan = ArtAssetImportParser.ParsePaths(new[] { lone, color, normal }, null, ArtAssetView.Front);
            ArtAssetImportGroup house = plan.Groups.Single();
            Assert.That(house.Name, Is.EqualTo("дом"));
            ArtAssetImportSlot slot = house.Slots.Single();
            Assert.That(slot.View, Is.EqualTo(ArtAssetView.Front));
            Assert.That(slot.ViewAssumed, Is.True, "В сводке видно, что ракурс не из имени.");
            Assert.That(slot.ColorPath, Is.EqualTo(color));
            Assert.That(slot.NormalPath, Is.EqualTo(normal));
            Assert.That(plan.Unresolved.Single().Path, Is.EqualTo(lone));

            // Без ракурса по умолчанию — как раньше: на ручное назначение.
            Assert.That(ArtAssetImportParser.ParsePaths(new[] { color }).Groups, Is.Empty);

            // Рядом с ракурсами объекта файл без ракурса не становится отдельным ассетом.
            Touch("Телега/Front/color.png");
            string stray = Touch("Телега/превью.png");
            plan = ArtAssetImportParser.ParsePaths(new[] { root + "/Телега" }, null, ArtAssetView.Front);
            Assert.That(plan.Groups.Select(group => group.Name), Is.EquivalentTo(new[] { "Телега" }));
            Assert.That(plan.Unresolved.Any(item => item.Path == stray), Is.True);
        }

        // ПР-12П: номер в конце имени — кадр анимации ракурса (папка с кадрами
        // из Blender: Front/0000.png …), нормаль — к рисунку своего кадра.
        [Test]
        public void FrameSequence_InViewFolder_BecomesFramesOfOneSlot()
        {
            for (int i = 0; i < 4; i++)
            {
                Touch("Трава/Front/" + i.ToString("0000") + ".png");
                Touch("Трава/Front/" + i.ToString("0000") + "_normal.png");
            }
            Touch("Трава/Back/0001.png");
            Touch("Трава/Back/0002.png");
            ArtAssetImportPlan plan = ArtAssetImportParser.ParsePaths(new[] { root + "/Трава" });
            ArtAssetImportGroup grass = plan.Groups.Single();
            Assert.That(grass.Name, Is.EqualTo("Трава"));
            Assert.That(grass.Parts, Is.EquivalentTo(new[] { "" }), "Номер кадра не становится частью «0001».");
            ArtAssetImportSlot front = grass.Slots.Single(slot => slot.View == ArtAssetView.Front);
            StringAssert.EndsWith("/Front/0000.png", front.ColorPath, "Первый кадр — наименьший номер.");
            StringAssert.EndsWith("/Front/0000_normal.png", front.NormalPath);
            Assert.That(front.FrameCount, Is.EqualTo(4));
            Assert.That(front.Frames.Select(frame => frame.Number), Is.EqualTo(new[] { 1, 2, 3 }));
            for (int i = 0; i < 3; i++)
            {
                StringAssert.EndsWith("/Front/" + (i + 1).ToString("0000") + ".png", front.Frames[i].ColorPath);
                StringAssert.EndsWith("/Front/" + (i + 1).ToString("0000") + "_normal.png", front.Frames[i].NormalPath, "Нормаль — к своему кадру.");
            }
            Assert.That(grass.Slots.Single(slot => slot.View == ArtAssetView.Back).FrameCount, Is.EqualTo(2), "Нумерация может начинаться не с нуля.");
            Assert.That(grass.MaxFrameCount, Is.EqualTo(4));
            Assert.That(plan.Unresolved, Is.Empty);
        }

        [Test]
        public void FlatFrames_WithViewInName_AreFrames_LoneNumberStaysInName()
        {
            Touch("export/flag_Front_01.png");
            Touch("export/flag_Front_02.png");
            Touch("export/flag_Front_02_normal.png");
            Touch("export/flag_Front_03.png");
            Touch("export/rock_Front_01.png");
            ArtAssetImportPlan plan = ArtAssetImportParser.ParsePaths(Directory.GetFiles(root + "/export"));
            ArtAssetImportSlot flag = plan.Groups.Single(group => group.Name == "flag").Slots.Single();
            Assert.That(flag.FrameCount, Is.EqualTo(3));
            Assert.That(flag.NormalPath, Is.Null);
            StringAssert.EndsWith("flag_Front_02_normal.png", flag.Frames[0].NormalPath);
            Assert.That(plan.Groups.Select(group => group.Name), Is.EquivalentTo(new[] { "flag", "rock_01" }),
                "Одиночный номер — часть имени объекта, как раньше.");
            Assert.That(plan.Groups.Single(group => group.Name == "rock_01").Slots.Single().FrameCount, Is.EqualTo(1));
        }

        [Test]
        public void LooseNumberedFiles_StaySeparateObjects_UnlessIntoAssetOrMarked()
        {
            string rock1 = Touch("Загрузки/rock_01.png");
            string rock2 = Touch("Загрузки/rock_02.png");
            ArtAssetImportPlan plan = ArtAssetImportParser.ParsePaths(new[] { rock1, rock2 }, null, ArtAssetView.Front);
            Assert.That(plan.Groups.Select(group => group.Name), Is.EquivalentTo(new[] { "rock_01", "rock_02" }),
                "Варианты камня без ракурса не склеиваются в анимацию.");

            // В выбранную запись (перетаскивание в ячейку ракурса) — кадры.
            plan = ArtAssetImportParser.ParsePaths(new[] { rock1, rock2, Touch("Загрузки/rock_03.png") }, "Трава", ArtAssetView.FrontLeft);
            ArtAssetImportSlot slot = plan.Groups.Single().Slots.Single();
            Assert.That(slot.View, Is.EqualTo(ArtAssetView.FrontLeft));
            Assert.That(slot.FrameCount, Is.EqualTo(3));
            Assert.That(slot.ViewAssumed, Is.True);

            // Слово frame / кадр перед номером — кадры и в новом объекте.
            string a = Touch("Загрузки/костёр_кадр_1.png"), b = Touch("Загрузки/костёр_кадр_2.png");
            plan = ArtAssetImportParser.ParsePaths(new[] { a, b }, null, ArtAssetView.Front);
            ArtAssetImportGroup fire = plan.Groups.Single();
            Assert.That(fire.Name, Is.EqualTo("костёр"));
            Assert.That(fire.Slots.Single().FrameCount, Is.EqualTo(2));
        }

        [Test]
        public void DuplicateFrameNumbers_AreLeftForManualAssignment()
        {
            Touch("Флаг/Front/01.png");
            Touch("Флаг/Front/1.png");
            Touch("Флаг/Front/02.png");
            Touch("Флаг/Front/05_normal.png");
            ArtAssetImportPlan plan = ArtAssetImportParser.ParsePaths(new[] { root + "/Флаг" });
            Assert.That(plan.Unresolved.Count(item => item.Reason.Contains("кадр 1")), Is.EqualTo(2), "Два файла на один кадр — неоднозначно.");
            Assert.That(plan.Unresolved.Any(item => item.Path.EndsWith("05_normal.png") && item.Reason.Contains("нормаль без рисунка")), Is.True);
            Assert.That(plan.Groups.Single().Slots.Single().FrameCount, Is.EqualTo(1));
        }

        // Рендер KS Sprite Renderer, как в Базе анимаций: Объект/Действие/Ракурс/
        // Действие_Ракурс_0001.png и нормали «…_n.png» рядом — всё из одной папки.
        [Test]
        public void SpriteRendererFolder_ActionFolderAndFramesWithNormals()
        {
            foreach (string folder in new[] { "Front", "Back" })
                for (int i = 1; i <= 3; i++)
                {
                    Touch("Трава/Idle/" + folder + "/Idle_" + folder + "_000" + i + ".png");
                    Touch("Трава/Idle/" + folder + "/Idle_" + folder + "_000" + i + "_n.png");
                }
            ArtAssetImportPlan plan = ArtAssetImportParser.ParsePaths(new[] { root + "/Трава" });
            Assert.That(plan.Unresolved, Is.Empty, string.Join("\n", plan.Unresolved.Select(item => item.Path + " — " + item.Reason)));
            ArtAssetImportGroup group = plan.Groups.Single();
            Assert.That(group.Name, Is.EqualTo("Трава"), "Папка действия не становится объектом.");
            Assert.That(group.Parts, Is.EqualTo(new[] { "" }), "Действие не становится частью.");
            Assert.That(group.Slots.Count, Is.EqualTo(2));
            foreach (ArtAssetImportSlot slot in group.Slots)
            {
                Assert.That(slot.FrameCount, Is.EqualTo(3));
                Assert.That(slot.NormalPath, Does.EndWith("_0001_n.png"));
                Assert.That(slot.Frames.All(frame => frame.NormalPath != null), Is.True);
            }
            Assert.That(group.SourceFolder, Does.EndWith("/Трава"), "Папка источника — папка объекта.");
        }

        // Имя объекта в имени каждого кадра (LowGrass_Wind_Loop_6s/Front/
        // LowGrass_Wind_Loop_6s_0001.png) — это основа, а не часть с тем же именем.
        [Test]
        public void ObjectNameInFrameNames_IsTheMainPart()
        {
            foreach (string folder in new[] { "Front", "Back_Left" })
                for (int i = 1; i <= 3; i++)
                {
                    Touch("LowGrass_Wind_Loop_6s/" + folder + "/LowGrass_Wind_Loop_6s_000" + i + ".png");
                    Touch("LowGrass_Wind_Loop_6s/" + folder + "/LowGrass_Wind_Loop_6s_000" + i + "_n.png");
                }
            ArtAssetImportPlan plan = ArtAssetImportParser.ParsePaths(new[] { root + "/LowGrass_Wind_Loop_6s" });
            Assert.That(plan.Unresolved, Is.Empty, string.Join("\n", plan.Unresolved.Select(item => item.Path + " — " + item.Reason)));
            ArtAssetImportGroup group = plan.Groups.Single();
            Assert.That(group.Parts, Is.EqualTo(new[] { "" }), "Кадры — в основе.");
            Assert.That(group.MainViewCount, Is.EqualTo(2));
            Assert.That(group.Slots.All(slot => slot.FrameCount == 3 && slot.NormalPath != null), Is.True);
            // Своя часть по-прежнему распознаётся.
            Touch("Дом/Front/Крыша.png");
            Touch("Дом/Front/Дом.png");
            ArtAssetImportGroup house = ArtAssetImportParser.ParsePaths(new[] { root + "/Дом" }).Groups.Single();
            Assert.That(house.Parts, Is.EquivalentTo(new[] { "", "крыша" }));
        }

        // Плоские имена рендера с отметкой времени: из нескольких рендеров
        // одного ракурса берётся самый свежий; отметка — не часть имени.
        [Test]
        public void StampedRenders_NewestWins()
        {
            Touch("renders/grass__Idle_Front__20261001-165836-060.png");
            Touch("renders/grass__Idle_Front__20261009-142811-641.png");
            Touch("renders/grass__Idle_Front__20261009-142811-641_n.png");
            Touch("renders/grass__Idle_Back_Left__20261009-142811-641.png");
            ArtAssetImportPlan plan = ArtAssetImportParser.ParsePaths(new[] { root + "/renders" });
            Assert.That(plan.Unresolved, Is.Empty, string.Join("\n", plan.Unresolved.Select(item => item.Path + " — " + item.Reason)));
            ArtAssetImportGroup group = plan.Groups.Single();
            Assert.That(group.Name, Is.EqualTo("grass"));
            ArtAssetImportSlot front = group.Slots.Single(slot => slot.View == ArtAssetView.Front);
            Assert.That(front.ColorPath, Does.EndWith("20261009-142811-641.png"));
            Assert.That(front.NormalPath, Does.EndWith("641_n.png"));
            Assert.That(front.FrameCount, Is.EqualTo(1), "Отметка времени — не номер кадра.");
            Assert.That(group.SkippedOlder, Is.EqualTo(1));
            Assert.That(group.Slots.Any(slot => slot.View == ArtAssetView.BackLeft), Is.True);
            Assert.That(ArtAssetImportParser.Stamp("x__20261009-142811-641.png"), Is.EqualTo("202610091428110641"));
        }

        // Нормали папкой, как в Базе анимаций: имена любые, суффикс не нужен;
        // ракурс — по папке, порядок кадров — по номеру в конце имени.
        [Test]
        public void NormalsFolder_WithoutSuffix_ByViewAndFrameNumber()
        {
            Touch("Нормали/Front/Idle_Front_0002.png");
            Touch("Нормали/Front/Idle_Front_0001.png");
            Touch("Нормали/Back_Left/whatever.png");
            Touch("Нормали/loose.png");
            List<string> problems = new List<string>();
            Dictionary<(ArtAssetView view, string part), List<string>> slots =
                ArtAssetImportParser.ParseNormals(new[] { root + "/Нормали" }, "Трава", ArtAssetView.Back, problems);
            Assert.That(problems, Is.Empty, string.Join("\n", problems));
            Assert.That(slots[(ArtAssetView.Front, "")].Select(Path.GetFileName), Is.EqualTo(new[] { "Idle_Front_0001.png", "Idle_Front_0002.png" }));
            Assert.That(slots[(ArtAssetView.BackLeft, "")].Count, Is.EqualTo(1));
            Assert.That(slots[(ArtAssetView.Back, "")].Single(), Does.EndWith("loose.png"), "Без ракурса — в выбранный ракурс карточки.");
        }

        [Test]
        public void MissingViewFallback_IsNearestOnTheRing_NextInOrderFirst()
        {
            Assert.That(ArtAssetLabels.FallbackOrder(ArtAssetView.Back), Is.EqualTo(new[]
            {
                ArtAssetView.Back, ArtAssetView.BackLeft, ArtAssetView.BackRight, ArtAssetView.FrontLeft, ArtAssetView.FrontRight, ArtAssetView.Front
            }));
            Assert.That(ArtAssetLabels.FallbackOrder(ArtAssetView.Front).Distinct().Count(), Is.EqualTo(6));
        }
    }
}
