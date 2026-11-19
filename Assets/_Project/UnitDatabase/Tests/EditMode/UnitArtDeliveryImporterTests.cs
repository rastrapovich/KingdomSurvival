using KingdomSurvival.UnitDatabase.Editor;
using NUnit.Framework;

namespace KingdomSurvival.UnitDatabase.Tests
{
    // Поставка рисунков по видам: имя файла задаёт существо, вид рисунка и вид карты.
    public sealed class UnitArtDeliveryImporterTests
    {
        [Test]
        public void Parse_ReadsUnitKindAndView()
        {
            UnitArtDeliveryImporter.DeliveryFile? field = UnitArtDeliveryImporter.Parse("x/gloom/blood_kleshchen__field__gloom.png");
            Assert.IsNotNull(field);
            Assert.AreEqual("blood_kleshchen", field.Value.UnitId);
            Assert.AreEqual(UnitArtKind.Battlefield, field.Value.Kind);
            Assert.AreEqual(UnitArtViews.GloomId, field.Value.ViewId);

            UnitArtDeliveryImporter.DeliveryFile? portrait = UnitArtDeliveryImporter.Parse("wolf__portrait__classic.png");
            Assert.IsNotNull(portrait);
            Assert.AreEqual(UnitArtKind.Portrait, portrait.Value.Kind);
            Assert.AreEqual(UnitArtViews.ClassicId, portrait.Value.ViewId);
        }

        [Test]
        public void Parse_RejectsForeignNames()
        {
            Assert.IsNull(UnitArtDeliveryImporter.Parse("wolf__field__20261001-161136-340.png"), "управляемый файл, а не поставка");
            Assert.IsNull(UnitArtDeliveryImporter.Parse("wolf__icon__classic.png"));
            Assert.IsNull(UnitArtDeliveryImporter.Parse("wolf__field__neon.png"));
            Assert.IsNull(UnitArtDeliveryImporter.Parse("README_RU.md"));
        }
    }
}
