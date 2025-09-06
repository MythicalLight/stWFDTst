using System;
using System.Collections.Generic;
using Stride.Core.Mathematics;
using Stride.Games;
using Stride.Rendering;
using Stride.Rendering.Images;

namespace Stride.Engine.Processors
{
    public class FogVolumeBoundingProcessor : EntityProcessor<FogVolumeBoundingComponent>
    {
        private Dictionary<FogVolumeComponent, List<RenderFogVolumeBoundingVolume>> volumesPerFogVolume = new Dictionary<FogVolumeComponent, List<RenderFogVolumeBoundingVolume>>();
        private bool isDirty;

        public override void Update(GameTime time)
        {
            RegenerateVolumesPerFogVolume();
        }

        public IReadOnlyList<RenderFogVolumeBoundingVolume> GetBoundingVolumesForComponent(FogVolumeComponent component)
        {
            if (!volumesPerFogVolume.TryGetValue(component, out var data))
                return null;
            return data;
        }

        protected override void OnEntityComponentAdding(Entity entity, FogVolumeBoundingComponent component, FogVolumeBoundingComponent data)
        {
            component.FogVolumeChanged += ComponentOnFogVolumeChanged;
            component.ModelChanged += ComponentOnModelChanged;
            component.EnabledChanged += ComponentOnEnabledChanged;
            isDirty = true;
        }

        protected override void OnEntityComponentRemoved(Entity entity, FogVolumeBoundingComponent component, FogVolumeBoundingComponent data)
        {
            component.FogVolumeChanged -= ComponentOnFogVolumeChanged;
            component.ModelChanged -= ComponentOnModelChanged;
            component.EnabledChanged -= ComponentOnEnabledChanged;
            isDirty = true;
        }

        private void ComponentOnEnabledChanged(object sender, EventArgs eventArgs)
        {
            isDirty = true;
        }

        private void ComponentOnModelChanged(object sender, EventArgs eventArgs)
        {
            isDirty = true;
        }

        private void ComponentOnFogVolumeChanged(object sender, EventArgs eventArgs)
        {
            isDirty = true;
        }

        private void RegenerateVolumesPerFogVolume()
        {
            // Clear
            if (isDirty)
            {
                volumesPerFogVolume.Clear();
            }
            // Keep existing collections
            else
            {
                foreach (var fogVolume in volumesPerFogVolume)
                {
                    fogVolume.Value.Clear();
                }
            }

            foreach (var pair in ComponentDatas)
            {
                if (!pair.Key.Enabled)
                    continue;

                var fogVolume = pair.Key.FogVolume;
                if (fogVolume == null)
                    continue;

                List<RenderFogVolumeBoundingVolume> data;
                if (!volumesPerFogVolume.TryGetValue(fogVolume, out data))
                    volumesPerFogVolume.Add(fogVolume, data = new List<RenderFogVolumeBoundingVolume>());

                data.Add(new RenderFogVolumeBoundingVolume
                {
                    World = pair.Key.Entity.Transform.WorldMatrix,
                    Model = pair.Key.Model,
                });
            }

            isDirty = false;
        }
    }
}
