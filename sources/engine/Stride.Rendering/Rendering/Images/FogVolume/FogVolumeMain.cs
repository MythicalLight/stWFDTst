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



        /// <summary>
        /// Size of the orthographic projection used to find minimum bounding volume distance behind the camera
        /// </summary>
        private const float BackSideOrthographicSize = 0.0001f;

        private DynamicEffectInstance minmaxVolumeEffectShader; // from lightshafts
        private EffectBytecode previousMinmaxEffectBytecode;
        private MutablePipelineState[] minmaxPipelineStates = new MutablePipelineState[2];

        private RenderFogVolume[] singleBoundingVolume = new RenderFogVolume[1];


        private List<RenderFogVolume> fogVolumes; // TODO


        protected override void InitializeCore()
        {
            base.InitializeCore();


            minmaxVolumeEffectShader = new DynamicEffectInstance("VolumeMinMaxShaderFog");
            minmaxVolumeEffectShader.Initialize(Context.Services);


            // Additive blending shader
            applyFogShader = ToLoadAndUnload(new ImageEffectShader("FogVolumeFX"));
            applyFogShader.BlendState = new BlendStateDescription(Blend.One, Blend.One);



            blur = ToLoadAndUnload(new GaussianBlur());



        }



        protected override void Destroy()
        {
            base.Destroy();
            minmaxVolumeEffectShader.Dispose();
            
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


            // Create a min/max buffer generated from scene bounding volumes
            var targetBoundingBoxBufferSize = new Size2(Math.Max(1, depthInput.Width / BoundingVolumeBufferDownsampleLevel), Math.Max(1, depthInput.Height / BoundingVolumeBufferDownsampleLevel));
            var boundingBoxBuffer = NewScopedRenderTarget2D(targetBoundingBoxBufferSize.Width, targetBoundingBoxBufferSize.Height, PixelFormat.R32G32_Float);

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

                fogVolumeParams.Set(FogVolumeKeys.SampleCount, fVolume.SampleCount);



                // Generate list of bounding volume (either all or one by one depending on SeparateBoundingVolumes)
                var currentBoundingVolumes = (fVolume.SeparateBoundingVolumes) ? singleBoundingVolume : fVolume.BoundingVolumes;
                if (fVolume.SeparateBoundingVolumes)
                    singleBoundingVolume[0] = fVolume.BoundingVolumes[0];




                using (context.PushRenderTargetsAndRestore())
                {
                    

                    context.CommandList.Clear(backSideRaycastBuffer, new Color4(1.0f, 0.0f, 0.0f, 0.0f));
                    context.CommandList.SetRenderTargetAndViewport(null, backSideRaycastBuffer);

                    // If nothing visible, skip second part
                    if (!DrawBoundingVolumeMinMax(context, currentBoundingVolumes))
                        continue;

                    context.CommandList.Clear(backSideRaycastBuffer, new Color4(1.0f, 0.0f, 0.0f, 0.0f));
                    context.CommandList.SetRenderTargetAndViewport(null, backSideRaycastBuffer);

                    // If nothing visible, skip second part
                    DrawBoundingVolumeBackside(context, currentBoundingVolumes);

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


                

                fogVolumeEffectShader.SetInput(0, boundingBoxBuffer);
                fogVolumeEffectShader.SetInput(1, backSideRaycastBuffer);

                DrawFogVolume(context, fVolume);


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



        private void DrawFogVolume(RenderDrawContext context, RenderFogVolume fogVolume)
        {
            //fogVolumeEffectShader.Parameters.Set(FogVolumeFXKeys.DensityFactor, fogVolume.DensityValue);

            fogVolumeEffectShader.Draw(context, "Fog volume");
        }




        //This part is integrated from Lightshafts code (Bounding volume)
        private bool DrawBoundingVolumeMinMax(RenderDrawContext context, IReadOnlyList<RenderFogVolume> boundingVolumes)
        {
            return DrawBoundingVolumes(context, boundingVolumes, context.RenderContext.RenderView.ViewProjection);
        }

        private void DrawBoundingVolumeBackside(RenderDrawContext context, IReadOnlyList<RenderFogVolume> boundingVolumes)
        {
            float backSideMaximumDistance = context.RenderContext.RenderView.FarClipPlane;
            float backSideMinimumDistance = -context.RenderContext.RenderView.NearClipPlane;
            Matrix backSideProjection = context.RenderContext.RenderView.View * Matrix.Scaling(1, 1, -1) * Matrix.OrthoRH(BackSideOrthographicSize, BackSideOrthographicSize, backSideMinimumDistance, backSideMaximumDistance);
            DrawBoundingVolumes(context, boundingVolumes, backSideProjection);
        }

        private bool DrawBoundingVolumes(RenderDrawContext context, IReadOnlyList<RenderFogVolume> boundingVolumes, Matrix viewProjection)
        {
            var commandList = context.CommandList;

            bool effectUpdated = minmaxVolumeEffectShader.UpdateEffect(GraphicsDevice);
            if (minmaxVolumeEffectShader.Effect == null)
                return false;

            var needEffectUpdate = effectUpdated || previousMinmaxEffectBytecode != minmaxVolumeEffectShader.Effect.Bytecode;
            bool visibleMeshes = false;

            for (int pass = 0; pass < 2; ++pass)
            {
                var minmaxPipelineState = minmaxPipelineStates[pass];

                bool pipelineDirty = false;
                if (needEffectUpdate)
                {
                    // The EffectInstance might have been updated from outside
                    previousMinmaxEffectBytecode = minmaxVolumeEffectShader.Effect.Bytecode;

                    minmaxPipelineState.State.RootSignature = minmaxVolumeEffectShader.RootSignature;
                    minmaxPipelineState.State.EffectBytecode = minmaxVolumeEffectShader.Effect.Bytecode;

                    minmaxPipelineState.State.Output.RenderTargetCount = 1;
                    minmaxPipelineState.State.Output.RenderTargetFormat0 = commandList.RenderTarget.Format;
                    pipelineDirty = true;
                }

                MeshDraw currentDraw = null;
                var frustum = new BoundingFrustum(ref viewProjection);
                foreach (var volume in boundingVolumes)
                {
                    if (volume.Model == null)
                        continue;

                    // Update parameters for the minmax shader
                    Matrix worldViewProjection = Matrix.Multiply(volume.World, viewProjection);
                    minmaxVolumeEffectShader.Parameters.Set(VolumeMinMaxShaderKeys.WorldViewProjection, ref worldViewProjection);

                    foreach (var mesh in volume.Model.Meshes)
                    {
                        // Frustum culling
                        BoundingBox meshBoundingBox;
                        Matrix world = volume.World;
                        BoundingBox.Transform(ref mesh.BoundingBox, ref world, out meshBoundingBox);
                        var boundingBoxExt = new BoundingBoxExt(meshBoundingBox);
                        if (boundingBoxExt.Extent != Vector3.Zero
                            && !VisibilityGroup.FrustumContainsBox(ref frustum, ref boundingBoxExt, true))
                            continue;

                        visibleMeshes = true;

                        var draw = mesh.Draw;

                        if (currentDraw != draw)
                        {
                            if (minmaxPipelineState.State.PrimitiveType != draw.PrimitiveType)
                            {
                                minmaxPipelineState.State.PrimitiveType = draw.PrimitiveType;
                                pipelineDirty = true;
                            }

                            var inputElements = draw.VertexBuffers.CreateInputElements();
                            if (inputElements.ComputeHash() != minmaxPipelineState.State.InputElements.ComputeHash())
                            {
                                minmaxPipelineState.State.InputElements = inputElements;
                                pipelineDirty = true;
                            }

                            // Update mesh
                            for (int i = 0; i < draw.VertexBuffers.Length; i++)
                            {
                                var vertexBuffer = draw.VertexBuffers[i];
                                commandList.SetVertexBuffer(i, vertexBuffer.Buffer, vertexBuffer.Offset, vertexBuffer.Stride);
                            }
                            if (draw.IndexBuffer != null)
                                commandList.SetIndexBuffer(draw.IndexBuffer.Buffer, draw.IndexBuffer.Offset, draw.IndexBuffer.Is32Bit);
                            currentDraw = draw;
                        }

                        if (pipelineDirty)
                        {
                            minmaxPipelineState.Update();
                            pipelineDirty = false;
                        }

                        context.CommandList.SetPipelineState(minmaxPipelineState.CurrentState);

                        minmaxVolumeEffectShader.Apply(context.GraphicsContext);

                        // Draw
                        if (currentDraw.IndexBuffer == null)
                            commandList.Draw(currentDraw.DrawCount, currentDraw.StartLocation);
                        else
                            commandList.DrawIndexed(currentDraw.DrawCount, currentDraw.StartLocation);
                    }
                }
            }

            return visibleMeshes;
        }



    }
}
