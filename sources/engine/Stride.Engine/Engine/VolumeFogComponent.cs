// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System;
using Stride.Core;
using Stride.Core.Annotations;
using Stride.Engine;
using Stride.Engine.Design;
using Stride.Engine.Processors;
using Stride.Rendering;
using Stride.Rendering.Images;
using Stride.Rendering.Shadows;

namespace Stride.Engine
{
    /// <summary>
    /// The source for light shafts, should be placed on the same entity as the light component which will be used for light shafts
    /// </summary>
    [Display("Fog Volume", Expand = ExpandRule.Always)]
    [DataContract("VolumeFogComponent")]
    [DefaultEntityComponentRenderer(typeof(FogVolumeProcessor))] // EDIT THIS LATER
    [ComponentCategory("Lights")]
    public class VolumeFogComponent : ActivableEntityComponent
    {
        /// <summary>
        /// Density of the fog
        /// </summary>
        /// <userdoc>
        /// Higher values produce denser fog
        /// </userdoc>
        [Display("Density")]
        public float DensityFactor { get; set; } = 0.2f;

        /// <summary>
        /// Number of samples taken per pixel
        /// </summary>
        /// <userdoc>
        /// Higher sample counts produce higher quality fog
        /// </userdoc>
        [DataMemberRange(1, 0)]
        public int SampleCount { get; set; } = 4;

        /// <summary>
        /// If true, all bounding volumes will be drawn one by one. -- IDK
        /// </summary>
        /// <remarks>
        /// If this is off, the light shafts might be lower in quality if the bounding volumes overlap (in the same pixel). -- STILl IDK
        /// If this is on, and the bounding volumes overlap (in space), the light shafts inside the overlapping area will become twice as bright.
        /// </remarks>
        /// <userdoc>
        /// This preserves light shaft quality when seen through separate bounding boxes, but uses more GPU HOW DO I REMOVE IT
        /// </userdoc>
        [Display("Volumetric fog")]
        public bool VolumetricCalculations { get; set; } = false;



        private Model model;
        
        private bool enabled = true;

        private VolumeFogComponent fogVolume;

        public override bool Enabled
        {
            get { return enabled; }
            set { enabled = value; EnabledChanged?.Invoke(this, null); }
        }

        /// <summary>
        /// The model used to define the bounding volume
        /// </summary>
        public Model Model
        {
            get { return model; }
            set { model = value; ModelChanged?.Invoke(this, null); }
        }


        /// <summary>
        /// The light shaft to which the bounding volume applies
        /// </summary>
        public VolumeFogComponent FogVolume
        {
            get { return fogVolume; }
            set { fogVolume = value; FogVolumeChanged?.Invoke(this, null); }
        }

        public event EventHandler FogVolumeChanged;
        public event EventHandler ModelChanged;
        public event EventHandler EnabledChanged;
    }
}
