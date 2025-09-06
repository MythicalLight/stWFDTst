using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Stride.Core;
using Stride.Engine.Design;
using Stride.Engine.Processors;
using Stride.Rendering;

namespace Stride.Engine
{
    /// <summary>
    /// A bounding volume for fog volumes to be rendered in, can take any <see cref="Model"/> as a volume
    /// </summary>
    [Display("Fog volume bounding volume", Expand = ExpandRule.Always)]
    [DataContract("FogVolumeBoundingComponent")]
    [DefaultEntityComponentProcessor(typeof(FogVolumeBoundingProcessor))]
    [ComponentCategory("Lights")]
    public class FogVolumeBoundingComponent : ActivableEntityComponent
    {
        private Model model;
        private FogVolumeComponent fogVolume;
        private bool enabled = true;

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
        public FogVolumeComponent FogVolume
        {
            get { return fogVolume; }
            set { fogVolume = value; FogVolumeChanged?.Invoke(this, null); }
        }

        public event EventHandler FogVolumeChanged;
        public event EventHandler ModelChanged;
        public event EventHandler EnabledChanged;
    }
}
