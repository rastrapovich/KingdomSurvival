using UnityEngine;

namespace KingdomSurvival.MapLab
{
    // Индексы палитры в текстуре _MapLabPalette (32 × 1). Объекты хранят индекс,
    // а не цвет, поэтому смена палитры не перестраивает меши.
    public static class MapLabPaletteIndex
    {
        public const int GrassA = 0;
        public const int GrassB = 1;
        public const int GrassDry = 2;
        public const int GrassLush = 3;
        public const int ForestFloor = 4;
        public const int FieldStraw = 5;
        public const int FieldOchre = 6;
        public const int FieldGreen = 7;
        public const int FieldFallow = 8;
        public const int Road = 9;
        public const int RoadEdge = 10;
        public const int RockLight = 11;
        public const int RockShadow = 12;
        public const int WaterDeep = 13;
        public const int WaterShallow = 14;
        public const int WaterStroke = 15;
        public const int Shore = 16;
        public const int WoodWall = 17;
        public const int WoodDark = 18;
        public const int RoofThatch = 19;
        public const int RoofShingle = 20;
        public const int RoofDark = 21;
        public const int TreeMoss = 22;
        public const int TreeSmoky = 23;
        public const int Conifer = 24;
        public const int ForestShadow = 25;
        public const int Bush = 26;
        public const int Reed = 27;
        public const int ShadowTint = 28;
        public const int LightTint = 29;
        public const int Ink = 30;
        public const int Paper = 31;
        public const int Count = 32;
    }

    // Палитра карты. Значения по умолчанию подобраны по эталону №4 и
    // не являются каноном цвета: художник меняет их здесь.
    [CreateAssetMenu(menuName = "Kingdom Survival/MapLab/Палитра", fileName = "MapLabPalette")]
    public sealed class MapLabPalette : ScriptableObject
    {
        [Header("Трава")]
        public Color GrassA = Hex(0x9A9748);
        public Color GrassB = Hex(0xACA45B);
        public Color GrassDry = Hex(0xC4AD62);
        public Color GrassLush = Hex(0x737B3F);
        public Color ForestFloor = Hex(0x4F5634);

        [Header("Поля")]
        public Color FieldStraw = Hex(0xDDBC6A);
        public Color FieldOchre = Hex(0xCFA04E);
        public Color FieldGreen = Hex(0xA9A957);
        public Color FieldFallow = Hex(0xA48A55);

        [Header("Дороги и камень")]
        public Color Road = Hex(0xDCCDA5);
        public Color RoadEdge = Hex(0x8C7B58);
        public Color RockLight = Hex(0xAFA2A6);
        public Color RockShadow = Hex(0x7E7178);

        [Header("Вода и берег")]
        public Color WaterDeep = Hex(0x46586B);
        public Color WaterShallow = Hex(0x637789);
        public Color WaterStroke = Hex(0xB9C4CC);
        public Color Shore = Hex(0x7C7447);

        [Header("Постройки")]
        public Color WoodWall = Hex(0x8C6747);
        public Color WoodDark = Hex(0x5C4633);
        public Color RoofThatch = Hex(0xB09A68);
        public Color RoofShingle = Hex(0x806C59);
        public Color RoofDark = Hex(0x60574F);

        [Header("Лес")]
        public Color TreeMoss = Hex(0x6A7A45);
        public Color TreeSmoky = Hex(0x344F4E);
        public Color Conifer = Hex(0x324840);
        public Color ForestShadow = Hex(0x1C2A2E);
        public Color Bush = Hex(0x66803E);
        public Color Reed = Hex(0xA49A55);

        [Header("Свет, линия, бумага")]
        public Color ShadowTint = Hex(0x666C8F);
        public Color LightTint = Hex(0xFFF0D0);
        public Color Ink = Hex(0x2B2721);
        public Color Paper = Hex(0xE6DCC2);

        public Color Get(int index)
        {
            switch (index)
            {
                case MapLabPaletteIndex.GrassA: return GrassA;
                case MapLabPaletteIndex.GrassB: return GrassB;
                case MapLabPaletteIndex.GrassDry: return GrassDry;
                case MapLabPaletteIndex.GrassLush: return GrassLush;
                case MapLabPaletteIndex.ForestFloor: return ForestFloor;
                case MapLabPaletteIndex.FieldStraw: return FieldStraw;
                case MapLabPaletteIndex.FieldOchre: return FieldOchre;
                case MapLabPaletteIndex.FieldGreen: return FieldGreen;
                case MapLabPaletteIndex.FieldFallow: return FieldFallow;
                case MapLabPaletteIndex.Road: return Road;
                case MapLabPaletteIndex.RoadEdge: return RoadEdge;
                case MapLabPaletteIndex.RockLight: return RockLight;
                case MapLabPaletteIndex.RockShadow: return RockShadow;
                case MapLabPaletteIndex.WaterDeep: return WaterDeep;
                case MapLabPaletteIndex.WaterShallow: return WaterShallow;
                case MapLabPaletteIndex.WaterStroke: return WaterStroke;
                case MapLabPaletteIndex.Shore: return Shore;
                case MapLabPaletteIndex.WoodWall: return WoodWall;
                case MapLabPaletteIndex.WoodDark: return WoodDark;
                case MapLabPaletteIndex.RoofThatch: return RoofThatch;
                case MapLabPaletteIndex.RoofShingle: return RoofShingle;
                case MapLabPaletteIndex.RoofDark: return RoofDark;
                case MapLabPaletteIndex.TreeMoss: return TreeMoss;
                case MapLabPaletteIndex.TreeSmoky: return TreeSmoky;
                case MapLabPaletteIndex.Conifer: return Conifer;
                case MapLabPaletteIndex.ForestShadow: return ForestShadow;
                case MapLabPaletteIndex.Bush: return Bush;
                case MapLabPaletteIndex.Reed: return Reed;
                case MapLabPaletteIndex.ShadowTint: return ShadowTint;
                case MapLabPaletteIndex.LightTint: return LightTint;
                case MapLabPaletteIndex.Ink: return Ink;
                case MapLabPaletteIndex.Paper: return Paper;
                default: return Color.magenta;
            }
        }

        public static Color Hex(int rgb)
        {
            return new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);
        }

        // Палитра без ассета — чтобы сцена запускалась при пустом слоте.
        public static MapLabPalette CreateFallback()
        {
            MapLabPalette palette = CreateInstance<MapLabPalette>();
            palette.name = "MapLabPalette (fallback)";
            palette.hideFlags = HideFlags.DontSave;
            return palette;
        }
    }
}
