using System.Collections.Generic;
using Stride.Core.Mathematics;
using Stride.Rendering.Lights;

namespace Stride.Rendering.Images
{
    public struct RenderFogVolume
    {
       
        public int SampleCount;
        public float DensityValue;
        public IReadOnlyList<RenderFogVolume> BoundingVolumes;
        public bool SeparateBoundingVolumes;
        //public IReadOnlyList<RenderLightShaftBoundingVolume> BoundingVolumes;
        //public bool SeparateBoundingVolumes;



        public Matrix World;
        public Model Model;
    }

    //public struct RenderFogVolumeBoundingVolume
    //{
    //    public Matrix World;
    //    public Model Model;
    //}
}
