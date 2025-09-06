using System.Collections.Generic;
using System.Linq;
using Stride.Core.Mathematics;
using Stride.Games;
using Stride.Rendering;
using Stride.Rendering.Images;
using Stride.Rendering.Lights;

namespace Stride.Engine.Processors
{
    public class FogVolumeProcessor : EntityProcessor<FogVolumeComponent, FogVolumeProcessor.AssociatedData>, IEntityComponentRenderProcessor
    {
        private readonly List<RenderFogVolume> activeFogVolumes = new List<RenderFogVolume>();

        /// <inheritdoc/>
        public VisibilityGroup VisibilityGroup { get; set; }

        protected internal override void OnSystemAdd()
        {
            base.OnSystemAdd();

            VisibilityGroup.Tags.Set(FogVolumes.CurrentFogVolumes, activeFogVolumes);
        }

        protected internal override void OnSystemRemove()
        {
            VisibilityGroup.Tags.Set(FogVolumes.CurrentFogVolumes, null);

            base.OnSystemRemove();
        }

        /// <inheritdoc />
        protected override AssociatedData GenerateComponentData(Entity entity, FogVolumeComponent component)
        {
            return new AssociatedData
            {
                Component = component,
                LightComponent = entity.Get<LightComponent>(),
            };
        }

        /// <inheritdoc />
        protected override bool IsAssociatedDataValid(Entity entity, FogVolumeComponent component, AssociatedData associatedData)
        {
            return component == associatedData.Component &&
                   entity.Get<LightComponent>() == associatedData.LightComponent;
        }

        /// <inheritdoc />
        public override void Update(GameTime time)
        {
            activeFogVolumes.Clear();

            // Get processors
            var lightProcessor = EntityManager.GetProcessor<LightProcessor>();
            if (lightProcessor == null)
                return;

            var fogVolumeBoundingVolumeProcessor = EntityManager.GetProcessor<FogVolumeBoundingProcessor>();
            if (fogVolumeBoundingVolumeProcessor == null)
                return;

            foreach (var pair in ComponentDatas)
            {
                if (!pair.Key.Enabled)
                    continue;

                var fogVolume = pair.Value;
                if (fogVolume.LightComponent == null)
                    continue;

                var light = lightProcessor.GetRenderLight(fogVolume.LightComponent);
                if (light == null)
                    continue;

                var directLight = light.Type as IDirectLight;
                if (directLight == null)
                    continue;

                var boundingVolumes = fogVolumeBoundingVolumeProcessor.GetBoundingVolumesForComponent(fogVolume.Component);
                if (boundingVolumes == null)
                    continue;

                activeFogVolumes.Add(new RenderFogVolume
                {
                    Light = light,
                    Light2 = directLight,
                    SampleCount = fogVolume.Component.SampleCount,
                    DensityFactor = fogVolume.Component.DensityFactor,
                    BoundingVolumes = boundingVolumes,
                    SeparateBoundingVolumes = fogVolume.Component.SeparateBoundingVolumes,
                });
            }
        }

        public class AssociatedData
        {
            public FogVolumeComponent Component;
            public LightComponent LightComponent;
        }
    }
}
