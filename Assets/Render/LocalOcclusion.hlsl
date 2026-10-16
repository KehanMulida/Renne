#ifndef RENNE_LOCAL_OCCLUSION_INCLUDED
#define RENNE_LOCAL_OCCLUSION_INCLUDED
float4 _OcclusionCenter;
float _OcclusionOpacity;
float _OcclusionRadius;
float _OcclusionDistance;

void ClipLocalOcclusion(float4 positionCS)
{
    float2 uv = positionCS.xy / _ScaledScreenParams.xy;
    float3 world = ComputeWorldSpacePosition(uv, positionCS.z, UNITY_MATRIX_I_VP);
    float3 axis = normalize(_OcclusionCenter.xyz - _WorldSpaceCameraPos);
    float3 delta = world - _OcclusionCenter.xyz;
    float along = dot(delta, axis);
    float transverse = length(delta - axis * along);
    float portal = 1.0 - smoothstep(_OcclusionRadius * 0.65, _OcclusionRadius, transverse);
    float nearby = 1.0 - smoothstep(_OcclusionDistance * 0.8, _OcclusionDistance,
        distance(world.xz, _OcclusionCenter.xz));
    float coverage = lerp(1.0, _OcclusionOpacity, portal * nearby * step(along, 0.0));
    // Stable screen-space dithering; shadow caster deliberately stays solid.
    // A stable solid cutaway avoids the stippled wall surface around the player.
    clip(coverage - 0.5);
}
#endif
