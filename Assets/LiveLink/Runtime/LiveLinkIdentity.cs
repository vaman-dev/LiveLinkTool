
using UnityEngine;

namespace LiveLink
{
    [DisallowMultipleComponent]
    public sealed class LiveLinkIdentity : MonoBehaviour
    {
        [SerializeField, HideInInspector]
        private string objectId = "";

        [SerializeField, HideInInspector]
        private string authoringKey = "";

        public string ObjectId => objectId;

#if UNITY_EDITOR
        public string AuthoringKey => authoringKey;
#endif

#if LIVELINK_ENABLED && !UNITY_EDITOR
        private void Awake()
        {
            LiveLinkObjectRegistry.Register(this);
        }

        private void OnDestroy()
        {
            LiveLinkObjectRegistry.Unregister(this);
        }
#endif
    }
}
