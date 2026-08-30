using UnityEngine;

public partial class PrototypeUIController
{
    // Unity 6 deprecates FindFirstObjectByType and ArmyLayout also imports System,
    // where unqualified Object becomes ambiguous. Keep the existing call sites
    // stable while routing them to the current Unity API.
    private static class Object
    {
        public static T FindFirstObjectByType<T>() where T : UnityEngine.Object
        {
            return UnityEngine.Object.FindAnyObjectByType<T>();
        }
    }
}
