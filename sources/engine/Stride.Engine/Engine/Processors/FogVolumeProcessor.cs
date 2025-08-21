using System.Collections.Generic;
using System.Linq;
using Stride.Core.Mathematics;
using Stride.Games;
using Stride.Rendering;
using Stride.Rendering.Images;
using Stride.Rendering.Lights;

namespace Stride.Engine.Processors
{
    public class FogVolumeProcessor : EntityProcessor<VolumeFogComponent, FogVolumeProcessor.AssociatedData>, IEntityComponentRenderProcessor
    {
        private readonly List<RenderFogVolume> activeFogVolumes = new List<RenderFogVolume>();

        private Dictionary<VolumeFogComponent, List<RenderFogVolume>> volumesPerFogVolume = new Dictionary<VolumeFogComponent, List<RenderFogVolume>>();
        private bool isDirty;

        /// <inheritdoc/>
        public VisibilityGroup VisibilityGroup { get; set; }

        protected internal override void OnSystemAdd()
        {
            base.OnSystemAdd();

            VisibilityGroup.Tags.Set(FogVolumeMain.CurrentFogVolumes, activeFogVolumes);
        }

        protected internal override void OnSystemRemove()
        {
            VisibilityGroup.Tags.Set(FogVolumeMain.CurrentFogVolumes, null);

            base.OnSystemRemove();
        }



        public IReadOnlyList<RenderFogVolume> GetBoundingVolumesForComponent(VolumeFogComponent component)
        {
            if (!volumesPerFogVolume.TryGetValue(component, out var data))
                return null;
            return data;
        }




        /// <inheritdoc />
        protected override AssociatedData GenerateComponentData(Entity entity, VolumeFogComponent component)
        {
            return new AssociatedData
            {
                Component = component,
                //LightComponent = entity.Get<LightComponent>(),
            };
        }

        /// <inheritdoc />
        //protected override bool IsAssociatedDataValid(Entity entity, VolumeFogComponent component, AssociatedData associatedData)
        //{
        //    return component == associatedData.Component;
        //}

        /// <inheritdoc />
        public override void Update(GameTime time)
        {
            activeFogVolumes.Clear();
            RegenVolumesPerFVolume();

            // Get processors
            //var lightProcessor = EntityManager.GetProcessor<LightProcessor>();
            //if (lightProcessor == null)
            //    return;

            

            foreach (var pair in ComponentDatas)
            {
                if (!pair.Key.Enabled)
                    continue;

                var fVolume = pair.Value;
                

                

                

                var boundingVolumes = GetBoundingVolumesForComponent(fVolume.Component);
                if (boundingVolumes == null)
                    continue;

                activeFogVolumes.Add(new RenderFogVolume
                {
                    
                    SampleCount = fVolume.Component.SampleCount,
                    DensityValue = fVolume.Component.DensityFactor,
                    BoundingVolumes = boundingVolumes,
                   
                });
            }
        }



        private void RegenVolumesPerFVolume()
        {
            // Clear
            if (isDirty)
            {
                volumesPerFogVolume.Clear();
            }
            // Keep existing collections
            else
            {
                foreach (var fVolume in volumesPerFogVolume)
                {
                    fVolume.Value.Clear();
                }
            }

            foreach (var pair in ComponentDatas)
            {
                if (!pair.Key.Enabled)
                    continue;

                var fVolume = pair.Key.FogVolume;
                if (fVolume == null)
                    continue;

                List<RenderFogVolume> data;
                if (!volumesPerFogVolume.TryGetValue(fVolume, out data))
                    volumesPerFogVolume.Add(fVolume, data = new List<RenderFogVolume>());

                data.Add(new RenderFogVolume
                {
                    World = pair.Key.Entity.Transform.WorldMatrix,
                    Model = pair.Key.Model,
                });
            }

            isDirty = false;
        }





        public class AssociatedData
        {
            public VolumeFogComponent Component;
            //public LightComponent LightComponent;
        }
    }
}

