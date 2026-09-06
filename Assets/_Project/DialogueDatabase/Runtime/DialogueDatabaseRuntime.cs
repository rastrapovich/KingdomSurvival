using UnityEngine;

namespace KingdomSurvival.DialogueDatabase
{
    public static class DialogueDatabaseRuntime
    {
        private static DialogueDatabaseAsset cachedDatabase;

        public static DialogueDatabaseAsset LoadDefaultDatabase()
        {
            if (cachedDatabase == null)
                cachedDatabase = Resources.Load<DialogueDatabaseAsset>(DialogueDatabaseAsset.ResourcesPath);
            return cachedDatabase;
        }

        public static bool TryBuild(
            string dialogueId,
            out NarrativeDialogueDefinition definition,
            out string error)
        {
            DialogueDatabaseAsset database = LoadDefaultDatabase();
            if (database == null)
            {
                definition = null;
                error = "Не найден Resources/" + DialogueDatabaseAsset.ResourcesPath + ".asset";
                return false;
            }

            return database.TryBuildRuntime(dialogueId, out definition, out error);
        }
    }
}
