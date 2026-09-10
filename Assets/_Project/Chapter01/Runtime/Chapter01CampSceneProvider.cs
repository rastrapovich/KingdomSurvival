using System;

namespace KingdomSurvival.Chapter01
{
    // Одна доступная сейчас авторская camp-сцена, либо null, если сцен нет
    // (раздел "Camp Scene Provider" производственной инструкции про лагерь:
    // "он отвечает только: какие авторские camp-scenes доступны сейчас?").
    public readonly struct CampSceneViewData
    {
        public readonly string DialogueId;
        public readonly string Title;

        public CampSceneViewData(string dialogueId, string title)
        {
            DialogueId = dialogueId;
            Title = title;
        }
    }

    // Read-only, как Chapter01JournalProvider — никакого процедурного
    // генератора лагерных событий и никакого собственного состояния кроме
    // NarrativeStateData (раздел "Что не строить сейчас": "не создавать
    // CampManager"). P09J1: единственная сцена v1 — «После телеги», доступна
    // ровно один раз, пока CartCampEchoSeen не выставлен D11C по завершении.
    public static class Chapter01CampSceneProvider
    {
        public static CampSceneViewData? GetAvailableScene(GameState gameState)
        {
            NarrativeStateData state = gameState?.Narrative;
            if (state == null)
                return null;

            if (state.HasFlag(Chapter01Ids.Flags.CartResolved) &&
                !state.HasFlag(Chapter01Ids.Flags.CartCampEchoSeen))
            {
                return new CampSceneViewData(Chapter01Ids.Dialogues.D11C, "После телеги");
            }

            return null;
        }
    }
}
