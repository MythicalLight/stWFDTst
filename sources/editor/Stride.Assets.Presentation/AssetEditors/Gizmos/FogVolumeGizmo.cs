using Stride.Core;
using Stride.Engine;
using Stride.Engine.Gizmos;
using Stride.Rendering;

namespace Stride.Assets.Presentation.AssetEditors.Gizmos
{
    [GizmoComponent(typeof(VolumeFogComponent), false)]
    public class FogVolumeGizmo : EntityGizmo<VolumeFogComponent>
    {
        private Model lastModel;
        private ModelComponent modelComponent;

        public FogVolumeGizmo(EntityComponent component) : base(component)
        {
            RenderGroup = FogVolumesGroup; // HMM
        }

        protected override Entity Create()
        {
            var entity = new Entity("Fog volume Gizmo Root Entity (id={0})".ToFormat(ContentEntity.Id));
            entity.Add(modelComponent = new ModelComponent { RenderGroup = RenderGroup });
            return entity;
        }

        public override void Update()
        {
            if (ContentEntity == null || GizmoRootEntity == null)
                return;

            if (lastModel != Component.Model)
            {
                UpdateModel(Component.Model);
                lastModel = Component.Model;
            }
            modelComponent.Enabled = Component.Enabled;
            GizmoRootEntity.Transform.UseTRS = false;
            GizmoRootEntity.Transform.LocalMatrix = Component.Entity.Transform.WorldMatrix;
            GizmoRootEntity.Transform.UpdateWorldMatrix();
        }

        void UpdateModel(Model model)
        {
            modelComponent.Model = model;
        }
    }
}