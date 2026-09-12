using System;
using UnityEngine;

namespace KingdomSurvival.WorldMapVisual
{
    // Связывает LocationData.Id (строку) с иконкой. LocationData сам не хранит
    // Sprite — только Id, поэтому смена иконки не требует правки игровых данных.
    [Serializable]
    public sealed class WorldMapLocationIconEntry
    {
        [SerializeField] private string locationId = string.Empty;
        [SerializeField] private Sprite icon;

        public string LocationId => locationId;
        public Sprite Icon => icon;
    }
}
