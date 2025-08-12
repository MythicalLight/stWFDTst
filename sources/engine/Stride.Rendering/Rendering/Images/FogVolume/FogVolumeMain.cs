using System;
using System.Collections.Generic;
using System.Linq;
using Stride.Core;
using Stride.Core.Annotations;
using Stride.Core.Collections;
using Stride.Core.Extensions;
using Stride.Core.Mathematics;
using Stride.Graphics;
using Stride.Rendering.Images;
using Stride.Rendering.Lights;
using Stride.Rendering.Rendering.Images.FogVolume;
using Stride.Shaders;

namespace Stride.Rendering.Images
{
    [DataContract("FogVolumeMain")]
    public class FogVolumeMain : ImageEffect
    {



        /// <summary>
        /// Property key to access the current collection of <see cref="List{RenderFogVolume}"/> from <see cref="VisibilityGroup.Tags"/>.
        /// </summary>
        [DataMemberIgnore]
        public static readonly PropertyKey<List<RenderFogVolume>> CurrentFogVolumes = new PropertyKey<List<RenderFogVolume>>("FogVolumes.CurrentFogVolumes", typeof(FogVolumeMain)); //new PropertyKey<List<RenderFogVolume>>("FogVolumes.CurrentFogVolumes", typeof(FogVolumeMain));

        /// <summary>
        /// The number of times the resolution is lowered for the light buffer
        /// </summary>
        /// <userdoc>
        /// Lower values produce more precise volume buffer areas, but use more GPU
        /// </userdoc>
        [DataMemberRange(1, 64, 1, 1, 0)]
        public int LightBufferDownsampleLevel { get; set; } = 2;

        /// <summary>
        /// The amount of time the resolution is lowered for the bounding volume buffer
        /// </summary>
        /// <userdoc>
        /// Lower values produce sharper light shafts, but use more GPU
        /// </userdoc>
        [DataMemberRange(1, 64, 1, 1, 0)]
        public int BoundingVolumeBufferDownsampleLevel { get; set; } = 8;

        private GaussianBlur blur;

        private ImageEffectShader fogVolumeEffectShader;

        private ImageEffectShader applyFogShader;

        private List<RenderFogVolume> fogVolumes; // TODO


        protected override void InitializeCore()
        {
            base.InitializeCore();



            // Additive blending shader
            applyFogShader = ToLoadAndUnload(new ImageEffectShader("FogVolumeEffect"));
            applyFogShader.BlendState = new BlendStateDescription(Blend.One, Blend.One);



            blur = ToLoadAndUnload(new GaussianBlur());



        }



        protected override void Destroy()
        {
            base.Destroy();
            //minmaxVolumeEffectShader.Dispose();
        }

        public void Collect(RenderContext context)
        {
            fogVolumes = context.VisibilityGroup.Tags.Get(CurrentFogVolumes);
        }



        protected override void DrawCore(RenderDrawContext context)
        {

            if (fogVolumes == null)
                return; // Not collected

            if (LightBufferDownsampleLevel < 1)
                throw new ArgumentOutOfRangeException(nameof(LightBufferDownsampleLevel));
            if (BoundingVolumeBufferDownsampleLevel < 1)
                throw new ArgumentOutOfRangeException(nameof(BoundingVolumeBufferDownsampleLevel));




            var depthInput = GetSafeInput(0);



            // Buffer that holds the minimum distance in case of being inside the bounding box
            var backSideRaycastBuffer = NewScopedRenderTarget2D(1, 1, PixelFormat.R32G32_Float);



            var fogVolumeParams = fogVolumeEffectShader.Parameters;
            fogVolumeParams.Set(DepthBaseKeys.DepthStencil, depthInput); // Bind scene depth



            if (!Initialized)
                Initialize(context.RenderContext);

            var renderView = context.RenderContext.RenderView;
            var viewInverse = Matrix.Invert(renderView.View);
            fogVolumeParams.Set(TransformationKeys.ViewInverse, ref viewInverse); // NOTE! PLEASE CHECK HOW ARE PARAMS ATTACHED
            fogVolumeParams.Set(TransformationKeys.Eye, new Vector4(viewInverse.TranslationVector, 1));

            // Setup parameters for Z reconstruction
            fogVolumeParams.Set(CameraKeys.ZProjection, CameraKeys.ZProjectionACalculate(renderView.NearClipPlane, renderView.FarClipPlane));

            Matrix projectionInverse;
            Matrix.Invert(ref renderView.Projection, out projectionInverse);
            fogVolumeParams.Set(TransformationKeys.ProjectionInverse, projectionInverse);

            var vFogBufferUsed = false;

            foreach (var fVolume in fogVolumes)
            {

                fogVolumeParams.Set(LightShaftsEffectKeys.SampleCount, fVolume.SampleCount);




                using (context.PushRenderTargetsAndRestore())
                {
                    

                    context.CommandList.Clear(backSideRaycastBuffer, new Color4(1.0f, 0.0f, 0.0f, 0.0f));
                    context.CommandList.SetRenderTargetAndViewport(null, backSideRaycastBuffer);

                }


                if (!vFogBufferUsed)
                {
                    // First pass: replace (avoid a clear and blend state)
                    fogVolumeEffectShader.BlendState = BlendStates.Opaque;
                    vFogBufferUsed = true;
                }
                else
                {
                    // Then: add
                    var desc = BlendStates.Additive;
                    desc.RenderTarget0.ColorSourceBlend = Blend.One; // But without multiplying alpha
                    fogVolumeEffectShader.BlendState = desc;
                }


                if (fVolume.SampleCount < 1)
                    throw new ArgumentOutOfRangeException(nameof(fVolume.SampleCount));


                fogVolumeEffectShader.SetInput(10, backSideRaycastBuffer); // necessary? ###


            }

            // Everything was outside, skip
            //if (!vFogBufferUsed)
                //continue;


        }


        public void Draw(RenderDrawContext drawContext, Texture inputDepthStencil, Texture output)
        {
            SetInput(10, inputDepthStencil);
            SetOutput(output);
            Draw(drawContext);
        }

    }
}
