using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Stride.Shaders;

namespace Stride.Rendering.Images
{
    public static class FogVolumeKeys
    {
        public static readonly PermutationParameterKey<ShaderSource> LightGroup = ParameterKeys.NewPermutation<ShaderSource>();
        public static readonly PermutationParameterKey<int> SampleCount = ParameterKeys.NewPermutation<int>();
    }
}
