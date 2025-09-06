using System;
using System.Collections.Generic;
using System.Linq;
using Stride.Core;
using Stride.Core.Annotations;
using Stride.Core.Collections;
using Stride.Core.Extensions;
using Stride.Core.Mathematics;
using Stride.Graphics;
using Stride.Rendering.Lights;
using Stride.Rendering.Images;
using Stride.Rendering.Shadows;
using Stride.Shaders;

namespace Stride.Rendering.Images
{

    [DataContract("FogVolumes")]
    public class FogVolumes : ImageEffect
    {
        /// <summary>
        /// Property key to access the current collection of <see cref="List{RenderFogVolume}"/> from <see cref="VisibilityGroup.Tags"/>.
        /// </summary>
        [DataMemberIgnore]
        public static readonly PropertyKey<List<RenderFogVolume>> CurrentFogVolumes = new PropertyKey<List<RenderFogVolume>>("FogVolumes.CurrentFogVolumes", typeof(FogVolumes));

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

        /// <summary>
        /// Size of the orthographic projection used to find minimum bounding volume distance behind the camera
        /// </summary>
        private const float BackSideOrthographicSize = 0.0001f;

        private ImageEffectShader fogVolumeEffectShader;

        private ImageEffectShader applyFogLightEffectShader;
        private DynamicEffectInstance fogminmaxVolumeEffectShader;
        private GaussianBlur blur;

        private IShadowMapRenderer shadowMapRenderer;
        private List<RenderFogVolume> fogVolumes;

        private MutablePipelineState[] minmaxPipelineStates = new MutablePipelineState[2];
        private EffectBytecode previousMinmaxEffectBytecode;

        private RenderFogVolumeBoundingVolume[] singleBoundingVolume = new RenderFogVolumeBoundingVolume[1];

        // This could be used at some point when we have colored shadows
        private bool needsColorLightBuffer = true;

        private int usageCounter = 0;

        private Dictionary<IDirectLight, FogVolumeRenderData> renderData = new Dictionary<IDirectLight, FogVolumeRenderData>();
        private List<IDirectLight> unusedLights = new List<IDirectLight>();

        protected override void InitializeCore()
        {
            base.InitializeCore();

            // Light accumulation shader
            fogVolumeEffectShader = ToLoadAndUnload(new ImageEffectShader("FogVolumesFX"));

            // Additive blending shader
            applyFogLightEffectShader = ToLoadAndUnload(new ImageEffectShader("FogAdditiveFX"));
            applyFogLightEffectShader.BlendState = new BlendStateDescription(Blend.One, Blend.One);

            fogminmaxVolumeEffectShader = new DynamicEffectInstance("FogVolumeMinMaxShader");
            fogminmaxVolumeEffectShader.Initialize(Context.Services);

            blur = ToLoadAndUnload(new GaussianBlur());

            // Need the shadow map renderer in order to render light shafts
            var meshRenderFeature = Context.RenderSystem.RenderFeatures.OfType<MeshRenderFeature>().FirstOrDefault();
            if (meshRenderFeature == null)
                throw new InvalidOperationException("Missing mesh render feature");

            var forwardLightingFeature = meshRenderFeature.RenderFeatures.OfType<ForwardLightingRenderFeature>().FirstOrDefault();
            if (forwardLightingFeature == null)
                throw new InvalidOperationException("Missing forward lighting render feature");

            shadowMapRenderer = forwardLightingFeature.ShadowMapRenderer;

            for (int i = 0; i < 2; ++i)
            {
                var minmaxPipelineState = new MutablePipelineState(Context.GraphicsDevice);
                minmaxPipelineState.State.SetDefaults();

                minmaxPipelineState.State.BlendState.RenderTarget0.BlendEnable = true;
                minmaxPipelineState.State.BlendState.RenderTarget0.ColorSourceBlend = Blend.One;
                minmaxPipelineState.State.BlendState.RenderTarget0.ColorDestinationBlend = Blend.One;
                minmaxPipelineState.State.BlendState.RenderTarget0.ColorBlendFunction = i == 0 ? BlendFunction.Min : BlendFunction.Max;
                minmaxPipelineState.State.BlendState.RenderTarget0.ColorWriteChannels = i == 0 ? ColorWriteChannels.Red : ColorWriteChannels.Green;

                minmaxPipelineState.State.DepthStencilState.DepthBufferEnable = false;
                minmaxPipelineState.State.DepthStencilState.DepthBufferWriteEnable = false;
                minmaxPipelineState.State.RasterizerState.DepthClipEnable = true;
                minmaxPipelineState.State.RasterizerState.CullMode = i == 0 ? CullMode.Back : CullMode.Front;

                minmaxPipelineStates[i] = minmaxPipelineState;
            }
        }

        protected override void Destroy()
        {
            base.Destroy();
            fogminmaxVolumeEffectShader.Dispose();
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
            var boundingBoxBufferFog = NewScopedRenderTarget2D(targetBoundingBoxBufferSize.Width, targetBoundingBoxBufferSize.Height, PixelFormat.R32G32_Float);

            // Buffer that holds the minimum distance in case of being inside the bounding box
            var backSideRaycastBufferFog = NewScopedRenderTarget2D(1, 1, PixelFormat.R32G32_Float);

            // Create a single channel light buffer
            PixelFormat lightBufferPixelFormat = needsColorLightBuffer ? PixelFormat.R16G16B16A16_Float : PixelFormat.R16_Float;
            var targetLightBufferSize = new Size2(Math.Max(1, depthInput.Width / LightBufferDownsampleLevel), Math.Max(1, depthInput.Height / LightBufferDownsampleLevel));
            var lightBufferFog = NewScopedRenderTarget2D(targetLightBufferSize.Width, targetLightBufferSize.Height, lightBufferPixelFormat);
            fogVolumeEffectShader.SetOutput(lightBufferFog);
            var fogVolumesParameters = fogVolumeEffectShader.Parameters;
            fogVolumesParameters.Set(DepthBaseKeys.DepthStencil, depthInput); // Bind scene depth

            if (!Initialized)
                Initialize(context.RenderContext);

            var renderView = context.RenderContext.RenderView;
            var viewInverse = Matrix.Invert(renderView.View);
            fogVolumesParameters.Set(TransformationKeys.ViewInverse, ref viewInverse);
            fogVolumesParameters.Set(TransformationKeys.Eye, new Vector4(viewInverse.TranslationVector, 1));

            // Setup parameters for Z reconstruction
            fogVolumesParameters.Set(CameraKeys.ZProjection, CameraKeys.ZProjectionACalculate(renderView.NearClipPlane, renderView.FarClipPlane));

            Matrix projectionInverse;
            Matrix.Invert(ref renderView.Projection, out projectionInverse);
            fogVolumesParameters.Set(TransformationKeys.ProjectionInverse, projectionInverse);

            applyFogLightEffectShader.SetOutput(GetSafeOutput(0));

            foreach (var fogVolume in fogVolumes)
            {
                if (fogVolume.Light == null)
                    continue; // Skip entities without a light component

                // Set sample count for this light
                fogVolumesParameters.Set(FogVolumesFXKeys.SampleCount, fogVolume.SampleCount);

                // Setup the shader group used for sampling shadows
                var shadowMapTexture = shadowMapRenderer.FindShadowMap(renderView.LightingView ?? renderView, fogVolume.Light);
                SetupLight(context, fogVolume, shadowMapTexture, fogVolumesParameters);

                // Check if we can pack bounding volumes together or need to draw them one by one
                var boundingVolumeLoop = fogVolume.SeparateBoundingVolumes ? fogVolume.BoundingVolumes.Count : 1;
                var lightBufferUsedFog = false;
                for (int i = 0; i < boundingVolumeLoop; ++i)
                {
                    // Generate list of bounding volume (either all or one by one depending on SeparateBoundingVolumes)
                    var currentBoundingVolumes = (fogVolume.SeparateBoundingVolumes) ? singleBoundingVolume : fogVolume.BoundingVolumes;
                    if (fogVolume.SeparateBoundingVolumes)
                        singleBoundingVolume[0] = fogVolume.BoundingVolumes[i];

                    using (context.PushRenderTargetsAndRestore())
                    {
                        // Clear bounding box buffer
                        context.CommandList.Clear(boundingBoxBufferFog, new Color4(1.0f, 0.0f, 0.0f, 0.0f));
                        context.CommandList.SetRenderTargetAndViewport(null, boundingBoxBufferFog);

                        // If nothing visible, skip second part
                        if (!DrawBoundingVolumeMinMax(context, currentBoundingVolumes))
                            continue;

                        context.CommandList.Clear(backSideRaycastBufferFog, new Color4(1.0f, 0.0f, 0.0f, 0.0f));
                        context.CommandList.SetRenderTargetAndViewport(null, backSideRaycastBufferFog);

                        // If nothing visible, skip second part
                        DrawBoundingVolumeBackside(context, currentBoundingVolumes);
                    }

                    if (!lightBufferUsedFog)
                    {
                        // First pass: replace (avoid a clear and blend state)
                        fogVolumeEffectShader.BlendState = BlendStates.Opaque;
                        lightBufferUsedFog = true;
                    }
                    else
                    {
                        // Then: add
                        var desc = BlendStates.Additive;
                        desc.RenderTarget0.ColorSourceBlend = Blend.One; // But without multiplying alpha
                        fogVolumeEffectShader.BlendState = desc;
                    }

                    if (fogVolume.SampleCount < 1)
                        throw new ArgumentOutOfRangeException(nameof(fogVolume.SampleCount));

                    // Set min/max input
                    fogVolumeEffectShader.SetInput(0, boundingBoxBufferFog);
                    fogVolumeEffectShader.SetInput(1, backSideRaycastBufferFog);

                    // Light accumulation pass (on low resolution buffer)
                    DrawFogVolume(context, fogVolume);
                }

                // Everything was outside, skip
                if (!lightBufferUsedFog)
                    continue;

                if (LightBufferDownsampleLevel != 1)
                {
                    // Blur the result
                    blur.Radius = LightBufferDownsampleLevel;
                    blur.SetInput(lightBufferFog);
                    blur.SetOutput(lightBufferFog);
                    blur.Draw(context);
                }

                // Additive blend pass
                Color3 lightColor = fogVolume.Light2.ComputeColor(context.GraphicsDevice.ColorSpace, fogVolume.Light.Intensity);
                applyFogLightEffectShader.Parameters.Set(FogAdditiveShaderKeys.LightColor, ref lightColor);
                applyFogLightEffectShader.Parameters.Set(FogAdditiveFXKeys.Color, needsColorLightBuffer);
                applyFogLightEffectShader.SetInput(lightBufferFog);
                applyFogLightEffectShader.Draw(context);
            }

            // Clean up unused render data
            unusedLights.Clear();
            foreach (var data in renderData)
            {
                if (data.Value.UsageCounter != usageCounter)
                    unusedLights.Add(data.Key);
            }
            foreach (var unusedLight in unusedLights)
            {
                renderData.Remove(unusedLight);
            }
            usageCounter++;
        }

        public void Draw(RenderDrawContext drawContext, Texture inputDepthStencil, Texture output)
        {
            SetInput(0, inputDepthStencil);
            SetOutput(output);
            Draw(drawContext);
        }

        private void UpdateRenderData(RenderDrawContext context, FogVolumeRenderData data, RenderFogVolume fogVolume, LightShadowMapTexture shadowMapTexture)
        {
            if (fogVolume.Light2 is LightPoint)
            {
                data.GroupRenderer = new LightPointGroupRenderer();
            }
            else if (fogVolume.Light2 is LightSpot)
            {
                data.GroupRenderer = new LightSpotGroupRenderer();
            }
            else if (fogVolume.Light2 is LightDirectional)
            {
                data.GroupRenderer = new LightDirectionalGroupRenderer();
            }
            else
            {
                throw new InvalidOperationException("Unsupported light type");
            }

            ILightShadowMapShaderGroupData shadowGroup = null;
            if (shadowMapTexture != null)
            {
                data.ShadowType = shadowMapTexture.ShadowType;
                data.ShadowMapRenderer = shadowMapTexture.Renderer;
                shadowGroup = data.ShadowMapRenderer.CreateShaderGroupData(data.ShadowType);
            }
            else
            {
                data.ShadowType = 0;
                data.ShadowMapRenderer = null;
            }
            data.ShaderGroup = data.GroupRenderer.CreateLightShaderGroup(context, shadowGroup);   // TODO: Implement support for texture projection and attenuation?
        }

        private void SetupLight(RenderDrawContext context, RenderFogVolume fogVolume, LightShadowMapTexture shadowMapTexture, ParameterCollection lightParameterCollection)
        {
            BoundingBoxExt box = new BoundingBoxExt(new Vector3(-float.MaxValue), new Vector3(float.MaxValue)); // TODO

            FogVolumeRenderData data;
            if (!renderData.TryGetValue(fogVolume.Light2, out data))
            {
                data = new FogVolumeRenderData();
                renderData.Add(fogVolume.Light2, data);
                UpdateRenderData(context, data, fogVolume, shadowMapTexture);
            }

            if (shadowMapTexture != null && data.ShadowMapRenderer != null)
            {
                // Detect changed shadow map renderer or type
                if (data.ShadowMapRenderer != shadowMapTexture.Renderer || data.ShadowType != shadowMapTexture.ShadowType)
                    UpdateRenderData(context, data, fogVolume, shadowMapTexture);
            }
            else if (shadowMapTexture?.Renderer != data.ShadowMapRenderer) // Change from no shadows to shadows
            {
                UpdateRenderData(context, data, fogVolume, shadowMapTexture);
            }

            data.RenderViews[0] = context.RenderContext.RenderView;
            data.ShaderGroup.Reset();
            data.ShaderGroup.SetViews(data.RenderViews);
            data.ShaderGroup.AddView(0, context.RenderContext.RenderView, 1);

            data.ShaderGroup.AddLight(fogVolume.Light, shadowMapTexture);
            data.ShaderGroup.UpdateLayout("lightGroup");

            lightParameterCollection.Set(FogVolumesFXKeys.LightGroup, data.ShaderGroup.ShaderSource);

            // Update the effect here so the layout is correct
            fogVolumeEffectShader.EffectInstance.UpdateEffect(GraphicsDevice);

            data.ShaderGroup.ApplyViewParameters(context, 0, lightParameterCollection);
            data.ShaderGroup.ApplyDrawParameters(context, 0, lightParameterCollection, ref box);

            data.UsageCounter = usageCounter;
        }

        private void DrawFogVolume(RenderDrawContext context, RenderFogVolume fogVolume)
        {
            fogVolumeEffectShader.Parameters.Set(FogVolumesShaderKeys.DensityFactor, fogVolume.DensityFactor);

            fogVolumeEffectShader.Draw(context, "Fog volume");
        }

        private bool DrawBoundingVolumeMinMax(RenderDrawContext context, IReadOnlyList<RenderFogVolumeBoundingVolume> boundingVolumes)
        {
            return DrawBoundingVolumes(context, boundingVolumes, context.RenderContext.RenderView.ViewProjection);
        }

        private void DrawBoundingVolumeBackside(RenderDrawContext context, IReadOnlyList<RenderFogVolumeBoundingVolume> boundingVolumes)
        {
            float backSideMaximumDistance = context.RenderContext.RenderView.FarClipPlane;
            float backSideMinimumDistance = -context.RenderContext.RenderView.NearClipPlane;
            Matrix backSideProjection = context.RenderContext.RenderView.View * Matrix.Scaling(1, 1, -1) * Matrix.OrthoRH(BackSideOrthographicSize, BackSideOrthographicSize, backSideMinimumDistance, backSideMaximumDistance);
            DrawBoundingVolumes(context, boundingVolumes, backSideProjection);
        }

        private bool DrawBoundingVolumes(RenderDrawContext context, IReadOnlyList<RenderFogVolumeBoundingVolume> boundingVolumes, Matrix viewProjection)
        {
            var commandList = context.CommandList;

            bool effectUpdated = fogminmaxVolumeEffectShader.UpdateEffect(GraphicsDevice);
            if (fogminmaxVolumeEffectShader.Effect == null)
                return false;

            var needEffectUpdate = effectUpdated || previousMinmaxEffectBytecode != fogminmaxVolumeEffectShader.Effect.Bytecode;
            bool visibleMeshes = false;

            for (int pass = 0; pass < 2; ++pass)
            {
                var minmaxPipelineState = minmaxPipelineStates[pass];

                bool pipelineDirty = false;
                if (needEffectUpdate)
                {
                    // The EffectInstance might have been updated from outside
                    previousMinmaxEffectBytecode = fogminmaxVolumeEffectShader.Effect.Bytecode;

                    minmaxPipelineState.State.RootSignature = fogminmaxVolumeEffectShader.RootSignature;
                    minmaxPipelineState.State.EffectBytecode = fogminmaxVolumeEffectShader.Effect.Bytecode;

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
                    fogminmaxVolumeEffectShader.Parameters.Set(VolumeMinMaxShaderKeys.WorldViewProjection, ref worldViewProjection);

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

                        fogminmaxVolumeEffectShader.Apply(context.GraphicsContext);

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

        private class FogVolumeRenderData
        {
            public LightGroupRendererDynamic GroupRenderer;
            public LightShaderGroupDynamic ShaderGroup;
            public IDirectLight Light;
            public List<RenderView> RenderViews = [.. new RenderView[1]];
            public LightShadowType ShadowType;
            public ILightShadowMapRenderer ShadowMapRenderer;
            public int UsageCounter = 0;
        }
    }
}
