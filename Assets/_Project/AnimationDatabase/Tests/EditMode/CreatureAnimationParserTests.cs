using System.Collections.Generic;
using System.Linq;
using KingdomSurvival.AnimationDatabase.Editor;
using NUnit.Framework;

namespace KingdomSurvival.AnimationDatabase.Tests
{
    public sealed class CreatureAnimationParserTests
    {
        private const string Root = "E:/Renders/Боец";

        private static string File(string action, string direction, string number)
        {
            return Root + "/" + action + "/" + direction + "/" + action + "_" + direction + "_" + number + ".png";
        }

        [TestCase("Idle_Front_0001", 1)]
        [TestCase("Idle_Front_0010", 10)]
        [TestCase("Idle_Front_-001", -1)]
        [TestCase("Idle_Front_-0003", -3)]
        [TestCase("Death_Front_Right_0098", 98)]
        [TestCase("Idle_Front-0004", 4)]
        [TestCase("-5", -5)]
        public void FrameNumber_IsTrailingInteger_WithSignOnlyAfterSeparator(string name, int expected)
        {
            Assert.IsTrue(CreatureAnimationImportParser.TryParseFrameNumber(name, out int number));
            Assert.AreEqual(expected, number);
        }

        [Test]
        public void FrameNumber_MissingNumber_IsNotParsed()
        {
            Assert.IsFalse(CreatureAnimationImportParser.TryParseFrameNumber("Idle_Front", out _));
            Assert.IsFalse(CreatureAnimationImportParser.TryParseFrameNumber("Idle_Front_0001 (copy)", out _));
        }

        [Test]
        public void Directions_AreCaseAndSeparatorInsensitive()
        {
            Assert.IsTrue(CreatureAnimationImportParser.TryMatchDirection("Front_Right", out CreatureAnimationDirection a));
            Assert.AreEqual(CreatureAnimationDirection.FrontRight, a);
            Assert.IsTrue(CreatureAnimationImportParser.TryMatchDirection("back_left", out CreatureAnimationDirection b));
            Assert.AreEqual(CreatureAnimationDirection.BackLeft, b);
            Assert.IsTrue(CreatureAnimationImportParser.TryMatchDirection("BACK", out CreatureAnimationDirection c));
            Assert.AreEqual(CreatureAnimationDirection.Back, c);
            Assert.IsFalse(CreatureAnimationImportParser.TryMatchDirection("Left", out _));
        }

        [Test]
        public void Actions_ExactSuggestedAndUnknown()
        {
            Assert.AreEqual(CreatureAnimationActionMatch.Exact, CreatureAnimationImportParser.MatchAction("idle", out CreatureAnimationAction idle, out _));
            Assert.AreEqual(CreatureAnimationAction.Idle, idle);
            Assert.AreEqual(CreatureAnimationActionMatch.Exact, CreatureAnimationImportParser.MatchAction("Ожидание", out _, out _));

            Assert.AreEqual(CreatureAnimationActionMatch.Suggested, CreatureAnimationImportParser.MatchAction("Run", out CreatureAnimationAction run, out _));
            Assert.AreEqual(CreatureAnimationAction.Walk, run);
            Assert.AreEqual(CreatureAnimationActionMatch.Suggested, CreatureAnimationImportParser.MatchAction("Бег", out CreatureAnimationAction beg, out _));
            Assert.AreEqual(CreatureAnimationAction.Walk, beg);

            Assert.AreEqual(CreatureAnimationActionMatch.Unknown, CreatureAnimationImportParser.MatchAction("Dance", out _, out _));
        }

        [Test]
        public void HitIsNeverTakenForAttack()
        {
            CreatureAnimationImportParser.MatchAction("Hit", out CreatureAnimationAction hit, out _);
            CreatureAnimationImportParser.MatchAction("GetHit", out CreatureAnimationAction getHit, out _);
            CreatureAnimationImportParser.MatchAction("Attack", out CreatureAnimationAction attack, out _);
            Assert.AreEqual(CreatureAnimationAction.Hit, hit);
            Assert.AreEqual(CreatureAnimationAction.Hit, getHit);
            Assert.AreEqual(CreatureAnimationAction.Attack, attack);
        }

        [Test]
        public void SpecialAttack_CarriesClipKey()
        {
            Assert.AreEqual(CreatureAnimationActionMatch.Exact, CreatureAnimationImportParser.MatchAction("SpecialAttack_Leap", out CreatureAnimationAction action, out string key));
            Assert.AreEqual(CreatureAnimationAction.SpecialAttack, action);
            Assert.AreEqual("Leap", key);
        }

        [Test]
        public void CreatureFolder_RecognizesActionsDirectionsAndOrder()
        {
            List<string> files = new List<string>();
            foreach (string direction in new[] { "Front", "Front_Right", "Back_Right", "Back", "Back_Left", "Front_Left" })
            {
                files.Add(File("Idle", direction, "0001"));
                files.Add(File("Idle", direction, "0004"));
                files.Add(File("Idle", direction, "0007"));
                files.Add(File("Hit", direction, "0001"));
                files.Add(File("Walk", direction, "0010"));
                files.Add(File("Walk", direction, "0002"));
                files.Add(File("Walk", direction, "-001"));
                files.Add(File("Death", direction, "0098"));
            }

            CreatureAnimationImportPackage package = CreatureAnimationImportParser.Analyze(Root, files);

            Assert.IsFalse(package.HasErrors, string.Join("\n", package.Issues));
            Assert.AreEqual(4, package.Groups.Count);
            Assert.IsTrue(package.Groups.All(group => group.Cells.Count == 6));
            CreatureAnimationImportGroup idle = package.Groups.Single(group => group.ChosenAction == CreatureAnimationAction.Idle);
            CollectionAssert.AreEqual(new[] { 1, 4, 7 }, idle.Cells[CreatureAnimationDirection.Front].Numbers.ToArray(),
                "0001, 0004, 0007 — три кадра, а не семь.");
            CreatureAnimationImportGroup walk = package.Groups.Single(group => group.ChosenAction == CreatureAnimationAction.Walk);
            CollectionAssert.AreEqual(new[] { -1, 2, 10 }, walk.Cells[CreatureAnimationDirection.BackLeft].Numbers.ToArray(),
                "Числовая сортировка с отрицательным стартом.");
            Assert.AreEqual(CreatureAnimationAction.Hit,
                package.Groups.Single(group => group.RawName == "Hit").ChosenAction);
        }

        [Test]
        public void StepExport_KeepsLastFrame_WithoutGaps()
        {
            List<int> numbers = CreatureAnimationTestFrames.ExportNumbers(1, 20, 3);
            CollectionAssert.AreEqual(new[] { 1, 4, 7, 10, 13, 16, 19, 20 }, numbers);

            List<string> files = numbers.AsEnumerable().Reverse()
                .Select(number => File("Attack", "Front", number.ToString("0000")))
                .ToList();
            CreatureAnimationImportPackage package = CreatureAnimationImportParser.Analyze(Root, files);
            Assert.IsFalse(package.HasErrors);
            CollectionAssert.AreEqual(numbers, package.Groups[0].Cells[CreatureAnimationDirection.Front].Numbers.ToArray());
        }

        [Test]
        public void SingleActionFolder_UsesFolderNameAsAction()
        {
            string root = "E:/Renders/Боец/Attack";
            List<string> files = new List<string>
            {
                root + "/Front/Attack_Front_0001.png",
                root + "/Back_Left/Attack_Back_Left_0001.png"
            };
            CreatureAnimationImportPackage package = CreatureAnimationImportParser.Analyze(root, files);
            Assert.IsFalse(package.HasErrors, string.Join("\n", package.Issues));
            Assert.AreEqual(CreatureAnimationAction.Attack, package.Groups.Single().ChosenAction);
            Assert.AreEqual(2, package.Groups.Single().Cells.Count);
        }

        [Test]
        public void SuggestedName_IsProposedWithWarning_UnknownIsSkippedByDefault()
        {
            List<string> files = new List<string>
            {
                File("Run", "Front", "0001"),
                File("Dance", "Front", "0001")
            };
            CreatureAnimationImportPackage package = CreatureAnimationImportParser.Analyze(Root, files);
            CreatureAnimationImportGroup run = package.Groups.Single(group => group.RawName == "Run");
            CreatureAnimationImportGroup dance = package.Groups.Single(group => group.RawName == "Dance");
            Assert.AreEqual(CreatureAnimationActionMatch.Suggested, run.Match);
            Assert.AreEqual(CreatureAnimationAction.Walk, run.ChosenAction);
            Assert.IsNull(dance.ChosenAction, "Неизвестное действие не создаётся молча.");
            Assert.AreEqual(2, package.Issues.Count(issue => issue.Severity == CreatureAnimationImportSeverity.Warning));
        }

        [Test]
        public void DuplicateNumbers_AreAnError()
        {
            List<string> files = new List<string>
            {
                Root + "/Idle/Front/Idle_Front_0001.png",
                Root + "/Idle/Front/Idle_Front_1.png"
            };
            CreatureAnimationImportPackage package = CreatureAnimationImportParser.Analyze(Root, files);
            Assert.IsTrue(package.HasErrors);
        }

        [Test]
        public void FileNameContradictingFolder_IsAnError()
        {
            List<string> files = new List<string> { Root + "/Attack/Front/Hit_Front_0001.png" };
            CreatureAnimationImportPackage package = CreatureAnimationImportParser.Analyze(Root, files);
            Assert.IsTrue(package.HasErrors, "Hit в папке Attack — ошибка, а не молчаливая подмена.");
        }

        [Test]
        public void TwoFoldersIntoOneCell_IsAnError()
        {
            List<string> files = new List<string>
            {
                File("Walk", "Front", "0001"),
                File("Run", "Front", "0001")
            };
            CreatureAnimationImportPackage package = CreatureAnimationImportParser.Analyze(Root, files);
            Assert.IsTrue(package.Issues.Any(issue => issue.Message.StartsWith("Две папки")));
            package.Groups.Single(group => group.RawName == "Run").ChosenAction = null;
            CreatureAnimationImportParser.ValidateDuplicateTargets(package);
            Assert.IsFalse(package.HasErrors);
        }

        [Test]
        public void Sequence_IntoChosenCell_IgnoresNames()
        {
            CreatureAnimationImportPackage package = CreatureAnimationImportParser.AnalyzeSequence(
                new[] { "C:/x/frame_10.png", "C:/x/frame_2.png", "C:/x/frame_1.png" },
                CreatureAnimationAction.Attack,
                CreatureAnimationDirection.BackLeft);
            Assert.IsFalse(package.HasErrors);
            CollectionAssert.AreEqual(new[] { 1, 2, 10 }, package.Groups[0].Cells[CreatureAnimationDirection.BackLeft].Numbers.ToArray());
        }
    }
}
