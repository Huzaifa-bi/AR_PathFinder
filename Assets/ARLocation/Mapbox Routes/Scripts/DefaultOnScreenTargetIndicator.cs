using UnityEngine;

namespace ARLocation.MapboxRoutes
{
    /// <summary>Legacy edge arrow to off-screen signposts — disabled; use ARNavigationUI banner instead.</summary>
    public class DefaultOnScreenTargetIndicator : AbstractOnScreenTargetIndicator
    {
        public enum TargetVisibilityState
        {
            None,
            Visible,
            OffUp,
            OffDown,
            OffLeft,
            OffRight
        }

        public enum ArrowDir
        {
            Left,
            Right
        }

        public Sprite ArrowSprite;
        public ArrowDir NeutralArrowDirection;
        public float Margin = 24;

        bool _initialized;

        public override void Init(MapboxRoute route)
        {
            if (_initialized) return;
            var existing = GameObject.Find("[OnScreenTargetIndicatorCanvas]");
            if (existing != null)
                Destroy(existing);
            _initialized = true;
        }

        public override void OnRouteUpdate(SignPostEventArgs args)
        {
            var existing = GameObject.Find("[OnScreenTargetIndicatorCanvas]");
            if (existing != null)
                existing.SetActive(false);
        }
    }
}
