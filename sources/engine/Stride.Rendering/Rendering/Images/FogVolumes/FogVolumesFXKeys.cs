using Stride.Shaders;

namespace Stride.Rendering.Images
{
    public static class FogVolumesFXKeys
    {
        public static readonly PermutationParameterKey<ShaderSource> LightGroup = ParameterKeys.NewPermutation<ShaderSource>();
        public static readonly PermutationParameterKey<int> SampleCount = ParameterKeys.NewPermutation<int>();
    }
}
