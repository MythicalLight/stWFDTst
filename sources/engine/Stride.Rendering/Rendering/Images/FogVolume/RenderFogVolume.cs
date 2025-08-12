using System.Collections.Generic;
using Stride.Core.Mathematics;
using Stride.Rendering.Lights;

namespace Stride.Rendering.Images
{
    public struct RenderFogVolume
    {
       
        public int SampleCount;
        public float DensityValue;
        //public IReadOnlyList<RenderLightShaftBoundingVolume> BoundingVolumes;
        //public bool SeparateBoundingVolumes;
    }

    //public struct RenderLightShaftBoundingVolume
    //{
    //    public Matrix World;
    //    public Model Model;
    //}
}
