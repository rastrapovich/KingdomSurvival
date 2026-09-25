using System;
using System.Collections.Generic;

namespace KingdomSurvival.DialogueDatabase
{
    public sealed class DialogueKnowledgeSource
    {
        public string KnowledgeId;
        public string Text;
        public string Source;
    }

    // ПР-11 (ProjectDocs/PR11_CHRONICLE_SPEC.md §3): текст сведения — та
    // реплика, которая его дала; источник — сцена и говорящий. Так карточка
    // сведения не пишет нового текста поверх уже утверждённых диалогов.
    public static class DialogueKnowledgeSources
    {
        public static Dictionary<string, DialogueKnowledgeSource> Build(DialogueDatabaseAsset database)
        {
            Dictionary<string, DialogueKnowledgeSource> sources = new Dictionary<string, DialogueKnowledgeSource>();
            if (database == null)
                return sources;

            foreach (DialogueDefinitionData dialogue in database.Dialogues)
            {
                if (dialogue == null)
                    continue;
                foreach (DialogueNodeData node in dialogue.Nodes)
                {
                    if (node == null)
                        continue;
                    foreach (DialogueTextBlockData block in node.TextBlocks)
                    {
                        if (block == null)
                            continue;
                        foreach (NarrativeEffect effect in block.OnRevealEffects)
                        {
                            if (effect == null || effect.Type != NarrativeEffectType.AddKnowledge ||
                                string.IsNullOrWhiteSpace(effect.StringParam) || sources.ContainsKey(effect.StringParam))
                                continue;

                            string speakerId = string.IsNullOrEmpty(block.SpeakerIdOverride) ? node.SpeakerId : block.SpeakerIdOverride;
                            DialogueSpeakerData speaker = database.FindSpeaker(speakerId);
                            string speakerName = speaker != null && speakerId != "narrator" && !string.IsNullOrEmpty(speaker.DisplayName)
                                ? " — " + speaker.DisplayName
                                : string.Empty;
                            sources[effect.StringParam] = new DialogueKnowledgeSource
                            {
                                KnowledgeId = effect.StringParam,
                                Text = block.Text,
                                Source = CleanTitle(dialogue.Title) + speakerName
                            };
                        }
                    }
                }
            }
            return sources;
        }

        // «N12 — Старый брод» → «Старый брод»: служебный код сцены игроку не нужен.
        public static string CleanTitle(string title)
        {
            if (string.IsNullOrEmpty(title))
                return string.Empty;
            int dash = title.IndexOf(" — ", StringComparison.Ordinal);
            return dash > 0 && dash < 12 ? title.Substring(dash + 3) : title;
        }
    }
}
