using UnityEngine;
using ARLocation.MapboxRoutes;

namespace ARLocation.MapboxRoutes.SampleProject
{
    /// <summary>No-op path renderer — AR guidance uses <see cref="ARGuidanceSystem"/> instead of LineRenderer.</summary>
    public class NullRoutePathRenderer : AbstractRouteRenderer
    {
        public override void Init(RoutePathRendererArgs args) { }
        public override void OnRouteUpdate(RoutePathRendererArgs args) { }
    }
}
