using UnityEngine;

namespace PsdUgui
{
    public static class GameObjectUtil
    {
        public static T GetOrAddComponent<T>(this GameObject go) where T : Component
        {
            var component = go.GetComponent<T>();
            return component != null ? component : go.AddComponent<T>();
        }
    }
}
