using KingdomSurvival.UnitDatabase.Editor;
using NUnit.Framework;

namespace KingdomSurvival.UnitDatabase.Tests
{
    // Поставка рисунков: имя файла задаёт существо и вид рисунка (портрет / поле).
    public sealed class UnitArtDeliveryImporterTests
    {
        [Test]
        public void Parse_ReadsUnitAndKind()
        {
            UnitArtDeliveryImporter.DeliveryFile? field = UnitArtDeliveryImporter.Parse("x/blood_kleshchen__field__classic.png");
            Assert.IsNotNull(field);
            Assert.AreEqual("blood_kleshchen", field.Value.UnitId);
            Assert.AreEqual(UnitArtKind.Battlefield, field.Value.Kind);

            UnitArtDeliveryImporter.DeliveryFile? portrait = UnitArtDeliveryImporter.Parse("wolf__portrait__classic.png");
            Assert.IsNotNull(portrait);
            Assert.AreEqual(UnitArtKind.Portrait, portrait.Value.Kind);
        }

        [Test]
        public void Parse_RejectsForeignNames_AndFormerViewFiles()
        {
            Assert.IsNull(UnitArtDeliveryImporter.Parse("wolf__field__20261001-161136-340.png"), "управляемый файл, а не поставка");
            Assert.IsNull(UnitArtDeliveryImporter.Parse("wolf__icon__classic.png"));
            Assert.IsNull(UnitArtDeliveryImporter.Parse("wolf__field__neon.png"));
            Assert.IsNull(UnitArtDeliveryImporter.Parse("README_RU.md"));
        }
    }
}
