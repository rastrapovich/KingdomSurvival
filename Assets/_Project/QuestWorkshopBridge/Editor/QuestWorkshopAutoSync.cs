using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KingdomSurvival.DialogueDatabase;
using KingdomSurvival.Encounters;
using UnityEditor;
using UnityEngine;

namespace KingdomSurvival.QuestWorkshopBridge.Editor
{
    // Фоновая синхронизация с Мастерской квестов.
    //
    // Всегда: когда сохраняются база диалогов, база встреч или реестр флагов,
    // снимок для мастерской (unity-snapshot.json и картинки) обновляется сам.
    //
    // По переключателю «Автосинхронизация» (окно Мастерской квестов): когда
    // мастерская сохраняет workspace.json, безопасные изменения переносятся в
    // Unity без вопроса — новые сцены и сцены, которые в Unity с прошлой
    // синхронизации не правили. Конфликты и правки, сделанные в Unity, не
    // трогаются: их решает автор.
    [InitializeOnLoad]
    public static class QuestWorkshopAutoSync
    {
        private const string PrefKey = "KS.QuestWorkshop.AutoSync";
        private const double PollSeconds = 2.0;

        private static double nextPoll;
        private static DateTime lastSeenWrite;
        private static bool snapshotQueued;

        public static event Action<WorkshopImportResult> Synced;

        static QuestWorkshopAutoSync()
        {
            EditorApplication.update += Poll;
            if (File.Exists(QuestWorkshopPaths.Workspace))
                lastSeenWrite = File.GetLastWriteTimeUtc(QuestWorkshopPaths.Workspace);
        }

        public static bool Enabled
        {
            get => EditorPrefs.GetBool(PrefKey, false);
            set
            {
                EditorPrefs.SetBool(PrefKey, value);
                if (value)
                    lastSeenWrite = default; // сразу сверить
            }
        }

        public static void QueueSnapshot()
        {
            if (snapshotQueued || !Directory.Exists(QuestWorkshopPaths.Folder))
                return;
            snapshotQueued = true;
            EditorApplication.delayCall += () =>
            {
                snapshotQueued = false;
                try
                {
                    QuestWorkshopPaths.WriteSnapshot();
                }
                catch (Exception error)
                {
                    Debug.LogWarning("Мастерская квестов: снимок не обновлён: " + error.Message);
                }
            };
        }

        private static void Poll()
        {
            if (!Enabled || EditorApplication.timeSinceStartup < nextPoll)
                return;
            nextPoll = EditorApplication.timeSinceStartup + PollSeconds;
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
                return;
            string path = QuestWorkshopPaths.Workspace;
            if (!File.Exists(path))
                return;
            DateTime write = File.GetLastWriteTimeUtc(path);
            if (write == lastSeenWrite)
                return;
            lastSeenWrite = write;
            RunOnce(false);
        }

        // Переносит безопасные изменения. Возвращает итог (или null, если нечего).
        public static WorkshopImportResult RunOnce(bool log)
        {
            WorkshopProject project;
            try
            {
                project = WorkshopProject.Load(QuestWorkshopPaths.Workspace);
            }
            catch (Exception error)
            {
                if (log)
                    Debug.LogWarning("Мастерская квестов: workspace.json не прочитан: " + error.Message);
                return null;
            }

            DialogueDatabaseAsset dialogues = QuestWorkshopPaths.LoadDialogues();
            EncounterDatabaseAsset encounters = QuestWorkshopPaths.LoadEncounters();
            EncounterFlagRegistryAsset flags = QuestWorkshopPaths.LoadFlags();
            if (dialogues == null)
                return null;

            QuestWorkshopSyncState sync = QuestWorkshopSyncState.Load(QuestWorkshopPaths.SyncState);
            List<WorkshopScene> scenes = QuestWorkshopImporter.Analyze(project, dialogues, encounters, sync)
                .Where(r => r.AutoTransferable)
                .Select(r => r.Scene)
                .ToList();
            List<WorkshopSpeakerReport> speakers = QuestWorkshopImporter.AnalyzeSpeakers(project, dialogues, sync)
                .Where(r => r.AutoTransferable)
                .ToList();

            WorkshopImportResult result = new WorkshopImportResult();
            if (scenes.Count + speakers.Count > 0)
            {
                List<UnityEngine.Object> targets = new List<UnityEngine.Object> { dialogues };
                if (encounters != null)
                    targets.Add(encounters);
                if (flags != null)
                    targets.Add(flags);
                Undo.RecordObjects(targets.ToArray(), "Мастерская квестов: автосинхронизация");
                result = QuestWorkshopImporter.Apply(project, scenes, dialogues, encounters, flags, sync);
                QuestWorkshopImporter.ApplySpeakers(project, speakers, dialogues, result, sync);
                if (result.Changed)
                {
                    foreach (UnityEngine.Object target in targets)
                        EditorUtility.SetDirty(target);
                    AssetDatabase.SaveAssets();
                    QueueSnapshot();
                    Debug.Log("Мастерская квестов: автосинхронизация.\n" + result.Summary());
                }
            }
            sync.Save(QuestWorkshopPaths.SyncState);
            Synced?.Invoke(result);
            return result;
        }
    }

    // Сохранение баз → свежий снимок для мастерской.
    public sealed class QuestWorkshopSnapshotPostprocessor : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            foreach (string path in imported)
            {
                if (path == QuestWorkshopPaths.DialogueDatabasePath ||
                    path == QuestWorkshopPaths.EncounterDatabasePath ||
                    path == QuestWorkshopPaths.EncounterFlagsPath)
                {
                    QuestWorkshopAutoSync.QueueSnapshot();
                    return;
                }
            }
        }
    }
}
