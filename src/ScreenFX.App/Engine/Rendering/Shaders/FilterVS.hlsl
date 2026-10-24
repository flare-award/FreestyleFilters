// ScreenFX Vertex Shader - Fullscreen Triangle
// No vertex buffer needed, SV_VertexID generates triangle covering entire screen

struct VSOutput
{
    float4 position : SV_Position;
    float2 uv : TEXCOORD0;
};

VSOutput main(uint vertexID : SV_VertexID)
{
    VSOutput output;
    // Fullscreen triangle technique (3 vertices)
    // uv = (0,0) (2,0) (0,2) -> covers screen with single triangle, no diagonal seam
    float2 uv = float2((vertexID << 1) & 2, vertexID & 2);
    output.uv = uv;
    output.position = float4(uv * float2(2.0, -2.0) + float2(-1.0, 1.0), 0.0, 1.0);
    return output;
}
