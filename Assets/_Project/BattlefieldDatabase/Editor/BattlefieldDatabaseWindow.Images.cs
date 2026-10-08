using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace KingdomSurvival.BattlefieldDatabase.Editor
{
    // Перетаскивание картинок: спрайты и текстуры проекта, файлы из
    // проводника. Общая реализация — BattlefieldHexEditorKit.
    public sealed partial class BattlefieldDatabaseWindow
    {
        private void RegisterImageDrop(VisualElement target, string importFolder, bool multiple, Action<List<Sprite>> onDrop) =>
            BattlefieldHexEditorKit.RegisterImageDrop(this, target, importFolder, multiple, onDrop);
    }
}
